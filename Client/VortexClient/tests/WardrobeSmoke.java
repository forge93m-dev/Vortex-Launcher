import java.nio.file.*;
import java.util.*;

public final class WardrobeSmoke {
    public static void run(ClassLoader target,Path root) throws Exception {
        Class<?> pixels=Class.forName("dev.vortex.client.SkinPixels",true,target);
        var png=new java.awt.image.BufferedImage(64,64,java.awt.image.BufferedImage.TYPE_INT_ARGB);
        png.setRGB(8,8,0x00112233);png.setRGB(40,8,0x00445566);
        var output=new java.io.ByteArrayOutputStream();javax.imageio.ImageIO.write(png,"png",output);
        byte[] rgba=(byte[])pixels.getMethod("decode",byte[].class).invoke(null,(Object)output.toByteArray());
        if(rgba.length!=16384 || (rgba[(8*64+8)*4+3]&255)!=255 || rgba[(8*64+40)*4+3]!=0)
            throw new AssertionError("Skin base opacity / transparent clothing layer");
        try {pixels.getMethod("decode",byte[].class).invoke(null,(Object)new byte[64]);throw new AssertionError("Non-PNG accepted as skin");}
        catch(java.lang.reflect.InvocationTargetException expected){if(!(expected.getCause() instanceof IllegalArgumentException))throw expected;}
        Class<?> selection=Class.forName("dev.vortex.client.SkinPixels$Selection",true,target);
        var selected=selection.getConstructor(boolean.class,boolean.class,byte[].class).newInstance(true,true,rgba);
        Path path=root.resolve("wardrobe-test/player.skin");
        pixels.getMethod("save",Path.class,selection).invoke(null,path,selected);
        Object saved=pixels.getMethod("load",Path.class).invoke(null,path);
        if(!(boolean)selection.getMethod("slim").invoke(saved) || !(boolean)selection.getMethod("enabled").invoke(saved) ||
            !Arrays.equals(rgba,(byte[])selection.getMethod("rgba").invoke(saved)))throw new AssertionError("Skin selection persistence");
        var reset=selection.getConstructor(boolean.class,boolean.class,byte[].class).newInstance(false,false,rgba);
        pixels.getMethod("save",Path.class,selection).invoke(null,path,reset);
        if((boolean)selection.getMethod("enabled").invoke(pixels.getMethod("load",Path.class).invoke(null,path)))throw new AssertionError("Vanilla skin restore persistence");
        Files.write(path,new byte[]{1,2,3});
        try {pixels.getMethod("load",Path.class).invoke(null,path);throw new AssertionError("Corrupt skin accepted");}
        catch(java.lang.reflect.InvocationTargetException expected){if(!Arrays.equals(Files.readAllBytes(path),new byte[]{1,2,3}))throw new AssertionError("Corrupt original was modified");}
        Class<?> screen=Class.forName("dev.vortex.client.WardrobeScreen",false,target);
        for(int[] dimensions:new int[][]{{320,240},{430,300},{640,360},{854,480},{1920,1080}}) {
            Object layout=screen.getMethod("layout",int.class,int.class).invoke(null,dimensions[0],dimensions[1]);Class<?> type=layout.getClass();
            int x=(int)type.getMethod("x").invoke(layout),y=(int)type.getMethod("y").invoke(layout);
            int w=(int)type.getMethod("width").invoke(layout),h=(int)type.getMethod("height").invoke(layout);
            int lx=(int)type.getMethod("listX").invoke(layout),lw=(int)type.getMethod("listWidth").invoke(layout);
            int px=(int)type.getMethod("previewX").invoke(layout),pw=(int)type.getMethod("previewWidth").invoke(layout);
            int step=(int)type.getMethod("rowHeight").invoke(layout);
            if(x<0 || y<0 || x+w>dimensions[0] || y+h>dimensions[1] || lw<90 || lx+lw+10>px || px+pw>x+w-12 ||
                42+5*step-2>h-83 || step-2<18)throw new AssertionError("Wardrobe sidebar/collection/model/footer overlap at GUI scale");
        }
        Class<?> preview=Class.forName("dev.vortex.client.WardrobePreview",true,target);
        if(Math.abs((float)preview.getMethod("turn",float.class,double.class).invoke(null,0f,180d)+108)>0.001 ||
            (float)preview.getMethod("turn",float.class,double.class).invoke(null,25f,Double.NaN)!=25)
            throw new AssertionError("Wardrobe rotation wrap/invalid input");
        Class<?> playerSkin=Class.forName("net.minecraft.world.entity.player.PlayerSkin",true,target);
        Object base=Class.forName("net.minecraft.client.resources.DefaultPlayerSkin",true,target).getMethod("getDefaultSkin").invoke(null);
        Object state=preview.getMethod("state",playerSkin).invoke(null,base);
        if(state.getClass().getField("skin").get(state)!=base || (float)state.getClass().getField("scale").get(state)!=1 ||
            !state.getClass().getField("showHat").getBoolean(state))throw new AssertionError("Independent avatar render state");
        Class<?> texture=Class.forName("net.minecraft.core.ClientAsset$Texture",true,target);
        Object customBody=playerSkin.getMethod("body").invoke(base);
        Object patched=Class.forName("dev.vortex.client.LocalSkins",true,target).getMethod("patch",playerSkin,texture,boolean.class).invoke(null,base,customBody,true);
        if(playerSkin.getMethod("cape").invoke(patched)!=playerSkin.getMethod("cape").invoke(base) ||
            playerSkin.getMethod("elytra").invoke(patched)!=playerSkin.getMethod("elytra").invoke(base) ||
            !playerSkin.getMethod("model").invoke(patched).toString().equals("SLIM") || (boolean)playerSkin.getMethod("secure").invoke(patched))
            throw new AssertionError("Skin/cape/elytra isolation and custom authenticity flag");
        System.out.println("PASS Wardrobe geometry, independent 3D avatar state, bounded skin PNG, transparent clothing, classic/slim, atomic per-profile persistence and vanilla reset");
    }
}
