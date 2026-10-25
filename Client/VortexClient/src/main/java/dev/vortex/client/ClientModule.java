package dev.vortex.client;

import java.util.Locale;
import net.minecraft.client.KeyMapping;

/** Only modules that actually exist are exposed in the menu. */
public enum ClientModule {
    FPS("FPS Counter", "Live frame rate", "HUD", HudModules.Element.FPS),
    ZOOM("Zoom", "Hold key + scroll to adjust", "Gameplay", null),
    ARMOR("Armor HUD", "Armor and held item durability", "HUD", HudModules.Element.ARMOR),
    SPRINT("Toggle Sprint", "Tap once to sprint", "Gameplay", HudModules.Element.SPRINT),
    COORDINATES("Coordinates", "XYZ, direction and biome", "HUD", HudModules.Element.COORDINATES),
    CROSSHAIR("Crosshair", "Shape, color and size", "HUD", null),
    EFFECTS("Potion Effects", "Active effects and timers", "HUD", HudModules.Element.EFFECTS),
    PING("Ping", "Server latency in milliseconds", "HUD", HudModules.Element.PING),
    SPEED("Speed", "Horizontal movement in m/s", "HUD", HudModules.Element.SPEED),
    CLOCK("Clock", "Your local time", "HUD", HudModules.Element.CLOCK),
    TPS("TPS", "Estimated server tick rate", "HUD", HudModules.Element.TPS),
    FREELOOK("Freelook", "Independent third-person view", "Gameplay", null),
    FULLBRIGHT("Fullbright", "Brighter caves and nights", "Gameplay", null),
    BLOCK_INFO("Block Info", "Block under your crosshair", "HUD", HudModules.Element.BLOCK_INFO),
    CHAT("Chat", "Timestamps and repeated messages", "Gameplay", null),
    CHEST_SEARCH("Chest Search", "Find items in chests and shulkers · Ctrl+F", "Gameplay", null),
    WAYPOINTS("Waypoints", "Private world markers · Press B", "Gameplay", null),
    SHULKER_PREVIEW("Shulker Preview", "View stored items in the tooltip", "Gameplay", null),
    ENTITY_HEARTS("Entity Hearts", "Mob health and server-shared player HP", "Gameplay", null),
    COMPACT_SHIELD("Compact Shield", "Smaller first-person shield; same blocking", "Gameplay", null),
    ENCHANTMENT_HINTS("Enchantment Hints", "Plain-English enchantment explanations", "Gameplay", null),
    DEATH_WAYPOINTS("Death Waypoints", "Save death coordinates in the Deaths group", "Gameplay", null),
    RESOURCE_COUNTER("Resource Counter", "Track inventory goals", "HUD", HudModules.Element.RESOURCES),
    STORAGE_MEMORY("Storage Finder", "Find items in previously opened storage", "Gameplay", null),
    PLAYER_LIST("Player List", "Party on top, ping in ms, Vortex badges · Tab", "Gameplay", null),
    EVENT_ALERTS("Event Alerts", "Sounds and notices for party changes, mentions and low health", "Gameplay", null);

