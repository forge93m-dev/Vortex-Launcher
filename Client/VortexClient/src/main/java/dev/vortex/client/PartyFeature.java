package dev.vortex.client;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.HexFormat;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Session-only groups. Only explicit marker actions read/send player coordinates. */
public final class PartyFeature {
    private static final java.util.concurrent.ExecutorService WORK = Executors.newSingleThreadExecutor(r -> {
        var thread = new Thread(r, "Vortex Party"); thread.setDaemon(true); return thread;
    });
    private static final AtomicBoolean BUSY = new AtomicBoolean();
    private static PartyConnection connection;
    private static PartyConnection.Session session;
    private static PartyConnection.Snapshot snapshot;
    private static int generation, failures;
    private static long nextPoll, lastUpdate, lastMark;
    private static boolean markerWasDown;
    private static boolean owner;
    private static String cachedAddress = "", cachedServerKey = "";
    public static String message = "Create or join a party. Location is shared only when you send a marker.";
    private PartyFeature() { }
    public static boolean connected() { return session != null; }
    public static boolean busy() { return BUSY.get(); }
    public static boolean owner() { return owner; }
    public static String invite() { return session == null ? "" : session.invite(); }
    public static List<PartyConnection.Member> members() { return snapshot == null ? List.of() : snapshot.members(); }

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
            return () -> { connection = transport; session = joined; owner = create; accept(joined.snapshot()); message = "Party connected. Share the invite only with friends."; };
        });
    }
    private static void accept(PartyConnection.Snapshot value) {
        snapshot = value; lastUpdate = System.nanoTime(); nextPoll = lastUpdate + 2_000_000_000L; failures = 0;
    }
    private static void validate(PartyConnection.Snapshot value) {
        if (value == null || value.members() == null || value.markers() == null || value.members().size() > 8 || value.markers().size() > 8)
            throw new IllegalArgumentException("Invalid party snapshot.");
        for (var m : value.members()) if (m == null || m.name() == null || !m.name().matches("[A-Za-z0-9_]{3,16}")) throw new IllegalArgumentException("Invalid member.");
        for (var m : value.markers()) {
            if (m == null || m.name() == null || !m.name().matches("[A-Za-z0-9_]{3,16}") || m.serverKey() == null || !m.serverKey().matches("[a-f0-9]{64}") ||
                m.dimension() == null || m.dimension().length() > 160 || m.kind() == null || !List.of("HERE", "COME", "DANGER").contains(m.kind()) ||
                Math.abs((long)m.x()) > 30000000 || Math.abs((long)m.z()) > 30000000 || Math.abs((long)m.y()) > 2048 || m.expiresUtc() == null)
                throw new IllegalArgumentException("Invalid marker.");
            Instant.parse(m.expiresUtc());
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
                    message = error; snapshot = null;
                    if (expired) { session = null; connection = null; }
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
    public static void leave() {
        var old = session; var transport = connection;
        generation++; session = null; connection = null; snapshot = null; lastUpdate = 0; owner = false;
        message = "Left the party. Your location is no longer shared.";
        if (old != null && transport != null) WORK.execute(() -> { try { transport.leave(old.token()); } catch (Exception ignored) { /* Server expires abandoned sessions. */ } });
    }
    public static void tick(Minecraft mc) {
        boolean down = mc.isWindowActive() && ClientBindings.down(ClientBindings.Action.PARTY_MARKER);
        if (down && !markerWasDown && mc.gui.screen() == null && mc.gui.overlay() == null) mark("HERE");
        markerWasDown = down;
        if (!connected() || busy() || System.nanoTime() < nextPoll) return;
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
    public static void mark(String kind) {
        var mc = Minecraft.getInstance();
        if (!connected()) { message = "Join a party first."; return; }
        if (mc.level == null || mc.player == null || mc.hasSingleplayerServer() || mc.getCurrentServer() == null) { message = "Markers are available on multiplayer servers."; return; }
        if (busy() || System.nanoTime() - lastMark < 2_000_000_000L) { message = "Wait a moment before sending another marker."; return; }
        String server = currentServerKey(mc.getCurrentServer().ip), dimension = mc.level.dimension().identifier().toString();
        int x = mc.player.blockPosition().getX(), y = mc.player.blockPosition().getY(), z = mc.player.blockPosition().getZ();
        var transport = connection; var token = session.token(); lastMark = System.nanoTime();
        submit(() -> { var value = transport.mark(token, server, dimension, x, y, z, kind); validate(value); return () -> { accept(value); message = "Marker sent to your party (60 seconds)."; }; });
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
            int color = marker.kind().equals("DANGER") ? 0xFFFF8585 : 0xFFC5B6FF;
            g.text(mc.font, label, Math.max(6, g.guiWidth() - mc.font.width(label) - 8), 36 + row++ * 12, color, true);
        }
    }
    public static boolean visible(PartyConnection.Marker marker, String server, String dimension, Instant now) {
        return server.equals(marker.serverKey()) && dimension.equals(marker.dimension()) && now.isBefore(Instant.parse(marker.expiresUtc()));
    }
}
