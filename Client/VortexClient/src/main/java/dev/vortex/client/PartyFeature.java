package dev.vortex.client;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Session-only groups. Only explicit actions (markers, pings, shared places, chat) send anything. */
public final class PartyFeature {
    private static final java.util.concurrent.ExecutorService WORK = Executors.newSingleThreadExecutor(r -> {
        var thread = new Thread(r, "Vortex Party"); thread.setDaemon(true); return thread;
    });
    private static final AtomicBoolean BUSY = new AtomicBoolean();
    /** Explicit sends wait here while a poll is in flight, so a message is never dropped for being "busy". */
    private static final ArrayDeque<Job> OUTBOX = new ArrayDeque<>();
    static final int OUTBOX_LIMIT = 8;
    private static PartyConnection connection;
    private static PartyConnection.Session session;
    private static PartyConnection.Snapshot snapshot;
    private static List<SharedPlace> shared = List.of();
    private static int generation, failures;
    private static long nextPoll, lastUpdate, lastMark, lastChatSeq = -1;
    private static boolean markerWasDown;
    private static boolean owner;
    private static String cachedAddress = "", cachedServerKey = "";
    public static String message = "Create or join a party. Location is shared only when you send a marker.";
    /** A shared waypoint ready to draw: the point uses local waypoint types so pins and block highlights are reused. */
    public record SharedPlace(PartyConnection.SharedWaypoint source, WaypointsStore.Point point) { }
    private PartyFeature() { }
    public static boolean connected() { return session != null; }
    public static boolean busy() { return BUSY.get(); }
    public static boolean owner() { return owner; }
    public static String invite() { return session == null ? "" : session.invite(); }
    public static List<PartyConnection.Member> members() { return snapshot == null ? List.of() : snapshot.members(); }
    public static List<SharedPlace> sharedPlaces() { return shared; }
    /** This player's own party member id, used to decide which shared places they may remove. */
    public static String memberId() {
        if (snapshot == null) return "";
        String name = Minecraft.getInstance().getUser().getName();
        return snapshot.members().stream().filter(m -> m.name().equalsIgnoreCase(name)).map(PartyConnection.Member::memberId).findFirst().orElse("");
    }

