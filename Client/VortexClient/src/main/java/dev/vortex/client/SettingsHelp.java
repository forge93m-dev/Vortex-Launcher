package dev.vortex.client;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
public final class SettingsHelp {
    public static String describe(ClientModule module){
        if(module==null)return "Client settings affect this instance only.\n\nHUD scale changes overlay size. Opacity changes visibility. Edit HUD moves overlays without changing Minecraft's inventory or hotbar.\n\nDisable UI animations for instant transitions. Notifications explain completed actions and errors. Keybinds lets you change or unbind Vortex shortcuts.";
        String detail=switch(module){
            case RESOURCE_COUNTER->"Tracks up to four item goals using items currently in your inventory and offhand, not lifetime pickups. Moving items into a chest reduces the count. Add goals by item ID or the held item. HUD Editor moves and resizes the counter. Goals are saved for this instance.";
            case STORAGE_MEMORY->"Remembers only server-received contents of chests, barrels and shulker boxes you manually opened. Search by item name or registry ID; all words must match. Results are nearest first within your chosen radius and show coordinates and last-seen age. Contents may have changed. Ender chests, minecarts and virtual server menus are not indexed. History is private, session-only, capped at 256 containers, and cleared on disconnect, dimension/world/profile change or disabling the module.";
            case CHEST_SEARCH->"Ctrl+F focuses search in an open chest or shulker. All words must match. -gold excludes gold. Use #food, #armor, #weapon, #tool, #block, #enchanted or #damaged. ench:mending finds an enchantment; dur:<25% finds low remaining durability. Example: #tool ench:efficiency -gold dur:>=50%. Unsupported filters match nothing. Search never moves items.";
            case WAYPOINTS->"B opens saved markers. Type a group name such as Bases, Farms or Travel to organize them. The left group selector filters the list. Hide next to a group hides all its markers; individual Hide keeps the item saved. No waypoint distance cutoff. Each world/server and dimension is separate.";
            case DEATH_WAYPOINTS->"On death, coordinates and dimension are captured once and saved in the Deaths group. Respawning does not move the marker. Open Waypoints to rename, hide or remove it. An on-screen notice reports coordinates; a storage error never claims the marker was saved. Markers are local and not broadcast.";
            case COMPACT_SHIELD->"Shield size changes only first-person appearance. At 72% the shield is smaller and lower, leaving more view unobstructed. Blocking, cooldowns, hitboxes, inventory icons and third-person appearance stay unchanged. Set 100% or disable the module for vanilla appearance.";
            case ENCHANTMENT_HINTS->"Adds plain-English explanations to supported vanilla enchantments on items and enchanted books. Vanilla enchantment names and levels are retained. Unknown mod enchantments are not guessed. This changes descriptions only, not enchantment effects.";
            case ENTITY_HEARTS->"Shows health over visible mobs. Player HP requires a server-published HEARTS score; unknown health is omitted, never guessed as full. Players and Mobs can be toggled separately. Visible range limits clutter and visibility checks. Large health pools use a compact proportional row plus numeric HP. Absorption is not base health.";
            case SHULKER_PREVIEW->"Hover over a shulker box to see its 27 item slots and counts. Empty boxes show an empty grid; server-side unresolved loot is unavailable. Preview reads existing item data only. It never opens containers or sends inventory actions.";
            case ARMOR->"Shows worn armor and optional held-item durability without backgrounds. Remaining durability is not damage taken. Horizontal layout orders helmet to boots from left to right. Edit HUD changes position and scale. Low-durability alerts are configured separately.";
            case CROSSHAIR->"Size changes arm length, gap changes the space around the center, and thickness changes line width. Outline improves contrast. Dot adds a center point. The preview shows appearance; aim and weapon accuracy do not change.";
            case ZOOM->"Hold the zoom key and scroll to adjust magnification. Higher strength narrows your field of view more. Releasing the key returns to normal. Keybind changes the shortcut; zoom scrolling does not switch hotbar slots.";
            case CHAT->"Timestamps show local arrival time. Collapse repeats folds adjacent identical messages without changing signed content. Different senders and message styles remain distinct.";
            case FREELOOK->"Hold its key in third person to look around without rotating movement direction. Sensitivity changes camera response. Release the key to return to the normal camera.";
            case FULLBRIGHT->"Brightens rendering locally. It does not place light sources, stop mob spawning or change server lighting. Some servers restrict visual-assistance mods; follow their rules.";
            case COORDINATES->"Shows XYZ, facing direction and biome. Coordinates reflect your current dimension. Edit HUD moves or resizes the overlay; it does not move the player.";
            case EFFECTS->"Shows active potion effects and remaining duration. Edit HUD changes placement and scale. Timers come from received game state; this does not add or extend effects.";
            case SPRINT->"Tap sprint once to toggle instead of holding it. Hunger and server movement rules still apply. Disable for vanilla key behavior.";
            case TPS->"An estimate from received server timing, not a direct server performance measurement. Network delays can affect it. Edit HUD changes position and appearance.";
            case PING->"Shows latency reported by the server. It is not FPS or TPS. Edit HUD changes position and appearance.";
            case FPS->"Shows the game's current frame-rate counter. It does not improve FPS by itself. Edit HUD changes position, scale and opacity.";
            case SPEED->"Shows horizontal movement speed in blocks per second. Vertical falling is excluded. It measures movement; it does not speed up the player.";
            case CLOCK->"Shows your computer's local time, not Minecraft world time. Edit HUD changes position and appearance.";
            case BLOCK_INFO->"Shows information about the block under your crosshair. It does not reveal blocks behind walls. Edit HUD changes placement and scale.";
        };
        return module.title+"\n\n"+detail+"\n\nEnabled turns only this module on or off. Settings are saved for the current instance.";
    }
    public static String control(String label,ClientModule module){
        String text=label.toLowerCase(java.util.Locale.ROOT);
        if(text.startsWith("enabled") || text.startsWith("disabled"))return "Turn this module on or off. Other modules and vanilla settings are unchanged.";
        if(text.contains("opacity"))return "Overlay transparency: lower values are more transparent. This does not change visibility of game objects.";
        if(text.contains("scale"))return "Visual size of this overlay. Position can be adjusted in Edit HUD.";
        if(text.contains("key") || text.contains("hold"))return "Choose a shortcut. Press Escape to cancel capture or Delete to unbind. Conflicts between Vortex shortcuts are detected.";
        if(text.contains("shield size"))return "First-person shield size: 50–100%. Lower values leave more screen visible; blocking is unchanged.";
        return label+"\n\n"+(module==null?"Changes the current instance's interface. Use ? for examples and details.":module.description+". Use ? for examples, limits and controls.");
    }
    public static void attach(AbstractWidget widget,ClientModule module){widget.setTooltip(Tooltip.create(Component.literal(control(widget.getMessage().getString(),module))));}
}
