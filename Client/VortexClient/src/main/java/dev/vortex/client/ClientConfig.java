package dev.vortex.client;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonObject;
import net.fabricmc.loader.api.FabricLoader;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;

public final class ClientConfig {
    public boolean discordPresenceEnabled = false;
    public boolean resourceCounterEnabled, storageMemoryEnabled;
    public java.util.List<ResourceCounter.Goal> resourceGoals = java.util.List.of();
    public int resourceX = 10, resourceY = 180, storageMemoryRadius = 64;
    public float resourceScale, resourceOpacity;
    public boolean capesEnabled = false;
    public String selectedCape = "none";
    // Read-only recovery data for the removed 0.1.24 preset; never applied automatically.
    public PerformanceProfile.Backup performanceBackup;
    private static final Gson JSON = new GsonBuilder().setPrettyPrinting().create();
    private static final Logger LOG = LoggerFactory.getLogger("Vortex Client");
    public int hudLayoutVersion = 1;
    public String partyServiceUrl = "";
    public int partyMarkerKey = com.mojang.blaze3d.platform.InputConstants.KEY_G;
    public boolean pingEnabled, speedEnabled, clockEnabled, tpsEnabled;
    public int pingX = 110, pingY = 38, speedX = 110, speedY = 66, clockX = 110, clockY = 94, tpsX = 110, tpsY = 122;
    public float pingScale, speedScale, clockScale, tpsScale;
    public float pingOpacity, speedOpacity, clockOpacity, tpsOpacity;
    public boolean fpsEnabled = true;
    public boolean chatEnabled = true;
    public boolean chestSearchEnabled = true;
    public boolean waypointsEnabled = true;
    public boolean shulkerPreviewEnabled = true;
    public boolean compactShieldEnabled = true, enchantmentHintsEnabled = true, deathWaypointsEnabled = true, playerListEnabled = true;
    /** Accent colour of the client interface (RGB). */
    public int clientAccent = 0xA78BFA;
    /** Title-screen panorama from an older version (its Minecraft version id), or "" for the current one. */
    public String menuPanorama = "";
    public float shieldScale=.72f;
    public java.util.Set<String> hiddenWaypointGroups=new java.util.HashSet<>();
    public boolean entityHeartsEnabled = true;
    public boolean entityHeartsPlayers = true, entityHeartsMobs = true;
    public int entityHeartsRange = 32;
    public int waypointsKey = com.mojang.blaze3d.platform.InputConstants.KEY_B;
    public boolean chatTimestamps = true, chatCollapseRepeats = true;
    public boolean zoomEnabled = true;
    public boolean freelookEnabled = false;
    public boolean fullbrightEnabled = false, blockInfoEnabled = false;
    public int blockInfoX = -1, blockInfoY = 92;
    public float blockInfoScale, blockInfoOpacity;
    public int freelookKey = com.mojang.blaze3d.platform.InputConstants.KEY_V;
    public int menuKey = com.mojang.blaze3d.platform.InputConstants.KEY_RSHIFT;
    public int zoomKey = com.mojang.blaze3d.platform.InputConstants.KEY_C;
    public boolean durabilityAlerts = true;
    public int durabilityThreshold = 10;
    public float freelookSensitivity = 1;
    // Legacy fields stay readable; Keystrokes is no longer an available module.
    public boolean keystrokesEnabled = false;
    public boolean uiAnimations = true;
    public boolean clientNotifications = true;
    public boolean armorEnabled = true;
    public boolean armorHorizontal = false;
    public boolean heldItemEnabled = true;
    public boolean heldOffhand = true;
    public boolean effectsEnabled = true;
    public boolean crosshairEnabled = false;
    public int crosshairShape = 0;
    public int crosshairColor = 0xFFFFFF;
    public int crosshairSize = 5;
    public int crosshairGap = 2;
    public int crosshairThickness = 1;
    public boolean crosshairOutline = true;
    public boolean crosshairDot = false;
    public float crosshairOpacity = 1;
    public int heldItemX = -1, heldItemY = -1;
    public int effectsX = -1, effectsY = 10;
    public float heldItemScale, heldItemOpacity, effectsScale, effectsOpacity;
    public boolean coordinatesEnabled = true;
    public boolean toggleSprintEnabled = false;
    public boolean hudEditorGrid = true;
    public boolean hudEditorSnapping = true;
    public float zoomMultiplier = 4;
    /** Zoom: press once to toggle instead of holding; slower mouse while zoomed in. */
    public boolean zoomToggle = false, zoomSlowMouse = true;
    public float hudScale = 1;
    public float hudOpacity = 0.85f;
    public int fpsX = 110;
    public int fpsY = 10;
    public int keysX = 10;
    public int keysY = 84;
    // -1 keeps this axis attached to the bottom/right edge until manually moved.
    public int armorX = -1;
    public int armorY = -1;
    public int coordinatesX = 10;
    public int coordinatesY = 10;
    public int sprintX = 110;
    public int sprintY = 150;
    // Zero inherits the global value, keeping existing instance settings intact.
    public float fpsScale, keysScale, armorScale, sprintScale, coordinatesScale;
    public float fpsOpacity, keysOpacity, armorOpacity, sprintOpacity, coordinatesOpacity;