    public static void connect(String url, String code, boolean create) {
        if (connected() || busy()) return;
        final PartyConnection transport;
        try { transport = new PartyConnection(url); }
        catch (IllegalArgumentException ex) { message = ex.getMessage(); return; }
        String name = Minecraft.getInstance().getUser().getName();
        submit(() -> {
            var joined = create ? transport.create(name) : transport.join(code.trim().toLowerCase(Locale.ROOT), name);
            if (joined.token() == null || !joined.token().matches("[a-f0-9]{64}") || joined.invite() == null || !joined.invite().matches("[a-f0-9]{32}"))
                throw new IllegalArgumentException("Invalid party credentials.");
            validate(joined.snapshot());
            return () -> {
                connection = transport; session = joined; owner = create; lastChatSeq = -1; OUTBOX.clear();
                accept(joined.snapshot()); message = "Party connected. Share the invite only with friends.";
            };
        });
    }
    private static void accept(PartyConnection.Snapshot value) {
        var before = snapshot;
        snapshot = value;
        EventAlerts.party(before, value); lastUpdate = System.nanoTime(); nextPoll = lastUpdate + 2_000_000_000L; failures = 0;
        long newest = lastChatSeq;
        for (var line : value.chatOrEmpty()) {
            // History from before joining is not replayed; only messages that arrive afterwards appear in chat.
            if (lastChatSeq >= 0 && line.seq() > lastChatSeq) PartyChat.show(line.name(), line.body());
            newest = Math.max(newest, line.seq());
        }
        lastChatSeq = Math.max(newest, 0);
        var places = new ArrayList<SharedPlace>();
        for (var waypoint : value.waypointsOrEmpty()) places.add(new SharedPlace(waypoint, point(waypoint)));
        shared = List.copyOf(places);
    }
    private static WaypointsStore.Point point(PartyConnection.SharedWaypoint w) {
        var blocks = w.blocks() == null ? List.<WaypointsStore.Block>of() : w.blocks().stream().map(b -> new WaypointsStore.Block(b.x(), b.y(), b.z())).toList();
        return new WaypointsStore.Point(UUID.nameUUIDFromBytes(w.id().getBytes(StandardCharsets.UTF_8)), "", w.dimension(), w.title() + " · " + w.name(),
            w.x(), w.y(), w.z(), w.color(), false, "Party", blocks);
    }
    static boolean plainText(String text, int max) {
        return text != null && !text.isBlank() && text.length() <= max && text.chars().noneMatch(c -> Character.isISOControl(c) || c >= 0x202A && c <= 0x202E || c >= 0x2066 && c <= 0x2069);
    }
    private static boolean position(int x, int y, int z) { return Math.abs((long)x) <= 30000000 && Math.abs((long)z) <= 30000000 && Math.abs((long)y) <= 2048; }
    static void validate(PartyConnection.Snapshot value) {
        if (value == null || value.members() == null || value.markers() == null || value.members().size() > 8 || value.markers().size() > 8 ||
            value.chatOrEmpty().size() > 50 || value.waypointsOrEmpty().size() > 32)
            throw new IllegalArgumentException("Invalid party snapshot.");
        for (var m : value.members()) if (m == null || m.name() == null || !m.name().matches("[A-Za-z0-9_]{3,16}")) throw new IllegalArgumentException("Invalid member.");
        for (var m : value.markers()) {
            if (m == null || m.name() == null || !m.name().matches("[A-Za-z0-9_]{3,16}") || m.serverKey() == null || !m.serverKey().matches("[a-f0-9]{64}") ||
                m.dimension() == null || m.dimension().length() > 160 || m.kind() == null || !List.of("HERE", "COME", "DANGER", "PING").contains(m.kind()) ||
                !position(m.x(), m.y(), m.z()) || m.expiresUtc() == null)
                throw new IllegalArgumentException("Invalid marker.");
            Instant.parse(m.expiresUtc());
        }
        for (var c : value.chatOrEmpty())
            if (c == null || c.seq() <= 0 || c.name() == null || !c.name().matches("[A-Za-z0-9_]{3,16}") || !plainText(c.body(), 256))
                throw new IllegalArgumentException("Invalid party message.");
        for (var w : value.waypointsOrEmpty()) {
            if (w == null || w.id() == null || !w.id().matches("[a-f0-9]{24}") || w.name() == null || !w.name().matches("[A-Za-z0-9_]{3,16}") ||
                !plainText(w.title(), 32) || w.serverKey() == null || !w.serverKey().matches("[a-f0-9]{64}") || w.dimension() == null || w.dimension().length() > 160 ||
                !position(w.x(), w.y(), w.z()) || w.color() < 0 || w.color() > 0xFFFFFF || w.blocks() != null && w.blocks().size() > 32)
                throw new IllegalArgumentException("Invalid shared waypoint.");
            if (w.blocks() != null) for (var b : w.blocks()) if (b == null || !position(b.x(), b.y(), b.z())) throw new IllegalArgumentException("Invalid shared waypoint.");
        }
    }
    private interface Job { Runnable run() throws Exception; }
    private static void submit(Job job) {
        if (!BUSY.compareAndSet(false, true)) return;
        int expected = generation;
        WORK.execute(() -> {
            Runnable apply;
            try { apply = job.run(); }
            catch (Exception ex) {
                String error = ex instanceof PartyConnection.Failure || ex instanceof IllegalArgumentException ? ex.getMessage() : "Party service unavailable. Check the address and connection.";
                boolean expired = ex instanceof PartyConnection.Failure f && f.status == 401;
                apply = () -> {
                    message = error;
                    if (expired) { session = null; connection = null; snapshot = null; shared = List.of(); OUTBOX.clear(); }
                    nextPoll = System.nanoTime() + Math.min(30, 2 << Math.min(4, ++failures)) * 1_000_000_000L;
                };
            }
            Runnable result = apply;
            Minecraft.getInstance().execute(() -> {
                try { if (expected == generation) result.run(); }
                finally { BUSY.set(false); }
            });
        });
    }
    /** Queues an explicit send; it goes out on the next tick when no request is in flight. */
    private static boolean queue(Job job) {
        if (!connected()) { message = "Join a party first."; return false; }
        if (OUTBOX.size() >= OUTBOX_LIMIT) { message = "Too many party actions at once. Wait a moment."; return false; }
        OUTBOX.add(job); return true;
    }
    public static void leave() {
        var old = session; var transport = connection;
        generation++; session = null; connection = null; snapshot = null; shared = List.of(); lastUpdate = 0; owner = false; OUTBOX.clear();
        message = "Left the party. Your location is no longer shared.";
        if (old != null && transport != null) WORK.execute(() -> { try { transport.leave(old.token()); } catch (Exception ignored) { /* Server expires abandoned sessions. */ } });
    }
    public static void tick(Minecraft mc) {
        boolean down = mc.isWindowActive() && ClientBindings.down(ClientBindings.Action.PARTY_MARKER);
        if (down && !markerWasDown && mc.gui.screen() == null && mc.gui.overlay() == null) ping();
        markerWasDown = down;
        if (!connected() || busy()) return;
        if (!OUTBOX.isEmpty()) { submit(OUTBOX.poll()); return; }
        if (System.nanoTime() < nextPoll) return;
        var transport = connection; var token = session.token();
        submit(() -> { var value = transport.read(token); validate(value); return () -> accept(value); });
    }
    public static String serverKey(String address) {
        try {
            String normalized = address.trim().toLowerCase(Locale.ROOT);
            if (!normalized.contains(":")) normalized += ":25565";
            return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(normalized.getBytes(StandardCharsets.UTF_8)));
        } catch (java.security.NoSuchAlgorithmException ex) { throw new IllegalStateException(ex); }
    }
    private static String currentServerKey(String address) {
        if (!address.equals(cachedAddress)) { cachedAddress = address; cachedServerKey = serverKey(address); }
        return cachedServerKey;
    }
    /** Server key and dimension of the current multiplayer world, or null in singleplayer / menus. */
    private static String[] place(Minecraft mc) {
        if (mc.level == null || mc.player == null || mc.hasSingleplayerServer() || mc.getCurrentServer() == null) return null;
        return new String[] {currentServerKey(mc.getCurrentServer().ip), mc.level.dimension().identifier().toString()};
    }
    /** Marker key: a ping on the block you look at (up to 96 blocks away), otherwise "I'm here" at your feet. */
    static void ping() {
        var mc = Minecraft.getInstance();
        if (!connected()) return;
        var hit = mc.player == null ? null : mc.player.pick(96, 1f, false);
        if (hit instanceof net.minecraft.world.phys.BlockHitResult block && hit.getType() == net.minecraft.world.phys.HitResult.Type.BLOCK) {
            var pos = block.getBlockPos();
            mark("PING", pos.getX(), pos.getY(), pos.getZ());
        } else mark("HERE");
    }
    public static void mark(String kind) {
        var mc = Minecraft.getInstance();
        if (mc.player == null) { message = "Markers are available on multiplayer servers."; return; }
        var feet = mc.player.blockPosition();
        mark(kind, feet.getX(), feet.getY(), feet.getZ());
    }
    static void mark(String kind, int x, int y, int z) {
        var mc = Minecraft.getInstance();
        if (!connected()) { message = "Join a party first."; return; }
        var where = place(mc);
        if (where == null) { message = "Markers are available on multiplayer servers."; return; }
        if (System.nanoTime() - lastMark < 2_000_000_000L) { message = "Wait a moment before sending another marker."; return; }
        var transport = connection; var token = session.token(); lastMark = System.nanoTime();
        queue(() -> { var value = transport.mark(token, where[0], where[1], x, y, z, kind); validate(value); return () -> {
            accept(value); message = kind.equals("PING") ? "Ping sent to your party (30 seconds)." : "Marker sent to your party (60 seconds).";
        }; });
    }
    /** Sends a party chat line; the server echoes it back, so it appears in order with everyone else's. */
    public static void chat(String text) {
        String line = text == null ? "" : text.strip();
        if (!connected()) { PartyChat.local("Join a party first (Vortex menu → Party). The message was not sent anywhere."); return; }
        if (!plainText(line, 256)) { PartyChat.local("Party messages are one line of up to 256 characters."); return; }
        var transport = connection; var token = session.token();
        if (!queue(() -> { var value = transport.chat(token, line); validate(value); return () -> accept(value); }))
            PartyChat.local(message);
    }
    /** Shares a saved local waypoint (and up to 32 of its blocks) with the party, on the current server only. */
    public static void share(WaypointsStore.Point point) {
        var mc = Minecraft.getInstance();
        var where = place(mc);
        if (!connected()) { message = "Join a party first."; return; }
        if (where == null || !where[1].equals(point.dimension())) { message = "Share waypoints from this server and dimension."; return; }
        if (point.blocks().size() > 32) { message = "Party waypoints can carry up to 32 blocks."; return; }
        var blocks = point.blocks().stream().map(b -> new PartyConnection.Block(b.x(), b.y(), b.z())).toList();
        var transport = connection; var token = session.token();
        if (queue(() -> {
            try {
                var value = transport.share(token, where[0], where[1], point.name(), point.x(), point.y(), point.z(), point.color(), blocks);
                validate(value); return () -> { accept(value); message = "\"" + point.name() + "\" is now shared with your party."; };
            } catch (PartyConnection.Failure full) {
                if (full.status == 409) throw new PartyConnection.Failure(409, "This party already shares 32 waypoints. Remove one first.");
                throw full;
            }
        })) message = "Sharing \"" + point.name() + "\"...";
    }
    public static void unshare(String id) {
        var transport = connection; var token = session == null ? "" : session.token();
        queue(() -> { var value = transport.unshare(token, id); validate(value); return () -> { accept(value); message = "Shared waypoint removed."; }; });
    }
    /** Live markers and shared places for this server and dimension, as points for the waypoint pin renderer. */
    public static List<WaypointsStore.Point> worldPoints() {
        var mc = Minecraft.getInstance();
        if (!connected() || snapshot == null || System.nanoTime() - lastUpdate > 8_000_000_000L) return List.of();
        var where = place(mc);
        if (where == null) return List.of();
        var result = new ArrayList<WaypointsStore.Point>();
        var now = Instant.now();
        for (var marker : snapshot.markers()) {
            if (!visible(marker, where[0], where[1], now)) continue;
            String label = marker.name() + " · " + switch (marker.kind()) { case "PING" -> "ping"; case "COME" -> "come here"; case "DANGER" -> "danger"; default -> "here"; };
            int color = switch (marker.kind()) { case "PING" -> ClientUi.ACCENT & 0xFFFFFF; case "DANGER" -> 0xFB7185; case "COME" -> 0x7DD3FC; default -> 0x6EE7B7; };
            // A ping points at a block; the pin should stand on it, not one block lower.
            int y = marker.kind().equals("PING") ? marker.y() : marker.y() - 1;
            result.add(new WaypointsStore.Point(UUID.nameUUIDFromBytes(marker.memberId().getBytes(StandardCharsets.UTF_8)), "", marker.dimension(), label,
                marker.x(), y, marker.z(), color, false, "Party", List.of()));
        }
        for (var place : shared)
            if (place.source().serverKey().equals(where[0]) && place.source().dimension().equals(where[1])) result.add(place.point());
        return result;
    }
    public static void draw(GuiGraphicsExtractor g) {
        var mc = Minecraft.getInstance();
        if (!connected() || snapshot == null || mc.level == null || mc.player == null || mc.gui.hud.isHidden() || mc.getCurrentServer() == null || System.nanoTime() - lastUpdate > 8_000_000_000L) return;
        String server = currentServerKey(mc.getCurrentServer().ip), dimension = mc.level.dimension().identifier().toString();
        int row = 0;
        for (var marker : snapshot.markers()) {
            if (!visible(marker, server, dimension, Instant.now())) continue;
            double distance = Math.hypot(marker.x() - mc.player.getX(), marker.z() - mc.player.getZ());
            String label = marker.name() + " · " + marker.kind() + " · " + Math.round(distance) + "m  [" + marker.x() + ", " + marker.y() + ", " + marker.z() + "]";
            int color = marker.kind().equals("DANGER") ? 0xFFFF8585 : ClientUi.ACCENT_LIGHT;
            g.text(mc.font, label, Math.max(6, g.guiWidth() - mc.font.width(label) - 8), 36 + row++ * 12, color, true);
        }
    }
    public static boolean visible(PartyConnection.Marker marker, String server, String dimension, Instant now) {
        return server.equals(marker.serverKey()) && dimension.equals(marker.dimension()) && now.isBefore(Instant.parse(marker.expiresUtc()));
    }
}
