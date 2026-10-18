import java.util.*;
import java.lang.reflect.*;

public final class InventoryHealthSmoke {
    static void check(boolean value,String message){if(!value)throw new AssertionError(message);}
    public static void run(ClassLoader loader)throws Exception{
        Class<?> stack=Class.forName("net.minecraft.world.item.ItemStack",true,loader),items=Class.forName("net.minecraft.world.item.Items",true,loader);
        Class<?> component=Class.forName("net.minecraft.core.component.DataComponentType",true,loader),components=Class.forName("net.minecraft.core.component.DataComponents",true,loader);
        Class<?> container=Class.forName("net.minecraft.world.item.component.ItemContainerContents",true,loader);
        Object shulker=items.getField("SHULKER_BOX").get(null);shulker=shulker.getClass().getMethod("getDefaultInstance").invoke(shulker);
        Object diamond=items.getField("DIAMOND").get(null);diamond=diamond.getClass().getMethod("getDefaultInstance").invoke(diamond);stack.getMethod("setCount",int.class).invoke(diamond,32);
        var slots=new ArrayList<Object>(Collections.nCopies(27,stack.getField("EMPTY").get(null)));slots.set(0,diamond);slots.set(26,diamond);
        Object contents=container.getMethod("fromItems",List.class).invoke(null,slots),containerKey=components.getField("CONTAINER").get(null);
        stack.getMethod("set",component,Object.class).invoke(shulker,containerKey,contents);
        Class<?> app=Class.forName("dev.vortex.client.VortexClient",true,loader);Object config=app.getField("config").get(null);
        var enabled=config.getClass().getField("shulkerPreviewEnabled");boolean previous=enabled.getBoolean(config);enabled.setBoolean(config,true);
        try{
            var image=stack.getMethod("getTooltipImage");
            Optional<?> shown=(Optional<?>)image.invoke(shulker);check(shown.isPresent(),"Real ItemStack tooltip hook missing");
            Object data=shown.get();List<?> snapshot=(List<?>)data.getClass().getMethod("items").invoke(data);
            check(snapshot.size()==27 && (int)stack.getMethod("getCount").invoke(snapshot.get(26))==32 && (boolean)stack.getMethod("isEmpty").invoke(snapshot.get(1)),"27-slot positions/counts/empty slots");
            check((Object)((Optional<?>)image.invoke(shulker)).get()==data,"Unchanged tooltip must reuse snapshot");
            stack.getMethod("setCount",int.class).invoke(snapshot.get(0),1);
        }finally{enabled.setBoolean(config,previous);}
        verifyTooltip(loader,shulker,diamond,stack,components,component,container,contents,containerKey,config);
        verifyHealth(loader);
        System.out.println("PASS Real shulker tooltip injection/factory, 27-slot snapshots, live updates, disabled isolation and authoritative health-score rules");
    }
    private static void verifyTooltip(ClassLoader loader,Object shulker,Object diamond,Class<?> stack,Class<?> components,Class<?> component,Class<?> container,Object contents,Object containerKey,Object config)throws Exception{
        var enabled=config.getClass().getField("shulkerPreviewEnabled");boolean previous=enabled.getBoolean(config);enabled.setBoolean(config,true);
        try{
            var copies=(java.util.stream.Stream<?>)container.getMethod("itemCopies").invoke(contents);
            check((int)stack.getMethod("getCount").invoke(copies.toList().get(0))==32,"Preview mutated stored shulker items");
            var image=stack.getMethod("getTooltipImage");Object data=((Optional<?>)image.invoke(shulker)).get();
            Class<?> tooltip=Class.forName("net.minecraft.world.inventory.tooltip.TooltipComponent",true,loader),client=Class.forName("net.minecraft.client.gui.screens.inventory.tooltip.ClientTooltipComponent",true,loader),font=Class.forName("net.minecraft.client.gui.Font",true,loader);
            Object grid=client.getMethod("create",tooltip).invoke(null,data);
            check(grid.getClass().getName().equals("dev.vortex.client.ShulkerPreview$Grid"),"Actual tooltip factory did not accept custom grid");
            check((int)client.getMethod("getWidth",font).invoke(grid,new Object[]{null})==166 && (int)client.getMethod("getHeight",font).invoke(grid,new Object[]{null})==66,"Tooltip layout");
            stack.getMethod("set",component,Object.class).invoke(shulker,containerKey,container.getField("EMPTY").get(null));
            Object changed=((Optional<?>)image.invoke(shulker)).get();check(changed!=data,"Changed contents stayed cached");
            check(((Optional<?>)image.invoke(diamond)).isEmpty(),"Non-shulker tooltip changed");
            enabled.setBoolean(config,false);check(((Optional<?>)image.invoke(shulker)).isEmpty(),"Disabled preview still injected");
            enabled.setBoolean(config,true);
            Object colors=Class.forName("net.minecraft.world.item.Items",true,loader).getField("DYED_SHULKER_BOX").get(null);
            for(var field:colors.getClass().getRecordComponents()){
                Object item=field.getAccessor().invoke(colors),dyed=item.getClass().getMethod("getDefaultInstance").invoke(item);
                check(((Optional<?>)image.invoke(dyed)).isPresent(),"Colored shulker missing: "+field.getName());
            }
            Class<?> key=Class.forName("net.minecraft.resources.ResourceKey",true,loader),id=Class.forName("net.minecraft.resources.Identifier",true,loader),registries=Class.forName("net.minecraft.core.registries.Registries",true,loader),loot=Class.forName("net.minecraft.world.item.component.SeededContainerLoot",true,loader);
            Object table=key.getMethod("create",key,id).invoke(null,registries.getField("LOOT_TABLE").get(null),id.getMethod("withDefaultNamespace",String.class).invoke(null,"chests/simple_dungeon"));
            Object deferred=loot.getConstructor(key,long.class).newInstance(table,1L);
            stack.getMethod("set",component,Object.class).invoke(shulker,components.getField("CONTAINER_LOOT").get(null),deferred);
            Object unknown=((Optional<?>)image.invoke(shulker)).get();
            check((boolean)unknown.getClass().getMethod("unknown").invoke(unknown),"Deferred server loot must not be labelled empty");
        }finally{enabled.setBoolean(config,previous);}
    }
    private static void verifyHealth(ClassLoader loader)throws Exception{
        Class<?> health=Class.forName("dev.vortex.client.EntityHearts",true,loader),board=Class.forName("net.minecraft.world.scores.Scoreboard",true,loader),holder=Class.forName("net.minecraft.world.scores.ScoreHolder",true,loader),slot=Class.forName("net.minecraft.world.scores.DisplaySlot",true,loader),objective=Class.forName("net.minecraft.world.scores.Objective",true,loader),criteria=Class.forName("net.minecraft.world.scores.criteria.ObjectiveCriteria",true,loader),render=Class.forName("net.minecraft.world.scores.criteria.ObjectiveCriteria$RenderType",true,loader),component=Class.forName("net.minecraft.network.chat.Component",true,loader),format=Class.forName("net.minecraft.network.chat.numbers.NumberFormat",true,loader);
        Object scores=board.getConstructor().newInstance(),player=holder.getMethod("forNameOnly",String.class).invoke(null,"FixturePlayer");
        var read=health.getMethod("playerHealth",board,holder,float.class);
        check(read.invoke(null,scores,player,20f)==null,"Unknown player HP must not invent 20/20");
        var add=board.getMethod("addObjective",String.class,criteria,component,render,boolean.class,format);
        Object title=component.getMethod("literal",String.class).invoke(null,"Fixture");
        Object arbitrary=add.invoke(scores,"coins",criteria.getField("DUMMY").get(null),title,render.getField("INTEGER").get(null),false,null);
        board.getMethod("setDisplayObjective",slot,objective).invoke(scores,slot.getField("BELOW_NAME").get(null),arbitrary);
        check(read.invoke(null,scores,player,20f)==null,"Arbitrary scores must not become HP");
        Object hp=add.invoke(scores,"health",criteria.getField("DUMMY").get(null),title,render.getField("HEARTS").get(null),false,null);
        board.getMethod("setDisplayObjective",slot,objective).invoke(scores,slot.getField("BELOW_NAME").get(null),hp);
        check(read.invoke(null,scores,player,20f)==null,"Missing heart score must remain unknown");
        check(((Collection<?>)board.getMethod("getTrackedPlayers").invoke(scores)).isEmpty(),"Reading absent HP created a score");
        Object access=board.getMethod("getOrCreatePlayerScore",holder,objective).invoke(scores,player,hp);
        Class.forName("net.minecraft.world.scores.ScoreAccess",true,loader).getMethod("set",int.class).invoke(access,7);
        Object value=read.invoke(null,scores,player,20f);
        check(value!=null && (float)value.getClass().getMethod("current").invoke(value)==7,"Server heart score not used");
        check(board.getMethod("listPlayerScores",objective).invoke(scores,hp) instanceof Collection<?> c && c.size()==1,"Reading health mutated scoreboard");
        check(health.getMethod("checked",float.class,float.class,boolean.class).invoke(null,Float.NaN,20f,false)==null,"Nonfinite HP");
        var fills=health.getMethod("fill",float.class,float.class,int.class);
        check((int)fills.invoke(null,7f,20f,2)==2 && (int)fills.invoke(null,7f,20f,3)==1 && (int)fills.invoke(null,7f,20f,4)==0,"Full/half/empty hearts");
        check((int)health.getMethod("hearts",float.class).invoke(null,300f)==10,"Boss heart rows must remain bounded");
    }
}
