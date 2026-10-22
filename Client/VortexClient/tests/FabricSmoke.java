import java.lang.reflect.Method;
import java.nio.file.Path;
import java.nio.file.Files;
import net.fabricmc.api.EnvType;
import net.fabricmc.loader.impl.launch.knot.Knot;

public class FabricSmoke {
    public static void main(String[] args) throws Exception {
        Knot knot = new Knot(EnvType.CLIENT);
        ClassLoader target = knot.init(new String[] { "--gameDir", args[0], "--version", "26.3" });
        for (String type : new String[] { "net.minecraft.client.Minecraft", "net.minecraft.client.Camera", "net.minecraft.client.gui.Hud", "net.minecraft.client.MouseHandler", "net.minecraft.client.ToggleKeyMapping", "net.minecraft.client.gui.screens.TitleScreen", "net.minecraft.client.gui.screens.PauseScreen", "net.minecraft.client.gui.Font", "net.minecraft.client.multiplayer.ClientPacketListener", "net.minecraft.client.resources.ClientPackSource", "net.minecraft.client.renderer.LightmapRenderStateExtractor", "net.minecraft.client.gui.font.FontManager" }) {
            Class<?> transformed = Class.forName(type, false, target);
            boolean injected = false;
            for (Method method : transformed.getDeclaredMethods()) if (method.getName().contains("vortex$")) injected = true;
            if (!injected) throw new AssertionError("Missing injected method in " + type);
            System.out.println("PASS Fabric applied mixin to " + type);
        }
        Class<?> glyphState = Class.forName("net.minecraft.client.renderer.state.gui.GlyphRenderState", false, target);
        if (java.util.Arrays.stream(glyphState.getDeclaredMethods()).noneMatch(m -> m.getName().contains("vortex$fontFilter")))
            throw new AssertionError("Menu font sampler hook was not applied");
        Class<?> sampling = Class.forName("dev.vortex.client.UiSampling", true, target);
        Class<?> filterMode = Class.forName("com.mojang.renderpearl.api.textures.FilterMode", true, target);
        Object nearest = filterMode.getField("NEAREST").get(null), linear = filterMode.getField("LINEAR").get(null);
        Method fontFilter = sampling.getMethod("fontFilter", String.class, filterMode);
        Class<?> stitcherType = Class.forName("net.minecraft.client.gui.font.GlyphStitcher", true, target);
        Class<?> textureManagerType = Class.forName("net.minecraft.client.renderer.texture.TextureManager", true, target);
        Class<?> samplerIdType = Class.forName("net.minecraft.resources.Identifier", true, target);
        Method textureName = stitcherType.getDeclaredMethod("textureName", int.class); textureName.setAccessible(true);
        for (String face : new String[] {"ui", "heading"}) {
            Object id = samplerIdType.getMethod("fromNamespaceAndPath", String.class, String.class).invoke(null, "vortex_client", face);
            Object stitcher = stitcherType.getConstructor(textureManagerType, samplerIdType).newInstance(null, id);
            String atlasLabel = textureName.invoke(stitcher, 0).toString();
            if (fontFilter.invoke(null, atlasLabel, nearest) != linear) throw new AssertionError("Real Vortex font atlas not smoothed: " + atlasLabel);
        }
        for (String label : new String[] {"minecraft:default/0", "minecraft:uniform/0", "other:ui/0", "vortex_client:ui_fake/0", "vortex_client:heading_fake/0", "vortex_client:textures/capes/vortex.png"})
            if (fontFilter.invoke(null, label, nearest) != nearest) throw new AssertionError("Sampler change escaped into vanilla/HUD/another mod");
        if (fontFilter.invoke(null, null, nearest) != nearest) throw new AssertionError("Null texture label");
        System.out.println("PASS Menu font sampler mixin, actual atlas names, linear filtering and strict vanilla/chat/HUD isolation");
        Class<?> transformedChat = Class.forName("net.minecraft.client.gui.components.ChatComponent", false, target);
        if (java.util.Arrays.stream(transformedChat.getDeclaredMethods()).noneMatch(m -> m.getName().contains("vortex$decorate")))
            throw new AssertionError("Chat display mixin was not applied");
        System.out.println("PASS Chat display mixin transforms Minecraft 26.3 without changing incoming message APIs");
        Class<?> entry = Class.forName("dev.vortex.client.VortexClient", true, target);
        entry.getMethod("onInitializeClient").invoke(entry.getConstructor().newInstance());
        Method adjust = entry.getMethod("adjustZoomStrength", float.class, double.class);
        float up = (float)adjust.invoke(null, 4f, 1.0);
        float down = (float)adjust.invoke(null, up, -1.0);
        if (up <= 4 || Math.abs(down - 4) > .001) throw new AssertionError("Scroll direction or symmetry");
        if ((float)adjust.invoke(null, 8f, 10.0) != 8 || (float)adjust.invoke(null, 2f, -10.0) != 2)
            throw new AssertionError("Zoom bounds");
        float fraction = (float)adjust.invoke(null, 4f, .1);
        if (fraction <= 4 || fraction >= up) throw new AssertionError("Trackpad precision");
        if ((float)adjust.invoke(null, 4f, Double.NaN) != 4) throw new AssertionError("Invalid input");
        System.out.println("PASS Scroll direction, limits, inverse scroll and fractional trackpad deltas");
        Class<?> screen = Class.forName("dev.vortex.client.VortexScreen", false, target);
        if (screen.getSuperclass().getName().equals("net.minecraft.client.gui.screens.Screen"))
            System.out.println("PASS Module screen links against Minecraft 26.3");
        Class<?> configType = Class.forName("dev.vortex.client.ClientConfig", true, target);
        Object current = entry.getField("config").get(null);
        CapeSmoke.run(target,Path.of(args[0]));
        Class<?> particleEngine = Class.forName("net.minecraft.client.particle.ParticleEngine", false, target);
        if (java.util.Arrays.stream(particleEngine.getDeclaredMethods()).anyMatch(m -> m.getName().contains("vortex$ambient")))
            throw new AssertionError("Removed particle limiter is still active");
        for (String removed : new String[] {"dev.vortex.client.AmbientParticleBudget", "dev.vortex.client.mixin.AmbientParticleMixin", "dev.vortex.client.PerformanceScreen"}) {
            try { Class.forName(removed, false, target); throw new AssertionError("Removed class is still packaged: " + removed); }
            catch (ClassNotFoundException expected) { }
        }
        if (configType.getField("performanceBackup").get(current) != null)
            throw new AssertionError("New settings must not contain a legacy graphics backup");
        Class<?> stateType = Class.forName("dev.vortex.client.PerformanceProfile$State", true, target);
        Class<?> profileType = Class.forName("dev.vortex.client.PerformanceProfile", true, target);
        Class<?> cloudType = Class.forName("net.minecraft.client.CloudStatus", true, target);
        Class<?> particleType = Class.forName("net.minecraft.server.level.ParticleStatus", true, target);
        var stateConstructor = stateType.getConstructor(int.class, int.class, double.class, int.class, cloudType, particleType, boolean.class);
        Object fancy = cloudType.getField("FANCY").get(null), allParticles = particleType.getField("ALL").get(null);
        Object beforeProfile = stateConstructor.newInstance(24, 12, 1d, 5, fancy, allParticles, true);
        Object lowProfile = stateConstructor.newInstance(8, 5, .75d, 0, cloudType.getField("OFF").get(null), allParticles, false);
        if (java.util.Arrays.stream(profileType.getDeclaredMethods()).anyMatch(m -> m.getName().equals("apply") || m.getName().equals("lowEnd")))
            throw new AssertionError("Removed graphics preset can still be applied");
        Object restored = profileType.getMethod("restore", stateType, stateType, stateType).invoke(null, lowProfile, lowProfile, beforeProfile);
        if (!restored.equals(beforeProfile)) throw new AssertionError("Performance profile does not restore originals");
        Object manualProfile = stateConstructor.newInstance(12, 5, .75d, 0, cloudType.getField("OFF").get(null), allParticles, false);
        Object restoredManual = profileType.getMethod("restore", stateType, stateType, stateType).invoke(null, manualProfile, lowProfile, beforeProfile);
        if ((int)stateType.getMethod("render").invoke(restoredManual) != 12) throw new AssertionError("Restore overwrote a manual distance change");
        Class<?> backupType = Class.forName("dev.vortex.client.PerformanceProfile$Backup", true, target);
        Object backup = backupType.getConstructor(stateType, stateType, boolean.class).newInstance(beforeProfile, lowProfile, false);
        var gson = Class.forName("com.google.gson.Gson", true, target).getConstructor().newInstance();
        String serialized = (String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, backup);
        Object profileReloaded = gson.getClass().getMethod("fromJson", String.class, Class.class).invoke(gson, serialized, backupType);
        if (!profileReloaded.equals(backup)) throw new AssertionError("Performance backup does not survive serialization");
        System.out.println("PASS Removed performance UI/preset/particle classes; restore-only legacy backup keeps originals and manual changes");
        Method menuLayout = screen.getMethod("menuLayout", int.class, int.class);
        for (int[] dimensions : new int[][] {{320, 240}, {430, 300}, {640, 360}, {854, 480}, {1920, 1080}}) {
            Object menu = menuLayout.invoke(null, dimensions[0], dimensions[1]);
            Class<?> geometry = menu.getClass();
            int x = (int)geometry.getMethod("x").invoke(menu), y = (int)geometry.getMethod("y").invoke(menu);
            int w = (int)geometry.getMethod("width").invoke(menu), h = (int)geometry.getMethod("height").invoke(menu);
            int columns = (int)geometry.getMethod("columns").invoke(menu), rows = (int)geometry.getMethod("rows").invoke(menu);
            int cardW = (int)geometry.getMethod("cardWidth").invoke(menu), cardH = (int)geometry.getMethod("cardHeight").invoke(menu);
            int content = (int)geometry.getMethod("contentWidth").invoke(menu);
            if (x < 0 || y < 0 || x + w > dimensions[0] || y + h > dimensions[1] ||
                columns * cardW + (columns - 1) * 8 > content || 112 + rows * cardH + (rows - 1) * 8 > h - 34)
                throw new AssertionError("Menu cards overlap the footer or leave the scaled window");
        }
        Class<?> moduleType = Class.forName("dev.vortex.client.ClientModule", true, target);
        ResourceToolsSmoke.run(target,java.nio.file.Path.of(args[0]));
        Object[] modules = moduleType.getEnumConstants();
        Method matches = moduleType.getMethod("matches", String.class, String.class);
        int hudCount = 0, gameplayCount = 0, enabledCount = 0;
        for (Object module : modules) {
            if ((boolean)matches.invoke(module, "", "HUD")) hudCount++;
            if ((boolean)matches.invoke(module, "", "Gameplay")) gameplayCount++;
            if ((boolean)matches.invoke(module, "", "Enabled")) enabledCount++;
        }
        if (hudCount != 12 || gameplayCount != 13 || enabledCount != 13 ||
            !(boolean)matches.invoke(modules[0], "  fPs  ", "HUD") ||
            (boolean)matches.invoke(modules[1], "Zoom", "HUD") ||
            (boolean)matches.invoke(modules[0], "no such module", "All Modules"))
            throw new AssertionError("Search / categories / enabled-only filter");
        if (!(boolean)matches.invoke(modules[4], "biome", "HUD") ||
            !(boolean)matches.invoke(modules[4], "coordinates", "Enabled") ||
            (boolean)matches.invoke(modules[4], "", "Gameplay"))
            throw new AssertionError("Coordinates search / categories");
        moduleType.getMethod("toggle").invoke(modules[4]);
        if ((boolean)matches.invoke(modules[4], "", "Enabled") || !(boolean)configType.getField("armorEnabled").get(current))
            throw new AssertionError("Coordinates toggle changed another module");
        moduleType.getMethod("toggle").invoke(modules[4]);
        moduleType.getMethod("toggle").invoke(modules[0]);
        if ((boolean)matches.invoke(modules[0], "", "Enabled") || !(boolean)configType.getField("armorEnabled").get(current))
            throw new AssertionError("Module toggle isolation / stale enabled filter");
        moduleType.getMethod("toggle").invoke(modules[0]);
        if (modules.length != 24) throw new AssertionError("New modules not registered");
        for (Object module : modules) if (module.toString().equals("KEYS") || module.toString().equals("HELD_ITEM") || module.toString().equals("CPS"))
            throw new AssertionError("Excluded module exposed");
        for (Object module : modules) {
            String iconName = module.toString().toLowerCase(java.util.Locale.ROOT);
            try (var stream = target.getResourceAsStream("assets/vortex_client/textures/ui/icons/" + iconName + ".png")) {
                if (stream == null) throw new AssertionError("Missing smooth module icon: " + iconName);
                var iconImage = javax.imageio.ImageIO.read(stream);
                if (iconImage.getWidth() != 96 || iconImage.getHeight() != 96) throw new AssertionError("Icon resolution");
                boolean softEdge = false;
                for (int ix=0;ix<96;ix++) for (int iy=0;iy<96;iy++) {
                    int alphaValue = iconImage.getRGB(ix,iy) >>> 24;
                    if (alphaValue > 0 && alphaValue < 255) softEdge = true;
                }
                if (!softEdge) throw new AssertionError("Icon has no antialiased edges: " + iconName);
            }
        }
        Class<?> scrollMath = Class.forName("dev.vortex.client.ModuleScroll", true, target);
        Method maximumOffset = scrollMath.getMethod("maximum", int.class, int.class, int.class, int.class);
        Method wheelOffset = scrollMath.getMethod("wheel", double.class, double.class, int.class);
        Method dragOffset = scrollMath.getMethod("drag", double.class, int.class, int.class);
        if ((int)maximumOffset.invoke(null, 14, 4, 96, 274) != 134 ||
            (double)wheelOffset.invoke(null, 0d, -0.5d, 134) != 18 ||
            (double)wheelOffset.invoke(null, 0d, -1000d, 134) != 134 ||
            (double)wheelOffset.invoke(null, 20d, Double.NaN, 134) != 20 ||
            (double)dragOffset.invoke(null, -100d, 274, 134) != 0 ||
            (double)dragOffset.invoke(null, 1000d, 274, 134) != 134)
            throw new AssertionError("Scroll wheel / trackpad / drag bounds");
        for (int[] dims : new int[][] {{320,240},{430,300},{640,360},{854,480},{1920,1080}}) {
            Object grid = menuLayout.invoke(null, dims[0], dims[1]);
            Class<?> gridType = grid.getClass();
            int cols = (int)gridType.getMethod("columns").invoke(grid);
            int cardHeight = (int)gridType.getMethod("cardHeight").invoke(grid);
            int viewport = (int)gridType.getMethod("height").invoke(grid) - 126;
            int maxOffset = (int)maximumOffset.invoke(null, 14, cols, cardHeight, viewport);
            int rows = (14 + cols - 1) / cols;
            if ((rows - 1) * (cardHeight + 8) + cardHeight - maxOffset > viewport)
                throw new AssertionError("Last module row is unreachable");
        }
        Class<?> bindings = Class.forName("dev.vortex.client.ClientBindings", true, target);
        Class<?> actionType = Class.forName("dev.vortex.client.ClientBindings$Action", true, target);
        Object[] bindingActions = actionType.getEnumConstants();
        Class<?> bindingInputs = Class.forName("com.mojang.blaze3d.platform.InputConstants", true, target);
        int cKey = bindingInputs.getField("KEY_C").getInt(null), f6Key = bindingInputs.getField("KEY_F6").getInt(null);
        Method assignBinding = bindings.getMethod("assign", actionType, int.class);
        if ((boolean)assignBinding.invoke(null, bindingActions[0], cKey) ||
            !(boolean)assignBinding.invoke(null, bindingActions[1], f6Key) ||
            !(boolean)assignBinding.invoke(null, bindingActions[2], -1) ||
            (boolean)assignBinding.invoke(null, bindingActions[0], 9999))
            throw new AssertionError("Free key assignment, conflict rejection or unbinding");
        if (!bindings.getMethod("label", int.class).invoke(null, -1).equals("Unbound"))
            throw new AssertionError("Unbound label");
        Class<?> bindingScreen = Class.forName("dev.vortex.client.KeybindScreen", true, target);
        Class<?> screenBase = Class.forName("net.minecraft.client.gui.screens.Screen", true, target);
        Class<?> bindingEvent = Class.forName("net.minecraft.client.input.KeyEvent", true, target);
        Object captureScreen = bindingScreen.getConstructor(screenBase, actionType).newInstance(null, bindingActions[1]);
        screenBase.getField("width").setInt(captureScreen, 320); screenBase.getField("height").setInt(captureScreen, 240);
        Object esc = bindingEvent.getConstructor(int.class,int.class,int.class).newInstance(bindingInputs.getField("KEY_ESCAPE").getInt(null),0,0);
        bindingScreen.getMethod("keyPressed", bindingEvent).invoke(captureScreen, esc);
        if ((boolean)bindingScreen.getMethod("capturing").invoke(captureScreen)) throw new AssertionError("Escape did not cancel binding capture");
        Object freeCapture = bindingScreen.getConstructor(screenBase, actionType).newInstance(null, bindingActions[2]);
        screenBase.getField("width").setInt(freeCapture, 320); screenBase.getField("height").setInt(freeCapture, 240);
        int f7Key = bindingInputs.getField("KEY_F7").getInt(null);
        Object f7Event = bindingEvent.getConstructor(int.class,int.class,int.class).newInstance(f7Key,0,0);
        bindingScreen.getMethod("keyPressed",bindingEvent).invoke(freeCapture,f7Event);
        if ((int)bindings.getMethod("key",actionType).invoke(null,bindingActions[2]) != f7Key ||
            (boolean)bindingScreen.getMethod("capturing").invoke(freeCapture)) throw new AssertionError("Captured keyboard event was not saved");
        Object menuCapture = bindingScreen.getConstructor(screenBase, actionType).newInstance(null, bindingActions[0]);
        screenBase.getField("width").setInt(menuCapture, 320); screenBase.getField("height").setInt(menuCapture, 240);
        bindingScreen.getMethod("keyPressed",bindingEvent).invoke(menuCapture,bindingEvent.getConstructor(int.class,int.class,int.class).newInstance(f6Key,0,0));
        if (!(boolean)bindingScreen.getMethod("capturing").invoke(menuCapture)) throw new AssertionError("Conflict should keep binding capture open");
        bindings.getMethod("reset").invoke(null);
        Class<?> warnings = Class.forName("dev.vortex.client.DurabilityWarnings", true, target);
        Method lowDurability = warnings.getMethod("low", int.class,int.class,int.class);
        if (!(boolean)lowDurability.invoke(null, 100,90,10) || (boolean)lowDurability.invoke(null,100,89,10) ||
            (boolean)lowDurability.invoke(null,0,0,10) || (boolean)lowDurability.invoke(null,Integer.MAX_VALUE,0,10))
            throw new AssertionError("Low durability threshold / non-damageable / overflow");
        Class<?> gateType = Class.forName("dev.vortex.client.DurabilityWarnings$Gate", true, target);
        Object gate = gateType.getConstructor().newInstance();
        Method checkGate = gateType.getMethod("check",int.class,Object.class,int.class,int.class,int.class,long.class);
        if (!(boolean)checkGate.invoke(gate,0,"helmet",100,95,10,0L) ||
            (boolean)checkGate.invoke(gate,0,"helmet",100,96,10,40000L) ||
            (boolean)checkGate.invoke(gate,1,"pickaxe",100,95,10,5000L) ||
            !(boolean)checkGate.invoke(gate,1,"pickaxe",100,95,10,30000L))
            throw new AssertionError("Durability repeats / global cooldown / pending low item");
        checkGate.invoke(gate,0,"helmet",100,80,10,40000L);
        if (!(boolean)checkGate.invoke(gate,0,"helmet",100,95,10,60000L))
            throw new AssertionError("Repaired durability warning was not re-armed");
        System.out.println("PASS Continuous scroll limits, key capture/cancel/conflicts and anti-spam durability warnings");
        Class<?> roundedShapes = Class.forName("dev.vortex.client.RoundedShapes", true, target);
        Method drawCost = roundedShapes.getMethod("drawOperations", int.class, int.class, int.class);
        for (int w : new int[] {0, 1, 2, 10, 26, 160, 800}) for (int h : new int[] {0, 1, 2, 10, 14, 96, 400})
            for (int r : new int[] {-1, 0, 1, 5, 7, 12, 1000}) {
                int cost = (int)drawCost.invoke(null, w, h, r);
                if (cost < 0 || cost > 7 || (w == 0 || h == 0) && cost != 0)
                    throw new AssertionError("Unbounded rounded shape geometry");
            }
        int cardCost = 0;
        for (int[] shape : new int[][] {{160,96,8},{158,94,7},{25,25,6},{18,20,4},{26,14,7},{10,10,5}})
            cardCost += (int)drawCost.invoke(null, shape[0],shape[1],shape[2]);
        if (cardCost * 8 != 296) throw new AssertionError("Eight-card geometry budget regressed");
        try (var stream = target.getResourceAsStream("assets/vortex_client/textures/ui/corners.png")) {
            var atlas = javax.imageio.ImageIO.read(stream);
            if (atlas.getWidth()!=512 || atlas.getHeight()!=384) throw new AssertionError("Corner atlas dimensions");
            for (int r=1;r<=12;r++) {
                int originX=(r-1)%4*128+4, originY=(r-1)/4*128+4, size=r*8;
                if ((atlas.getRGB(originX,originY)>>>24)!=0 ||
                    (atlas.getRGB(originX+size/2,originY+size/2)>>>24)!=255)
                    throw new AssertionError("Corner atlas transparency/center");
                boolean smooth = false;
                for (int ix=originX;ix<originX+size;ix++) for(int iy=originY;iy<originY+size;iy++){
                    int alphaValue=atlas.getRGB(ix,iy)>>>24;
                    if(alphaValue>0 && alphaValue<255) smooth=true;
                }
                if (!smooth) throw new AssertionError("Missing smooth corner edge");
            }
        }
        System.out.println("PASS Rounded atlas masks, degenerate sizes and 296-quad eight-card geometry budget");
        Class<?> lightingType = Class.forName("net.minecraft.client.renderer.state.LightmapRenderState", true, target);
        Object lighting = lightingType.getConstructor().newInstance();
        lightingType.getField("brightness").setFloat(lighting, .4f);
        lightingType.getField("nightVisionEffectIntensity").setFloat(lighting, .2f);
        Class<?> brightness = Class.forName("dev.vortex.client.Fullbright", true, target);
        brightness.getMethod("apply", lightingType, boolean.class).invoke(null, lighting, false);
        if (lightingType.getField("brightness").getFloat(lighting) != .4f) throw new AssertionError("Disabled Fullbright changed vanilla");
        brightness.getMethod("apply", lightingType, boolean.class).invoke(null, lighting, true);
        if (lightingType.getField("brightness").getFloat(lighting) != 1 ||
            lightingType.getField("nightVisionEffectIntensity").getFloat(lighting) != 1 ||
            !lightingType.getField("needsUpdate").getBoolean(lighting)) throw new AssertionError("Fullbright render state");
        Class<?> blockInfo = Class.forName("dev.vortex.client.BlockInfo", true, target);
        if (!blockInfo.getMethod("displayName", String.class).invoke(null, "polished_deepslate").equals("Polished Deepslate"))
            throw new AssertionError("English block name");
        Method blockReadout = blockInfo.getMethod("readout", Class.forName("net.minecraft.client.Minecraft", true, target), boolean.class);
        if (blockReadout.invoke(null, null, false) != null || blockReadout.invoke(null, null, true) == null)
            throw new AssertionError("Block Info editor preview leaked into gameplay");
        System.out.println("PASS Smooth icon assets, Fullbright render-only state and Block Info preview isolation");
        Class<?> freelook = Class.forName("dev.vortex.client.Freelook", true, target);
        Class<?> viewType = Class.forName("dev.vortex.client.Freelook$View", true, target);
        Object view = viewType.getConstructor().newInstance();
        Method beginView = viewType.getMethod("begin", float.class, float.class);
        Method turnView = viewType.getMethod("turn", double.class, double.class, float.class);
        beginView.invoke(view, 170f, 30f);
        turnView.invoke(view, 100d, 100d, 1f);
        if ((float)viewType.getMethod("yaw").invoke(view) != -175 || (float)viewType.getMethod("pitch").invoke(view) != 45)
            throw new AssertionError("Freelook sensitivity / wrapped yaw");
        turnView.invoke(view, 0d, 10000d, 1f);
        if ((float)viewType.getMethod("pitch").invoke(view) != 90) throw new AssertionError("Freelook pitch clamp");
        turnView.invoke(view, Double.NaN, 0d, 1f);
        if ((float)viewType.getMethod("yaw").invoke(view) != -175) throw new AssertionError("Invalid freelook delta");
        viewType.getMethod("end").invoke(view);
        turnView.invoke(view, 100d, 100d, 1f);
        if ((boolean)viewType.getMethod("active").invoke(view) || (float)viewType.getMethod("yaw").invoke(view) != -175)
            throw new AssertionError("Inactive view must not turn");
        Class<?> perspectiveType = Class.forName("net.minecraft.client.CameraType", true, target);
        Object firstPerson = perspectiveType.getField("FIRST_PERSON").get(null);
        Object backPerson = perspectiveType.getField("THIRD_PERSON_BACK").get(null);
        Object frontPerson = perspectiveType.getField("THIRD_PERSON_FRONT").get(null);
        Method restoreView = freelook.getMethod("restoreCamera", perspectiveType, perspectiveType, boolean.class);
        if (restoreView.invoke(null, firstPerson, backPerson, true) != firstPerson ||
            restoreView.invoke(null, frontPerson, backPerson, true) != frontPerson ||
            restoreView.invoke(null, firstPerson, frontPerson, true) != frontPerson ||
            restoreView.invoke(null, firstPerson, backPerson, false) != backPerson)
            throw new AssertionError("Freelook restore / user's F5 choice");
        if ((float)freelook.getMethod("yaw", float.class).invoke(null, 12f) != 12 ||
            (float)freelook.getMethod("pitch", float.class).invoke(null, -30f) != -30)
            throw new AssertionError("Disabled freelook must preserve vanilla angles");
        System.out.println("PASS Freelook detached angles, sensitivity, pitch limits, deactivation and perspective restoration");
        Class<?> metrics = Class.forName("dev.vortex.client.MetricsHud", true, target);
        Method horizontalSpeed = metrics.getMethod("horizontalSpeed", double.class, double.class);
        if (Math.abs((double)horizontalSpeed.invoke(null, .15, .2) - 5) > .0001 ||
            (double)horizontalSpeed.invoke(null, 100, 100) != 0 ||
            (double)horizontalSpeed.invoke(null, Double.NaN, 0) != 0)
            throw new AssertionError("Horizontal speed / teleport rejection");
        Class<?> estimateType = Class.forName("dev.vortex.client.MetricsHud$TpsEstimate", true, target);
        Object estimate = estimateType.getConstructor().newInstance();
        Method sampleTick = estimateType.getMethod("sample", long.class, long.class);
        Method tpsValue = estimateType.getMethod("value", long.class);
        sampleTick.invoke(estimate, 100L, 1_000_000_000L);
        if (!Double.isNaN((double)tpsValue.invoke(estimate, 1_000_000_000L))) throw new AssertionError("TPS warmup");
        sampleTick.invoke(estimate, 120L, 2_000_000_000L);
        if ((double)tpsValue.invoke(estimate, 2_000_000_000L) != 20) throw new AssertionError("20 TPS estimate");
        sampleTick.invoke(estimate, 140L, 4_000_000_000L);
        if (Math.abs((double)tpsValue.invoke(estimate, 4_000_000_000L) - 40d/3) > .0001) throw new AssertionError("Slow server estimate");
        if (!Double.isNaN((double)tpsValue.invoke(estimate, 15_000_000_000L))) throw new AssertionError("Stale TPS estimate");
        sampleTick.invoke(estimate, 10L, 16_000_000_000L);
        if (!Double.isNaN((double)tpsValue.invoke(estimate, 16_000_000_000L))) throw new AssertionError("World time reset");
        for (String name : new String[] {"manrope-regular.ttf", "manrope-semibold.ttf"}) {
            try (var stream = target.getResourceAsStream("assets/vortex_client/font/" + name)) {
                if (stream == null) throw new AssertionError("Font not packaged: " + name);
                var uiFont = java.awt.Font.createFont(java.awt.Font.TRUETYPE_FONT, stream);
                if (!uiFont.canDisplay('A') || !uiFont.canDisplay('Я')) throw new AssertionError("UI font missing Latin/Cyrillic");
            }
        }
        Class<?> packs = Class.forName("net.minecraft.server.packs.repository.Pack", true, target);
        Object uiPack = Class.forName("dev.vortex.client.UiResources", true, target).getMethod("createPack").invoke(null);
        if (!(boolean)packs.getMethod("isRequired").invoke(uiPack)) throw new AssertionError("UI resources must be enabled");
        Class<?> repositorySource = Class.forName("net.minecraft.server.packs.repository.RepositorySource", true, target);
        Class.forName("net.minecraft.SharedConstants", true, target).getMethod("tryDetectVersion").invoke(null);
        Class<?> validatorType = Class.forName("net.minecraft.world.level.validation.DirectoryValidator", true, target);
        Object validator = validatorType.getConstructor(java.nio.file.PathMatcher.class).newInstance((java.nio.file.PathMatcher)path -> false);
        Object source = Class.forName("net.minecraft.client.resources.ClientPackSource", true, target)
            .getConstructor(Path.class, validatorType).newInstance(Path.of(args[0]), validator);
        Object sourceArray = java.lang.reflect.Array.newInstance(repositorySource, 1);
        java.lang.reflect.Array.set(sourceArray, 0, source);
        Class<?> repositoryType = Class.forName("net.minecraft.server.packs.repository.PackRepository", true, target);
        Object repository = repositoryType.getConstructor(sourceArray.getClass()).newInstance(sourceArray);
        repositoryType.getMethod("reload").invoke(repository);
        repositoryType.getMethod("reload").invoke(repository);
        if (!((java.util.Collection<?>)repositoryType.getMethod("getSelectedIds").invoke(repository)).contains("vortex_client_ui") ||
            ((java.util.Collection<?>)repositoryType.getMethod("getAvailableIds").invoke(repository)).stream().filter("vortex_client_ui"::equals).count() != 1)
            throw new AssertionError("UI pack discovery/required selection/reload");
        Class<?> packType = Class.forName("net.minecraft.server.packs.PackType", true, target);
        Object clientResources = packType.getField("CLIENT_RESOURCES").get(null);
        Class<?> resourceManagerType = Class.forName("net.minecraft.server.packs.resources.MultiPackResourceManager", true, target);
        Object resourceManager = resourceManagerType.getConstructor(packType, java.util.List.class).newInstance(clientResources,
            repositoryType.getMethod("openAllSelected").invoke(repository));
        Class<?> identifierType = Class.forName("net.minecraft.resources.Identifier", true, target);
        for (String path : new String[] {"font/ui.json", "font/heading.json", "font/manrope-regular.ttf", "font/manrope-semibold.ttf", "textures/ui/corners.png"}) {
            Object identifier = identifierType.getMethod("fromNamespaceAndPath", String.class, String.class).invoke(null, "vortex_client", path);
            if (((java.util.Optional<?>)resourceManagerType.getMethod("getResource", identifierType).invoke(resourceManager, identifier)).isEmpty())
                throw new AssertionError("Minecraft resource manager cannot see bundled font: " + path);
        }
        System.out.println("PASS Required UI pack is discovered, selected and readable through Minecraft resource manager");
        Class<?> definition = Class.forName("net.minecraft.client.gui.font.providers.TrueTypeGlyphProviderDefinition", true, target);
        Class<?> mapCodecType = Class.forName("com.mojang.serialization.MapCodec", true, target);
        Object fontCodec = mapCodecType.getMethod("codec").invoke(definition.getField("CODEC").get(null));
        Class<?> codecType = Class.forName("com.mojang.serialization.Codec", true, target);
        Class<?> opsType = Class.forName("com.mojang.serialization.DynamicOps", true, target);
        Object jsonOps = Class.forName("com.mojang.serialization.JsonOps", true, target).getField("INSTANCE").get(null);
        Class<?> parserType = Class.forName("com.google.gson.JsonParser", true, target);
        Class<?> jsonElementType = Class.forName("com.google.gson.JsonElement", true, target);
        Class<?> jsonObjectType = Class.forName("com.google.gson.JsonObject", true, target);
        Class<?> jsonArrayType = Class.forName("com.google.gson.JsonArray", true, target);
        for (String name : new String[] {"ui", "heading"}) {
            try (var stream = target.getResourceAsStream("assets/vortex_client/font/" + name + ".json")) {
                Object json = parserType.getMethod("parseString", String.class).invoke(null, new String(stream.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8));
                Object object = jsonElementType.getMethod("getAsJsonObject").invoke(json);
                Object providers = jsonObjectType.getMethod("getAsJsonArray", String.class).invoke(object, "providers");
                Object first = jsonArrayType.getMethod("get", int.class).invoke(providers, 0);
                Object result = codecType.getMethod("parse", opsType, Object.class).invoke(fontCodec, jsonOps, first);
                Object parsed = Class.forName("com.mojang.serialization.DataResult", true, target).getMethod("getOrThrow").invoke(result);
                Class<?> managerType = Class.forName("net.minecraft.server.packs.resources.ResourceManager", true, target);
                Object resources = resourceManager;
                Method loadFont = definition.getDeclaredMethod("load", managerType); loadFont.setAccessible(true);
                Object provider = loadFont.invoke(parsed, resources);
                Class<?> providerType = Class.forName("com.mojang.blaze3d.font.GlyphProvider", true, target);
                if (providerType.getMethod("getGlyph", int.class).invoke(provider, (int)'A') == null)
                    throw new AssertionError("Native Minecraft font provider missing glyph");
                providerType.getMethod("close").invoke(provider);
            }
        }
        resourceManagerType.getMethod("close").invoke(resourceManager);
        Class<?> uiFonts = Class.forName("dev.vortex.client.UiFonts", true, target);
        Class<?> fontProvider = Class.forName("net.minecraft.client.gui.Font$Provider", true, target);
        Class<?> glyphSource = Class.forName("net.minecraft.client.gui.GlyphSource", true, target);
        Object missingGlyphs = java.lang.reflect.Proxy.newProxyInstance(target, new Class<?>[]{glyphSource}, (p, m, a) -> null);
        Object fallbackGlyphs = java.lang.reflect.Proxy.newProxyInstance(target, new Class<?>[]{glyphSource}, (p, m, a) -> null);
        Class<?> fontDescription = Class.forName("net.minecraft.network.chat.FontDescription", true, target);
        Object defaultDescription = fontDescription.getField("DEFAULT").get(null);
        Object provider = java.lang.reflect.Proxy.newProxyInstance(target, new Class<?>[]{fontProvider}, (p, m, a) ->
            m.getName().equals("glyphs") ? a[0].equals(defaultDescription) ? fallbackGlyphs : missingGlyphs : null);
        Class<?> resourceDescription = Class.forName("net.minecraft.network.chat.FontDescription$Resource", true, target);
        Object uiIdentifier = identifierType.getMethod("fromNamespaceAndPath", String.class, String.class).invoke(null, "vortex_client", "ui");
        Object requestedFont = resourceDescription.getConstructor(identifierType).newInstance(uiIdentifier);
        if (uiFonts.getMethod("resolve", fontProvider, resourceDescription).invoke(null, provider, requestedFont) != fallbackGlyphs)
            throw new AssertionError("Missing UI resource fallback should keep text readable");
        Class<?> cachedType = Class.forName("dev.vortex.client.UiFonts$CachedProvider", true, target);
        int[] resolutions = {0}; boolean[] available = {false};
        Object realGlyphs = java.lang.reflect.Proxy.newProxyInstance(target, new Class<?>[]{glyphSource}, (p,m,a)->null);
        Object monitored = java.lang.reflect.Proxy.newProxyInstance(target, new Class<?>[]{fontProvider}, (p,m,a)->{
            if(!m.getName().equals("glyphs"))return null;
            resolutions[0]++;
            if(a[0].equals(defaultDescription)) return fallbackGlyphs;
            return available[0] && a[0].equals(requestedFont) ? realGlyphs : missingGlyphs;
        });
        Object cachedProvider = cachedType.getConstructor(fontProvider, resourceDescription).newInstance(monitored, requestedFont);
        Method cachedGlyphs = cachedType.getMethod("glyphs", fontDescription);
        for(int i=0;i<2000;i++)
            if(cachedGlyphs.invoke(cachedProvider, defaultDescription)!=fallbackGlyphs) throw new AssertionError("Cached fallback");
        if(resolutions[0]!=3)throw new AssertionError("Font fallback checked per glyph: "+resolutions[0]);
        available[0]=true; uiFonts.getMethod("invalidate").invoke(null);
        for(int i=0;i<2000;i++)
            if(cachedGlyphs.invoke(cachedProvider, defaultDescription)!=realGlyphs)throw new AssertionError("Font reload retained stale source");
        if(resolutions[0]!=5)throw new AssertionError("Reloaded font cache checked per glyph");
        System.out.println("PASS 4000 glyph requests resolve fonts only twice, including fallback and reload");
        System.out.println("PASS Optional metrics, real smooth font assets/codecs, excluded Keys/CPS and passive TPS estimation");
        System.out.println("PASS Responsive menu geometry, case-insensitive search, categories and live enabled filtering");
        Class<?> chrome = Class.forName("dev.vortex.client.VortexMenuChrome", true, target);
        var uniqueMenuIcons = new java.util.HashSet<String>();
        var uniqueIconBytes = new java.util.HashSet<String>();
        for (String actionKey : new String[] {"menu.options", "options.accessibility", "options.worldOptions.button", "options.language", "menu.online", "menu.shareToLan", "menu.quit", "gui.stats", "gui.advancements", "menu.sendFeedback", "menu.reportBugs", "menu.playerReporting", "gui.socialInteractions", "title.credits", "menu.resetdemo"}) {
            String icon = (String)chrome.getMethod("shortcutIcon", String.class).invoke(null, actionKey);
            if (!uniqueMenuIcons.add(icon) || icon.equals("help")) throw new AssertionError("Repeated placeholder icon: " + actionKey);
            try (var data = target.getResourceAsStream("assets/vortex_client/textures/ui/icons/" + icon + ".png")) {
                if (data == null || !uniqueIconBytes.add(java.util.HexFormat.of().formatHex(java.security.MessageDigest.getInstance("SHA-256").digest(data.readAllBytes()))))
                    throw new AssertionError("Missing/repeated icon artwork: " + icon);
            }
            try (var data = target.getResourceAsStream("assets/vortex_client/textures/ui/icons/" + icon + ".png.mcmeta")) {
                if (data == null || !new String(data.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8).contains("\"blur\":true"))
                    throw new AssertionError("Menu icon lacks smooth filtering: " + icon);
            }
        }
        System.out.println("PASS Fifteen distinct action icons: unique artwork, no generic placeholders and smooth texture metadata");
        Method chromeLayout = chrome.getMethod("layout", int.class, int.class, int.class);
        for (int[] dimensions : new int[][] {{320, 240}, {430, 300}, {640, 360}, {854, 480}, {1920, 1080}})
            for (boolean paused : new boolean[] {false, true})
            for (int count : new int[] {6, 9, 12, 16}) {
                Object panel = chrome.getMethod("layout", int.class, int.class, int.class, boolean.class).invoke(null, dimensions[0], dimensions[1], count, paused);
                Class<?> panelType = panel.getClass();
                int px = (int)panelType.getMethod("x").invoke(panel), py = (int)panelType.getMethod("y").invoke(panel);
                int pw = (int)panelType.getMethod("width").invoke(panel), ph = (int)panelType.getMethod("height").invoke(panel);
                if (px < 0 || py < 0 || px + pw > dimensions[0] || py + ph > dimensions[1])
                    throw new AssertionError("Title/pause panel leaves GUI-scaled window");
                for (int i = 0; i < count; i++) {
                    int bw = (int)panelType.getMethod("widthFor", int.class).invoke(panel, i);
                    int bh = (int)panelType.getMethod("heightFor", int.class).invoke(panel, i);
                    int bx = (int)panelType.getMethod("buttonX", int.class).invoke(panel, i);
                    int by = (int)panelType.getMethod("buttonY", int.class).invoke(panel, i);
                    if (bw < 24 || bh < 18 || bx < px || by < py || bx + bw > px + pw || by + bh > py + ph - 14)
                        throw new AssertionError("Title/pause button clips its panel");
                    int primarySlots = (int)panelType.getMethod("primaryCount").invoke(panel);
                    if (i >= primarySlots && bw < 64) throw new AssertionError("Utility actions regressed to unlabeled icon-sized targets");
                    for (int j = 0; j < i; j++) {
                        int ox = (int)panelType.getMethod("buttonX", int.class).invoke(panel, j);
                        int oy = (int)panelType.getMethod("buttonY", int.class).invoke(panel, j);
                        int ow = (int)panelType.getMethod("widthFor", int.class).invoke(panel, j);
                        int oh = (int)panelType.getMethod("heightFor", int.class).invoke(panel, j);
                        if (bx < ox + ow && bx + bw > ox && by < oy + oh && by + bh > oy)
                            throw new AssertionError("Menu actions overlap");
                    }
                }
                int primary = (int)panelType.getMethod("primaryCount").invoke(panel);
                int header = (int)panelType.getMethod("header").invoke(panel);
                int mainHeight = (int)panelType.getMethod("buttonHeight").invoke(panel);
                int gap = (int)panelType.getMethod("gap").invoke(panel);
                int pairBottom = py + header + primary * (mainHeight + gap) + 22;
                int dockY = (int)panelType.getMethod("buttonY", int.class).invoke(panel, primary);
                if (pairBottom + 4 > dockY) throw new AssertionError("Central Modules/Capes actions overlap bottom dock");
            }
        Method pauseLayout = chrome.getMethod("layout", int.class, int.class, int.class, boolean.class);
        Object titleLayout = chromeLayout.invoke(null, 854, 480, 9);
        Object pausePanel = pauseLayout.invoke(null, 854, 480, 12, true);
        if ((int)titleLayout.getClass().getMethod("primaryCount").invoke(titleLayout) != 2 ||
            (int)pausePanel.getClass().getMethod("primaryCount").invoke(pausePanel) != 1 ||
            (int)titleLayout.getClass().getMethod("widthFor", int.class).invoke(titleLayout, 0) <=
            (int)titleLayout.getClass().getMethod("widthFor", int.class).invoke(titleLayout, 2))
            throw new AssertionError("Main/pause primary buttons are not separated from compact utility actions");
        for (String vanillaScreen : new String[] {"net.minecraft.client.gui.screens.inventory.InventoryScreen", "net.minecraft.client.gui.screens.options.OptionsScreen"})
            for (Method method : Class.forName(vanillaScreen, false, target).getDeclaredMethods())
                if (method.getName().contains("vortex$")) throw new AssertionError("Menu restyle escaped into " + vanillaScreen);
        System.out.println("PASS Main/pause menu geometry at multiple GUI scales; inventory and options remain unmodified");
        Class<?> home = Class.forName("dev.vortex.client.VortexHomeScreen", true, target);
        home.getDeclaredMethods();
        Method homeLayout = home.getMethod("layout", int.class, int.class);
        for (int[] dimensions : new int[][] {{320, 240}, {430, 300}, {640, 360}, {854, 480}, {1920, 1080}}) {
            Object hub = homeLayout.invoke(null, dimensions[0], dimensions[1]);
            Class<?> geometry = hub.getClass();
            int hx = (int)geometry.getMethod("x").invoke(hub), hy = (int)geometry.getMethod("y").invoke(hub);
            int hw = (int)geometry.getMethod("width").invoke(hub), bh = (int)geometry.getMethod("buttonHeight").invoke(hub);
            int backY = (int)geometry.getMethod("backY").invoke(hub);
            if (hx < 0 || hx + hw > dimensions[0] || hy - 60 < 0 || backY + bh > dimensions[1] - 14)
                throw new AssertionError("Labeled hub clips at GUI scale");
            for (int i = 0; i < 6; i++) {
                int bx = (int)geometry.getMethod("buttonX", int.class).invoke(hub, i);
                int by = (int)geometry.getMethod("buttonY", int.class).invoke(hub, i);
                int bw = (int)geometry.getMethod("columnWidth").invoke(hub);
                if (bw < 130 || bx < hx || bx + bw > hx + hw || by + bh >= backY)
                    throw new AssertionError("Hub text actions clip or overlap Back");
            }
        }
        for (String icon : new String[] {"brand", "modules", "hud", "keybinds", "globe", "profile", "exit", "back"})
            if (home.getClassLoader().getResource("assets/vortex_client/textures/ui/icons/" + icon + ".png") == null)
                throw new AssertionError("Missing dock icon " + icon);
        if (home.getMethod("isInGameUi").getDeclaringClass() != home ||
            home.getMethod("extractBackground", Class.forName("net.minecraft.client.gui.GuiGraphicsExtractor", false, target), int.class, int.class, float.class).getDeclaringClass() != home)
            throw new AssertionError("Hub must explicitly preserve the real HUD and avoid vanilla blur");
        System.out.println("PASS Labeled Vortex hub, readable text-action geometry and explicit HUD-preserving background");
        Class<?> partyScreen = Class.forName("dev.vortex.client.PartyScreen", true, target);
        partyScreen.getDeclaredMethods();
        for (int[] dimensions : new int[][] {{320, 240}, {430, 300}, {640, 360}, {854, 480}}) {
            Object layout = partyScreen.getMethod("layout", int.class, int.class).invoke(null, dimensions[0], dimensions[1]);
            Class<?> shape = layout.getClass();
            int px = (int)shape.getMethod("x").invoke(layout), py = (int)shape.getMethod("y").invoke(layout);
            if (px < 0 || py < 0 || px + (int)shape.getMethod("width").invoke(layout) > dimensions[0] || py + (int)shape.getMethod("height").invoke(layout) > dimensions[1])
                throw new AssertionError("Party panel leaves GUI-scaled window");
        }
        Class<?> party = Class.forName("dev.vortex.client.PartyFeature", true, target);
        Method serverKey = party.getMethod("serverKey", String.class);
        String key = (String)serverKey.invoke(null, "EXAMPLE.COM");
        if (!key.equals(serverKey.invoke(null, "example.com:25565")) || key.equals(serverKey.invoke(null, "other.example"))) throw new AssertionError("Party server normalization");
        Class<?> pointType = Class.forName("dev.vortex.client.PartyConnection$Marker", true, target);
        Object point = pointType.getConstructors()[0].newInstance("id", "Friend", key, "minecraft:overworld", 1, 64, 2, "HERE", "2026-01-01T00:01:00Z");
        Method visible = party.getMethod("visible", pointType, String.class, String.class, java.time.Instant.class);
        var beforeExpiry = java.time.Instant.parse("2026-01-01T00:00:30Z");
        if (!(boolean)visible.invoke(null, point, key, "minecraft:overworld", beforeExpiry) ||
            (boolean)visible.invoke(null, point, "other", "minecraft:overworld", beforeExpiry) ||
            (boolean)visible.invoke(null, point, key, "minecraft:the_nether", beforeExpiry) ||
            (boolean)visible.invoke(null, point, key, "minecraft:overworld", java.time.Instant.parse("2026-01-01T00:01:00Z")))
            throw new AssertionError("Party marker crosses server/dimension/expiry boundary");
        if ((boolean)party.getMethod("connected").invoke(null) || (boolean)party.getMethod("busy").invoke(null) ||
            !configType.getField("partyServiceUrl").get(current).equals("")) throw new AssertionError("Party must start offline and opt-in");
        System.out.println("PASS Party screen layout, offline default, server hashing and server/dimension/expiry marker isolation");
        Method toggle = entry.getMethod("useToggleSprint", String.class, boolean.class);
        if ((boolean)toggle.invoke(null, "key.sprint", false)) throw new AssertionError("Sprint must default OFF");
        configType.getField("toggleSprintEnabled").set(current, true);
        if (!(boolean)toggle.invoke(null, "key.sprint", false) || (boolean)toggle.invoke(null, "key.sneak", false) ||
            !(boolean)toggle.invoke(null, "key.sneak", true)) throw new AssertionError("Sprint isolation / vanilla option");
        configType.getField("toggleSprintEnabled").set(current, false);
        if ((boolean)toggle.invoke(null, "key.sprint", false)) throw new AssertionError("Sprint disable");
        System.out.println("PASS Sprint defaults, sprint-only override and vanilla option preservation");
        Class<?> hud = Class.forName("dev.vortex.client.HudModules", true, target);
        Method armorDurability = hud.getMethod("armorDurability", int.class, int.class);
        if (!armorDurability.invoke(null, 363, 63).equals("300") ||
            !armorDurability.invoke(null, 363, 0).equals("363") ||
            !armorDurability.invoke(null, 363, 999).equals("0") ||
            !armorDurability.invoke(null, 363, -1).equals("363") ||
            !armorDurability.invoke(null, 0, 0).equals(""))
            throw new AssertionError("Armor durability formatting / bounds / non-damageable item");
        Method barWidth = hud.getMethod("armorBarWidth", int.class, int.class);
        Method barColor = hud.getMethod("armorBarColor", int.class, int.class);
        if ((int)barWidth.invoke(null, 100, 0) != 16 || (int)barWidth.invoke(null, 100, 50) != 8 ||
            (int)barWidth.invoke(null, 100, 100) != 0 || (int)barWidth.invoke(null, 100, -1) != 16 ||
            (int)barWidth.invoke(null, 100, 999) != 0 || (int)barWidth.invoke(null, 0, 0) != 0 ||
            (int)barWidth.invoke(null, Integer.MAX_VALUE, 0) != 16 ||
            (int)barColor.invoke(null, 100, 0) != 0x55FF55 || (int)barColor.invoke(null, 100, 50) != 0xFFFF55 ||
            (int)barColor.invoke(null, 100, 100) != 0xFF5555)
            throw new AssertionError("Durability bar limits / green-yellow-red colors");
        Method armorLayout = hud.getMethod("armorLayout", boolean.class, int[].class);
        Object horizontal = armorLayout.invoke(null, true, new int[] {18, 18, 18, 18});
        Class<?> armorGeometry = horizontal.getClass();
        int lastX = -1;
        for (int slot = 0; slot < 4; slot++) {
            int slotX = (int)armorGeometry.getMethod("iconX", int.class).invoke(horizontal, slot);
            int slotY = (int)armorGeometry.getMethod("iconY", int.class).invoke(horizontal, slot);
            if (slotX <= lastX || slotY != 0 || slotX + 16 > (int)armorGeometry.getMethod("width").invoke(horizontal))
                throw new AssertionError("Horizontal armor order / clipping");
            lastX = slotX;
        }
        Object vertical = armorLayout.invoke(null, false, new int[] {18, 18, 18, 18});
        if ((int)armorGeometry.getMethod("height").invoke(vertical) != 84 ||
            (int)armorGeometry.getMethod("height").invoke(horizontal) != 34)
            throw new AssertionError("Armor orientation geometry");
        if ((int)armorGeometry.getMethod("width").invoke(horizontal) != 82 ||
            (int)armorGeometry.getMethod("iconX", int.class).invoke(horizontal, 1) -
            (int)armorGeometry.getMethod("iconX", int.class).invoke(horizontal, 0) != 21)
            throw new AssertionError("Horizontal armor still reserves oversized slots");
        int[] mixedWidths = {6, 18, 24, 60};
        Object mixedArmor = armorLayout.invoke(null, true, mixedWidths);
        int previousEnd = -2;
        for (int slot = 0; slot < 4; slot++) {
            int tx = (int)armorGeometry.getMethod("textX", int.class, int.class).invoke(mixedArmor, slot, mixedWidths[slot]);
            if (tx < previousEnd + 2) throw new AssertionError("Compact armor numbers overlap");
            previousEnd = tx + mixedWidths[slot] + 1;
        }
        if (previousEnd > (int)armorGeometry.getMethod("width").invoke(mixedArmor))
            throw new AssertionError("Long durability value clips the HUD");
        System.out.println("PASS Compact per-item armor widths, two-pixel gaps and non-overlapping mixed durability values");
        configType.getField("armorHorizontal").setBoolean(current, true);
        if (!(boolean)configType.getMethod("save").invoke(current)) throw new AssertionError("Armor orientation save");
        Object armorSaved = configType.getMethod("load", Path.class).invoke(null, Path.of(args[0], "config", "vortex-client.json"));
        if (!(boolean)configType.getField("armorHorizontal").get(armorSaved)) throw new AssertionError("Armor orientation persistence");
        configType.getField("armorHorizontal").setBoolean(current, false);
        configType.getField("keystrokesEnabled").setBoolean(current, true);
        Class<?> legacyElementType = Class.forName("dev.vortex.client.HudModules$Element", true, target);
        if ((boolean)hud.getMethod("enabled", legacyElementType).invoke(null, legacyElementType.getEnumConstants()[1]))
            throw new AssertionError("Removed Keystrokes can reappear from legacy settings");
        for (Object module : modules) if (module.toString().equals("KEYS")) throw new AssertionError("Keystrokes still in module menu");
        Object wideMenu = menuLayout.invoke(null, 1920, 1080);
        if ((int)wideMenu.getClass().getMethod("width").invoke(wideMenu) != 680)
            throw new AssertionError("Menu remains capped at the old narrow width");
        System.out.println("PASS Armor orientation, helmet-to-boots left-to-right order, persistence, removed keys and wider GUI");
        System.out.println("PASS Remaining-only Armor HUD text and bounded durability bars");
        Method direction = hud.getMethod("direction", float.class);
        String[] directions = {"South", "Southwest", "West", "Northwest", "North", "Northeast", "East", "Southeast"};
        for (int i = 0; i < directions.length; i++) {
            if (!direction.invoke(null, i * 45f).equals(directions[i]) ||
                !direction.invoke(null, i * 45f - 720).equals(directions[i]))
                throw new AssertionError("Incorrect Minecraft yaw / wraparound: " + i);
        }
        if (!direction.invoke(null, 22.49f).equals("South") || !direction.invoke(null, 22.5f).equals("Southwest") ||
            !direction.invoke(null, -22.5f).equals("South") || !direction.invoke(null, -22.51f).equals("Southeast") ||
            !direction.invoke(null, Float.NaN).equals("Unknown") || !direction.invoke(null, Float.POSITIVE_INFINITY).equals("Unknown"))
            throw new AssertionError("Compass sector boundaries / invalid yaw");
        Method biomeName = hud.getMethod("biomeName", String.class);
        if (!biomeName.invoke(null, "plains").equals("Plains") ||
            !biomeName.invoke(null, "snowy_taiga").equals("Snowy Taiga") ||
            !biomeName.invoke(null, "custom/deep-dark_forest").equals("Custom Deep Dark Forest") ||
            !biomeName.invoke(null, "").equals("Unknown")) throw new AssertionError("English biome labels");
        java.util.Locale savedLocale = java.util.Locale.getDefault();
        try {
            java.util.Locale.setDefault(java.util.Locale.forLanguageTag("tr-TR"));
            if (!biomeName.invoke(null, "ICE_SPIKES").equals("Ice Spikes")) throw new AssertionError("Locale-dependent biome name");
        } finally { java.util.Locale.setDefault(savedLocale); }
        Class<?> blockPosType = Class.forName("net.minecraft.core.BlockPos", true, target);
        Object negative = blockPosType.getMethod("containing", double.class, double.class, double.class).invoke(null, -.1, -54.1, 860.9);
        if ((int)blockPosType.getMethod("getX").invoke(negative) != -1 ||
            (int)blockPosType.getMethod("getY").invoke(negative) != -55 ||
            (int)blockPosType.getMethod("getZ").invoke(negative) != 860)
            throw new AssertionError("Coordinate block floor behavior");
        System.out.println("PASS Eight compass sectors, negative coordinates and English vanilla/custom biome names");
        Method bounds = hud.getMethod("clampBounds", int.class, int.class, int.class, int.class, float.class, int.class, int.class);
        Object edge = bounds.invoke(null, 500, -100, 86, 94, 2f, 320, 240);
        if ((int)edge.getClass().getMethod("x").invoke(edge) != 148 || (int)edge.getClass().getMethod("y").invoke(edge) != 0)
            throw new AssertionError("Scaled HUD bounds");
        Object tiny = bounds.invoke(null, 100, 100, 86, 94, 2f, 20, 20);
        if ((int)tiny.getClass().getMethod("x").invoke(tiny) != 0) throw new AssertionError("Small window bounds");
        Method anchored = hud.getMethod("anchoredBounds", int.class, int.class, int.class, int.class, float.class, int.class, int.class);
        for (int[] size : new int[][] {{320, 240}, {640, 360}, {854, 480}, {1920, 1080}}) for (float scale : new float[] {.75f, 1, 2}) {
            Object corner = anchored.invoke(null, -1, -1, 54, 84, scale, size[0], size[1]);
            Class<?> geometry = corner.getClass();
            if ((int)geometry.getMethod("x").invoke(corner) + (int)geometry.getMethod("width").invoke(corner) != size[0] - 8 ||
                (int)geometry.getMethod("y").invoke(corner) + (int)geometry.getMethod("height").invoke(corner) != size[1] - 8)
                throw new AssertionError("Armor not anchored to the bottom-right at different GUI scales");
        }
        Object absolute = anchored.invoke(null, 25, 30, 54, 84, 1f, 640, 360);
        if ((int)absolute.getClass().getMethod("x").invoke(absolute) != 25 || (int)absolute.getClass().getMethod("y").invoke(absolute) != 30)
            throw new AssertionError("Custom armor position lost to automatic anchoring");
        Class<?> elementType = Class.forName("dev.vortex.client.HudModules$Element", true, target);
        Object armor = elementType.getEnumConstants()[2], coordinates = elementType.getEnumConstants()[4];
        if (moduleType.getMethod("forHud", elementType).invoke(null, coordinates) != modules[4])
            throw new AssertionError("Coordinates editor settings routing");
        hud.getMethod("keepOnScreen", elementType, int.class, int.class).invoke(null, armor, 320, 240);
        if (configType.getField("armorX").getInt(current) != -1 || configType.getField("armorY").getInt(current) != -1)
            throw new AssertionError("Appearance changes removed corner anchoring");
        hud.getMethod("position", elementType, int.class, int.class).invoke(null, armor, 100, 60);
        if (configType.getField("armorX").getInt(current) != 100 || configType.getField("armorY").getInt(current) != 60)
            throw new AssertionError("Manual placement did not detach armor from corner");
        hud.getMethod("resetPositions").invoke(null);
        if (configType.getField("armorX").getInt(current) != -1 || configType.getField("coordinatesX").getInt(current) != 10 ||
            configType.getField("coordinatesY").getInt(current) != 10 || configType.getField("keysY").getInt(current) != 84)
            throw new AssertionError("Reset did not restore reference layout");
        System.out.println("PASS Bottom-right anchoring, custom placement, resize anchoring and reference layout reset");
        Class.forName("dev.vortex.client.HudEditorScreen", false, target).getDeclaredMethods();
        System.out.println("PASS HUD Editor links and scaled layout bounds");
        Method label = hud.getMethod("compactKeyLabel", String.class, String.class);
        if (!label.invoke(null, "key.mouse.left", "Left Button").equals("LMB") ||
            !label.invoke(null, "key.mouse.right", "Right Button").equals("RMB") ||
            !label.invoke(null, "key.keyboard.r", "R").equals("R")) throw new AssertionError("Compact key labels / rebinding");
        Class<?> editorType = Class.forName("dev.vortex.client.HudEditorScreen", true, target);
        Class<?> mcType = Class.forName("net.minecraft.client.Minecraft", false, target);
        Class<?> fontType = Class.forName("net.minecraft.client.gui.Font", false, target);
        Class<?> screenType = Class.forName("net.minecraft.client.gui.screens.Screen", false, target);
        var ctor = editorType.getDeclaredConstructor(mcType, fontType, screenType);
        ctor.setAccessible(true);
        Object editor = ctor.newInstance(null, null, null);
        screenType.getField("width").set(editor, 320); screenType.getField("height").set(editor, 240);
        Method toolbarLayout = editorType.getMethod("toolbarLayout", int.class, int.class, boolean.class);
        for (int[] dimensions : new int[][] {{320, 240}, {430, 300}, {640, 360}, {854, 480}, {1920, 1080}}) {
            for (boolean expanded : new boolean[] {false, true}) {
                Object toolbar = toolbarLayout.invoke(null, dimensions[0], dimensions[1], expanded);
                Class<?> geometry = toolbar.getClass();
                int x = (int)geometry.getMethod("x").invoke(toolbar), y = (int)geometry.getMethod("y").invoke(toolbar);
                int w = (int)geometry.getMethod("width").invoke(toolbar), h = (int)geometry.getMethod("height").invoke(toolbar);
                if (x < 0 || y < 0 || x + w > dimensions[0] || y + h > dimensions[1] - 60)
                    throw new AssertionError("Editor controls cover the vanilla hotbar / health / XP area");
                if (!expanded && (w > 76 || h > 22 || x + w != dimensions[0] - 8))
                    throw new AssertionError("Collapsed controls must stay compact and top-right");
            }
        }
        var expandedField = editorType.getDeclaredField("controlsExpanded"); expandedField.setAccessible(true);
        if (expandedField.getBoolean(editor) || (boolean)editorType.getMethod("isInGameUi").invoke(editor))
            throw new AssertionError("Controls should start collapsed; no-world editor should use the panorama");
        System.out.println("PASS Editor controls leave vanilla hotbar / health / XP visible at multiple GUI scales");
        configType.getField("fpsEnabled").set(current, false);
        configType.getField("armorEnabled").set(current, false);
        configType.getField("coordinatesEnabled").set(current, false);
        configType.getField("heldItemEnabled").set(current, false);
        configType.getField("effectsEnabled").set(current, false);
        configType.getField("toggleSprintEnabled").setBoolean(current, true);
        configType.getField("sprintX").setInt(current, 10); configType.getField("sprintY").setInt(current, 42);
        Class<?> buttonType = Class.forName("net.minecraft.client.input.MouseButtonInfo", true, target);
        Class<?> eventType = Class.forName("net.minecraft.client.input.MouseButtonEvent", true, target);
        int left = Class.forName("com.mojang.blaze3d.platform.InputConstants", true, target).getField("MOUSE_BUTTON_LEFT").getInt(null);
        Object button = buttonType.getConstructor(int.class, int.class).newInstance(left, 0);
        Class<?> inputs = Class.forName("com.mojang.blaze3d.platform.InputConstants", true, target);
        Class<?> keyType = Class.forName("net.minecraft.client.input.KeyEvent", true, target);
        Object controlsKey = keyType.getConstructor(int.class, int.class, int.class).newInstance(inputs.getField("KEY_H").getInt(null), 0, 0);
        if (!(boolean)editorType.getMethod("keyPressed", keyType).invoke(editor, controlsKey) || !expandedField.getBoolean(editor))
            throw new AssertionError("H does not expand controls");
        if (!(boolean)editorType.getMethod("keyPressed", keyType).invoke(editor, controlsKey) || expandedField.getBoolean(editor))
            throw new AssertionError("H does not collapse controls");
        System.out.println("PASS Real H-key controls toggle without changing the HUD configuration");
        Class<?> cardType = Class.forName("dev.vortex.client.VortexScreen$ModuleCard", true, target);
        var cardCtor = cardType.getDeclaredConstructor(int.class, int.class, int.class, int.class, moduleType, Runnable.class, Runnable.class);
        cardCtor.setAccessible(true);
        int[] actions = {0, 0};
        Object card = cardCtor.newInstance(0, 0, 100, 96, modules[0],
            (Runnable)() -> actions[0]++, (Runnable)() -> actions[1]++);
        Method cardClick = cardType.getMethod("onClick", eventType, boolean.class); cardClick.setAccessible(true);
        cardClick.invoke(card, eventType.getConstructor(double.class, double.class, buttonType).newInstance(20d, 20d, button), false);
        cardClick.invoke(card, eventType.getConstructor(double.class, double.class, buttonType).newInstance(85d, 20d, button), false);
        Object rightButton = buttonType.getConstructor(int.class, int.class).newInstance(inputs.getField("MOUSE_BUTTON_RIGHT").getInt(null), 0);
        cardClick.invoke(card, eventType.getConstructor(double.class, double.class, buttonType).newInstance(20d, 20d, rightButton), false);
        cardType.getMethod("setFocused", boolean.class).invoke(card, true);
        Method cardKey = cardType.getMethod("keyPressed", keyType); cardKey.setAccessible(true);
        cardKey.invoke(card, keyType.getConstructor(int.class, int.class, int.class).newInstance(inputs.getField("KEY_R").getInt(null), 0, 0));
        if (actions[0] != 1 || actions[1] != 3) throw new AssertionError("Card settings clicks accidentally toggle the module");
        Class<?> sliderType = Class.forName("dev.vortex.client.ClientUi$Slider", true, target);
        var sliderCtor = sliderType.getDeclaredConstructor(int.class, int.class, int.class, String.class,
            double.class, double.class, double.class, java.util.function.DoubleFunction.class,
            java.util.function.DoubleConsumer.class, Runnable.class);
        sliderCtor.setAccessible(true);
        double[] sliderValue = {4}; int[] commits = {0};
        Object slider = sliderCtor.newInstance(0, 0, 100, "Zoom strength", 2d, 8d, 4d,
            (java.util.function.DoubleFunction<String>)value -> value + "x",
            (java.util.function.DoubleConsumer)value -> sliderValue[0] = value, (Runnable)() -> commits[0]++);
        Method sliderClick = sliderType.getMethod("onClick", eventType, boolean.class);
        sliderClick.invoke(slider, eventType.getConstructor(double.class, double.class, buttonType).newInstance(150d, 24d, button), false);
        if (sliderValue[0] != 8) throw new AssertionError("Slider maximum");
        sliderClick.invoke(slider, eventType.getConstructor(double.class, double.class, buttonType).newInstance(-10d, 24d, button), false);
        if (sliderValue[0] != 2) throw new AssertionError("Slider minimum");
        sliderClick.invoke(slider, eventType.getConstructor(double.class, double.class, buttonType).newInstance(27d, 24d, button), false);
        if (Math.abs(sliderValue[0] - 3.5) > .001 || commits[0] != 0) throw new AssertionError("Slider precision / excessive disk writes");
        Method sliderKey = sliderType.getMethod("keyPressed", keyType); sliderKey.setAccessible(true);
        sliderKey.invoke(slider, keyType.getConstructor(int.class, int.class, int.class).newInstance(inputs.getField("KEY_RETURN").getInt(null), 0, 0));
        sliderKey.invoke(slider, keyType.getConstructor(int.class, int.class, int.class).newInstance(inputs.getField("KEY_RIGHT").getInt(null), 0, 0));
        if (sliderValue[0] <= 3.5 || commits[0] != 2) throw new AssertionError("Slider keyboard adjustment / commit");
        var clipTopField = cardType.getDeclaredField("clipTop"); clipTopField.setAccessible(true); clipTopField.setInt(card,20);
        var clipBottomField = cardType.getDeclaredField("clipBottom"); clipBottomField.setAccessible(true); clipBottomField.setInt(card,70);
        Object clippedClick = eventType.getConstructor(double.class,double.class,buttonType).newInstance(10d,5d,button);
        Method clippedMouse = cardType.getMethod("mouseClicked",eventType,boolean.class); clippedMouse.setAccessible(true);
        if ((boolean)clippedMouse.invoke(card,clippedClick,false)) throw new AssertionError("Clipped module captured a header click");
        Method clippedOver = cardType.getMethod("isMouseOver",double.class,double.class); clippedOver.setAccessible(true);
        if ((boolean)clippedOver.invoke(card,10d,5d) || !(boolean)clippedOver.invoke(card,10d,50d))
            throw new AssertionError("Clipped card hover bounds");
        clipTopField.setInt(card,Integer.MIN_VALUE); clipBottomField.setInt(card,Integer.MAX_VALUE);
        System.out.println("PASS Module card toggle vs. settings routing, continuous bounded sliders and keyboard adjustment");
        Class<?> component = Class.forName("net.minecraft.network.chat.Component", true, target);
        Class<?> buttonClass = Class.forName("net.minecraft.client.gui.components.Button", true, target);
        Class<?> onPressType = Class.forName("net.minecraft.client.gui.components.Button$OnPress", true, target);
        int[] vanillaPresses = {0};
        Object originalPress = java.lang.reflect.Proxy.newProxyInstance(target, new Class<?>[]{onPressType}, (proxy, method, arguments) -> {
            if (method.getName().equals("onPress")) vanillaPresses[0]++;
            return null;
        });
        Object resumeText = component.getMethod("translatable", String.class).invoke(null, "menu.returnToGame");
        Object builder = buttonClass.getMethod("builder", component, onPressType).invoke(null, resumeText, originalPress);
        Object vanillaButton = builder.getClass().getMethod("build").invoke(builder);
        Object wrappedButton = chrome.getMethod("wrap", buttonClass, int.class, int.class, int.class, int.class, boolean.class)
            .invoke(null, vanillaButton, 10, 10, 120, 24, true);
        Class<?> inputType = Class.forName("net.minecraft.client.input.InputWithModifiers", true, target);
        Method press = buttonClass.getMethod("onPress", inputType);
        press.invoke(wrappedButton, nudgeEvent(keyType, inputs));
        if (vanillaPresses[0] != 1) throw new AssertionError("Restyled menu lost vanilla callback");
        buttonClass.getField("active").setBoolean(vanillaButton, false);
        press.invoke(wrappedButton, nudgeEvent(keyType, inputs));
        if (vanillaPresses[0] != 1) throw new AssertionError("Restyled menu bypassed vanilla disabled state");
        buttonClass.getField("active").setBoolean(vanillaButton, true);
        buttonClass.getField("visible").setBoolean(vanillaButton, false);
        press.invoke(wrappedButton, nudgeEvent(keyType, inputs));
        if (vanillaPresses[0] != 1) throw new AssertionError("Restyled menu activated hidden vanilla button");
        String englishResume = (String)chrome.getMethod("label", component).invoke(null, resumeText);
        if (!englishResume.equals("Back to Game")) throw new AssertionError("English menu labels");
        var actionSources = new java.util.ArrayList<Object>();
        for (String actionKey : new String[]{"menu.returnToGame", "menu.options", "gui.advancements", "gui.stats", "menu.shareToLan", "menu.returnToMenu"}) {
            Object text = component.getMethod("translatable", String.class).invoke(null, actionKey);
            Object sourceBuilder = buttonClass.getMethod("builder", component, onPressType).invoke(null, text, originalPress);
            actionSources.add(sourceBuilder.getClass().getMethod("build").invoke(sourceBuilder));
        }
        Object pauseChrome = chrome.getConstructor(screenBase, boolean.class, java.util.List.class).newInstance(null, true, actionSources);
        var menuActions = (java.util.List<?>)chrome.getMethod("widgets", int.class, int.class).invoke(pauseChrome, 854, 480);
        if (menuActions.size() != actionSources.size() + 3) throw new AssertionError("Unexpected Lunar-style menu shortcuts");
        var labels = new java.util.HashSet<String>();
        for (Object action : menuActions) {
            Object text = buttonClass.getMethod("getMessage").invoke(action);
            String labelText = (String)component.getMethod("getString").invoke(text);
            if (labelText.isBlank() || !labels.add(labelText) || (int)buttonClass.getMethod("getWidth").invoke(action) < 24)
                throw new AssertionError("Pause action has no unique accessible label / usable target");
        }
        if (!labels.containsAll(java.util.List.of("Back to Game", "Options", "Advancements", "Statistics", "Open to LAN", "Save & Quit", "Vortex Client", "Modules", "Wardrobe")))
            throw new AssertionError("Vanilla action disappeared from the labeled pause menu");
        for (int i = 0; i < actionSources.size(); i++) {
            press.invoke(menuActions.get(i), nudgeEvent(keyType, inputs));
            if (vanillaPresses[0] != i + 2) throw new AssertionError("Dock lost a vanilla action callback");
        }
        for (int[] dims : new int[][] {{320,240}, {430,300}, {854,480}}) {
            var dockActions = (java.util.List<?>)chrome.getMethod("widgets", int.class, int.class).invoke(pauseChrome, dims[0], dims[1]);
            for (int i = 0; i < dockActions.size(); i++) {
                Object a = dockActions.get(i);
                int ax = (int)buttonClass.getMethod("getX").invoke(a), ay = (int)buttonClass.getMethod("getY").invoke(a);
                int aw = (int)buttonClass.getMethod("getWidth").invoke(a), ah = (int)buttonClass.getMethod("getHeight").invoke(a);
                if (ax < 0 || ay < 0 || ax + aw > dims[0] || ay + ah > dims[1] - 14)
                    throw new AssertionError("Actual pause widget clips window/footer");
                for (int j = 0; j < i; j++) {
                    Object b = dockActions.get(j);
                    int bx = (int)buttonClass.getMethod("getX").invoke(b), by = (int)buttonClass.getMethod("getY").invoke(b);
                    int bw = (int)buttonClass.getMethod("getWidth").invoke(b), bh = (int)buttonClass.getMethod("getHeight").invoke(b);
                    if (ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by)
                        throw new AssertionError("Actual central actions overlap dock");
                }
            }
        }
        System.out.println("PASS Labeled utility grid and central actions: unique accessible names, all vanilla callbacks and non-overlapping live widget bounds");
        for (int[] dims : new int[][] {{320,240}, {430,300}, {854,480}}) {
            Object hub = home.getConstructor(screenBase).newInstance((Object)null);
            screenBase.getField("width").setInt(hub, dims[0]); screenBase.getField("height").setInt(hub, dims[1]);
            Method initialize = home.getDeclaredMethod("init"); initialize.setAccessible(true); initialize.invoke(hub);
            var children = (java.util.List<?>)screenBase.getMethod("children").invoke(hub);
            if (children.size() != 7) throw new AssertionError("Hub must expose six labeled destinations and Back");
            var destinations = new java.util.HashSet<String>();
            for (Object child : children) {
                String name = (String)component.getMethod("getString").invoke(buttonClass.getMethod("getMessage").invoke(child));
                destinations.add(name);
                int bx = (int)buttonClass.getMethod("getX").invoke(child), by = (int)buttonClass.getMethod("getY").invoke(child);
                int bw = (int)buttonClass.getMethod("getWidth").invoke(child), bh = (int)buttonClass.getMethod("getHeight").invoke(child);
                if (bw < 130 || bx < 0 || by < 0 || bx + bw > dims[0] || by + bh > dims[1] - 14)
                    throw new AssertionError("Actual labeled hub action clips or becomes icon-only: " + name);
            }
            if (!destinations.containsAll(java.util.List.of("Modules", "Edit HUD", "Wardrobe", "Party", "Key bindings", "Client settings", "Back")))
                throw new AssertionError("Hub destination missing");
        }
        System.out.println("PASS Actual hub widgets expose six labeled destinations and Back at three GUI scales");
        for (String tool : new String[]{"KeybindScreen", "DurabilitySettingsScreen", "ModuleHelpScreen"}) {
            Class<?> page = Class.forName("dev.vortex.client." + tool, true, target);
            if (page.getMethod("isInGameUi").getDeclaringClass() != page ||
                page.getMethod("extractBackground", Class.forName("net.minecraft.client.gui.GuiGraphicsExtractor", false, target), int.class, int.class, float.class).getDeclaringClass() != page)
                throw new AssertionError("Client tool falls back to a vanilla blur pass: " + tool);
        }
        Class<?> moduleScreen = Class.forName("dev.vortex.client.VortexScreen", true, target);
        for (int[] dims : new int[][]{{320,240},{854,480}}) {
            Object settings = moduleScreen.getMethod("settings", screenBase).invoke(null, (Object)null);
            screenBase.getField("width").setInt(settings,dims[0]); screenBase.getField("height").setInt(settings,dims[1]);
            Method initialize = moduleScreen.getDeclaredMethod("init"); initialize.setAccessible(true); initialize.invoke(settings);
            var names = new java.util.HashSet<String>();
            var children = (java.util.List<?>)screenBase.getMethod("children").invoke(settings);
            for(Object child : children) if(buttonClass.isInstance(child)) {
                names.add((String)component.getMethod("getString").invoke(buttonClass.getMethod("getMessage").invoke(child)));
                int y=(int)buttonClass.getMethod("getY").invoke(child),h=(int)buttonClass.getMethod("getHeight").invoke(child);
                if(y<0 || y+h>dims[1]) throw new AssertionError("General settings button leaves viewport");
            }
            if(!names.contains("Key bindings") || !names.contains("HUD Editor")) throw new AssertionError("General settings lost labeled destinations");
            var pageOpened = moduleScreen.getDeclaredField("pageOpened"); pageOpened.setAccessible(true);
            pageOpened.setLong(settings, 1234L);
            Object firstWidgetBeforeSpam = children.get(0);
            Object settingsButton = children.stream().filter(buttonClass::isInstance).filter(child -> {
                try { return "Settings".equals(component.getMethod("getString").invoke(buttonClass.getMethod("getMessage").invoke(child))); }
                catch (Exception ex) { throw new RuntimeException(ex); }
            }).findFirst().orElseThrow();
            for(int clickIndex=0; clickIndex<100; clickIndex++) press.invoke(settingsButton,nudgeEvent(keyType,inputs));
            if(pageOpened.getLong(settings)!=1234L || ((java.util.List<?>)screenBase.getMethod("children").invoke(settings)).get(0)!=firstWidgetBeforeSpam)
                throw new AssertionError("Repeated active Settings clicks restarted entrance or rebuilt widgets");
            initialize.invoke(settings);
            if(pageOpened.getLong(settings)!=1234L) throw new AssertionError("Widget rebuild restarted page entrance");
        }
        Object filteredMenu = moduleScreen.getConstructor(screenBase).newInstance((Object)null);
        var filterMethod = moduleScreen.getDeclaredMethod("selectFilter",String.class); filterMethod.setAccessible(true);
        var pageTime = moduleScreen.getDeclaredField("pageOpened"); pageTime.setAccessible(true); pageTime.setLong(filteredMenu,1234L);
        var scrollTarget = moduleScreen.getDeclaredField("scrollTarget"); scrollTarget.setAccessible(true); scrollTarget.setDouble(filteredMenu,17d);
        for(int clickIndex=0; clickIndex<100; clickIndex++) filterMethod.invoke(filteredMenu,"All Modules");
        if(pageTime.getLong(filteredMenu)!=1234L || scrollTarget.getDouble(filteredMenu)!=17d)
            throw new AssertionError("Repeated active module filter reset entrance or scrolling");
        System.out.println("PASS Active-tab click spam preserves entrance timestamp, widgets and module scrolling; rebuilds do not restart entrance");
        System.out.println("PASS Actual general-settings destinations and tool backgrounds preserve HUD without extra blur");
        System.out.println("PASS Custom menu buttons delegate to vanilla handlers and preserve active/visible restrictions");
        Object click = eventType.getConstructor(double.class, double.class, buttonType).newInstance(15d, 50d, button);
        if (!(boolean)editorType.getMethod("mouseClicked", eventType, boolean.class).invoke(editor, click, false))
            throw new AssertionError("Real SDL left click did not select HUD");
        Object move = eventType.getConstructor(double.class, double.class, buttonType).newInstance(85d, 90d, button);
        if (!(boolean)editorType.getMethod("mouseDragged", eventType, double.class, double.class).invoke(editor, move, 70d, 40d) ||
            configType.getField("sprintX").getInt(current) != 80 || configType.getField("sprintY").getInt(current) != 82)
            throw new AssertionError("HUD did not move with pointer offset preserved");
        editorType.getMethod("mouseReleased", eventType).invoke(editor, move);
        if ((boolean)editorType.getMethod("mouseDragged", eventType, double.class, double.class).invoke(editor, move, 1d, 1d))
            throw new AssertionError("HUD continued dragging after release");
        editorType.getMethod("mouseScrolled", double.class, double.class, double.class, double.class).invoke(editor, 85d, 90d, 0d, 1d);
        if (configType.getField("sprintScale").getFloat(current) <= 1 || configType.getField("armorScale").getFloat(current) != 0)
            throw new AssertionError("Per-module scale isolation");
        float oldScale = configType.getField("sprintScale").getFloat(current);
        Object keys = elementType.getEnumConstants()[3];
        Object keyBounds = hud.getMethod("bounds", elementType, int.class, int.class).invoke(null, keys, 320, 240);
        int right = (int)keyBounds.getClass().getMethod("x").invoke(keyBounds) + (int)keyBounds.getClass().getMethod("width").invoke(keyBounds);
        int bottom = (int)keyBounds.getClass().getMethod("y").invoke(keyBounds) + (int)keyBounds.getClass().getMethod("height").invoke(keyBounds);
        Object handleClick = eventType.getConstructor(double.class, double.class, buttonType).newInstance((double)right, (double)bottom, button);
        if (!(boolean)editorType.getMethod("mouseClicked", eventType, boolean.class).invoke(editor, handleClick, false))
            throw new AssertionError("Resize handle not selectable outside overlay bounds");
        Object handleDrag = eventType.getConstructor(double.class, double.class, buttonType).newInstance(right + 20d, bottom + 20d, button);
        editorType.getMethod("mouseDragged", eventType, double.class, double.class).invoke(editor, handleDrag, 20d, 20d);
        if (configType.getField("sprintScale").getFloat(current) <= oldScale ||
            configType.getField("sprintX").getInt(current) != 80 || configType.getField("armorScale").getFloat(current) != 0)
            throw new AssertionError("Corner resize moved the module or changed another overlay");
        editorType.getMethod("mouseReleased", eventType).invoke(editor, handleDrag);
        if ((boolean)editorType.getMethod("mouseDragged", eventType, double.class, double.class).invoke(editor, handleDrag, 1d, 1d))
            throw new AssertionError("Resize continued after release");
        Method resize = hud.getMethod("resizeScale", float.class, int.class, int.class, double.class, double.class);
        if ((float)resize.invoke(null, 1f, 86, 94, 10000d, 10000d) != 2 ||
            (float)resize.invoke(null, 1f, 86, 94, -10000d, -10000d) != .75f ||
            (float)resize.invoke(null, 1f, 86, 94, Double.NaN, 1d) != 1)
            throw new AssertionError("Resize limits / invalid pointer input");
        Object nudge = keyType.getConstructor(int.class, int.class, int.class).newInstance(inputs.getField("KEY_RIGHT").getInt(null), 0, 0);
        editorType.getMethod("keyPressed", keyType).invoke(editor, nudge);
        if (configType.getField("sprintX").getInt(current) != 81) throw new AssertionError("One-pixel keyboard nudge");
        Object fastNudge = keyType.getConstructor(int.class, int.class, int.class).newInstance(inputs.getField("KEY_LEFT").getInt(null), 0, inputs.getField("MOD_SHIFT").getInt(null));
        editorType.getMethod("keyPressed", keyType).invoke(editor, fastNudge);
        if (configType.getField("sprintX").getInt(current) != 71) throw new AssertionError("Shift nudge must be ten pixels");
        configType.getField("sprintX").setInt(current, 80);
        configType.getField("sprintScale").setFloat(current, oldScale);
        if (!(boolean)configType.getField("hudEditorGrid").get(current) || !(boolean)configType.getField("hudEditorSnapping").get(current))
            throw new AssertionError("Grid / snapping defaults lost when loading an older config");
        configType.getField("hudEditorGrid").setBoolean(current, false);
        configType.getField("hudEditorSnapping").setBoolean(current, false);
        editorType.getMethod("mouseClicked", eventType, boolean.class).invoke(editor, move, false);
        Object unsnapped = eventType.getConstructor(double.class, double.class, buttonType).newInstance(121d, 90d, button);
        editorType.getMethod("mouseDragged", eventType, double.class, double.class).invoke(editor, unsnapped, 36d, 0d);
        if (configType.getField("sprintX").getInt(current) != 116) throw new AssertionError("Snapping OFF was ignored");
        editorType.getMethod("mouseReleased", eventType).invoke(editor, unsnapped);
        configType.getField("sprintX").setInt(current, 80);
        System.out.println("PASS Real corner resizing, aspect-preserving limits, keyboard nudges and optional snapping");
        configType.getField("toggleSprintEnabled").setBoolean(current, false);
        configType.getField("coordinatesEnabled").setBoolean(current, true);
        configType.getField("coordinatesX").setInt(current, 10); configType.getField("coordinatesY").setInt(current, 10);
        Object coordinateClick = eventType.getConstructor(double.class, double.class, buttonType).newInstance(15d, 15d, button);
        if (!(boolean)editorType.getMethod("mouseClicked", eventType, boolean.class).invoke(editor, coordinateClick, false))
            throw new AssertionError("Coordinates HUD cannot be selected in the editor");
        Object coordinateMove = eventType.getConstructor(double.class, double.class, buttonType).newInstance(45d, 45d, button);
        editorType.getMethod("mouseDragged", eventType, double.class, double.class).invoke(editor, coordinateMove, 30d, 30d);
        editorType.getMethod("mouseReleased", eventType).invoke(editor, coordinateMove);
        if (configType.getField("coordinatesX").getInt(current) != 40 || configType.getField("coordinatesY").getInt(current) != 40 ||
            configType.getField("sprintX").getInt(current) != 80)
            throw new AssertionError("Coordinates drag failed / changed another overlay");
        hud.getMethod("appearance", elementType, float.class, float.class).invoke(null, coordinates, 1.5f, .7f);
        if (configType.getField("coordinatesScale").getFloat(current) != 1.5f || configType.getField("coordinatesOpacity").getFloat(current) != .7f ||
            configType.getField("armorScale").getFloat(current) != 0)
            throw new AssertionError("Coordinates appearance isolation");
        configType.getField("toggleSprintEnabled").setBoolean(current, true);
        configType.getField("fpsEnabled").set(current, true); configType.getField("armorEnabled").set(current, true);
        if (!(boolean)configType.getMethod("save").invoke(current)) throw new AssertionError("Layout save");
        Object layout = configType.getMethod("load", Path.class).invoke(null, Path.of(args[0], "config", "vortex-client.json"));
        if (configType.getField("sprintX").getInt(layout) != 80 || configType.getField("sprintScale").getFloat(layout) <= 1)
            throw new AssertionError("Layout round-trip");
        if ((boolean)configType.getField("hudEditorGrid").get(layout) || (boolean)configType.getField("hudEditorSnapping").get(layout))
            throw new AssertionError("Editor preferences did not persist");
        if (!(boolean)configType.getField("coordinatesEnabled").get(layout) || configType.getField("coordinatesX").getInt(layout) != 40 ||
            configType.getField("coordinatesY").getInt(layout) != 40 || configType.getField("coordinatesScale").getFloat(layout) != 1.5f ||
            configType.getField("coordinatesOpacity").getFloat(layout) != .7f || configType.getField("armorX").getInt(layout) != -1)
            throw new AssertionError("Coordinates / corner anchor round-trip");
        Method snap = hud.getMethod("snapPosition", int.class, int.class, int.class);
        if ((int)snap.invoke(null, 119, 86, 320) != 117 || (int)snap.invoke(null, -20, 86, 320) != 0)
            throw new AssertionError("Center snapping / screen bounds");
        System.out.println("PASS Real editor SDL click-drag-release, wheel scaling, key labels, snapping and layout persistence");
        Path oldConfig = Path.of(args[0], "old-config.json");
        Files.writeString(oldConfig, "{\"fpsX\":10,\"fpsY\":10,\"keysX\":10,\"keysY\":42,\"armorX\":110,\"armorY\":10}");
        Object migrated = configType.getMethod("load", Path.class).invoke(null, oldConfig);
        if (configType.getField("fpsX").getInt(migrated) != 110 || configType.getField("keysY").getInt(migrated) != 84 ||
            configType.getField("armorX").getInt(migrated) != -1 || configType.getField("armorY").getInt(migrated) != -1 ||
            !(boolean)configType.getField("coordinatesEnabled").get(migrated)) throw new AssertionError("Old factory-default migration");
        Files.writeString(oldConfig, "{\"fpsX\":20,\"fpsY\":30,\"keysX\":40,\"keysY\":50,\"armorX\":60,\"armorY\":70}");
        Object custom = configType.getMethod("load", Path.class).invoke(null, oldConfig);
        for (String field : new String[] {"fpsX", "fpsY", "keysX", "keysY", "armorX", "armorY"}) {
            int expected = switch (field) {case "fpsX" -> 20; case "fpsY" -> 30; case "keysX" -> 40; case "keysY" -> 50; case "armorX" -> 60; default -> 70;};
            if (configType.getField(field).getInt(custom) != expected) throw new AssertionError("Customized layout migrated: " + field);
        }
        System.out.println("PASS Old default layout migration preserves customized overlay positions");
        Files.writeString(oldConfig, "{\"hudLayoutVersion\":1,\"fpsX\":10,\"fpsY\":10,\"keysX\":10,\"keysY\":42,\"armorX\":110,\"armorY\":10}");
        Object intentional = configType.getMethod("load", Path.class).invoke(null, oldConfig);
        if (configType.getField("fpsX").getInt(intentional) != 10 || configType.getField("keysY").getInt(intentional) != 42 ||
            configType.getField("armorX").getInt(intentional) != 110 || configType.getField("armorY").getInt(intentional) != 10)
            throw new AssertionError("Migration re-ran on an intentionally positioned new layout");
        System.out.println("PASS Layout migration runs only for legacy settings, not on subsequent custom placements");
        // Initialize the real item registry, but still no window, player or world.
        Class.forName("net.minecraft.SharedConstants", true, target).getMethod("tryDetectVersion").invoke(null);
        Class.forName("net.minecraft.server.Bootstrap", true, target).getMethod("bootStrap").invoke(null);
        checkChat(target);
        Method previewArmor = hud.getDeclaredMethod("previewArmor"); previewArmor.setAccessible(true);
        for (Object item : (Object[])previewArmor.invoke(null)) {
            if (!(boolean)item.getClass().getMethod("isEmpty").invoke(item))
                throw new AssertionError("Unbound title-screen item preview must wait for components");
        }
        // 26.3 binds default item components after bootstrap using the world registry lookup.
        Object registries = Class.forName("net.minecraft.data.registries.VanillaRegistries", true, target).getMethod("createWorldLookup").invoke(null);
        Object initializers = Class.forName("net.minecraft.core.registries.BuiltInRegistries", true, target).getField("DATA_COMPONENT_INITIALIZERS").get(null);
        Class<?> providerType = Class.forName("net.minecraft.core.HolderLookup$Provider", true, target);
        java.util.List<?> pending = (java.util.List<?>)initializers.getClass().getMethod("build", providerType).invoke(initializers, registries);
        Method bindComponents = Class.forName("net.minecraft.core.component.DataComponentInitializers$PendingComponents", true, target).getMethod("apply");
        for (Object components : pending) bindComponents.invoke(components);
        Object[] sampleItems = (Object[])previewArmor.invoke(null);
        String[] sampleDurability = {"389", "576", "534", "466"};
        if (sampleItems.length != 4) throw new AssertionError("Editor preview does not have four armor slots");
        for (int i = 0; i < sampleItems.length; i++) {
            Object item = sampleItems[i];
            int maximum = (int)item.getClass().getMethod("getMaxDamage").invoke(item);
            int damage = (int)item.getClass().getMethod("getDamageValue").invoke(item);
            if ((boolean)item.getClass().getMethod("isEmpty").invoke(item) ||
                !armorDurability.invoke(null, maximum, damage).equals(sampleDurability[i]))
                throw new AssertionError("Editor sample armor / durability: slot " + i);
        }
        System.out.println("PASS Editor sample armor links to the real Minecraft item registry without a player/world");
        Method equipment = hud.getMethod("equipment", Class.forName("net.minecraft.client.Minecraft", true, target), boolean.class);
        configType.getField("heldItemEnabled").setBoolean(current, true);
        if (((java.util.List<?>)equipment.invoke(null, null, true)).size() != 5 ||
            !((java.util.List<?>)equipment.invoke(null, null, false)).isEmpty())
            throw new AssertionError("Merged equipment preview / gameplay isolation");
        configType.getField("heldItemEnabled").setBoolean(current, false);
        if (((java.util.List<?>)equipment.invoke(null, null, true)).size() != 4)
            throw new AssertionError("Held items toggle inside Armor HUD");
        for (int count : new int[] {1, 5, 6}) {
            int[] widths = new int[count]; java.util.Arrays.fill(widths, 18);
            Object combined = armorLayout.invoke(null, true, widths);
            if ((int)combined.getClass().getMethod("width").invoke(combined) != count * 19 + (count - 1) * 2)
                throw new AssertionError("Compact merged equipment layout");
        }
        Class<?> motion = Class.forName("dev.vortex.client.UiMotion", true, target);
        Method reveal = motion.getMethod("reveal", double.class);
        float previous = 0;
        for (double time : new double[] {-10, 0, 50, 100, 220, 1000}) {
            float value = (float)reveal.invoke(null, time);
            if (value < previous || value < 0 || value > 1) throw new AssertionError("UI transition bounds");
            previous = value;
        }
        System.out.println("PASS Merged Armor HUD, compact 1–6 item layout and UI easing");
        Class<?> extra = Class.forName("dev.vortex.client.ExtraHud", true, target);
        Method duration = extra.getMethod("duration", int.class, boolean.class);
        if (!duration.invoke(null, 1, false).equals("0:01") || !duration.invoke(null, 1200, false).equals("1:00") ||
            !duration.invoke(null, 0, false).equals("0:00") || !duration.invoke(null, -10, false).equals("0:00") ||
            !duration.invoke(null, -1, true).equals("∞") || !duration.invoke(null, Integer.MAX_VALUE, false).equals("1789569:43"))
            throw new AssertionError("Effect timer rounding, infinity or overflow");
        Method itemDurability = extra.getMethod("durability", int.class, int.class);
        if (!itemDurability.invoke(null, 1561, 280).equals("1281 / 1561") ||
            !itemDurability.invoke(null, 0, 0).equals("") || !itemDurability.invoke(null, 100, 999).equals("0 / 100"))
            throw new AssertionError("Held item durability formatting");
        Class<?> minecraftType = Class.forName("net.minecraft.client.Minecraft", true, target);
        java.util.List<?> heldPreview = (java.util.List<?>)extra.getMethod("heldItems", minecraftType, boolean.class).invoke(null, null, true);
        if (heldPreview.size() != 1 || !(boolean)heldPreview.get(0).getClass().getMethod("isDamageableItem").invoke(heldPreview.get(0)))
            throw new AssertionError("Editor tool preview does not use a real damageable item");
        if (!((java.util.List<?>)extra.getMethod("heldItems", minecraftType, boolean.class).invoke(null, null, false)).isEmpty())
            throw new AssertionError("Sample tool leaks into normal gameplay");
        java.util.List<?> effectPreview = (java.util.List<?>)extra.getMethod("effects", minecraftType, boolean.class).invoke(null, null, true);
        if (effectPreview.size() != 3 || !((java.util.List<?>)extra.getMethod("effects", minecraftType, boolean.class).invoke(null, null, false)).isEmpty())
            throw new AssertionError("Effect preview / real gameplay isolation");
        Method rows = extra.getMethod("rows", int.class, float.class, int.class);
        if ((int)rows.invoke(null, 240, 2f, 100) != 4 || (int)rows.invoke(null, 240, 1f, 0) != 0)
            throw new AssertionError("Effect HUD scaled viewport bounds");
        Class<?> crosshair = Class.forName("dev.vortex.client.CustomCrosshair", true, target);
        Method geometryMethod = crosshair.getMethod("geometry", int.class, int.class, int.class, int.class, boolean.class);
        for (int shape = 0; shape < 3; shape++) {
            java.util.List<?> rects = (java.util.List<?>)geometryMethod.invoke(null, shape, 5, 2, 1, false);
            if (rects.isEmpty() || (shape == 0 && rects.size() != 4) || (shape == 1 && rects.size() != 1))
                throw new AssertionError("Crosshair shape geometry");
            for (Object rect : rects) {
                int rx = (int)rect.getClass().getMethod("x").invoke(rect), ry = (int)rect.getClass().getMethod("y").invoke(rect);
                int rw = (int)rect.getClass().getMethod("width").invoke(rect), rh = (int)rect.getClass().getMethod("height").invoke(rect);
                if (rw <= 0 || rh <= 0 || rx < -21 || ry < -21 || rx + rw > 21 || ry + rh > 21)
                    throw new AssertionError("Crosshair geometry is not bounded");
            }
        }
        if (((java.util.List<?>)geometryMethod.invoke(null, 0, 5, 2, 1, true)).size() != 5)
            throw new AssertionError("Center dot toggle");
        Method nextColor = crosshair.getMethod("nextColor", int.class);
        int color = 0xFFFFFF;
        for (int i = 0; i < 6; i++) color = (int)nextColor.invoke(null, color);
        if (color != 0xFFFFFF) throw new AssertionError("Crosshair palette cycle");
        configType.getField("crosshairEnabled").setBoolean(current, true);
        configType.getField("crosshairShape").setInt(current, 2);
        configType.getField("crosshairColor").setInt(current, 0x55FF55);
        configType.getField("crosshairSize").setInt(current, 8);
        configType.getField("crosshairGap").setInt(current, 3);
        configType.getField("crosshairThickness").setInt(current, 2);
        configType.getField("crosshairOpacity").setFloat(current, .6f);
        configType.getField("heldItemEnabled").setBoolean(current, true);
        configType.getField("effectsEnabled").setBoolean(current, true);
        configType.getField("heldItemX").setInt(current, 42);
        configType.getField("effectsX").setInt(current, 260);
        configType.getField("fullbrightEnabled").setBoolean(current, true);
        configType.getField("blockInfoEnabled").setBoolean(current, true);
        configType.getField("blockInfoX").setInt(current, 90);
        configType.getField("blockInfoScale").setFloat(current, 1.25f);
        configType.getField("menuKey").setInt(current, bindingInputs.getField("KEY_F8").getInt(null));
        configType.getField("zoomKey").setInt(current, f6Key);
        configType.getField("durabilityThreshold").setInt(current, 15);
        configType.getField("durabilityAlerts").setBoolean(current, false);
        configType.getField("freelookEnabled").setBoolean(current, true);
        configType.getField("freelookKey").setInt(current, inputs.getField("KEY_G").getInt(null));
        configType.getField("freelookSensitivity").setFloat(current, 1.5f);
        for (String metric : new String[] {"ping", "speed", "clock", "tps"}) {
            configType.getField(metric + "Enabled").setBoolean(current, true);
            configType.getField(metric + "X").setInt(current, 230);
            configType.getField(metric + "Y").setInt(current, 75);
            configType.getField(metric + "Scale").setFloat(current, 1.5f);
            configType.getField(metric + "Opacity").setFloat(current, .6f);
        }
        configType.getField("chatEnabled").setBoolean(current, false);
        configType.getField("chatTimestamps").setBoolean(current, false);
        configType.getField("chatCollapseRepeats").setBoolean(current, false);
        if (!(boolean)configType.getMethod("save").invoke(current)) throw new AssertionError("New module settings save");
        Object newSettings = configType.getMethod("load", Path.class).invoke(null, Path.of(args[0], "config", "vortex-client.json"));
        if (configType.getField("chatEnabled").getBoolean(newSettings) || configType.getField("chatTimestamps").getBoolean(newSettings) || configType.getField("chatCollapseRepeats").getBoolean(newSettings))
            throw new AssertionError("Chat toggles did not persist independently");
        Path badBindings = Path.of(args[0], "invalid-bindings.json");
        int customF9 = bindingInputs.getField("KEY_F9").getInt(null);
        Files.writeString(badBindings, "{\"menuKey\":9999,\"zoomKey\":0,\"freelookKey\":" + customF9 + ",\"durabilityThreshold\":999}");
        Object boundedBindings = configType.getMethod("load",Path.class).invoke(null,badBindings);
        if (configType.getField("menuKey").getInt(boundedBindings) != bindingInputs.getField("KEY_RSHIFT").getInt(null) ||
            configType.getField("zoomKey").getInt(boundedBindings) != cKey ||
            configType.getField("freelookKey").getInt(boundedBindings) != customF9 ||
            configType.getField("durabilityThreshold").getInt(boundedBindings) != 30)
            throw new AssertionError("Free key validation / preserved arbitrary key / alert threshold clamp");
        if (configType.getField("menuKey").getInt(newSettings) != bindingInputs.getField("KEY_F8").getInt(null) ||
            configType.getField("zoomKey").getInt(newSettings) != f6Key ||
            configType.getField("durabilityThreshold").getInt(newSettings) != 15 ||
            configType.getField("durabilityAlerts").getBoolean(newSettings))
            throw new AssertionError("Free bindings / durability settings did not persist");
        if (!configType.getField("fullbrightEnabled").getBoolean(newSettings) ||
            !configType.getField("blockInfoEnabled").getBoolean(newSettings) ||
            configType.getField("blockInfoX").getInt(newSettings) != 90 ||
            configType.getField("blockInfoScale").getFloat(newSettings) != 1.25f)
            throw new AssertionError("Fullbright / Block Info settings persistence");
        if (!configType.getField("freelookEnabled").getBoolean(newSettings) ||
            configType.getField("freelookKey").getInt(newSettings) != inputs.getField("KEY_G").getInt(null) ||
            configType.getField("freelookSensitivity").getFloat(newSettings) != 1.5f)
            throw new AssertionError("Freelook settings did not persist");
        for (String metric : new String[] {"ping", "speed", "clock", "tps"}) {
            if (!configType.getField(metric + "Enabled").getBoolean(newSettings) ||
                configType.getField(metric + "X").getInt(newSettings) != 230 ||
                configType.getField(metric + "Y").getInt(newSettings) != 75 ||
                configType.getField(metric + "Scale").getFloat(newSettings) != 1.5f ||
                configType.getField(metric + "Opacity").getFloat(newSettings) != .6f)
                throw new AssertionError("Metric HUD settings did not persist: " + metric);
        }
        if (!(boolean)configType.getField("crosshairEnabled").get(newSettings) ||
            configType.getField("crosshairShape").getInt(newSettings) != 2 || configType.getField("crosshairColor").getInt(newSettings) != 0x55FF55 ||
            configType.getField("crosshairSize").getInt(newSettings) != 8 || configType.getField("crosshairThickness").getInt(newSettings) != 2 ||
            configType.getField("crosshairGap").getInt(newSettings) != 3 || configType.getField("crosshairOpacity").getFloat(newSettings) != .6f ||
            configType.getField("heldItemX").getInt(newSettings) != 42 || configType.getField("effectsX").getInt(newSettings) != 260)
            throw new AssertionError("New HUD / crosshair settings did not persist");
        Files.writeString(oldConfig, "{\"crosshairShape\":99,\"crosshairSize\":500,\"crosshairGap\":-2,\"crosshairThickness\":0,\"crosshairOpacity\":0,\"heldItemScale\":99,\"effectsOpacity\":-2}");
        Object bounded = configType.getMethod("load", Path.class).invoke(null, oldConfig);
        if (configType.getField("crosshairShape").getInt(bounded) != 2 || configType.getField("crosshairSize").getInt(bounded) != 12 ||
            configType.getField("crosshairGap").getInt(bounded) != 0 || configType.getField("crosshairThickness").getInt(bounded) != 1 ||
            configType.getField("crosshairOpacity").getFloat(bounded) != .25f || configType.getField("heldItemScale").getFloat(bounded) != 2 ||
            configType.getField("effectsOpacity").getFloat(bounded) != .25f)
            throw new AssertionError("Malformed new HUD values are not bounded");
        System.out.println("PASS Held item preview and durability, potion timers, viewport bounds, crosshair shapes/palette and per-instance persistence");
        Path badConfig = Path.of(args[0], "invalid-config.json");
        Files.writeString(badConfig, "{bad json");
        Object fallback = configType.getMethod("load", Path.class).invoke(null, badConfig);
        if (!(boolean)configType.getField("fpsEnabled").get(fallback)) throw new AssertionError("No config fallback");
        if (!Files.readString(badConfig).equals("{bad json")) throw new AssertionError("Damaged config was modified");
        Path clampConfig = Path.of(args[0], "clamp-config.json");
        Files.writeString(clampConfig, "{\"zoomMultiplier\":100,\"hudScale\":-10,\"hudOpacity\":0,\"fpsX\":-50,\"keysX\":-20,\"armorY\":-30,\"sprintX\":-1,\"coordinatesX\":-9,\"coordinatesY\":-5,\"coordinatesScale\":9,\"coordinatesOpacity\":-2}");
        Object clamped = configType.getMethod("load", Path.class).invoke(null, clampConfig);
        if ((float)configType.getField("zoomMultiplier").get(clamped) != 8 ||
            (float)configType.getField("hudScale").get(clamped) != .75f ||
            (float)configType.getField("hudOpacity").get(clamped) != .25f ||
            (int)configType.getField("fpsX").get(clamped) != 0 ||
            (int)configType.getField("keysX").get(clamped) != 0 ||
            (int)configType.getField("armorY").get(clamped) != 0 ||
            (int)configType.getField("sprintX").get(clamped) != 0 ||
            configType.getField("coordinatesX").getInt(clamped) != 0 || configType.getField("coordinatesY").getInt(clamped) != 0 ||
            configType.getField("coordinatesScale").getFloat(clamped) != 2 || configType.getField("coordinatesOpacity").getFloat(clamped) != .25f)
            throw new AssertionError("Config not clamped");
        if (!(boolean)configType.getMethod("save").invoke(clamped)) throw new AssertionError("Save failed");
        Path savedConfig = Path.of(args[0], "config", "vortex-client.json");
        if (!Files.exists(savedConfig)) throw new AssertionError("No per-instance config");
        Object reloaded = configType.getMethod("load", Path.class).invoke(null, savedConfig);
        if ((boolean)configType.getField("keystrokesEnabled").get(reloaded) || !(boolean)configType.getField("armorEnabled").get(reloaded))
            throw new AssertionError("New module settings did not persist");
        System.out.println("PASS Corrupt config fallback, bounded settings and atomic per-instance save");
        WardrobeSmoke.run(target,Path.of(args[0]));
        CollectionSmoke.run(target,Path.of(args[0]));
        MotionSmoke.run(target);
        WardrobeQoLSmoke.run(target,Path.of(args[0]));
        NotificationSmoke.run(target);
        ChestSearchSmoke.run(target);
        WaypointsSmoke.run(target,Path.of(args[0]));
        InventoryHealthSmoke.run(target);
        AtlasQoLSmoke.run(target,Path.of(args[0]));
        for(String removed:java.util.List.of("VisibilityTest","WallOcclusion","CubeOcclusion","mixin.VisibilityTestMixin")){
            try{Class.forName("dev.vortex.client."+removed,false,target);throw new AssertionError("Experimental optimization still packaged: "+removed);}catch(ClassNotFoundException expected){}
        }
        if(java.util.Arrays.stream(configType.getFields()).anyMatch(f->f.getName().equals("visibilityTestEnabled")))throw new AssertionError("Removed optimization config still present");
        System.out.println("PASS Experimental visibility optimization classes, module, hooks and config removed");
        System.out.println("FABRIC_SMOKE_OK (no game window or worlds opened)");
    }
    private static void checkChat(ClassLoader target) throws Exception {
        Class<?> chat = Class.forName("dev.vortex.client.ChatPresentation", true, target);
        Class<?> message = Class.forName("net.minecraft.client.multiplayer.chat.GuiMessage", true, target);
        Class<?> component = Class.forName("net.minecraft.network.chat.Component", true, target);
        Class<?> signature = Class.forName("net.minecraft.network.chat.MessageSignature", true, target);
        Class<?> source = Class.forName("net.minecraft.client.multiplayer.chat.GuiMessageSource", true, target);
        Class<?> tag = Class.forName("net.minecraft.client.multiplayer.chat.GuiMessageTag", true, target);
        var constructor = message.getConstructor(int.class, component, signature, source, tag);
        Object content = component.getMethod("literal", String.class).invoke(null, "<Alex> Hello");
        Object signed = signature.getConstructor(byte[].class).newInstance((Object)new byte[256]);
        Object player = source.getField("PLAYER").get(null), server = source.getField("SYSTEM_SERVER").get(null);
        Object first = constructor.newInstance(100, content, signed, player, null);
        Object second = constructor.newInstance(101, content, signed, player, null);
        Object state = chat.getConstructor().newInstance();
        Method prepare = chat.getMethod("prepare", message, boolean.class), decorate = chat.getMethod("decorate", message, boolean.class, int.class);
        Method text = component.getMethod("getString");
        if ((int)prepare.invoke(state, first, true) != 1 || (int)prepare.invoke(state, second, true) != 2)
            throw new AssertionError("Adjacent identical chat messages not folded");
        String display = (String)text.invoke(decorate.invoke(state, second, true, 2));
        if (!display.matches("\\[\\d{2}:\\d{2}\\] <Alex> Hello ×2")) throw new AssertionError("Timestamp/repeat format: " + display);
        if (!text.invoke(content).equals("<Alex> Hello") || message.getMethod("content").invoke(second) != content || message.getMethod("signature").invoke(second) != signed)
            throw new AssertionError("Original signed chat was modified");
        if (!text.invoke(decorate.invoke(state, second, false, 1)).equals("<Alex> Hello")) throw new AssertionError("Chat feature toggles");
        if ((int)prepare.invoke(state, constructor.newInstance(102, content, null, server, null), true) != 1 ||
            (int)prepare.invoke(state, constructor.newInstance(103, content, signed, player, null), true) != 1 ||
            (int)prepare.invoke(state, constructor.newInstance(2000, content, signed, player, null), true) != 1)
            throw new AssertionError("Different chat sources / old messages folded");
        if ((int)prepare.invoke(state, second, false) != 1) throw new AssertionError("Repeat toggle ignored");
        chat.getMethod("clear").invoke(state);
        if (chat.getMethod("previous").invoke(state) != null || (int)prepare.invoke(state, second, true) != 1)
            throw new AssertionError("Chat state leaks across clears");
        Object other = component.getMethod("literal", String.class).invoke(null, "<Other> Hello");
        if ((int)prepare.invoke(state, constructor.newInstance(104, other, signed, player, null), true) != 1)
            throw new AssertionError("Different senders folded");
        Class<?> styleType = Class.forName("net.minecraft.network.chat.Style", true, target);
        Class<?> clickType = Class.forName("net.minecraft.network.chat.ClickEvent", true, target);
        Class<?> hoverType = Class.forName("net.minecraft.network.chat.HoverEvent", true, target);
        Object click = Class.forName("net.minecraft.network.chat.ClickEvent$SuggestCommand", true, target).getConstructor(String.class).newInstance("/msg Alex ");
        Object hover = Class.forName("net.minecraft.network.chat.HoverEvent$ShowText", true, target).getConstructor(component).newInstance(other);
        Object style = styleType.getMethod("withClickEvent", clickType).invoke(styleType.getField("EMPTY").get(null), click);
        style = styleType.getMethod("withHoverEvent", hoverType).invoke(style, hover);
        Object linked = component.getMethod("literal", String.class).invoke(null, "<Alex> Hello");
        linked.getClass().getMethod("withStyle", styleType).invoke(linked, style);
        Object linkedMessage = constructor.newInstance(105, linked, signed, player, null);
        prepare.invoke(state, second, true);
        if ((int)prepare.invoke(state, linkedMessage, true) != 1) throw new AssertionError("Different hover/click payloads folded based on plain text");
        Object decorated = decorate.invoke(state, linkedMessage, true, 1);
        var siblings = (java.util.List<?>)component.getMethod("getSiblings").invoke(decorated);
        if (siblings.size() != 2 || siblings.get(1) != linked || component.getMethod("getStyle").invoke(linked) != style)
            throw new AssertionError("Chat decoration lost interactive component metadata");
        String stamped = (String)text.invoke(decorated);
        chat.getMethod("resetGroup").invoke(state); prepare.invoke(state, linkedMessage, true);
        if (!text.invoke(decorate.invoke(state, linkedMessage, true, 1)).equals(stamped)) throw new AssertionError("Chat refresh changed original arrival time");
        chat.getMethod("retain", java.util.List.class).invoke(state, java.util.List.of(linkedMessage));
        var times = chat.getDeclaredField("times"); times.setAccessible(true);
        if (((java.util.Map<?,?>)times.get(state)).size() != 1) throw new AssertionError("Chat timestamp cache grows beyond retained history");
        System.out.println("PASS Local chat timestamps, adjacent repeat folding, source/time boundaries, toggles, clear and original signed content preservation");
        System.out.println("PASS Click/hover preservation, exact styled equality, stable arrival times after refresh and bounded timestamp cache");
    }
    private static Object nudgeEvent(Class<?> keyType, Class<?> inputs) throws Exception {
        return keyType.getConstructor(int.class, int.class, int.class).newInstance(inputs.getField("KEY_RETURN").getInt(null), 0, 0);
    }
}
