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
    public static String message="Private local waypoints. No distance limit.";
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
    public static void open(Screen parent,boolean create){if(scope==null){ClientNotifications.info("waypoint-world","Waypoints","Join a world or server first.");return;}if(!loaded)load();Minecraft.getInstance().gui.setScreen(new WaypointsScreen(parent,create));}
    public static void tick(Minecraft mc){
        if(level!=mc.level || scope==null && mc.level!=null){level=mc.level;scope=null;
            if(mc.level!=null){String identity=null,kind=null;
                if(mc.hasSingleplayerServer() && mc.getSingleplayerServer()!=null){identity=mc.getSingleplayerServer().getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize().toString();kind="world";}
                else if(mc.getCurrentServer()!=null){identity=PartyFeature.serverKey(mc.getCurrentServer().ip);kind="server";}
                if(identity!=null)scope=new Scope(worldKey(kind,identity),mc.level.dimension().identifier().toString());
            }refreshVisible();if(scope!=null && !loaded && !busy)load();
        }
        boolean down=mc.isWindowActive() && ClientBindings.down(ClientBindings.Action.WAYPOINTS);
        if(VortexClient.config.waypointsEnabled && down && !keyDown && mc.player!=null && mc.gui.screen()==null && mc.gui.overlay()==null)open(null,true);keyDown=down;
    }
    public static void draw(GuiGraphicsExtractor g){
        var mc=Minecraft.getInstance();if(!VortexClient.config.waypointsEnabled || mc.level==null || mc.player==null || mc.gui.hud.isHidden() || mc.gui.screen() instanceof HudEditorScreen)return;
        var camera=mc.gameRenderer.mainCamera();var origin=camera.position();var matrix=camera.getViewRotationProjectionMatrix(new Matrix4f());var font=UiFonts.body();
        for(var point:visible){
            double dx=point.x()+.5-origin.x,dy=point.y()+1-origin.y,dz=point.z()+.5-origin.z;
            var pin=WaypointProjection.project(dx,dy,dz,matrix,g.guiWidth(),g.guiHeight());if(pin==null)continue;
            int x=pin.x(),y=pin.y(),color=0xFF000000|point.color();
            if(pin.edge()){g.pose().pushMatrix();g.pose().translate(x,y);g.pose().rotate((float)pin.angle());g.fill(3,-1,8,1,color);g.fill(4,-3,6,-1,color);g.fill(4,1,6,3,color);g.pose().popMatrix();}
            ClientUi.rounded(g,x-4,y-4,8,8,4,color);ClientUi.rounded(g,x-1,y-1,2,2,1,0xFFFFFFFF);
            String name=font.plainSubstrByWidth(point.name(),112),distance=WaypointProjection.distance(Math.sqrt(dx*dx+dy*dy+dz*dz));
            int tx=Math.clamp(x-font.width(name)/2,3,Math.max(3,g.guiWidth()-font.width(name)-3));
            int labelY=Math.min(y+8,g.guiHeight()-24);g.text(font,name,tx,labelY,0xFFF3F4F7,true);
            g.text(font,distance,Math.clamp(x-font.width(distance)/2,3,Math.max(3,g.guiWidth()-font.width(distance)-3)),labelY+10,0xFFB6BCC9,true);
        }
    }
}
