package dev.vortex.client;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.network.chat.Component;
import net.minecraft.world.level.GameType;

/**
 * The Tab player list: your party in its own block on top, everyone else below, ping in milliseconds and
 * a Vortex badge for players whose Vortex cape the client already knows. The server's header and footer stay.
 * Distances to party members come only from players already loaded around you; nothing extra is sent.
 */
public final class PlayerList {
    private PlayerList() { }
    static final int ROW = 16, HEAD = 12, COLUMN = 210, SECTION = 14;
    /** A row: the vanilla entry, or null for a party member who is not on this server. */
    public record Row(PlayerInfo info, String name, boolean self, boolean party, boolean spectator) { }

    /** Ping colour: green up to 100 ms, yellow up to 250 ms, red beyond; grey while unknown. */
    public static int pingColor(int latency) {
        if (latency <= 0) return ClientUi.MUTED;
        return latency <= 100 ? 0xFF6EE7B7 : latency <= 250 ? 0xFFFDE68A : 0xFFFB7185;
    }
    public static String pingText(int latency) { return latency <= 0 ? "—" : latency + " ms"; }
    /** Party block first (you, then the rest by name), then everyone else by name with spectators last. */
    public static List<List<Row>> sections(List<Row> rows) {
        var party = new ArrayList<Row>(); var others = new ArrayList<Row>();
        Comparator<Row> order = Comparator.<Row>comparingInt(r -> r.self() ? 0 : 1).thenComparingInt(r -> r.info() == null ? 1 : 0)
            .thenComparingInt(r -> r.spectator() ? 1 : 0).thenComparing(r -> r.name().toLowerCase(Locale.ROOT));
        for (var row : rows) (row.party() ? party : others).add(row);
        party.sort(order); others.sort(order);
        return List.of(party, others);
    }
    /** One line in the list: a section label or a player row. */
    public record Item(String label, Row row) {
        int height() { return row == null ? SECTION : ROW; }
    }
    public static List<Item> items(List<List<Row>> sections) {
        var items = new ArrayList<Item>();
        if (!sections.get(0).isEmpty()) items.add(new Item("YOUR PARTY · " + sections.get(0).size(), null));
        for (var row : sections.get(0)) items.add(new Item("", row));
        if (!sections.get(1).isEmpty()) items.add(new Item("ON THIS SERVER · " + sections.get(1).size(), null));
        for (var row : sections.get(1)) items.add(new Item("", row));
        return items;
    }
    /** Splits items into columns no taller than {@code available}; a label never ends a column on its own. */
    public static List<List<Item>> columns(List<Item> items, int available) {
        var columns = new ArrayList<List<Item>>();
        var current = new ArrayList<Item>(); int used = 0;
        for (int i = 0; i < items.size(); i++) {
            var item = items.get(i);
            int need = item.height() + (item.row() == null && i + 1 < items.size() ? ROW : 0);
            if (!current.isEmpty() && used + need > available) { columns.add(current); current = new ArrayList<>(); used = 0; }
            current.add(item); used += item.height();
        }
        if (!current.isEmpty()) columns.add(current);
        return columns;
    }

