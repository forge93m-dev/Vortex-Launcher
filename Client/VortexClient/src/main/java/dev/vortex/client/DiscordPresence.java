package dev.vortex.client;

import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;

public final class DiscordPresence {
    private static DiscordRpc rpc;
    private static int ticks;
    private static final String VERSION = FabricLoader.getInstance().getModContainer("minecraft").orElseThrow().getMetadata().getVersion().getFriendlyString();
    public static void startInvites() { DiscordServerInvites.start(Minecraft.getInstance(), VERSION); }
    public static void tick(Minecraft minecraft) {
        if (++ticks % 20 != 0) return;
        if (!VortexClient.config.discordPresenceEnabled && rpc == null) return;
        if (rpc == null) {
            rpc = new DiscordRpc();
            Runtime.getRuntime().addShutdownHook(new Thread(() -> rpc.close(), "Vortex Discord cleanup"));
        }
        rpc.update(VortexClient.config.discordPresenceEnabled, new DiscordRpc.Activity(VERSION,
            minecraft.level == null ? DiscordRpc.Mode.MENU : minecraft.hasSingleplayerServer() ? DiscordRpc.Mode.SINGLEPLAYER : DiscordRpc.Mode.MULTIPLAYER,
            DiscordServerInvites.current(minecraft)));
    }
    public static String status() { return !VortexClient.config.discordPresenceEnabled ? "Disabled" : rpc == null ? "Connecting to Discord desktop…" : rpc.status(); }
}
