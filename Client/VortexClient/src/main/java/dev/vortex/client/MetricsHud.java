package dev.vortex.client;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;
import java.util.Locale;
import net.minecraft.client.Minecraft;

/** Passive metrics: no polling packets or server commands. */
public final class MetricsHud {
    private static final DateTimeFormatter CLOCK = DateTimeFormatter.ofPattern("HH:mm");
    private static Object player, level, connection;
    private static double lastX, lastZ, speed;
    private static final TpsEstimate TPS = new TpsEstimate();
    private MetricsHud() { }
    public static double horizontalSpeed(double dx, double dz) {
        double distance = Math.hypot(dx, dz);
        return Double.isFinite(distance) && distance <= 5 ? distance * 20 : 0;
    }
    public static void tick(Minecraft mc) {
        if (mc.level != level) { level = mc.level; player = null; speed = 0; TPS.reset(); connection = null; }
        if (mc.player == null) { player = null; speed = 0; return; }
        double x = mc.player.getX(), z = mc.player.getZ();
        speed = player == mc.player ? horizontalSpeed(x - lastX, z - lastZ) : 0;
        player = mc.player; lastX = x; lastZ = z;
    }
    public static void serverTime(Object source, long ticks) {
        if (source != connection) { connection = source; TPS.reset(); }
        TPS.sample(ticks, System.nanoTime());
    }
    public static String readout(HudModules.Element element, Minecraft mc, boolean preview) {
        boolean sample = preview && (mc == null || mc.player == null);
        return switch (element) {
            case PING -> sample ? "Ping 42 ms" : ping(mc);
            case SPEED -> String.format(Locale.ROOT, "Speed %.1f m/s", sample ? 4.3 : speed);
            case CLOCK -> "Time " + LocalTime.now().format(CLOCK);
            case TPS -> sample ? "TPS ~20.0" : tps();
            default -> "";
        };
    }
    private static String ping(Minecraft mc) {
        if (mc == null || mc.player == null || mc.getConnection() == null) return "Ping —";
        if (mc.hasSingleplayerServer()) return "Ping Local";
        var info = mc.getConnection().getPlayerInfo(mc.player.getUUID());
        return info == null ? "Ping —" : "Ping " + Math.max(0, info.getLatency()) + " ms";
    }
    private static String tps() {
        double value = TPS.value(System.nanoTime());
        return Double.isNaN(value) ? "TPS ~—" : String.format(Locale.ROOT, "TPS ~%.1f", value);
    }
    /** Rolling five-packet estimate. Unknown on warmup, disconnect, time reset or stale data. */
    public static final class TpsEstimate {
        private long lastTick = -1, lastTime;
        private final double[] tickDeltas = new double[5], seconds = new double[5];
        private int count, cursor;
        public void reset() { lastTick = -1; lastTime = 0; count = cursor = 0; }
        public void sample(long ticks, long now) {
            if (lastTick >= 0 && ticks > lastTick && now > lastTime && now - lastTime <= 15_000_000_000L) {
                tickDeltas[cursor] = (double)ticks - lastTick;
                seconds[cursor] = (now - lastTime) / 1e9;
                cursor = (cursor + 1) % seconds.length; count = Math.min(seconds.length, count + 1);
            } else count = cursor = 0;
            lastTick = ticks; lastTime = now;
        }
        public double value(long now) {
            if (count == 0 || now < lastTime || now - lastTime > 10_000_000_000L) return Double.NaN;
            double ticks = 0, elapsed = 0;
            for (int i = 0; i < count; i++) { ticks += tickDeltas[i]; elapsed += seconds[i]; }
            return Math.clamp(ticks / elapsed, 0, 20);
        }
    }
}
