import java.nio.file.*;
import java.util.*;
import java.lang.reflect.*;

public final class WaypointsSmoke {
    static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);}
    public static void run(ClassLoader loader,Path root)throws Exception {
        Class<?> store=Class.forName("dev.vortex.client.WaypointsStore",true,loader);
        Class<?> point=Class.forName("dev.vortex.client.WaypointsStore$Point",true,loader);
        Class<?> feature=Class.forName("dev.vortex.client.WaypointsFeature",true,loader);
        Method hash=feature.getMethod("worldKey",String.class,String.class);
        String world=(String)hash.invoke(null,"world","fixture-one"),other=(String)hash.invoke(null,"server","fixture-one");
        check(world.equals(hash.invoke(null,"world","fixture-one")) && !world.equals(other),"Stable, isolated world identities");
        Path path=root.resolve("waypoint-fixture/waypoints-v1.json");
        Object data=store.getConstructor(Path.class).newInstance(path);
        Method update=store.getMethod("update",point,UUID.class),load=store.getMethod("load");
        var ctor=point.getConstructor(UUID.class,String.class,String.class,String.class,int.class,int.class,int.class,int.class,boolean.class);
        UUID id=UUID.randomUUID();
        Object home=ctor.newInstance(id,world,"minecraft:overworld","Home",1,64,2,0xBDA5FF,false);
        update.invoke(data,home,null);
        Object reopened=store.getConstructor(Path.class).newInstance(path);
        check(((List<?>)load.invoke(reopened)).equals(List.of(home)),"Restart must preserve marker");
        Object hidden=point.getMethod("hidden",boolean.class).invoke(home,true);
        update.invoke(reopened,hidden,null);
        Method visible=store.getMethod("visible",List.class,String.class,String.class);
        check(((List<?>)visible.invoke(null,load.invoke(data),world,"minecraft:overworld")).isEmpty(),"Hidden marker must not render");
        check(((List<?>)load.invoke(data)).size()==1,"Hide must not delete");
        Object renamed=ctor.newInstance(id,world,"minecraft:overworld","Base",1,64,2,0xBDA5FF,false);
        update.invoke(data,renamed,null);
        Object nether=ctor.newInstance(UUID.randomUUID(),world,"minecraft:the_nether","Portal",0,70,0,0x44CCAA,false);
        Object server=ctor.newInstance(UUID.randomUUID(),other,"minecraft:overworld","Server",0,70,0,0x44CCAA,false);
        update.invoke(reopened,nether,null);update.invoke(data,server,null);
        check(((List<?>)load.invoke(data)).size()==3,"Independent writers preserve entries");
        check(((List<?>)visible.invoke(null,load.invoke(data),world,"minecraft:overworld")).equals(List.of(renamed)),"World/dimension isolation and rename");
        update.invoke(data,null,id);check(((List<?>)load.invoke(data)).size()==2,"Delete exact UUID");
        String broken="{broken";Files.writeString(path,broken);
        try{update.invoke(data,home,null);throw new AssertionError("Corrupt original overwritten");}catch(InvocationTargetException expected){}
        check(Files.readString(path).equals(broken),"Corrupt original must remain intact");
        check(store.getMethod("cleanName",String.class).invoke(null,"  A\nB§  ").equals("AB"),"Sanitize marker names");
        Object invalid=ctor.newInstance(id,world,"minecraft:overworld","Bad",Integer.MIN_VALUE,64,0,0,false);
        check(!(boolean)store.getMethod("valid",point).invoke(null,invalid),"Coordinate overflow");
        Class<?> matrix=Class.forName("org.joml.Matrix4f",true,loader),matrixInterface=Class.forName("org.joml.Matrix4fc",true,loader);
        Object perspective=matrix.getConstructor().newInstance();
        matrix.getMethod("perspective",float.class,float.class,float.class,float.class).invoke(perspective,(float)Math.toRadians(70),16f/9f,.05f,1000f);
        Class<?> projection=Class.forName("dev.vortex.client.WaypointProjection",true,loader);
        Method project=projection.getMethod("project",double.class,double.class,double.class,matrixInterface,int.class,int.class);
        for(double[] vector:new double[][]{{0,0,-10},{0,0,-20000000},{10000,0,-1},{0,0,10}}){
            Object pin=project.invoke(null,vector[0],vector[1],vector[2],perspective,854,480);
            check(pin!=null,"Marker rejected by distance");Class<?> pinType=pin.getClass();
            int x=(int)pinType.getMethod("x").invoke(pin),y=(int)pinType.getMethod("y").invoke(pin);
            check(x>=0 && x<854 && y>=0 && y<480,"Projected marker escaped viewport");
            if(vector[2]>0 || vector[0]>0)check((boolean)pinType.getMethod("edge").invoke(pin),"Offscreen direction arrow");
        }
        check(project.invoke(null,Double.NaN,0d,-1d,perspective,854,480)==null,"Invalid projection");
        Class<?> screen=Class.forName("dev.vortex.client.WaypointsScreen",true,loader);
        for(int[] size:new int[][]{{320,240},{430,300},{854,480},{1920,1080}}){
            Object l=screen.getMethod("layout",int.class,int.class).invoke(null,size[0],size[1]);Class<?> type=l.getClass();
            int x=(int)type.getMethod("x").invoke(l),y=(int)type.getMethod("y").invoke(l),w=(int)type.getMethod("width").invoke(l),h=(int)type.getMethod("height").invoke(l);
            int rows=(int)type.getMethod("rows").invoke(l);
            check(x>=0 && y>=0 && x+w<=size[0] && y+h<=size[1] && 64+rows*32<=h-8,"Waypoint layout bounds");
            check((int)type.getMethod("editorWidth").invoke(l)>=140 && h-66>=144,"Editor controls overlap");
        }
        System.out.println("PASS Waypoints restart/rename/hide/delete, concurrent stores, corrupt-file preservation, context isolation, unlimited-distance projection and responsive layout");
    }
}
