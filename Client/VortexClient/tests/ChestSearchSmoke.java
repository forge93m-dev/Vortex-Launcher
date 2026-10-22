import java.util.*;

public final class ChestSearchSmoke {
    public static void run(ClassLoader target)throws Exception{
        Class<?> search=Class.forName("dev.vortex.client.ChestSearch",true,target),query=Class.forName("dev.vortex.client.ChestSearch$Query",true,target);
        var matches=query.getMethod("matches",String.class,String.class);
        Object words=query.getConstructor(String.class).newInstance(" DIAMOND sword ");
        if(!(boolean)matches.invoke(words,"Renamed tool","minecraft:diamond_sword") || (boolean)matches.invoke(words,"Diamond","minecraft:diamond"))throw new AssertionError("Literal all-word registry/name search failed");
        Object russian=query.getConstructor(String.class).newInstance("АЛМАЗ");if(!(boolean)matches.invoke(russian,"Алмазный меч","minecraft:diamond_sword"))throw new AssertionError("Localized case-insensitive matching failed");
        if((boolean)query.getMethod("active").invoke(query.getConstructor(String.class).newInstance("   ")) || (boolean)matches.invoke(query.getConstructor(String.class).newInstance("[.*"),"Diamond","minecraft:diamond"))throw new AssertionError("Empty/literal query semantics");
        Class<?> inventory=Class.forName("net.minecraft.world.entity.player.Inventory",true,target),player=Class.forName("net.minecraft.world.entity.player.Player",false,target),equipment=Class.forName("net.minecraft.world.entity.EntityEquipment",true,target);
        Object inv=inventory.getConstructor(player,equipment).newInstance(null,equipment.getConstructor().newInstance());
        Class<?> simple=Class.forName("net.minecraft.world.SimpleContainer",true,target),container=Class.forName("net.minecraft.world.Container",true,target),stack=Class.forName("net.minecraft.world.item.ItemStack",true,target),items=Class.forName("net.minecraft.world.item.Items",true,target);
        Object chest=simple.getConstructor(int.class).newInstance(27),diamond=items.getField("DIAMOND").get(null),stone=items.getField("STONE").get(null);
        Object d=diamond.getClass().getMethod("getDefaultInstance").invoke(diamond),s=stone.getClass().getMethod("getDefaultInstance").invoke(stone);
        var set=simple.getMethod("setItem",int.class,stack);set.invoke(chest,0,d);set.invoke(chest,1,s);inventory.getMethod("setItem",int.class,stack).invoke(inv,0,d);
        Class<?> chestMenu=Class.forName("net.minecraft.world.inventory.ChestMenu",true,target),menu=Class.forName("net.minecraft.world.inventory.AbstractContainerMenu",true,target);
        Object opened=chestMenu.getMethod("threeRows",int.class,inventory,container).invoke(null,1,inv,chest);
        Class<?> counter=Class.forName("dev.vortex.client.ResourceCounter",true,target),memory=Class.forName("dev.vortex.client.StorageMemory",true,target);
        Map<?,?> counted=(Map<?,?>)counter.getMethod("count",Iterable.class).invoke(null,List.of(d,s,d));
        if(!Integer.valueOf(2).equals(counted.get("minecraft:diamond")) || !Integer.valueOf(1).equals(counted.get("minecraft:stone")))throw new AssertionError("Resource counter did not sum real stacks by registry ID");
        List<?> remembered=(List<?>)memory.getMethod("contents",menu).invoke(null,opened);
        if(remembered.size()!=2)throw new AssertionError("Storage snapshot includes player inventory or empty slots");
        Class<?> gameMode=Class.forName("net.minecraft.client.multiplayer.MultiPlayerGameMode",false,target);
        if(Arrays.stream(gameMode.getDeclaredMethods()).noneMatch(m->m.getName().contains("vortex$storageInteraction")) || Arrays.stream(menu.getDeclaredMethods()).noneMatch(m->m.getName().contains("vortex$storageContents")))throw new AssertionError("Storage capture mixins were not applied to real interaction and menu updates");
        Class<?> state=Class.forName("dev.vortex.client.ChestSearch$State",true,target);Object filter=state.getConstructor().newInstance();
        state.getMethod("query",String.class).invoke(filter,"diamond");state.getMethod("refresh",menu).invoke(filter,opened);
        if((int)state.getMethod("found").invoke(filter)!=1 || (int)state.getMethod("total").invoke(filter)!=2 || (boolean)state.getMethod("matches",int.class).invoke(filter,27))throw new AssertionError("Search included player inventory/empty slots");
        set.invoke(chest,1,d);state.getMethod("refresh",menu).invoke(filter,opened);if((int)state.getMethod("found").invoke(filter)!=1)throw new AssertionError("Search recomputed unnecessarily inside a draw pass");
        state.getMethod("changed").invoke(filter);state.getMethod("refresh",menu).invoke(filter,opened);if((int)state.getMethod("found").invoke(filter)!=2)throw new AssertionError("Changed container content was not refreshed");
        if(simple.getMethod("getItem",int.class).invoke(chest,0)!=d || (int)stack.getMethod("getCount").invoke(d)!=1)throw new AssertionError("Search changed a real item stack");
        Object large=chestMenu.getMethod("sixRows",int.class,inventory,container).invoke(null,2,inv,simple.getConstructor(int.class).newInstance(54));
        if((int)search.getMethod("storageSlots",menu).invoke(null,large)!=54 || (int)search.getMethod("storageSlots",menu).invoke(null,new Object[]{null})!=0)throw new AssertionError("Storage type/size gate");
        for(int height:new int[]{240,360,480})for(int imageHeight:new int[]{166,222}){
            int top=(height-imageHeight)/2;Object l=search.getMethod("layout",int.class,int.class,int.class,int.class,int.class).invoke(null,72,top,176,imageHeight,height);
            int y=(int)l.getClass().getMethod("y").invoke(l),h=(int)l.getClass().getMethod("height").invoke(l);
            if(y<0 || y+h>height)throw new AssertionError("Search field outside scaled window");
            if((boolean)l.getClass().getMethod("compact").invoke(l) && y+h>top+18)throw new AssertionError("Compact field overlaps item slots");
        }
        verifyInput(target,opened,inv);
        System.out.println("PASS Chest Search: localized/literal/all-word names and IDs, cached real chest contents, no player-slot/item mutations, scaled layout and actual focused keyboard routing");
    }
    private static void verifyInput(ClassLoader target,Object menu,Object inv)throws Exception{
        Class<?> mcType=Class.forName("net.minecraft.client.Minecraft",false,target),font=Class.forName("net.minecraft.client.gui.Font",true,target),provider=Class.forName("net.minecraft.client.gui.Font$Provider",true,target);
        var unsafeField=sun.misc.Unsafe.class.getDeclaredField("theUnsafe");unsafeField.setAccessible(true);var unsafe=(sun.misc.Unsafe)unsafeField.get(null);
        Object mc=unsafe.allocateInstance(mcType),emptyFont=font.getConstructor(provider).newInstance(new Object[]{null});
        var fontField=mcType.getField("font");fontField.setAccessible(true);fontField.set(mc,emptyFont);var instance=mcType.getDeclaredField("instance");instance.setAccessible(true);Object previous=instance.get(null);instance.set(null,mc);
        Class<?> app=Class.forName("dev.vortex.client.VortexClient",true,target);Object config=app.getField("config").get(null);var enabled=config.getClass().getField("chestSearchEnabled");boolean old=enabled.getBoolean(config);enabled.setBoolean(config,true);
        try{
            Class<?> screen=Class.forName("net.minecraft.client.gui.screens.Screen",true,target),base=Class.forName("net.minecraft.client.gui.screens.inventory.AbstractContainerScreen",true,target),chest=Class.forName("net.minecraft.client.gui.screens.inventory.ContainerScreen",true,target),component=Class.forName("net.minecraft.network.chat.Component",true,target);
            Object ui=chest.getConstructor(menu.getClass(),inv.getClass(),component).newInstance(menu,inv,component.getMethod("literal",String.class).invoke(null,"Chest"));
            screen.getField("width").setInt(ui,320);screen.getField("height").setInt(ui,240);var init=base.getDeclaredMethod("init");init.setAccessible(true);init.invoke(ui);
            var field=base.getDeclaredField("vortex$search");field.setAccessible(true);Object text=field.get(ui);if(text==null)throw new AssertionError("No search field in actual chest screen");
            Class<?> key=Class.forName("net.minecraft.client.input.KeyEvent",true,target),inputs=Class.forName("com.mojang.blaze3d.platform.InputConstants",true,target);
            var keyCtor=key.getConstructor(int.class,int.class,int.class);var press=base.getMethod("keyPressed",key);
            // Set only fixture focus state: native SDL IME requires a real window.
            Class<?> widget=Class.forName("net.minecraft.client.gui.components.AbstractWidget",true,target);var focused=widget.getDeclaredField("focused");focused.setAccessible(true);focused.setBoolean(text,true);
            for(String name:new String[]{"KEY_E","KEY_Q","KEY_1"})if(!(boolean)press.invoke(ui,keyCtor.newInstance(inputs.getField(name).getInt(null),0,0)))throw new AssertionError("Search typing escaped into vanilla item actions: "+name);
            focused.setBoolean(text,false);
        }finally{enabled.setBoolean(config,old);instance.set(null,previous);}
    }
}
