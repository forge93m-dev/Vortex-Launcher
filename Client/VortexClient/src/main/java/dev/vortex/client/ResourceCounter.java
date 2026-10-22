package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;

/** Counts carried items, not lifetime pickups. Never moves or changes an item. */
public final class ResourceCounter {
    public record Goal(@com.google.gson.annotations.SerializedName("item") String itemId, int target) { }
    private static Map<String,Integer> counts = Map.of();
    private static int ticks;
    public static boolean valid(Goal goal) {
        return goal != null && goal.itemId() != null && goal.itemId().length() <= 160 &&
            goal.itemId().matches("[a-z0-9_.-]+:[a-z0-9_./-]+") && goal.target() > 0 && goal.target() <= 1000000;
    }
    public static List<Goal> clean(List<Goal> goals) {
        var unique = new LinkedHashMap<String,Goal>();
        if (goals != null) for (var goal : goals) if (valid(goal) && unique.size() < 4) unique.put(goal.itemId(),goal);
        return List.copyOf(unique.values());
    }
    public static ItemStack icon(String id) {
        var key = Identifier.tryParse(id);
        if (key == null || !BuiltInRegistries.ITEM.containsKey(key)) return ItemStack.EMPTY;
        var item = BuiltInRegistries.ITEM.getValue(key);
        return item == null || item == Items.AIR ? ItemStack.EMPTY : new ItemStack(item);
    }
    public static String name(String id) { var stack=icon(id); return stack.isEmpty()?id:stack.getHoverName().getString(); }
    public static Map<String,Integer> count(Iterable<ItemStack> items) {
        var result=new HashMap<String,Integer>();
        for(var item:items) if(!item.isEmpty()) result.merge(BuiltInRegistries.ITEM.getKey(item.getItem()).toString(),item.getCount(),Integer::sum);
        return Map.copyOf(result);
    }
    public static int current(String id) { return counts.getOrDefault(id,0); }
    public static void tick(Minecraft mc) {
        if(mc.player==null || !VortexClient.config.resourceCounterEnabled){counts=Map.of();ticks=0;return;}
        if(++ticks%5!=0)return;
        var carried=new ArrayList<ItemStack>();
        var inventory=mc.player.getInventory();
        for(int i=0;i<Math.min(36,inventory.getContainerSize());i++)carried.add(inventory.getItem(i));
        carried.add(mc.player.getOffhandItem());
        counts=count(carried);
    }
    public static int height(){return Math.max(1,VortexClient.config.resourceGoals.size())*24;}
    public static void draw(GuiGraphicsExtractor g,boolean preview,int alpha,int width) {
        var goals=VortexClient.config.resourceGoals;
        if(goals.isEmpty()){g.text(Minecraft.getInstance().font,"Resource Counter: add a goal",0,5,alpha|0xFFFFFF,true);return;}
        int row=0;
        for(var goal:goals){
            int y=row++*24,have=preview?Math.min(32,goal.target()):current(goal.itemId());
            var item=icon(goal.itemId());if(!item.isEmpty())g.item(item,0,y+2);
            var font=Minecraft.getInstance().font;
            String progress=have+" / "+goal.target();
            int available=Math.max(0,width-24-font.width(progress)-8);
            g.text(font,font.plainSubstrByWidth(name(goal.itemId()),available),22,y,alpha|0xFFFFFF,true);
            g.text(font,progress,Math.max(22,width-font.width(progress)),y,alpha|(have>=goal.target()?0x96DBA2:0xFFFFFF),true);
            g.fill(22,y+14,width,y+16,alpha|0x333C40);
            int filled=(int)Math.round(Math.max(0,width-22)*Math.min(1,have/(double)goal.target()));
            if(filled>0)g.fill(22,y+14,22+filled,y+16,alpha|0x96DBA2);
        }
    }
}
