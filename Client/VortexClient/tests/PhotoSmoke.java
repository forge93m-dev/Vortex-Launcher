import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

public final class PhotoSmoke {
    public static byte[] hdFixture;
    public static void run(ClassLoader target,Path root) throws Exception {
        Class<?> artwork=Class.forName("dev.vortex.client.CapeArtwork",true,target),source=Class.forName("dev.vortex.client.CapeArtwork$Source",true,target);
        Class<?> pixels=Class.forName("dev.vortex.client.CapePixels",true,target);
        byte[] rgba=new byte[320*320*4];
        for(int y=0;y<320;y++) for(int x=0;x<320;x++) {
            int p=(y*320+x)*4; rgba[p]=(byte)(x<160?255:0); rgba[p+1]=(byte)(x>=160?255:0); rgba[p+2]=(byte)(y>=160?255:0); rgba[p+3]=(byte)255;
        }
        Object photo=source.getConstructor(int.class,int.class,byte[].class).newInstance(320,320,rgba);
        Method face=artwork.getMethod("face",source,int.class,int.class,double.class,double.class,double.class,boolean.class);
        byte[] fit=(byte[])face.invoke(null,photo,160,256,1d,0d,0d,true);
        if((fit[0]&255)!=16 || (fit[(80*160+40)*4]&255)!=255 || (fit[(180*160+120)*4+1]&255)!=255) throw new AssertionError("Fit photo stretched, cropped or lost color channels");
        byte[] fill=(byte[])face.invoke(null,photo,160,256,1d,0d,0d,false);
        if((fill[0]&255)==16 || Arrays.equals(fill,fit)) throw new AssertionError("Fill mode did not remove letterboxing");
        byte[] moved=(byte[])face.invoke(null,photo,160,256,2d,.5d,.4d,false);
        if(Arrays.equals(moved,fill)) throw new AssertionError("Crop zoom/pan has no effect");
        fails(()->face.invoke(null,photo,160,256,Double.NaN,0d,0d,true));
        byte[] packed=(byte[])artwork.getMethod("compose",source,double.class,double.class,double.class,boolean.class).invoke(null,photo,1d,0d,0d,true);
        byte[] noise=new byte[160*256*4]; new Random(731).nextBytes(noise); for(int i=3;i<noise.length;i+=4) noise[i]=(byte)255;
        Object noisyPhoto=source.getConstructor(int.class,int.class,byte[].class).newInstance(160,256,noise);
        hdFixture=(byte[])artwork.getMethod("compose",source,double.class,double.class,double.class,boolean.class).invoke(null,noisyPhoto,1d,0d,0d,true);
        if(hdFixture.length<=65536) throw new AssertionError("HD network fixture must exercise the larger bounded body");
        Object image=pixels.getMethod("unpack",byte[].class).invoke(null,(Object)packed);
        Class<?> imageType=image.getClass();
        if((int)imageType.getMethod("width").invoke(image)!=1024 || (int)imageType.getMethod("height").invoke(image)!=512) throw new AssertionError("Photo downgraded to 64x32");
        byte[] atlas=(byte[])imageType.getMethod("rgba").invoke(image);
        for(int y=0;y<256;y++) {
            if(!Arrays.equals(Arrays.copyOfRange(atlas,((y+16)*1024+16)*4,((y+16)*1024+176)*4),Arrays.copyOfRange(fit,y*160*4,(y+1)*160*4)) ||
               !Arrays.equals(Arrays.copyOfRange(atlas,((y+16)*1024+192)*4,((y+16)*1024+352)*4),Arrays.copyOfRange(fit,y*160*4,(y+1)*160*4))) throw new AssertionError("Front/back UV layout differs from crop preview");
        }
        byte[] truncated=Arrays.copyOf(packed,packed.length-1),trailing=Arrays.copyOf(packed,packed.length+1);
        fails(()->pixels.getMethod("unpack",byte[].class).invoke(null,(Object)truncated));
        fails(()->pixels.getMethod("unpack",byte[].class).invoke(null,(Object)trailing));
        var bomb=new java.io.ByteArrayOutputStream(); bomb.write(java.nio.ByteBuffer.allocate(12).putInt(0x56435032).putInt(64).putInt(32).array());
        try(var deflater=new java.util.zip.DeflaterOutputStream(bomb)) {deflater.write(new byte[8193]);}
        fails(()->pixels.getMethod("unpack",byte[].class).invoke(null,(Object)bomb.toByteArray()));
        Path saved=root.resolve("photo-cape-fixture/hd.cape");
        pixels.getMethod("save",Path.class,byte[].class).invoke(null,saved,packed);
        if(!Arrays.equals(packed,(byte[])pixels.getMethod("load",Path.class).invoke(null,saved))) throw new AssertionError("HD persistence lost quality");
        var buffered=new java.awt.image.BufferedImage(555,555,java.awt.image.BufferedImage.TYPE_INT_RGB);
        buffered.setRGB(277,277,0xFF00FF);
        for(String format:new String[]{"png","jpeg"}) {
            var out=new java.io.ByteArrayOutputStream(); javax.imageio.ImageIO.write(buffered,format,out);
            Object decoded=artwork.getMethod("decode",byte[].class).invoke(null,(Object)out.toByteArray());
            if((int)source.getMethod("width").invoke(decoded)!=555 || (int)source.getMethod("height").invoke(decoded)!=555) throw new AssertionError("Ordinary photo dimensions lost");
        }
        String local=System.getProperty("vortex.testPhoto");
        if(local!=null) {
            Object userPhoto=artwork.getMethod("read",Path.class).invoke(null,Path.of(local));
            byte[] result=(byte[])artwork.getMethod("compose",source,double.class,double.class,double.class,boolean.class).invoke(null,userPhoto,1d,0d,0d,true);
            pixels.getMethod("unpack",byte[].class).invoke(null,(Object)result);
            System.out.println("PASS Supplied photo imports locally to HD without uploading; compressed bytes="+result.length);
        }
        Class<?> screen=Class.forName("dev.vortex.client.CapeCropScreen",true,target);
        for(int[] size:new int[][]{{320,240},{430,300},{640,360},{1920,1080}}) {
            Object l=screen.getMethod("layout",int.class,int.class).invoke(null,size[0],size[1]); Class<?> t=l.getClass();
            int x=(int)t.getMethod("x").invoke(l),y=(int)t.getMethod("y").invoke(l),w=(int)t.getMethod("width").invoke(l),h=(int)t.getMethod("height").invoke(l);
            if(x<0 || y<0 || x+w>size[0] || y+h>size[1] || (int)t.getMethod("controlsWidth").invoke(l)<130 || y+181>=y+h-30) throw new AssertionError("Photo crop UI clips or overlaps footer");
        }
        System.out.println("PASS PNG/JPEG photos, fit/fill/zoom/pan, exact HD front/back UVs, bounded zlib/truncation/trailing bytes, persistent HD quality and responsive crop UI");
    }
    interface Checked {void run() throws Exception;}
    static void fails(Checked test) throws Exception {try {test.run();} catch(InvocationTargetException expected) {return;} throw new AssertionError("Invalid photo/cape accepted");}
}
