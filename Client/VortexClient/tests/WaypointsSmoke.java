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
        for(double[] vector:new double[][]{{0,0,-10},{0,0,-20000000}}){
            Object pin=project.invoke(null,vector[0],vector[1],vector[2],perspective,854,480);
            check(pin!=null,"Marker rejected by distance");Class<?> pinType=pin.getClass();
            int x=(int)pinType.getMethod("x").invoke(pin),y=(int)pinType.getMethod("y").invoke(pin);
            check(x>=0 && x<854 && y>=0 && y<480,"Projected marker escaped viewport");
            check(!(boolean)pinType.getMethod("edge").invoke(pin),"Waypoints must never use edge arrows");
        }
        for(double[] vector:new double[][]{{10000,0,-1},{0,0,10},{0,10000,-1}})check(project.invoke(null,vector[0],vector[1],vector[2],perspective,854,480)==null,"Behind-camera/offscreen marker must disappear, not stick to edge");
        check(project.invoke(null,10d,0d,-10d,perspective,854,480)!=null,"Marker near horizontal screen edge stays visible without aiming crosshair");
        check(project.invoke(null,0d,6d,-10d,perspective,854,480)!=null,"Marker near vertical screen edge stays visible");
        check(project.invoke(null,14d,0d,-10d,perspective,854,480)==null,"Marker outside horizontal FOV is hidden");
        check(project.invoke(null,0d,8d,-10d,perspective,854,480)==null,"Marker outside vertical FOV is hidden");
        Object turned=matrix.getConstructor().newInstance();matrix.getMethod("perspective",float.class,float.class,float.class,float.class).invoke(turned,(float)Math.toRadians(70),16f/9f,.05f,1000f);matrix.getMethod("rotateY",float.class).invoke(turned,(float)Math.PI);
        check(project.invoke(null,0d,0d,10d,turned,854,480)!=null&&project.invoke(null,0d,0d,-10d,turned,854,480)==null,"Visibility follows camera rotation, not a fixed world direction");
        check(project.invoke(null,Double.NaN,0d,-1d,perspective,854,480)==null,"Invalid projection");
        Class<?> screen=Class.forName("dev.vortex.client.WaypointsScreen",true,loader);
        for(int[] size:new int[][]{{320,240},{430,300},{854,480},{1920,1080}}){
            Object l=screen.getMethod("layout",int.class,int.class).invoke(null,size[0],size[1]);Class<?> type=l.getClass();
            int x=(int)type.getMethod("x").invoke(l),y=(int)type.getMethod("y").invoke(l),w=(int)type.getMethod("width").invoke(l),h=(int)type.getMethod("height").invoke(l);
            int rows=(int)type.getMethod("rows").invoke(l);
            check(x>=0 && y>=0 && x+w<=size[0] && y+h<=size[1] && 128+rows*44<=h-30,"Waypoint list rows overlap footer or viewport");
        }
        Class<?> block=Class.forName("dev.vortex.client.WaypointsStore$Block",true,loader);var blockCtor=block.getConstructor(int.class,int.class,int.class);
        Object picked=blockCtor.newInstance(10,65,-20),picked2=blockCtor.newInstance(11,65,-20);
        var multiCtor=point.getConstructor(UUID.class,String.class,String.class,String.class,int.class,int.class,int.class,int.class,boolean.class,String.class,List.class);
        Object multi=multiCtor.newInstance(id,world,"minecraft:overworld","Blocks",10,65,-20,0x7DDCBB,false,"Traps",List.of(picked,picked2));
        Path multiPath=root.resolve("multi-block/waypoints-v1.json");Object multiStore=store.getConstructor(Path.class).newInstance(multiPath);
        update.invoke(multiStore,multi,null);check(((List<?>)load.invoke(multiStore)).equals(List.of(multi)),"Selected real blocks must persist across restarts");
        Object multiHidden=point.getMethod("hidden",boolean.class).invoke(multi,true);check(point.getMethod("blocks").invoke(multiHidden).equals(List.of(picked,picked2)),"Hiding marker must preserve block selection");
        // Block picking is back (owner's request): H toggles the aimed block, outlines show selected blocks.
        Class<?> picker=Class.forName("dev.vortex.client.WaypointBlocks",true,loader);Method toggleBlock=picker.getMethod("toggle",List.class,block);
        List<?> chosen=(List<?>)toggleBlock.invoke(null,List.of(),picked);chosen=(List<?>)toggleBlock.invoke(null,chosen,picked2);
        check(chosen.equals(List.of(picked,picked2)),"Picking adds blocks in order");
        check(((List<?>)toggleBlock.invoke(null,chosen,picked)).equals(List.of(picked2)),"Picking the same block again unselects it");
        List<Object> full=new java.util.ArrayList<>();for(int i=0;i<256;i++)full.add(blockCtor.newInstance(i,64,0));
        check(((List<?>)toggleBlock.invoke(null,full,blockCtor.newInstance(999,64,0))).size()==256,"Block selection stays within the 256-block safety bound");
        check(((List<?>)toggleBlock.invoke(null,List.of(),blockCtor.newInstance(0,5000,0))).isEmpty(),"Invalid block coordinates are never selected");
        Method edges=picker.getMethod("silhouetteEdges",java.util.Collection.class);
        check(((List<?>)edges.invoke(null,List.of(picked))).size()==12,"One block has a 12-edge outline");
        check(((List<?>)edges.invoke(null,List.of(picked,picked2))).size()==16,"Neighbouring blocks merge into one outline without inner lines");
        check(((List<?>)edges.invoke(null,List.of(blockCtor.newInstance(0,0,0),blockCtor.newInstance(1,1,0)))).size()==23,"Blocks touching at an edge keep that edge once");
        double[] middle=(double[])picker.getMethod("center",List.class).invoke(null,List.of(picked,picked2,blockCtor.newInstance(10,66,-20)));
        check(Math.abs(middle[0]-(10.5+11.5+10.5)/3)<1e-9&&middle[1]==67&&middle[2]==-19.5,"Marker stands on top of the selection's centre");
        int[] anchor=(int[])picker.getMethod("anchor",List.class).invoke(null,List.of(picked,picked2));
        check(anchor[0]==11&&anchor[1]==65&&anchor[2]==-20,"Saved XYZ is the block under the selection's centre");
        Method faces=picker.getMethod("silhouetteFaces",java.util.Collection.class);
        check(((List<?>)faces.invoke(null,List.of(picked))).size()==6&&((List<?>)faces.invoke(null,List.of(picked,picked2))).size()==10,"Highlighted faces skip the faces between selected blocks");
        check(Arrays.stream(picker.getDeclaredMethods()).noneMatch(m->m.getName().equals("highlight")||m.getName().equals("fillPolygon")),"Block highlight must be world gizmos, not per-row GUI fills (FPS drop when standing on it)");
        check(Arrays.stream(picker.getDeclaredMethods()).anyMatch(m->m.getName().equals("emit")),"Block highlight emits world gizmos");
        Class<?> bindings=Class.forName("dev.vortex.client.ClientBindings$Action",true,loader);
        for(Object action:bindings.getEnumConstants())check(!action.toString().equals("WAYPOINT_BLOCK"),"Block picking uses H only while picking; it must not reserve a global key");
        Method matching=screen.getMethod("matching",List.class,String.class,String.class);
        check(((List<?>)matching.invoke(null,List.of(multi),"blocks -20","Traps")).size()==1&&((List<?>)matching.invoke(null,List.of(multi),"blocks","Bases")).isEmpty(),"Search matches all words and group filter");
        Class<?> edit=Class.forName("dev.vortex.client.WaypointEditScreen",true,loader);
        for(int[] size:new int[][]{{320,240},{430,300},{854,480}}){var l=edit.getMethod("layout",int.class,int.class).invoke(null,size[0],size[1]);var type=l.getClass();int h=(int)type.getMethod("height").invoke(l);boolean compact=(boolean)type.getMethod("compact").invoke(l);check(178<h-36&&(compact||233<h-36),"Waypoint form controls and preview overlap save actions");}
        System.out.println("PASS Waypoints full-viewport FOV (no crosshair cone/edge arrows/distance cap), block picking with outlines, legacy data preservation, search/groups and responsive list/editor");
    }
}
