import java.nio.file.*;
import java.util.*;

public final class CollectionSmoke {
    public static void run(ClassLoader target,Path root) throws Exception {
        Class<?> storeType=Class.forName("dev.vortex.client.CosmeticCollection",true,target);
        Class<?> selection=Class.forName("dev.vortex.client.SkinPixels$Selection",true,target);
        Class<?> kind=Class.forName("dev.vortex.client.CosmeticCollection$Kind",true,target);
        Class<?> entry=Class.forName("dev.vortex.client.CosmeticCollection$Entry",true,target);
        Object skins=kind.getEnumConstants()[0],capes=kind.getEnumConstants()[1];
        Path folder=root.resolve("collection-test");Object store=storeType.getConstructor(Path.class).newInstance(folder);
        byte[] first=new byte[16384],second=new byte[16384];first[0]=12;second[0]=96;
        Object a=selection.getConstructor(boolean.class,boolean.class,byte[].class).newInstance(true,false,first);
        Object b=selection.getConstructor(boolean.class,boolean.class,byte[].class).newInstance(true,false,second);
        Object slim=selection.getConstructor(boolean.class,boolean.class,byte[].class).newInstance(true,true,first);
        var addSkin=storeType.getMethod("addSkin",selection);var entries=storeType.getMethod("entries",kind);
        Object skinA=addSkin.invoke(store,a),skinB=addSkin.invoke(store,b);addSkin.invoke(store,slim);addSkin.invoke(store,a);
        if(((List<?>)entries.invoke(store,skins)).size()!=3 || skinA.equals(skinB))throw new AssertionError("Skin collection lost designs, duplicates or classic/slim choices");
        store=storeType.getConstructor(Path.class).newInstance(folder);
        Object restored=storeType.getMethod("readSkin",entry).invoke(store,skinA);
        if(!Arrays.equals((byte[])selection.getMethod("rgba").invoke(restored),first))throw new AssertionError("Collection restart lost skin pixels");
        byte[] capeA=new byte[8192],capeB=new byte[8192];capeB[1]=53;
        var addCape=storeType.getMethod("addCape",byte[].class);Object savedCapeA=addCape.invoke(store,(Object)capeA);
        addCape.invoke(store,(Object)capeB);addCape.invoke(store,(Object)capeA);
        if(((List<?>)entries.invoke(store,capes)).size()!=2 || !Arrays.equals((byte[])storeType.getMethod("readCape",entry).invoke(store,savedCapeA),capeA))
            throw new AssertionError("Adding a cape overwrote the previous one or duplicated it");
        Class<?> capePixels=Class.forName("dev.vortex.client.CapePixels",true,target);
        Class<?> capeImage=Class.forName("dev.vortex.client.CapePixels$Image",true,target);
        Object hd=capeImage.getConstructor(int.class,int.class,byte[].class).newInstance(128,64,new byte[128*64*4]);
        byte[] packed=(byte[])capePixels.getMethod("pack",capeImage).invoke(null,hd);
        Object savedHd=addCape.invoke(store,(Object)packed);
        if(!Arrays.equals(packed,(byte[])storeType.getMethod("readCape",entry).invoke(store,savedHd)))throw new AssertionError("HD cape collection changes data");
        Object bad=entry.getConstructor(kind,String.class).newInstance(skins,"../../foreign");
        reject(()->storeType.getMethod("readSkin",entry).invoke(storeType.getConstructor(Path.class).newInstance(folder),bad));
        String id=(String)entry.getMethod("id").invoke(skinA);Path file=folder.resolve("skins/"+id+".skin");
        byte[] changed=Files.readAllBytes(file);changed[99]^=33;Files.write(file,changed);
        reject(()->storeType.getMethod("readSkin",entry).invoke(storeType.getConstructor(Path.class).newInstance(folder),skinA));
        if(!Arrays.equals(changed,Files.readAllBytes(file)))throw new AssertionError("Unreadable collection file was overwritten");
        Object limited=storeType.getConstructor(Path.class).newInstance(root.resolve("collection-limit"));
        for(int i=0;i<64;i++){byte[] pixels=new byte[8192];pixels[0]=(byte)i;addCape.invoke(limited,(Object)pixels);}
        byte[] tooMany=new byte[8192];tooMany[0]=(byte)129;
        reject(()->addCape.invoke(limited,(Object)tooMany));
        if(((List<?>)entries.invoke(limited,capes)).size()!=64)throw new AssertionError("Collection limit removed existing entries");
        Class<?> screen=Class.forName("dev.vortex.client.WardrobeScreen",false,target);
        for(int[] dims:new int[][]{{320,240},{430,300},{640,360},{854,480},{1920,1080}}) {
            Object layout=screen.getMethod("layout",int.class,int.class).invoke(null,dims[0],dims[1]);Class<?> t=layout.getClass();
            int w=(int)t.getMethod("listWidth").invoke(layout),cols=(int)t.getMethod("columns").invoke(layout),tw=(int)t.getMethod("tileWidth").invoke(layout);
            int top=(int)t.getMethod("galleryTop").invoke(layout),bottom=(int)t.getMethod("galleryBottom").invoke(layout),y=(int)t.getMethod("y").invoke(layout),h=(int)t.getMethod("height").invoke(layout);
            if(tw<70 || cols*tw+(cols-1)*6>w || top<y+96 || bottom>y+h-62 || bottom-top<48)throw new AssertionError("Collection tiles overlap controls/footer at GUI scale");
            int content=((68+cols-1)/cols)*54-6,offset=Math.max(0,content-(bottom-top));
            if(top+content-offset>bottom)throw new AssertionError("Last saved collection row cannot be reached");
        }
        Class<?> ui=Class.forName("dev.vortex.client.ClientUi",false,target);
        for(String name:new String[]{"PANEL","SURFACE"}){var f=ui.getDeclaredField(name);f.setAccessible(true);int c=f.getInt(null);
            if((c>>>24)!=255 || (c>>16&255)>40 || (c>>8&255)>40 || (c&255)>40)throw new AssertionError("Dark theme has translucent or bright base surfaces");}
        Class<?> cameraType=Class.forName("dev.vortex.client.WardrobePreview$Camera",true,target);
        Object camera=cameraType.getConstructor().newInstance();var focus=cameraType.getMethod("focusCape",boolean.class);
        var step=cameraType.getMethod("step",double.class,boolean.class);var view=cameraType.getMethod("view");
        focus.invoke(camera,true);step.invoke(camera,.016,true);Object middle=view.invoke(camera);Class<?> viewType=middle.getClass();
        float zoom=(float)viewType.getMethod("zoom").invoke(middle),angle=(float)viewType.getMethod("yaw").invoke(middle);
        if(zoom<=1 || zoom>=1.65 || angle<=25 || angle>=180)throw new AssertionError("Cape focus must animate, not snap");
        step.invoke(camera,0d,false);Object capeView=view.invoke(camera);
        if((float)viewType.getMethod("zoom").invoke(capeView)!=1.65f || (float)viewType.getMethod("yaw").invoke(capeView)!=180f ||
            (float)viewType.getMethod("focusY").invoke(capeView)!=1.12f)throw new AssertionError("Cape framing/back view");
        cameraType.getMethod("drag",double.class,double.class).invoke(camera,30d,5d);step.invoke(camera,Double.NaN,true);
        if(!Float.isFinite((float)viewType.getMethod("yaw").invoke(view.invoke(camera))))throw new AssertionError("Manual rotation/invalid frame delta");
        focus.invoke(camera,false);step.invoke(camera,0d,false);Object front=view.invoke(camera);
        if((float)viewType.getMethod("zoom").invoke(front)!=1 || (float)viewType.getMethod("yaw").invoke(front)!=25)
            throw new AssertionError("Skin tab must restore full front framing");
        System.out.println("PASS Saved skin/cape collections: restart, deduplication, slim variants, original/HD bytes, checksum/path rejection, 64-item bound, reachable gallery rows and opaque dark theme");
    }
    private interface Check {void run() throws Exception;}
    private static void reject(Check action) throws Exception {try{action.run();}catch(java.lang.reflect.InvocationTargetException expected){return;}throw new AssertionError("Invalid collection request was accepted");}
}
