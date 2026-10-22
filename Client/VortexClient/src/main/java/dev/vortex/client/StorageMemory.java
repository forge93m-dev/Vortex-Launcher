package dev.vortex.client;

import java.util.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.core.BlockPos;
import net.minecraft.world.inventory.AbstractContainerMenu;
import net.minecraft.world.inventory.ShulkerBoxMenu;
import net.minecraft.world.level.block.*;
import net.minecraft.world.level.block.state.properties.ChestType;
import net.minecraft.world.phys.BlockHitResult;

/** Session-only, server-received snapshots of manually opened storage. No chunk or block-entity scans. */
public final class StorageMemory {
    public record Item(String id,String name,int count) { }
    public record Seen(String scope,BlockPos pos,String title,List<Item> items,long time) {
        public Seen { pos=pos.immutable();items=List.copyOf(items); }
    }
    public record Match(Seen storage,int count,double distance) { }
    public static final class Index {
        private final LinkedHashMap<BlockPos,Seen> seen=new LinkedHashMap<>();
        private String scope="";
        public void context(String next){if(!Objects.equals(scope,next)){scope=next;seen.clear();}}
        public int size(){return seen.size();}
        public void clear(){seen.clear();}
        public void observe(Seen snapshot){
            if(snapshot==null || !Objects.equals(scope,snapshot.scope()) || snapshot.items()==null || snapshot.items().size()>54)return;
            seen.remove(snapshot.pos());seen.put(snapshot.pos(),snapshot);
            while(seen.size()>256)seen.remove(seen.keySet().iterator().next());
        }
        public List<Match> find(String search,BlockPos origin,int radius){
            if(search==null || search.isBlank() || origin==null)return List.of();
            var query=new ChestSearch.Query(search);
            var results=new ArrayList<Match>();
            for(var snapshot:seen.values()){
                double distance=Math.sqrt(snapshot.pos().distSqr(origin));
                if(distance>Math.clamp(radius,16,256))continue;
                int count=0;for(var item:snapshot.items())if(query.matches(item.name(),item.id()))count+=item.count();
                if(count>0)results.add(new Match(snapshot,count,distance));
            }
            results.sort(Comparator.comparingDouble(Match::distance));return List.copyOf(results);
        }
    }
    private static final Index INDEX=new Index();
    private record Pending(String scope,BlockPos pos,boolean shulker,long time) { }
    private static Pending pending;
    private static AbstractContainerMenu activeMenu;
    private static BlockPos activePos;
    private static String context="";
    private static WaypointsStore.Point marker;
    private static Object level;
    private static String scope(Minecraft mc){
        var scope=WaypointsFeature.scope();
        return mc.player==null || scope==null?"":scope.world()+"/"+scope.dimension()+"/"+mc.player.getUUID();
    }
    public static void tick(Minecraft mc){
        String next=scope(mc);
        if(level!=mc.level || !Objects.equals(context,next) || !VortexClient.config.storageMemoryEnabled){
            level=mc.level;context=next;INDEX.context("");INDEX.context(next);pending=null;activeMenu=null;activePos=null;marker=null;
        }
        if(mc.player==null || mc.player.containerMenu!=activeMenu){activeMenu=null;activePos=null;}
        if(pending!=null && System.nanoTime()-pending.time()>3_000_000_000L)pending=null;
    }
    public static void interaction(BlockHitResult hit){
        var mc=Minecraft.getInstance();pending=null;
        if(mc==null || !VortexClient.config.storageMemoryEnabled || mc.level==null || mc.player==null || mc.gui.screen()!=null || scope(mc).isEmpty())return;
        BlockPos pos=hit.getBlockPos().immutable();var state=mc.level.getBlockState(pos);var block=state.getBlock();
        if(!(block instanceof ChestBlock) && !(block instanceof BarrelBlock) && !(block instanceof ShulkerBoxBlock))return;
        // Both halves of the same double chest share one canonical location.
        if(block instanceof ChestBlock && state.getValue(ChestBlock.TYPE)==ChestType.RIGHT)pos=pos.relative(ChestBlock.getConnectedDirection(state));
        pending=new Pending(scope(mc),pos,block instanceof ShulkerBoxBlock,System.nanoTime());
    }
    public static List<Item> contents(AbstractContainerMenu menu){
        var items=new ArrayList<Item>();
        for(int i=0;i<Math.min(ChestSearch.storageSlots(menu),menu.slots.size());i++){
            var stack=menu.slots.get(i).getItem();if(stack.isEmpty())continue;
            String name=stack.getHoverName().getString();if(name.length()>128)name=name.substring(0,128);
            items.add(new Item(net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(stack.getItem()).toString(),name,Math.clamp(stack.getCount(),1,1000000)));
        }
        return List.copyOf(items);
    }
    public static void received(AbstractContainerMenu menu,boolean full){
        var mc=Minecraft.getInstance();
        if(mc==null || !VortexClient.config.storageMemoryEnabled || mc.player==null || mc.level==null || mc.player.containerMenu!=menu ||
            !(mc.gui.screen() instanceof AbstractContainerScreen<?> screen) || screen.getMenu()!=menu || ChestSearch.storageSlots(menu)==0)return;
        if(full && pending!=null && System.nanoTime()-pending.time()<3_000_000_000L && pending.scope().equals(scope(mc)) && pending.shulker()==(menu instanceof ShulkerBoxMenu)){
            activeMenu=menu;activePos=pending.pos();pending=null;
        }
        if(activeMenu!=menu || activePos==null)return;
        String title=screen.getTitle().getString();if(title.length()>80)title=title.substring(0,80);
        INDEX.context(scope(mc));INDEX.observe(new Seen(scope(mc),activePos,title,contents(menu),System.currentTimeMillis()));
        if(full && marker!=null && marker.x()==activePos.getX() && marker.y()==activePos.getY() && marker.z()==activePos.getZ())marker=null;
    }
    public static WaypointsStore.Point markerFor(Seen seen,WaypointsFeature.Scope world){
        if(seen==null || world==null || !seen.scope().startsWith(world.world()+"/"+world.dimension()+"/"))return null;
        var p=seen.pos();return new WaypointsStore.Point(new UUID(0,1),world.world(),world.dimension(),"Last seen: "+seen.title(),p.getX(),p.getY(),p.getZ(),0x96DBA2,false,"Storage Finder");
    }
    public static boolean mark(Match match){
        var mc=Minecraft.getInstance();
        if(mc==null || match==null || !VortexClient.config.storageMemoryEnabled || !context.equals(scope(mc)) || !context.equals(match.storage().scope()))return false;
        marker=markerFor(match.storage(),WaypointsFeature.scope());return marker!=null;
    }
    public static WaypointsStore.Point marker(){
        var mc=Minecraft.getInstance();return mc==null || !VortexClient.config.storageMemoryEnabled || !context.equals(scope(mc))?null:marker;
    }
    public static void clearMarker(){marker=null;}
    public static int size(){return INDEX.size();}
    public static List<Match> find(String search){var mc=Minecraft.getInstance();return !VortexClient.config.storageMemoryEnabled || mc.player==null || !context.equals(scope(mc))?List.of():INDEX.find(search,mc.player.blockPosition(),VortexClient.config.storageMemoryRadius);}
    public static void clear(){INDEX.clear();pending=null;activeMenu=null;activePos=null;marker=null;}
}
