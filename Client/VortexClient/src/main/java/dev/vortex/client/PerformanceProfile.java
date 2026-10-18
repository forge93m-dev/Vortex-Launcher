package dev.vortex.client;

import net.minecraft.client.CloudStatus;
import net.minecraft.client.Minecraft;
import net.minecraft.server.level.ParticleStatus;

/** Restore-only compatibility for users who applied the removed 0.1.24 preset. */
public final class PerformanceProfile {
    private PerformanceProfile() { }
    public record State(int render, int simulation, double entities, int blend,
                        CloudStatus clouds, ParticleStatus particles, boolean shadows) {
        public boolean valid() {
            return render >= 2 && render <= 64 && simulation >= 2 && simulation <= 64 &&
                Double.isFinite(entities) && entities >= .5 && entities <= 5 &&
                blend >= 0 && blend <= 7 && clouds != null && particles != null;
        }
    }
    public record Backup(State before, State applied, boolean ambientBefore) { }
    /** Preserve individual changes the user made after applying the preset. */
    public static State restore(State current, State applied, State before) {
        return new State(current.render() == applied.render() ? before.render() : current.render(),
            current.simulation() == applied.simulation() ? before.simulation() : current.simulation(),
            current.entities() == applied.entities() ? before.entities() : current.entities(),
            current.blend() == applied.blend() ? before.blend() : current.blend(),
            current.clouds() == applied.clouds() ? before.clouds() : current.clouds(),
            current.particles() == applied.particles() ? before.particles() : current.particles(),
            current.shadows() == applied.shadows() ? before.shadows() : current.shadows());
    }
    private static State read(Minecraft mc) {
        var o = mc.options;
        return new State(o.renderDistance().get(), o.simulationDistance().get(), o.entityDistanceScaling().get(),
            o.biomeBlendRadius().get(), o.cloudStatus().get(), o.particles().get(), o.entityShadows().get());
    }
    private static void write(Minecraft mc, State s) {
        var o = mc.options;
        o.renderDistance().set(s.render()); o.simulationDistance().set(s.simulation());
        o.entityDistanceScaling().set(s.entities()); o.biomeBlendRadius().set(s.blend());
        o.cloudStatus().set(s.clouds()); o.particles().set(s.particles()); o.entityShadows().set(s.shadows());
        o.save();
    }
    public static String restore(Minecraft mc) {
        var config = VortexClient.config;
        Backup backup = config.performanceBackup;
        if (backup == null) return "No saved profile to restore.";
        if (backup.before() == null || backup.applied() == null || !backup.before().valid() || !backup.applied().valid())
            return "Saved profile is invalid; no Minecraft options were changed.";
        write(mc, restore(read(mc), backup.applied(), backup.before()));
        config.performanceBackup = null;
        if (!config.save()) { config.performanceBackup = backup; return "Restored; could not save Vortex settings. Retry before closing."; }
        return "Previous settings restored; later manual changes were kept.";
    }
}
