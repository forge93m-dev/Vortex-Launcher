package dev.vortex.client;
import java.util.Map;
import java.util.function.Consumer;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.enchantment.ItemEnchantments;

public final class EnchantmentHints {
    private static final Map<String,String> TEXT=Map.ofEntries(
        Map.entry("protection","Reduces most incoming damage."),Map.entry("fire_protection","Reduces fire damage and burn time."),Map.entry("feather_falling","Reduces fall damage."),Map.entry("blast_protection","Reduces explosion damage and knockback."),Map.entry("projectile_protection","Reduces projectile damage."),Map.entry("respiration","Lets you breathe underwater longer."),Map.entry("aqua_affinity","Removes the underwater mining penalty."),Map.entry("thorns","May damage attackers; costs durability."),Map.entry("depth_strider","Increases underwater movement speed."),Map.entry("frost_walker","Freezes water beneath your feet."),Map.entry("binding_curse","Cannot be removed normally once worn."),Map.entry("sharpness","Increases melee damage."),Map.entry("smite","Deals more damage to undead mobs."),Map.entry("bane_of_arthropods","More damage and slowness to arthropods."),Map.entry("knockback","Pushes melee targets farther away."),Map.entry("fire_aspect","Sets melee targets on fire."),Map.entry("looting","Improves drops from killed mobs."),Map.entry("sweeping_edge","Increases sweeping attack damage."),Map.entry("efficiency","Speeds up mining with suitable tools."),Map.entry("silk_touch","Drops supported blocks intact."),Map.entry("unbreaking","Reduces how often durability is consumed."),Map.entry("fortune","Increases drops from supported blocks."),Map.entry("power","Increases arrow damage."),Map.entry("punch","Increases arrow knockback."),Map.entry("flame","Sets arrows on fire."),Map.entry("infinity","Normal arrows are not consumed."),Map.entry("luck_of_the_sea","Improves the chance of fishing treasure."),Map.entry("lure","Reduces the wait for a fishing bite."),Map.entry("loyalty","Returns a thrown trident to you."),Map.entry("impaling","More trident damage to aquatic mobs."),Map.entry("riptide","Launches you when wet; replaces throwing."),Map.entry("channeling","Can summon lightning during thunderstorms."),Map.entry("multishot","Fires multiple projectiles per shot."),Map.entry("quick_charge","Speeds up crossbow loading."),Map.entry("piercing","Crossbow arrows pass through targets."),Map.entry("mending","Uses collected XP to repair the item."),Map.entry("vanishing_curse","Item disappears when you die."),Map.entry("soul_speed","Faster movement on soul sand and soil."),Map.entry("swift_sneak","Increases movement speed while sneaking."),Map.entry("density","Increases mace smash damage per fall distance."),Map.entry("breach","Reduces the effectiveness of target armor."),Map.entry("wind_burst","Launches you after a successful mace smash."),Map.entry("lunge","Adds a forward burst to spear attacks.")
    );
    public static String describe(String id){return id!=null && id.startsWith("minecraft:")?TEXT.get(id.substring(10)):null;}
    public static void append(ItemEnchantments enchantments,Consumer<Component> output){
        if(!VortexClient.config.enchantmentHintsEnabled)return;
        enchantments.keySet().stream().sorted(java.util.Comparator.comparing(h->h.unwrapKey().map(k->k.identifier().toString()).orElse(""))).forEach(holder->{
            String id=holder.unwrapKey().map(k->k.identifier().toString()).orElse(""),description=describe(id);
            if(description!=null){output.accept(Component.literal(HudModules.biomeName(id.substring(10))).withStyle(net.minecraft.ChatFormatting.GRAY));output.accept(Component.literal("  "+description).withStyle(net.minecraft.ChatFormatting.DARK_GRAY));}
        });
    }
}
