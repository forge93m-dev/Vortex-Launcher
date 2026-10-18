import java.nio.file.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.function.Consumer;

public final class AtlasQoLSmoke {
    static void check(boolean value,String message){if(!value)throw new AssertionError(message);}
    public static void run(ClassLoader loader,Path root)throws Exception{
        groups(loader,root);searchAndHints(loader);shield(loader);death(loader);helpAndRemoval(loader);
        System.out.println("PASS Old waypoint migration and groups, death dedup, shield pose restoration, enchantment tooltips, advanced filters and removed map classes");
    }
    static void groups(ClassLoader l,Path root)throws Exception{
        Class<?> store=Class.forName("dev.vortex.client.WaypointsStore",true,l),point=Class.forName("dev.vortex.client.WaypointsStore$Point",true,l),feature=Class.forName("dev.vortex.client.WaypointsFeature",true,l),scope=Class.forName("dev.vortex.client.WaypointsFeature$Scope",true,l);
        Path path=root.resolve("old-waypoints.json");String world="a".repeat(64),dimension="minecraft:overworld";UUID id=UUID.randomUUID();
        Files.writeString(path,"{\"version\":1,\"points\":[{\"id\":\""+id+"\",\"world\":\""+world+"\",\"dimension\":\"minecraft:overworld\",\"name\":\"Old base\",\"x\":1,\"y\":64,\"z\":2,\"color\":123,\"hidden\":false}]}");
        Object data=store.getConstructor(Path.class).newInstance(path);List<?> old=(List<?>)store.getMethod("load").invoke(data);check(point.getMethod("group").invoke(old.getFirst()).equals("General"),"Old waypoints lost default group");
        Object grouped=point.getConstructor(UUID.class,String.class,String.class,String.class,int.class,int.class,int.class,int.class,boolean.class,String.class).newInstance(id,world,dimension,"Base",1,64,2,123,false,"Bases");
        store.getMethod("update",point,UUID.class).invoke(data,grouped,null);List<?> loaded=(List<?>)store.getMethod("load").invoke(data);check(point.getMethod("group").invoke(loaded.getFirst()).equals("Bases"),"Group persistence");
        check(point.getMethod("group").invoke(point.getMethod("hidden",boolean.class).invoke(grouped,true)).equals("Bases"),"Individual hiding lost group");
        Object config=Class.forName("dev.vortex.client.VortexClient",true,l).getField("config").get(null);Field hidden=config.getClass().getField("hiddenWaypointGroups");Object previousHidden=hidden.get(config);
        var scopeField=feature.getDeclaredField("scope");var pointsField=feature.getDeclaredField("points");var visibleField=feature.getDeclaredField("visible");for(var field:new Field[]{scopeField,pointsField,visibleField})field.setAccessible(true);
        Object previousScope=scopeField.get(null),previousPoints=pointsField.get(null),previousVisible=visibleField.get(null);
        try{
            scopeField.set(null,scope.getConstructor(String.class,String.class).newInstance(world,dimension));pointsField.set(null,loaded);
            String hiddenKey=(String)feature.getMethod("worldKey",String.class,String.class).invoke(null,"group",world+":"+dimension+":Bases");hidden.set(config,new HashSet<>(List.of(hiddenKey)));
            var refresh=feature.getDeclaredMethod("refreshVisible");refresh.setAccessible(true);refresh.invoke(null);
            check(((List<?>)feature.getMethod("visiblePoints").invoke(null)).isEmpty() && ((List<?>)feature.getMethod("points").invoke(null)).size()==1,"Group hiding must not delete markers");
            hidden.set(config,new HashSet<>());refresh.invoke(null);check(((List<?>)feature.getMethod("visiblePoints").invoke(null)).size()==1,"Group show");
        }finally{scopeField.set(null,previousScope);pointsField.set(null,previousPoints);visibleField.set(null,previousVisible);hidden.set(config,previousHidden);}
    }
    static void searchAndHints(ClassLoader l)throws Exception{
        Class<?> stack=Class.forName("net.minecraft.world.item.ItemStack",true,l),items=Class.forName("net.minecraft.world.item.Items",true,l),query=Class.forName("dev.vortex.client.ChestSearch$Query",true,l);
        Object pick=item(items,"DIAMOND_PICKAXE"),apple=item(items,"APPLE");stack.getMethod("setDamageValue",int.class).invoke(pick,1500);
        check(matches(query,stack,"#tool #damaged dur:<25% -gold",pick),"Compound durability/type/negative filters");check(!matches(query,stack,"#food",pick) && matches(query,stack,"#food -gold",apple),"Food category");
        check(!matches(query,stack,"dur:>101",pick) && !matches(query,stack,"ench:",pick) && !matches(query,stack,"#unknown",pick),"Invalid filters must not broaden results");
        Class<?> enchant=Class.forName("net.minecraft.world.item.enchantment.Enchantment",true,l),definition=Class.forName("net.minecraft.world.item.enchantment.Enchantment$EnchantmentDefinition",true,l),cost=Class.forName("net.minecraft.world.item.enchantment.Enchantment$Cost",true,l),holderSet=Class.forName("net.minecraft.core.HolderSet",true,l),component=Class.forName("net.minecraft.network.chat.Component",true,l),dataMap=Class.forName("net.minecraft.core.component.DataComponentMap",true,l);
        Object empty=holderSet.getMethod("empty").invoke(null),oneCost=enchant.getMethod("constantCost",int.class).invoke(null,1);
        Object def=definition.getConstructor(holderSet,Optional.class,int.class,int.class,cost,cost,int.class,List.class).newInstance(empty,Optional.empty(),1,1,oneCost,oneCost,1,List.of());
        Object value=enchant.getConstructor(component,definition,holderSet,dataMap).newInstance(component.getMethod("literal",String.class).invoke(null,"Mending"),def,empty,dataMap.getField("EMPTY").get(null));
        Class<?> key=Class.forName("net.minecraft.resources.ResourceKey",true,l),identifier=Class.forName("net.minecraft.resources.Identifier",true,l),registries=Class.forName("net.minecraft.core.registries.Registries",true,l),owner=Class.forName("net.minecraft.core.HolderOwner",true,l),ref=Class.forName("net.minecraft.core.Holder$Reference",true,l),holder=Class.forName("net.minecraft.core.Holder",true,l);
        Object enchantKey=key.getMethod("create",key,identifier).invoke(null,registries.getField("ENCHANTMENT").get(null),identifier.getMethod("withDefaultNamespace",String.class).invoke(null,"mending"));
        Object bound=ref.getMethod("createStandAlone",owner,key).invoke(null,null,enchantKey);var bind=ref.getDeclaredMethod("bindValue",Object.class);bind.setAccessible(true);bind.invoke(bound,value);var bindTags=ref.getDeclaredMethod("bindTags",Collection.class);bindTags.setAccessible(true);bindTags.invoke(bound,List.of());
        Class<?> enchants=Class.forName("net.minecraft.world.item.enchantment.ItemEnchantments",true,l),mutable=Class.forName("net.minecraft.world.item.enchantment.ItemEnchantments$Mutable",true,l),dataType=Class.forName("net.minecraft.core.component.DataComponentType",true,l),components=Class.forName("net.minecraft.core.component.DataComponents",true,l);
        Object builder=mutable.getConstructor(enchants).newInstance(enchants.getField("EMPTY").get(null));mutable.getMethod("set",holder,int.class).invoke(builder,bound,1);Object added=mutable.getMethod("toImmutable").invoke(builder);
        stack.getMethod("set",dataType,Object.class).invoke(pick,components.getField("ENCHANTMENTS").get(null),added);
        check(matches(query,stack,"#tool ench:mending dur:<25%",pick) && !matches(query,stack,"-ench:mending",pick),"Enchantment filters with namespace/localized descriptions");
        Class<?> context=Class.forName("net.minecraft.world.item.Item$TooltipContext",true,l),flag=Class.forName("net.minecraft.world.item.TooltipFlag",true,l),getter=Class.forName("net.minecraft.core.component.DataComponentGetter",true,l);
        var lines=new ArrayList<Object>();enchants.getMethod("addToTooltip",context,Consumer.class,flag,getter).invoke(added,context.getField("EMPTY").get(null),(Consumer<Object>)lines::add,flag.getField("NORMAL").get(null),pick);
        check(lines.stream().anyMatch(line->{try{return ((String)component.getMethod("getString").invoke(line)).contains("repair");}catch(Exception e){throw new RuntimeException(e);}}),"Actual enchantment tooltip injection did not explain Mending");
        Class<?> hints=Class.forName("dev.vortex.client.EnchantmentHints",true,l);check(hints.getMethod("describe",String.class).invoke(null,"other:custom")==null,"Unknown mod enchantments must not be guessed");
    }
    static Object item(Class<?> items,String field)throws Exception{Object item=items.getField(field).get(null);return item.getClass().getMethod("getDefaultInstance").invoke(item);}
    static boolean matches(Class<?> query,Class<?> stack,String text,Object item)throws Exception{return (boolean)query.getMethod("matches",stack).invoke(query.getConstructor(String.class).newInstance(text),item);}
    static void shield(ClassLoader l)throws Exception{
        Class<?> renderer=Class.forName("net.minecraft.client.renderer.FirstPersonHandsAndItemsRenderer",false,l),shield=Class.forName("dev.vortex.client.CompactShield",true,l),stack=Class.forName("net.minecraft.world.item.ItemStack",true,l),items=Class.forName("net.minecraft.world.item.Items",true,l);
        check(Arrays.stream(renderer.getDeclaredMethods()).anyMatch(m->m.getName().contains("vortex$small")) && Arrays.stream(renderer.getDeclaredMethods()).anyMatch(m->m.getName().contains("vortex$restore")),"Actual hand renderer hooks missing");
        Object config=Class.forName("dev.vortex.client.VortexClient",true,l).getField("config").get(null);Field enabled=config.getClass().getField("compactShieldEnabled");boolean before=enabled.getBoolean(config);
        try{enabled.setBoolean(config,true);float small=(float)shield.getMethod("scale",stack).invoke(null,item(items,"SHIELD"));check(small>=.5 && small<1,"Shield size");check((float)shield.getMethod("scale",stack).invoke(null,item(items,"DIAMOND_SWORD"))==1,"Shield mode changed sword");
            Class<?> mc=Class.forName("net.minecraft.client.Minecraft",false,l),poseType=Class.forName("com.mojang.blaze3d.vertex.PoseStack",true,l),hand=Class.forName("net.minecraft.world.InteractionHand",true,l),callback=Class.forName("org.spongepowered.asm.mixin.injection.callback.CallbackInfo",true,l);
            Object hands=renderer.getConstructor(mc).newInstance(new Object[]{null}),pose=poseType.getConstructor().newInstance(),info=callback.getConstructor(String.class,boolean.class).newInstance("fixture",false);
            Method start=Arrays.stream(renderer.getDeclaredMethods()).filter(m->m.getName().contains("vortex$small")).findFirst().orElseThrow(),finish=Arrays.stream(renderer.getDeclaredMethods()).filter(m->m.getName().contains("vortex$restore")).findFirst().orElseThrow();start.setAccessible(true);finish.setAccessible(true);
            Object[] args={null,null,0f,0f,hand.getField("MAIN_HAND").get(null),0f,item(items,"SHIELD"),0f,pose,null,0,info};start.invoke(hands,args);check(!(boolean)poseType.getMethod("isEmpty").invoke(pose),"Shield did not isolate its transform");finish.invoke(hands,args);check((boolean)poseType.getMethod("isEmpty").invoke(pose),"Shield leaked pose transforms into other hands");
            enabled.setBoolean(config,false);check((float)shield.getMethod("scale",stack).invoke(null,item(items,"SHIELD"))==1,"Disabled shield still shrinks");}finally{enabled.setBoolean(config,before);}
    }
    static void death(ClassLoader l)throws Exception{
        Class<?> gate=Class.forName("dev.vortex.client.DeathWaypoints$Gate",true,l);Object state=gate.getConstructor().newInstance(),player=new Object();var died=gate.getMethod("died",Object.class,boolean.class);
        check(!(boolean)died.invoke(state,player,false) && (boolean)died.invoke(state,player,true) && !(boolean)died.invoke(state,player,true),"Repeated death ticks created duplicates");
        check(!(boolean)died.invoke(state,new Object(),false),"Respawning created a death");
        check(Arrays.stream(Class.forName("net.minecraft.client.multiplayer.ClientPacketListener",false,l).getDeclaredMethods()).anyMatch(m->m.getName().contains("vortex$death")),"Instant-respawn health packet hook missing");
    }
    static void helpAndRemoval(ClassLoader l)throws Exception{
        for(String name:new String[]{"WorldMap","WorldMapScreen","MapCanvas","MapTileStore"}){
            try{Class.forName("dev.vortex.client."+name,false,l);throw new AssertionError("Removed map class still packaged: "+name);}catch(ClassNotFoundException expected){}
        }
        Class<?> bindings=Class.forName("dev.vortex.client.ClientBindings$Action",true,l);for(Object action:bindings.getEnumConstants())check(!action.toString().equals("WORLD_MAP"),"Map key still registered");
        Class<?> module=Class.forName("dev.vortex.client.ClientModule",true,l),help=Class.forName("dev.vortex.client.SettingsHelp",true,l);for(Object entry:module.getEnumConstants())check(((String)help.getMethod("describe",module).invoke(null,entry)).length()>100,"Missing module explanation");
    }
}