    /** Draws the list; returns false when vanilla must draw instead (a scoreboard column is shown). */
    public static boolean draw(GuiGraphicsExtractor g, int screenWidth, boolean scoreboardColumn, Component header, Component footer) {
        var mc = Minecraft.getInstance();
        if (!VortexClient.config.playerListEnabled || scoreboardColumn || mc.getConnection() == null || mc.player == null) return false;
        var font = mc.font;
        int height = g.guiHeight();
        String server = mc.getCurrentServer() == null ? "Singleplayer" : mc.getCurrentServer().ip;
        var selfInfo = mc.getConnection().getPlayerInfo(mc.player.getUUID());
        String summary = mc.getConnection().getListedOnlinePlayers().size() + " players · your ping " + pingText(selfInfo == null ? 0 : selfInfo.getLatency());
        int textWidth = Math.max(120, screenWidth - 40);
        var headerLines = header == null ? List.<net.minecraft.util.FormattedCharSequence>of() : font.split(header, textWidth);
        var footerLines = footer == null ? List.<net.minecraft.util.FormattedCharSequence>of() : font.split(footer, textWidth);
        int top = 36 + headerLines.size() * 10 + 4;
        int available = Math.max(ROW + SECTION, height - top - footerLines.size() * 10 - 20);
        var columns = columns(items(sections(rows(mc))), available);
        int maxColumns = Math.max(1, (screenWidth - 20) / (COLUMN + 6));
        if (columns.size() > maxColumns) columns = columns.subList(0, maxColumns);
        int bodyHeight = 0;
        for (var column : columns) bodyHeight = Math.max(bodyHeight, column.stream().mapToInt(Item::height).sum());
        int width = Math.max(COLUMN, columns.size() * COLUMN + (columns.size() - 1) * 6), left = (screenWidth - width) / 2;
        int bottom = top + bodyHeight + 4 + (footerLines.isEmpty() ? 0 : footerLines.size() * 10 + 4);
        ClientUi.rounded(g, left - 8, 6, width + 16, bottom - 6 + 4, 12, ClientUi.GLASS);
        g.text(font, server, (screenWidth - font.width(server)) / 2, 12, ClientUi.TEXT, false);
        g.text(font, summary, (screenWidth - font.width(summary)) / 2, 24, ClientUi.MUTED, false);
        int y = 36;
        for (var line : headerLines) { g.text(font, line, (screenWidth - font.width(line)) / 2, y, 0xFFFFFFFF, false); y += 10; }
        for (int c = 0; c < columns.size(); c++) {
            int x = left + c * (COLUMN + 6), rowY = top;
            for (var item : columns.get(c)) {
                if (item.row() == null) g.text(font, item.label(), x + 2, rowY + 3, item.label().startsWith("YOUR") ? ClientUi.ACCENT : ClientUi.MUTED, false);
                else drawRow(g, font, mc, item.row(), x, rowY);
                rowY += item.height();
            }
        }
        y = top + bodyHeight + 6;
        for (var line : footerLines) { g.text(font, line, (screenWidth - font.width(line)) / 2, y, 0xFFFFFFFF, false); y += 10; }
        return true;
    }
    private static void drawRow(GuiGraphicsExtractor g, Font font, Minecraft mc, Row row, int x, int y) {
        if (row.self()) ClientUi.rounded(g, x - 2, y, COLUMN + 4, ROW - 1, 7, ClientUi.accentAlpha(0x40));
        else if (row.party()) ClientUi.rounded(g, x - 2, y, COLUMN + 4, ROW - 1, 7, ClientUi.ACCENT_TINT & 0xFFFFFF | 0x30000000);
        int textColor = row.info() == null || row.spectator() ? ClientUi.MUTED : ClientUi.TEXT;
        if (row.info() != null) net.minecraft.client.gui.components.PlayerFaceExtractor.extractRenderState(g, row.info().getSkin(), x + 2, y + 2, HEAD);
        else ClientUi.rounded(g, x + 2, y + 2, HEAD, HEAD, 3, 0xFF2E3140);
        Component name = row.info() != null && row.info().getTabListDisplayName() != null ? row.info().getTabListDisplayName() : Component.literal(row.name());
        int right = x + COLUMN - 4;
        // Ping pill at the right edge.
        if (row.info() != null) {
            String ping = row.spectator() ? "—" : pingText(row.info().getLatency());
            int color = row.spectator() ? ClientUi.MUTED : pingColor(row.info().getLatency()), w = font.width(ping) + 10;
            ClientUi.pill(g, right - w, y + 2, w, 11, (color & 0xFFFFFF) | 0x33000000);
            g.text(font, ping, right - w + 5, y + 4, color, false);
            right -= w + 4;
        }
        if (row.info() != null && Capes.isVortexPlayer(row.info().getProfile().id())) {
            int w = font.width("VORTEX") / 2 + 10;
            ClientUi.pill(g, right - w, y + 3, w, 9, ClientUi.accentAlpha(0x40));
            g.pose().pushMatrix(); g.pose().translate(right - w + 5, y + 5.5f); g.pose().scale(.5f, .5f);
            g.text(font, "VORTEX", 0, 0, ClientUi.ACCENT_PALE, false); g.pose().popMatrix();
            right -= w + 4;
        }
        String detail = row.self() ? "you" : row.info() == null ? "not on this server" : row.party() ? distance(mc, row.info().getProfile().id()) : row.spectator() ? "spectator" : "";
        int nameX = x + HEAD + 7, nameWidth = Math.max(20, right - nameX - (detail.isEmpty() ? 0 : font.width(detail) + 6));
        var clipped = font.split(name, nameWidth);
        if (!clipped.isEmpty()) g.text(font, clipped.getFirst(), nameX, y + 4, textColor, false);
        if (!detail.isEmpty()) g.text(font, detail, nameX + Math.min(nameWidth, clipped.isEmpty() ? 0 : font.width(clipped.getFirst())) + 6, y + 4, ClientUi.MUTED, false);
    }
    /** Distance to a party member who is already loaded nearby; empty when they are out of view range. */
    private static String distance(Minecraft mc, UUID id) {
        if (mc.level == null || mc.player == null) return "";
        var other = mc.level.getPlayerByUUID(id);
        return other == null ? "" : Math.round(other.distanceTo(mc.player)) + " m";
    }
    private static List<Row> rows(Minecraft mc) {
        var result = new ArrayList<Row>();
        var partyNames = new java.util.HashSet<String>();
        for (var member : PartyFeature.members()) partyNames.add(member.name().toLowerCase(Locale.ROOT));
        var seen = new java.util.HashSet<String>();
        UUID self = mc.player.getUUID();
        for (PlayerInfo info : mc.getConnection().getListedOnlinePlayers()) {
            String name = info.getProfile().name();
            seen.add(name.toLowerCase(Locale.ROOT));
            boolean mine = info.getProfile().id().equals(self);
            result.add(new Row(info, name, mine, partyNames.contains(name.toLowerCase(Locale.ROOT)), info.getGameMode() == GameType.SPECTATOR));
        }
        for (var member : PartyFeature.members())
            if (!seen.contains(member.name().toLowerCase(Locale.ROOT))) result.add(new Row(null, member.name(), false, true, false));
        return result;
    }
}
