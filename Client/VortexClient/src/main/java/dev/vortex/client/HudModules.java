package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.KeyMapping;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.Items;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.BuiltInRegistries;
import java.util.Locale;

public final class HudModules {
    public enum Element { FPS, KEYS, ARMOR, SPRINT, COORDINATES, HELD_ITEM, EFFECTS, PING, SPEED, CLOCK, TPS, BLOCK_INFO }
    public record Bounds(int x, int y, int width, int height) {
        public boolean contains(double mx, double my) {
            return mx >= x && my >= y && mx < x + width && my < y + height;
        }
    }
    private static final EquipmentSlot[] ARMOR = {EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET};
    private static final String[] DIRECTIONS = {"South", "Southwest", "West", "Northwest", "North", "Northeast", "East", "Southeast"};
    private record CoordinateReadout(int x, int y, int z, String direction, String biome) { }
    private static final CoordinateReadout SAMPLE_COORDINATES = new CoordinateReadout(209, -55, 860, "Southeast", "Plains");
    private static ItemStack[] sampleArmor;
    private static Object cachedBiome;
    private static String cachedBiomeName = "Unknown";

    public static float scale(Element element) {
        var c = VortexClient.config;
        float value = switch (element) { case FPS -> c.fpsScale; case KEYS -> c.keysScale; case ARMOR -> c.armorScale; case SPRINT -> c.sprintScale; case COORDINATES -> c.coordinatesScale; case HELD_ITEM -> c.heldItemScale; case EFFECTS -> c.effectsScale; case PING -> c.pingScale; case SPEED -> c.speedScale; case CLOCK -> c.clockScale; case TPS -> c.tpsScale; case BLOCK_INFO -> c.blockInfoScale; };
        return value == 0 ? c.hudScale : value;
    }
    public static float opacity(Element element) {
        var c = VortexClient.config;
        float value = switch (element) { case FPS -> c.fpsOpacity; case KEYS -> c.keysOpacity; case ARMOR -> c.armorOpacity; case SPRINT -> c.sprintOpacity; case COORDINATES -> c.coordinatesOpacity; case HELD_ITEM -> c.heldItemOpacity; case EFFECTS -> c.effectsOpacity; case PING -> c.pingOpacity; case SPEED -> c.speedOpacity; case CLOCK -> c.clockOpacity; case TPS -> c.tpsOpacity; case BLOCK_INFO -> c.blockInfoOpacity; };
        return value == 0 ? c.hudOpacity : value;
    }
    public static void appearance(Element element, float scale, float opacity) {
        var c = VortexClient.config;
        scale = Math.clamp(scale, .75f, 2); opacity = Math.clamp(opacity, .25f, 1);
        switch (element) {
            case FPS -> { c.fpsScale = scale; c.fpsOpacity = opacity; }
            case KEYS -> { c.keysScale = scale; c.keysOpacity = opacity; }
            case ARMOR -> { c.armorScale = scale; c.armorOpacity = opacity; }
            case SPRINT -> { c.sprintScale = scale; c.sprintOpacity = opacity; }
            case COORDINATES -> { c.coordinatesScale = scale; c.coordinatesOpacity = opacity; }
            case HELD_ITEM -> { c.heldItemScale = scale; c.heldItemOpacity = opacity; }
            case EFFECTS -> { c.effectsScale = scale; c.effectsOpacity = opacity; }
            case PING -> { c.pingScale = scale; c.pingOpacity = opacity; }
            case SPEED -> { c.speedScale = scale; c.speedOpacity = opacity; }
            case CLOCK -> { c.clockScale = scale; c.clockOpacity = opacity; }
            case TPS -> { c.tpsScale = scale; c.tpsOpacity = opacity; }
            case BLOCK_INFO -> { c.blockInfoScale = scale; c.blockInfoOpacity = opacity; }
        }
    }
    public static String name(Element element) {
        return switch (element) { case FPS -> "FPS Counter"; case KEYS -> "Keystrokes"; case ARMOR -> "Armor HUD"; case SPRINT -> "Toggle Sprint"; case COORDINATES -> "Coordinates"; case HELD_ITEM -> "Held Item"; case EFFECTS -> "Potion Effects"; case PING -> "Ping"; case SPEED -> "Speed"; case CLOCK -> "Clock"; case TPS -> "TPS"; case BLOCK_INFO -> "Block Info"; };
    }
    public static boolean isDragButton(int button) { return button == InputConstants.MOUSE_BUTTON_LEFT; }
    public static String armorDurability(int maximum, int damage) {
        if (maximum <= 0) return "";
        return Integer.toString(maximum - Math.clamp(damage, 0, maximum));
    }
    public static int armorBarWidth(int maximum, int damage) {
        return maximum <= 0 ? 0 : Math.round(16f * (maximum - Math.clamp(damage, 0, maximum)) / maximum);
    }
    public static int armorBarColor(int maximum, int damage) {
        float remaining = maximum <= 0 ? 0 : (maximum - Math.clamp(damage, 0, maximum)) / (float)maximum;
        return remaining < .5f ? ClientUi.mix(0xFF5555, 0xFFFF55, remaining * 2) :
            ClientUi.mix(0xFFFF55, 0x55FF55, remaining * 2 - 1);
    }
    /** Minecraft yaw: 0 faces south, 90 west, 180 north, and -90 east. */
    public static String direction(float yaw) {
        if (!Float.isFinite(yaw)) return "Unknown";
        double normalized = ((double)yaw % 360 + 360) % 360;
        return DIRECTIONS[(int)Math.floor((normalized + 22.5) / 45) % 8];
    }
    public static String biomeName(String path) {
        StringBuilder result = new StringBuilder();
        for (String word : path.toLowerCase(Locale.ROOT).split("[_/\\-\\s]+")) {
            if (word.isEmpty()) continue;
            if (!result.isEmpty()) result.append(' ');
            result.append(Character.toUpperCase(word.charAt(0))).append(word.substring(1));
        }
        return result.isEmpty() ? "Unknown" : result.toString();
    }
    public static String compactKeyLabel(String savedKey, String translated) {
        return switch (savedKey) {
            case "key.mouse.left" -> "LMB";
            case "key.mouse.right" -> "RMB";
            case "key.mouse.middle" -> "MMB";
            case "key.keyboard.space" -> "SPACE";
            default -> translated;
        };
    }
    public static int snapPosition(int value, int size, int screenSize) {
        for (int anchor : new int[] {0, (screenSize - size) / 2, screenSize - size})
            if (Math.abs(value - anchor) <= 5) return Math.max(0, anchor);
        return Math.clamp(value, 0, Math.max(0, screenSize - size));
    }

