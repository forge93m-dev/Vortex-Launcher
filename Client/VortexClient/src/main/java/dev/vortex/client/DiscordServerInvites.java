package dev.vortex.client;

import net.minecraft.client.Minecraft;
import java.util.UUID;

/** Explicit, in-memory 30-minute invitation for one currently connected server. */
public final class DiscordServerInvites {
    private static ServerInvite active;
    private static String source = "";
    public static String message = "Server invitations are off.";
    private DiscordServerInvites() { }
    public static boolean sharing() { return active != null; }
    public static void stop() { active = null; source = ""; message = "Server invitations are off."; }
    public static void start(Minecraft mc, String version) {
        if (!VortexClient.config.discordPresenceEnabled) { message = "Enable Share activity first."; return; }
        if (mc.level == null || mc.hasSingleplayerServer() || mc.getCurrentServer() == null) { message = "Join a multiplayer server first. Singleplayer is not supported."; return; }
        try {
            source = mc.getCurrentServer().ip;
            var invite = new ServerInvite(version, source, System.currentTimeMillis()/1000+1800, UUID.randomUUID().toString().replace("-", "").substring(0,16));
            invite.encode(); active = invite; message = "Invitations enabled for this server for 30 minutes.";
        } catch (IllegalArgumentException ex) { stop(); message = ex.getMessage(); }
    }
    public static String current(Minecraft mc) {
        if (active == null) return "";
        if (!VortexClient.config.discordPresenceEnabled || mc.level == null || mc.hasSingleplayerServer() || mc.getCurrentServer() == null || !source.equals(mc.getCurrentServer().ip)) { stop(); return ""; }
        if (System.currentTimeMillis()/1000 >= active.expires()) { stop(); message = "Invitation expired. Enable server invitations again."; return ""; }
        return active.encode();
    }
}