    public final String title, description, category;
    public final HudModules.Element hud;
    ClientModule(String title, String description, String category, HudModules.Element hud) {
        this.title = title; this.description = description; this.category = category; this.hud = hud;
    }
    public boolean enabled() {
        var c = VortexClient.config;
        return switch (this) {
            case RESOURCE_COUNTER -> c.resourceCounterEnabled; case STORAGE_MEMORY -> c.storageMemoryEnabled;
            case FPS -> c.fpsEnabled; case ZOOM -> c.zoomEnabled;
            case ARMOR -> c.armorEnabled; case SPRINT -> c.toggleSprintEnabled;
            case COORDINATES -> c.coordinatesEnabled;
            case CROSSHAIR -> c.crosshairEnabled;
            case EFFECTS -> c.effectsEnabled;
            case PING -> c.pingEnabled; case SPEED -> c.speedEnabled;
            case CLOCK -> c.clockEnabled; case TPS -> c.tpsEnabled;
            case FREELOOK -> c.freelookEnabled;
            case FULLBRIGHT -> c.fullbrightEnabled; case BLOCK_INFO -> c.blockInfoEnabled;
            case CHAT -> c.chatEnabled;
            case CHEST_SEARCH -> c.chestSearchEnabled;
            case WAYPOINTS -> c.waypointsEnabled;
            case SHULKER_PREVIEW -> c.shulkerPreviewEnabled;
            case ENTITY_HEARTS -> c.entityHeartsEnabled;
            case COMPACT_SHIELD -> c.compactShieldEnabled;
            case ENCHANTMENT_HINTS -> c.enchantmentHintsEnabled;
            case DEATH_WAYPOINTS -> c.deathWaypointsEnabled;
            case PLAYER_LIST -> c.playerListEnabled;
            case EVENT_ALERTS -> c.eventAlertsEnabled;
        };
    }
    public void toggle() {
        var c = VortexClient.config;
        switch (this) {
            case RESOURCE_COUNTER -> c.resourceCounterEnabled=!c.resourceCounterEnabled;
            case STORAGE_MEMORY -> c.storageMemoryEnabled=!c.storageMemoryEnabled;
            case FPS -> c.fpsEnabled = !c.fpsEnabled;
            case ZOOM -> c.zoomEnabled = !c.zoomEnabled;
            case ARMOR -> c.armorEnabled = !c.armorEnabled;
            case SPRINT -> { c.toggleSprintEnabled = !c.toggleSprintEnabled; KeyMapping.resetToggleKeys(); }
            case COORDINATES -> c.coordinatesEnabled = !c.coordinatesEnabled;
            case CROSSHAIR -> c.crosshairEnabled = !c.crosshairEnabled;
            case EFFECTS -> c.effectsEnabled = !c.effectsEnabled;
            case PING -> c.pingEnabled = !c.pingEnabled; case SPEED -> c.speedEnabled = !c.speedEnabled;
            case CLOCK -> c.clockEnabled = !c.clockEnabled; case TPS -> c.tpsEnabled = !c.tpsEnabled;
            case FREELOOK -> { c.freelookEnabled = !c.freelookEnabled; if (!c.freelookEnabled) Freelook.stop(net.minecraft.client.Minecraft.getInstance()); }
            case FULLBRIGHT -> c.fullbrightEnabled = !c.fullbrightEnabled;
            case BLOCK_INFO -> c.blockInfoEnabled = !c.blockInfoEnabled;
            case CHAT -> c.chatEnabled = !c.chatEnabled;
            case CHEST_SEARCH -> c.chestSearchEnabled = !c.chestSearchEnabled;
            case WAYPOINTS -> c.waypointsEnabled = !c.waypointsEnabled;
            case SHULKER_PREVIEW -> c.shulkerPreviewEnabled = !c.shulkerPreviewEnabled;
            case ENTITY_HEARTS -> c.entityHeartsEnabled = !c.entityHeartsEnabled;
            case COMPACT_SHIELD -> c.compactShieldEnabled=!c.compactShieldEnabled;
            case ENCHANTMENT_HINTS -> c.enchantmentHintsEnabled=!c.enchantmentHintsEnabled;
            case DEATH_WAYPOINTS -> c.deathWaypointsEnabled=!c.deathWaypointsEnabled;
            case PLAYER_LIST -> c.playerListEnabled=!c.playerListEnabled;
            case EVENT_ALERTS -> c.eventAlertsEnabled=!c.eventAlertsEnabled;
        }
    }
    public boolean matches(String search, String filter) {
        String query = search.strip().toLowerCase(Locale.ROOT);
        boolean categoryMatches = switch (filter) {
            case "HUD" -> hud != null || category.equals("HUD");
            case "Gameplay" -> category.equals("Gameplay");
            case "Enabled" -> enabled();
            default -> true;
        };
        return categoryMatches && (title + " " + description + " " + category).toLowerCase(Locale.ROOT).contains(query);
    }
    public static ClientModule forHud(HudModules.Element element) {
        for (var module : values()) if (module.hud == element) return module;
        throw new IllegalArgumentException("Unknown HUD module: " + element);
    }
}