    public static boolean resizeHandleContains(Bounds bounds, double x, double y) {
        int right = bounds.x() + bounds.width(), bottom = bounds.y() + bounds.height();
        return x >= right - 7 && x <= right + 5 && y >= bottom - 7 && y <= bottom + 5;
    }
    /** Project the pointer onto the initial diagonal; preserve the overlay's aspect ratio. */
    public static float resizeScale(float initial, int width, int height, double dx, double dy) {
        float start = Float.isFinite(initial) ? Math.clamp(initial, .75f, 2) : 1;
        double diagonal = (double)width * width + (double)height * height;
        if (!Double.isFinite(dx) || !Double.isFinite(dy) || diagonal <= 0) return start;
        return (float)Math.clamp(start * (1 + (dx * width + dy * height) / diagonal), .75, 2);
    }

    public static boolean enabled(Element element) {
        var c = VortexClient.config;
        return switch (element) {
            case FPS -> c.fpsEnabled;
            case KEYS -> false; // Legacy enum value is never rendered or exposed in the editor.
            case ARMOR -> c.armorEnabled;
            case SPRINT -> c.toggleSprintEnabled;
            case COORDINATES -> c.coordinatesEnabled;
            case HELD_ITEM -> false; // Legacy overlay: held items now belong to Armor HUD.
            case EFFECTS -> c.effectsEnabled;
            case PING -> c.pingEnabled; case SPEED -> c.speedEnabled; case CLOCK -> c.clockEnabled; case TPS -> c.tpsEnabled; case BLOCK_INFO -> c.blockInfoEnabled;
        };
    }
    public static Bounds bounds(Element element, int screenWidth, int screenHeight) {
        var c = VortexClient.config;
        var mc = Minecraft.getInstance();
        int x = switch (element) { case FPS -> c.fpsX; case KEYS -> c.keysX; case ARMOR -> c.armorX; case SPRINT -> c.sprintX; case COORDINATES -> c.coordinatesX; case HELD_ITEM -> c.heldItemX; case EFFECTS -> c.effectsX; case PING -> c.pingX; case SPEED -> c.speedX; case CLOCK -> c.clockX; case TPS -> c.tpsX; case BLOCK_INFO -> c.blockInfoX; };
        int y = switch (element) { case FPS -> c.fpsY; case KEYS -> c.keysY; case ARMOR -> c.armorY; case SPRINT -> c.sprintY; case COORDINATES -> c.coordinatesY; case HELD_ITEM -> c.heldItemY; case EFFECTS -> c.effectsY; case PING -> c.pingY; case SPEED -> c.speedY; case CLOCK -> c.clockY; case TPS -> c.tpsY; case BLOCK_INFO -> c.blockInfoY; };
        int w = switch (element) {
            case FPS -> mc == null || mc.font == null ? 60 : mc.font.width(mc.getFps() + " FPS") + 14;
            case PING, SPEED, CLOCK, TPS -> mc == null || mc.font == null ? 100 : mc.font.width(MetricsHud.readout(element, mc, mc.player == null)) + 14;
            case BLOCK_INFO -> Math.min(190, Math.max(1, (int)(screenWidth / scale(element))));
            case KEYS -> 86; case ARMOR -> armorLayout(c.armorHorizontal, armorTextWidths(mc)).width(); case SPRINT -> 124;
            case HELD_ITEM, EFFECTS -> Math.min(ExtraHud.width(element, mc), Math.max(1, (int)(screenWidth / scale(element))));
            case COORDINATES -> Math.min(coordinateWidth(mc, coordinates(mc)), Math.max(1, (int)(screenWidth / scale(element))));
        };
        int h = switch (element) { case FPS, SPRINT, PING, SPEED, CLOCK, TPS -> 22; case KEYS -> 94; case BLOCK_INFO -> 32; case ARMOR -> armorLayout(c.armorHorizontal, armorTextWidths(mc)).height(); case COORDINATES -> 64; case HELD_ITEM, EFFECTS -> ExtraHud.height(element, mc, screenHeight); };
        if (element == Element.BLOCK_INFO && x == -1) x = (screenWidth - (int)Math.ceil(w * scale(element))) / 2;
        if (element == Element.HELD_ITEM && x == -1) x = 10;
        if (element == Element.ARMOR || element == Element.HELD_ITEM || element == Element.EFFECTS) return anchoredBounds(x, y, w, h, scale(element), screenWidth, screenHeight);
        return clampBounds(x, y, w, h, scale(element), screenWidth, screenHeight);
    }
    public static Bounds anchoredBounds(int x, int y, int w, int h, float scale, int sw, int sh) {
        if (x == -1) x = sw - (int)Math.ceil(w * scale) - 8;
        if (y == -1) y = sh - (int)Math.ceil(h * scale) - 8;
        return clampBounds(x, y, w, h, scale, sw, sh);
    }
    public static Bounds clampBounds(int x, int y, int w, int h, float scale, int sw, int sh) {
        int width = (int)Math.ceil(w * scale), height = (int)Math.ceil(h * scale);
        return new Bounds(Math.clamp(x, 0, Math.max(0, sw - width)), Math.clamp(y, 0, Math.max(0, sh - height)), width, height);
    }
    public static void position(Element element, int x, int y) {
        var c = VortexClient.config;
        x = Math.max(0, x); y = Math.max(0, y);
        switch (element) {
            case FPS -> { c.fpsX = x; c.fpsY = y; }
            case KEYS -> { c.keysX = x; c.keysY = y; }
            case ARMOR -> { c.armorX = x; c.armorY = y; }
            case SPRINT -> { c.sprintX = x; c.sprintY = y; }
            case COORDINATES -> { c.coordinatesX = x; c.coordinatesY = y; }
            case HELD_ITEM -> { c.heldItemX = x; c.heldItemY = y; }
            case EFFECTS -> { c.effectsX = x; c.effectsY = y; }
            case PING -> { c.pingX = x; c.pingY = y; }
            case SPEED -> { c.speedX = x; c.speedY = y; }
            case CLOCK -> { c.clockX = x; c.clockY = y; }
            case TPS -> { c.tpsX = x; c.tpsY = y; }
            case BLOCK_INFO -> { c.blockInfoX = x; c.blockInfoY = y; }
        }
    }
    /** Preserve automatic corner anchoring when changing size/opacity, but not when dragging. */
    public static void keepOnScreen(Element element, int width, int height) {
        var b = bounds(element, width, height);
        var c = VortexClient.config;
        if (element == Element.ARMOR) {
            if (c.armorX != -1) c.armorX = b.x();
            if (c.armorY != -1) c.armorY = b.y();
        } else if (element == Element.HELD_ITEM) {
            if (c.heldItemX != -1) c.heldItemX = b.x();
            if (c.heldItemY != -1) c.heldItemY = b.y();
        } else if (element == Element.EFFECTS) {
            if (c.effectsX != -1) c.effectsX = b.x();
            c.effectsY = b.y();
        } else if (element == Element.BLOCK_INFO) {
            if (c.blockInfoX != -1) c.blockInfoX = b.x(); c.blockInfoY = b.y();
        } else position(element, b.x(), b.y());
    }
    public static void resetPositions() {
        position(Element.COORDINATES, 10, 10);
        position(Element.FPS, 110, 10); position(Element.KEYS, 10, 84);
        VortexClient.config.armorX = VortexClient.config.armorY = -1;
        VortexClient.config.heldItemX = VortexClient.config.heldItemY = -1;
        VortexClient.config.effectsX = -1; VortexClient.config.effectsY = 10;
        position(Element.SPRINT, 110, 150);
        position(Element.PING, 110, 38); position(Element.SPEED, 110, 66);
        position(Element.CLOCK, 110, 94); position(Element.TPS, 110, 122);
        VortexClient.config.blockInfoX = -1; VortexClient.config.blockInfoY = 92;
    }
    public static void draw(GuiGraphicsExtractor g, boolean preview) {
        var mc = Minecraft.getInstance();
        if (!preview && (mc.level == null || mc.player == null || mc.gui.hud.isHidden())) return;
        for (var element : Element.values()) {
            if (element == Element.FPS || !enabled(element)) continue;
            var b = bounds(element, g.guiWidth(), g.guiHeight());
            g.pose().pushMatrix();
            g.pose().translate(b.x(), b.y()); g.pose().scale(scale(element), scale(element));
            int alpha = (int)(opacity(element) * 255) << 24;
            switch (element) {
                case ARMOR -> {
                    var items = equipment(mc, preview);
                    var layout = armorLayout(VortexClient.config.armorHorizontal, equipmentTextWidths(mc, items));
                    for (int i = 0; i < items.size(); i++) {
                        int x = layout.iconX(i), y = layout.iconY(i);
                        var stack = items.get(i);
                        if (stack.isEmpty()) continue;
                        g.item(stack, x, y);
                        if (stack.isDamageableItem()) {
                            g.fill(x, y + 16, x + 16, y + 18, alpha);
                            int bar = armorBarWidth(stack.getMaxDamage(), stack.getDamageValue());
                            if (bar > 0) g.fill(x, y + 16, x + bar, y + 17, alpha | armorBarColor(stack.getMaxDamage(), stack.getDamageValue()));
                            String durability = armorDurability(stack.getMaxDamage(), stack.getDamageValue());
                            boolean low = VortexClient.config.durabilityAlerts && DurabilityWarnings.low(stack.getMaxDamage(), stack.getDamageValue(), VortexClient.config.durabilityThreshold);
                            g.text(mc.font, durability,
                                layout.textX(i, mc.font.width(durability)),
                                layout.horizontal() ? 22 : y + 4, alpha | (low ? 0xFF6868 : 0xFFFFFF), true);
                        }
                    }
                }
                case COORDINATES -> {
                    var values = coordinates(mc);
                    int textWidth = (int)(b.width() / scale(element)) - 1;
                    String[] axes = {"X: ", "Y: ", "Z: "};
                    int[] numbers = {values.x(), values.y(), values.z()};
                    for (int i = 0; i < axes.length; i++) {
                        g.text(mc.font, axes[i], 0, i * 11, alpha | 0x55FF55, true);
                        g.text(mc.font, Integer.toString(numbers[i]), mc.font.width(axes[i]), i * 11, alpha | 0xFFFFFF, true);
                    }
                    g.text(mc.font, mc.font.plainSubstrByWidth(values.direction(), textWidth), 0, 44, alpha | 0x55FF55, true);
                    String label = "Biome: ";
                    g.text(mc.font, label, 0, 55, alpha | 0x55FF55, true);
                    g.text(mc.font, mc.font.plainSubstrByWidth(values.biome(), Math.max(0, textWidth - mc.font.width(label))),
                        mc.font.width(label), 55, alpha | 0xA5C84C, true);
                }
                case SPRINT -> {
                    g.text(mc.font, mc.player != null && mc.player.isSprinting() ? "Sprint: ACTIVE" : "Sprint: READY", 7, 7, alpha | 0xF1B9DF, true);
                }
                case PING, SPEED, CLOCK, TPS -> g.text(mc.font, MetricsHud.readout(element, mc, preview), 7, 7, alpha | 0xFFFFFF, true);
                case BLOCK_INFO -> BlockInfo.draw(g, mc, preview, alpha, (int)(b.width() / scale(element)));
                case HELD_ITEM, EFFECTS -> ExtraHud.draw(g, mc, element, preview, alpha, (int)(b.width() / scale(element)));
                default -> { }
            }
            g.pose().popMatrix();
        }
    }
    public record ArmorLayout(boolean horizontal, int width, int height, int[] starts, int[] cells) {
        public int iconX(int index) { return horizontal ? starts[index] + (cells[index] - 16) / 2 : 0; }
        public int iconY(int index) { return horizontal ? 0 : index * 22; }
        public int textX(int index, int textWidth) { return horizontal ? starts[index] + (cells[index] - textWidth) / 2 : 22; }
    }
    public static ArmorLayout armorLayout(boolean horizontal, int[] textWidths) {
        if (textWidths.length < 1 || textWidths.length > 6) throw new IllegalArgumentException("One to six equipment slots are required");
        int[] starts = new int[textWidths.length], cells = new int[textWidths.length];
        int width = 0, verticalWidth = 54;
        for (int i = 0; i < textWidths.length; i++) {
            int numberWidth = Math.max(0, textWidths[i]);
            // Only the actual number, its one-pixel shadow and a two-pixel gap.
            // Do not reuse the vertical layout's 54px minimum for horizontal slots.
            cells[i] = Math.max(16, numberWidth == 0 ? 0 : numberWidth + 1);
            starts[i] = width;
            width += cells[i] + (i < textWidths.length - 1 ? 2 : 0);
            verticalWidth = Math.max(verticalWidth, 23 + numberWidth);
        }
        return new ArmorLayout(horizontal, horizontal ? width : verticalWidth, horizontal ? 34 : (textWidths.length - 1) * 22 + 18, starts, cells);
    }
    private static CoordinateReadout coordinates(Minecraft mc) {
        if (mc == null || mc.player == null || mc.level == null) {
            cachedBiome = null;
            return SAMPLE_COORDINATES;
        }
        var position = mc.player.blockPosition(); // Correct floor behavior below zero.
        var biome = mc.level.getBiome(position);
        if (biome != cachedBiome) {
            cachedBiome = biome;
            cachedBiomeName = biome.unwrapKey().map(key -> biomeName(key.identifier().getPath())).orElse("Unknown");
        }
        return new CoordinateReadout(position.getX(), position.getY(), position.getZ(), direction(mc.player.getYRot()), cachedBiomeName);
    }
    private static int coordinateWidth(Minecraft mc, CoordinateReadout values) {
        if (mc == null || mc.font == null) return 110;
        int width = Math.max(90, mc.font.width("Biome: " + values.biome()) + 1);
        for (String text : new String[] {"X: " + values.x(), "Y: " + values.y(), "Z: " + values.z(), values.direction()})
            width = Math.max(width, mc.font.width(text) + 1);
        return width;
    }
    public static java.util.List<ItemStack> equipment(Minecraft mc, boolean preview) {
        var items = new java.util.ArrayList<ItemStack>();
        if (preview && !hasArmor(mc)) {
            for (var stack : previewArmor()) if (!stack.isEmpty()) items.add(stack);
        } else if (mc != null && mc.player != null) {
            for (var slot : ARMOR) {
                var stack = mc.player.getItemBySlot(slot);
                if (!stack.isEmpty()) items.add(stack);
            }
        }
        if (VortexClient.config.heldItemEnabled) items.addAll(ExtraHud.heldItems(mc, preview));
        return items;
    }
    private static int[] equipmentTextWidths(Minecraft mc, java.util.List<ItemStack> items) {
        if (items.isEmpty()) return new int[] {0};
        return items.stream().mapToInt(stack -> stack.isDamageableItem()
            ? mc.font.width(armorDurability(stack.getMaxDamage(), stack.getDamageValue())) : 0).toArray();
    }
    private static int[] armorTextWidths(Minecraft mc) {
        if (mc == null || mc.font == null) return new int[] {18, 18, 18, 18};
        boolean preview = mc.player == null || mc.gui.screen() instanceof HudEditorScreen;
        return equipmentTextWidths(mc, equipment(mc, preview));
    }
    private static boolean hasArmor(Minecraft mc) {
        if (mc != null && mc.player != null) for (var slot : ARMOR) if (!mc.player.getItemBySlot(slot).isEmpty()) return true;
        return false;
    }
    private static ItemStack[] previewArmor() {
        if (sampleArmor == null) {
            Item[] items = {Items.NETHERITE_HELMET, Items.NETHERITE_CHESTPLATE, Items.NETHERITE_LEGGINGS, Items.NETHERITE_BOOTS};
            // The title-screen editor can open before 26.3 has bound item components.
            // Keep the movable outline usable and retry once the registries are ready.
            for (var item : items) if (BuiltInRegistries.ITEM.wrapAsHolder(item) instanceof Holder.Reference<Item> reference && !reference.areComponentsBound())
                return new ItemStack[] {ItemStack.EMPTY, ItemStack.EMPTY, ItemStack.EMPTY, ItemStack.EMPTY};
            sampleArmor = new ItemStack[items.length];
            int[] damage = {18, 16, 21, 15};
            for (int i = 0; i < sampleArmor.length; i++) {
                sampleArmor[i] = new ItemStack(items[i]);
                sampleArmor[i].setDamageValue(damage[i]);
            }
        }
        return sampleArmor;
    }
    private static void key(GuiGraphicsExtractor g, KeyMapping key, int x, int y, int width, boolean preview, int alpha) {
        var font = Minecraft.getInstance().font;
        boolean pressed = !preview && key.isDown();
        String label = font.plainSubstrByWidth(compactKeyLabel(key.saveString(), key.getTranslatedKeyMessage().getString()), width - 4);
        g.text(font, label, x + (width - font.width(label)) / 2, y + 9,
            alpha | (pressed ? 0xF1B9DF : 0xFFFFFF), true);
    }
}
