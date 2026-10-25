package dev.vortex.client;

import java.util.*;
import java.util.concurrent.Executors;
import java.util.function.Consumer;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.world.level.storage.LevelResource;
import org.joml.Matrix4f;

public final class WaypointsFeature {
    public record Scope(String world,String dimension){}
    private static final java.util.concurrent.ExecutorService IO=Executors.newSingleThreadExecutor(r->{var t=new Thread(r,"Vortex waypoints");t.setDaemon(true);return t;});
    private static List<WaypointsStore.Point> points=List.of(),visible=List.of();
    private static Scope scope;
    private static Object level;
    private static boolean loaded,busy,keyDown;
    public static String message="Markers are visible anywhere inside your field of view.";
    public static final int[] COLORS={0xBDA5FF,0x7DDCBB,0x85B9FF,0xFFD178,0xFF969F,0xF2A7DD,0xE7EBF2,0xA8D981};
    public static Scope scope(){return scope;}
    public static boolean busy(){return busy;}
    public static boolean ready(){return loaded;}
    public static List<WaypointsStore.Point> points(){return scope==null?List.of():points.stream().filter(p->p.world().equals(scope.world()) && p.dimension().equals(scope.dimension())).sorted(Comparator.comparing(WaypointsStore.Point::name,String.CASE_INSENSITIVE_ORDER)).toList();}
    public static String worldKey(String kind,String identity){try{return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest((kind+":"+identity).getBytes(StandardCharsets.UTF_8)));}catch(Exception error){throw new IllegalStateException(error);}}
    public static List<WaypointsStore.Point> visiblePoints(){return visible;}
    public static List<String> groups(){return points().stream().map(WaypointsStore.Point::group).distinct().sorted(String.CASE_INSENSITIVE_ORDER).toList();}
    private static String groupKey(String group){return scope==null?"":worldKey("group",scope.world()+":"+scope.dimension()+":"+group);}
    public static boolean groupHidden(String group){return VortexClient.config.hiddenWaypointGroups.contains(groupKey(group));}
    public static void toggleGroup(String group){String key=groupKey(group);if(key.isEmpty())return;var set=VortexClient.config.hiddenWaypointGroups;if(!set.remove(key))set.add(key);VortexClient.config.save();refreshVisible();}
    private static void refreshVisible(){visible=scope==null?List.of():WaypointsStore.visible(points,scope.world(),scope.dimension()).stream().filter(p->!groupHidden(p.group())).toList();}
    public static void load(){if(busy)return;busy=true;IO.execute(()->{try{var read=WaypointsStore.local().load();Minecraft.getInstance().execute(()->{points=read;loaded=true;busy=false;refreshVisible();message="Private local waypoints. No distance limit.";});}catch(Exception invalid){Minecraft.getInstance().execute(()->{busy=false;loaded=false;message="Could not read waypoints. Original file was preserved.";});}});}
    public static void save(WaypointsStore.Point point,UUID remove,Consumer<Boolean> done){
        if(busy || !loaded){message="Wait for waypoints to load, then retry.";done.accept(false);return;}busy=true;
        IO.execute(()->{try{var saved=WaypointsStore.local().update(point,remove);Minecraft.getInstance().execute(()->{points=saved;busy=false;refreshVisible();message=remove==null?"Waypoint saved.":"Waypoint removed.";done.accept(true);});}catch(Exception error){Minecraft.getInstance().execute(()->{busy=false;message=error instanceof IllegalArgumentException||error instanceof IllegalStateException?error.getMessage():"Could not save waypoints. Check disk space and file permissions.";done.accept(false);});}});
    }
    public static void open(Screen parent,boolean create){if(scope==null){ClientNotifications.info("waypoint-world","Waypoints","Join a world or server first.");return;}if(!loaded)load();var list=new WaypointsScreen(parent,false);Minecraft.getInstance().gui.setScreen(create?new WaypointEditScreen(list,null):list);}
    public static void tick(Minecraft mc){
        if(level!=mc.level || scope==null && mc.level!=null){level=mc.level;scope=null;
            if(mc.level!=null){String identity=null,kind=null;
                if(mc.hasSingleplayerServer() && mc.getSingleplayerServer()!=null){identity=mc.getSingleplayerServer().getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize().toString();kind="world";}
                else if(mc.getCurrentServer()!=null){identity=PartyFeature.serverKey(mc.getCurrentServer().ip);kind="server";}
                if(identity!=null)scope=new Scope(worldKey(kind,identity),mc.level.dimension().identifier().toString());
            }refreshVisible();if(scope!=null && !loaded && !busy)load();
        }
        boolean down=mc.isWindowActive() && ClientBindings.down(ClientBindings.Action.WAYPOINTS);
        // While picking blocks the waypoints key means "done", handled by WaypointBlocks.
        if(WaypointBlocks.picking()){WaypointBlocks.tick(mc);keyDown=true;return;}
        if(VortexClient.config.waypointsEnabled && down && !keyDown && mc.player!=null && mc.gui.screen()==null && mc.gui.overlay()==null)open(null,false);keyDown=down;
    }
    public static void draw(GuiGraphicsExtractor g){
        var mc=Minecraft.getInstance();var storageMarker=StorageMemory.marker();
        var partyPoints=PartyFeature.worldPoints();
        if(!VortexClient.config.waypointsEnabled && storageMarker==null && !WaypointBlocks.picking() && partyPoints.isEmpty() || mc.level==null || mc.player==null || mc.gui.hud.isHidden() || mc.gui.screen()!=null)return;
        var camera=mc.gameRenderer.mainCamera();var origin=camera.position();var matrix=camera.getViewRotationProjectionMatrix(new Matrix4f());var font=UiFonts.body();
        List<WaypointsStore.Point> renderPoints=VortexClient.config.waypointsEnabled?visible:List.of();
        if(storageMarker!=null||!partyPoints.isEmpty()){renderPoints=new ArrayList<>(renderPoints);if(storageMarker!=null)renderPoints.add(storageMarker);renderPoints.addAll(partyPoints);}
        // Block highlights are world gizmos emitted each tick by WaypointBlocks.emit; only pins and labels are GUI.
        WaypointBlocks.drawHint(g);
        for(var point:renderPoints){
            // A marker with blocks stands on top of their exact centre (it can fall between two blocks).
            double[] at=point.blocks().isEmpty()?new double[]{point.x()+.5,point.y()+1,point.z()+.5}:WaypointBlocks.center(point.blocks());
            double dx=at[0]-origin.x,dy=at[1]-origin.y,dz=at[2]-origin.z;
            var pin=WaypointProjection.project(dx,dy,dz,matrix,g.guiWidth(),g.guiHeight());if(pin==null)continue;
            double meters=Math.sqrt(dx*dx+dy*dy+dz*dz);
            WaypointUi.marker(g,font,pin.x(),pin.y(),point.color(),point.name(),WaypointProjection.distance(meters),
                WaypointUi.showsName(pin.x(),pin.y(),g.guiWidth(),g.guiHeight(),meters),g.guiWidth(),g.guiHeight());
        }
    }
}