    public static ClientConfig load(Path path) {
        if (!Files.exists(path)) return new ClientConfig();
        try (var reader = Files.newBufferedReader(path)) {
            JsonObject document = JSON.fromJson(reader, JsonObject.class);
            if (document == null) return new ClientConfig();
            ClientConfig config = JSON.fromJson(document, ClientConfig.class);
            config.resourceGoals = ResourceCounter.clean(config.resourceGoals);
            config.storageMemoryRadius = Math.clamp(config.storageMemoryRadius,16,256);
            config.resourceX = Math.max(0,config.resourceX); config.resourceY = Math.max(0,config.resourceY);
            config.resourceScale = optionalClamp(config.resourceScale,.75f,2); config.resourceOpacity = optionalClamp(config.resourceOpacity,.25f,1);
            if (!CapeCatalog.valid(config.selectedCape)) config.selectedCape = "none";
            // One-time upgrade: move only the old factory defaults. After saving the
            // version marker, users can freely choose even the old default positions.
            if (!document.has("hudLayoutVersion") || config.hudLayoutVersion < 1) {
                if (config.armorX == 110 && config.armorY == 10) config.armorX = config.armorY = -1;
                if (config.coordinatesEnabled) {
                    if (config.fpsX == 10 && config.fpsY == 10) config.fpsX = 110;
                    if (config.keysX == 10 && config.keysY == 42) config.keysY = 84;
                }
            }
            config.hudLayoutVersion = 1;
            config.keystrokesEnabled = false;
            config.entityHeartsRange = Math.clamp(config.entityHeartsRange,8,64);
            config.shieldScale=finiteClamp(config.shieldScale,.5f,1f,.72f);
            if(config.hiddenWaypointGroups==null)config.hiddenWaypointGroups=new java.util.HashSet<>();
            config.hiddenWaypointGroups=new java.util.HashSet<>(config.hiddenWaypointGroups.stream().filter(s->s!=null && s.matches("[a-f0-9]{64}")).limit(512).toList());
            config.crosshairShape = Math.clamp(config.crosshairShape, 0, 2);
            config.crosshairColor &= 0xFFFFFF;
            config.clientAccent &= 0xFFFFFF;
            if (config.menuPanorama == null || !config.menuPanorama.isEmpty() && !PanoramaLibrary.valid(config.menuPanorama)) config.menuPanorama = "";
            config.crosshairSize = Math.clamp(config.crosshairSize, 2, 12);
            config.crosshairGap = Math.clamp(config.crosshairGap, 0, 8);
            config.crosshairThickness = Math.clamp(config.crosshairThickness, 1, 4);
            config.crosshairOpacity = finiteClamp(config.crosshairOpacity, .25f, 1, 1);
            config.heldItemX = config.heldItemX == -1 ? -1 : Math.max(0, config.heldItemX);
            config.heldItemY = config.heldItemY == -1 ? -1 : Math.max(0, config.heldItemY);
            config.effectsX = config.effectsX == -1 ? -1 : Math.max(0, config.effectsX);
            config.effectsY = Math.max(0, config.effectsY);
            config.heldItemScale = optionalClamp(config.heldItemScale, .75f, 2);
            config.effectsScale = optionalClamp(config.effectsScale, .75f, 2);
            config.heldItemOpacity = optionalClamp(config.heldItemOpacity, .25f, 1);
            config.effectsOpacity = optionalClamp(config.effectsOpacity, .25f, 1);
            config.zoomMultiplier = finiteClamp(config.zoomMultiplier, 2, VortexClient.MAX_ZOOM, 4);
            if (!ClientBindings.valid(config.freelookKey))
                config.freelookKey = com.mojang.blaze3d.platform.InputConstants.KEY_V;
            if (!ClientBindings.valid(config.menuKey)) config.menuKey = com.mojang.blaze3d.platform.InputConstants.KEY_RSHIFT;
            if (!ClientBindings.valid(config.zoomKey)) config.zoomKey = com.mojang.blaze3d.platform.InputConstants.KEY_C;
            if (!ClientBindings.valid(config.partyMarkerKey)) config.partyMarkerKey = com.mojang.blaze3d.platform.InputConstants.KEY_G;
            if (!ClientBindings.valid(config.waypointsKey)) config.waypointsKey = com.mojang.blaze3d.platform.InputConstants.KEY_B;
            if (config.partyServiceUrl == null || config.partyServiceUrl.length() > 240) config.partyServiceUrl = "";
            config.durabilityThreshold = Math.clamp(config.durabilityThreshold, 1, 30);
            config.freelookSensitivity = finiteClamp(config.freelookSensitivity, .25f, 2, 1);
            config.hudScale = finiteClamp(config.hudScale, 0.75f, 2, 1);
            config.hudOpacity = finiteClamp(config.hudOpacity, 0.25f, 1, 0.85f);
            config.fpsX = Math.max(0, config.fpsX);
            config.fpsY = Math.max(0, config.fpsY);
            config.keysX = Math.max(0, config.keysX);
            config.keysY = Math.max(0, config.keysY);
            config.armorX = config.armorX == -1 ? -1 : Math.max(0, config.armorX);
            config.armorY = config.armorY == -1 ? -1 : Math.max(0, config.armorY);
            config.coordinatesX = Math.max(0, config.coordinatesX);
            config.coordinatesY = Math.max(0, config.coordinatesY);
            config.sprintX = Math.max(0, config.sprintX);
            config.sprintY = Math.max(0, config.sprintY);
            config.fpsScale = optionalClamp(config.fpsScale, 0.75f, 2);
            config.keysScale = optionalClamp(config.keysScale, 0.75f, 2);
            config.armorScale = optionalClamp(config.armorScale, 0.75f, 2);
            config.sprintScale = optionalClamp(config.sprintScale, 0.75f, 2);
            config.coordinatesScale = optionalClamp(config.coordinatesScale, 0.75f, 2);
            config.fpsOpacity = optionalClamp(config.fpsOpacity, 0.25f, 1);
            config.keysOpacity = optionalClamp(config.keysOpacity, 0.25f, 1);
            config.armorOpacity = optionalClamp(config.armorOpacity, 0.25f, 1);
            config.sprintOpacity = optionalClamp(config.sprintOpacity, 0.25f, 1);
            config.coordinatesOpacity = optionalClamp(config.coordinatesOpacity, 0.25f, 1);
            config.pingX = Math.max(0, config.pingX); config.pingY = Math.max(0, config.pingY);
            config.speedX = Math.max(0, config.speedX); config.speedY = Math.max(0, config.speedY);
            config.clockX = Math.max(0, config.clockX); config.clockY = Math.max(0, config.clockY);
            config.tpsX = Math.max(0, config.tpsX); config.tpsY = Math.max(0, config.tpsY);
            config.pingScale = optionalClamp(config.pingScale, .75f, 2); config.speedScale = optionalClamp(config.speedScale, .75f, 2);
            config.clockScale = optionalClamp(config.clockScale, .75f, 2); config.tpsScale = optionalClamp(config.tpsScale, .75f, 2);
            config.pingOpacity = optionalClamp(config.pingOpacity, .25f, 1); config.speedOpacity = optionalClamp(config.speedOpacity, .25f, 1);
            config.clockOpacity = optionalClamp(config.clockOpacity, .25f, 1); config.tpsOpacity = optionalClamp(config.tpsOpacity, .25f, 1);
            config.blockInfoX = config.blockInfoX == -1 ? -1 : Math.max(0, config.blockInfoX);
            config.blockInfoY = Math.max(0, config.blockInfoY);
            config.blockInfoScale = optionalClamp(config.blockInfoScale, .75f, 2);
            config.blockInfoOpacity = optionalClamp(config.blockInfoOpacity, .25f, 1);
            return config;
        } catch (IOException | RuntimeException ex) {
            LOG.warn("Could not read Vortex Client settings. Using defaults; original file was not changed.", ex);
            return new ClientConfig();
        }
    }

    public boolean save() {
        Path path = FabricLoader.getInstance().getConfigDir().resolve("vortex-client.json");
        Path temporary = null;
        try {
            Files.createDirectories(path.getParent());
            temporary = Files.createTempFile(path.getParent(), "vortex-client-", ".tmp");
            Files.writeString(temporary, JSON.toJson(this));
            try { Files.move(temporary, path, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING); }
            catch (AtomicMoveNotSupportedException ex) { Files.move(temporary, path, StandardCopyOption.REPLACE_EXISTING); }
            return true;
        } catch (IOException ex) {
            LOG.error("Could not save Vortex Client settings", ex);
            return false;
        } finally {
            if (temporary != null) try { Files.deleteIfExists(temporary); } catch (IOException ignored) { }
        }
    }

    private static float finiteClamp(float value, float minimum, float maximum, float fallback) {
        return Float.isFinite(value) ? Math.clamp(value, minimum, maximum) : fallback;
    }
    private static float optionalClamp(float value, float min, float max) {
        return value == 0 || !Float.isFinite(value) ? 0 : Math.clamp(value, min, max);
    }
}
