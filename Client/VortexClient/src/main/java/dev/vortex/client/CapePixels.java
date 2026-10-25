package dev.vortex.client;

import com.mojang.blaze3d.platform.NativeImage;
import java.nio.ByteBuffer;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.util.HexFormat;

/** Fixed-size raw pixels on the wire: no remote PNG decoder or metadata. */
public final class CapePixels {
    public static final int SIZE = 64 * 32 * 4;
    public static final int MAX_PACKED = 640 * 1024;
    /** Animated custom cape: "VCA1", width, height, frames, milliseconds per frame, then one zlib stream of all RGBA frames. */
    public static final int MAX_ANIMATED = 1_000_000;
    public static final int MAX_FRAMES = 120;
    public static final int MIN_FRAME_MS = 20;
    /** Frames x width x height: bounds the memory an animated cape needs on every player's computer (32 MB of RGBA). */
    public static final long MAX_ANIMATED_PIXELS = 8_000_000L;
    public static boolean withinBudget(int frames, int width) {return (long)frames*width*(width/2)<=MAX_ANIMATED_PIXELS;}
    public record Image(int width,int height,byte[] rgba) { }
    public record Animation(int width,int height,int frameMs,byte[][] frames) { }
    public static boolean animated(byte[] data) {return data!=null && data.length>=20 && ByteBuffer.wrap(data).getInt()==0x56434131;}
    public static boolean hd(byte[] data) {return data!=null && data.length>=12 && ByteBuffer.wrap(data).getInt()==0x56435032;}
    private CapePixels() { }
    public static String id(byte[] pixels) throws Exception {
        check(pixels); return (animated(pixels)?"anim:":hd(pixels)?"hd:":"custom:") + HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(pixels));
    }
    public static void check(byte[] pixels) {
        if(animated(pixels)) {
            var header=ByteBuffer.wrap(pixels); header.getInt(); int w=header.getInt(),h=header.getInt(),frames=header.getInt(),frameMs=header.getInt();
            if(pixels.length<24 || pixels.length>MAX_ANIMATED || !(w==64 || w==128 || w==256 || w==512) || h!=w/2 || frames<2 || frames>MAX_FRAMES || !withinBudget(frames,w) || frameMs<MIN_FRAME_MS || frameMs>1000)
                throw new IllegalArgumentException("Invalid animated cape dimensions, frame count or size.");
        } else if(hd(pixels)) {
            var header=ByteBuffer.wrap(pixels); header.getInt(); int w=header.getInt(),h=header.getInt();
            if(pixels.length<18 || pixels.length>MAX_PACKED || !dimensions(w,h)) throw new IllegalArgumentException("Invalid HD cape dimensions or size.");
        } else if (pixels == null || pixels.length != SIZE) throw new IllegalArgumentException("Invalid cape image.");
    }
    public static boolean dimensions(int w,int h) {return (w==64 || w==128 || w==256 || w==512 || w==1024) && h==w/2;}
    public static byte[] pack(Image image) throws Exception {
        if(!dimensions(image.width(),image.height()) || image.rgba().length!=image.width()*image.height()*4) throw new IllegalArgumentException("Invalid HD cape dimensions.");
        var output=new java.io.ByteArrayOutputStream();
        output.write(ByteBuffer.allocate(12).putInt(0x56435032).putInt(image.width()).putInt(image.height()).array());
        try(var zipped=new java.util.zip.DeflaterOutputStream(output)) {zipped.write(image.rgba());}
        byte[] data=output.toByteArray(); check(data); return data;
    }
    public static byte[] packAnimation(int width,int height,int frameMs,java.util.List<byte[]> frames) throws Exception {
        if(!(width==64 || width==128 || width==256 || width==512) || height!=width/2 || frames.size()<2 || frames.size()>MAX_FRAMES || !withinBudget(frames.size(),width) || frameMs<MIN_FRAME_MS || frameMs>1000)
            throw new IllegalArgumentException("Invalid animated cape dimensions or frame count.");
        var output=new java.io.ByteArrayOutputStream();
        output.write(ByteBuffer.allocate(20).putInt(0x56434131).putInt(width).putInt(height).putInt(frames.size()).putInt(frameMs).array());
        try(var zipped=new java.util.zip.DeflaterOutputStream(output,new java.util.zip.Deflater(9))) {
            for(byte[] frame:frames) {
                if(frame.length!=width*height*4) throw new IllegalArgumentException("Invalid animated cape frame.");
                zipped.write(frame);
            }
        }
        byte[] data=output.toByteArray();
        // An oversized result is returned as it is so the importer can shrink the frames; saving and uploading check it again.
        if(data.length<=MAX_ANIMATED) check(data);
        return data;
    }
    public static Animation unpackAnimation(byte[] data) throws Exception {
        check(data); if(!animated(data)) throw new IllegalArgumentException("Not an animated cape.");
        var header=ByteBuffer.wrap(data,4,16); int w=header.getInt(),h=header.getInt(),count=header.getInt(),frameMs=header.getInt();
        int frameSize=w*h*4,expected=frameSize*count;
        var inflater=new java.util.zip.Inflater();
        try {
            inflater.setInput(data,20,data.length-20);
            byte[] all=new byte[expected+1]; int size=0;
            while(!inflater.finished() && size<all.length) {
                int read=inflater.inflate(all,size,all.length-size);
                if(read==0) break; size+=read;
            }
            if(size!=expected || !inflater.finished() || inflater.getRemaining()!=0) throw new IllegalArgumentException("Invalid compressed animated cape pixels.");
            byte[][] frames=new byte[count][];
            for(int i=0;i<count;i++) frames[i]=java.util.Arrays.copyOfRange(all,i*frameSize,(i+1)*frameSize);
            return new Animation(w,h,frameMs,frames);
        } finally {inflater.end();}
    }
    public static Image unpack(byte[] data) throws Exception {
        check(data);
        if(animated(data)) {var animation=unpackAnimation(data); return new Image(animation.width(),animation.height(),animation.frames()[0]);}
        if(!hd(data)) return new Image(64,32,data);
        int w=ByteBuffer.wrap(data,4,4).getInt(),h=w/2,expected=w*h*4;
        var inflater=new java.util.zip.Inflater();
        try {
            inflater.setInput(data,12,data.length-12);
            byte[] rgba=new byte[expected+1]; int size=0;
            while(!inflater.finished() && size<rgba.length) {
                int count=inflater.inflate(rgba,size,rgba.length-size);
                if(count==0) break; size+=count;
            }
            if(size!=expected || !inflater.finished() || inflater.getRemaining()!=0) throw new IllegalArgumentException("Invalid compressed cape pixels.");
            return new Image(w,h,java.util.Arrays.copyOf(rgba,expected));
        } finally {inflater.end();}
    }
    public static byte[] decode(byte[] png) throws Exception {
        if (png.length < 33 || png.length > 65536 || ByteBuffer.wrap(png).getLong() != 0x89504e470d0a1a0aL ||
            ByteBuffer.wrap(png,8,8).getInt() != 13 || ByteBuffer.wrap(png,12,4).getInt() != 0x49484452 ||
            ByteBuffer.wrap(png,16,8).getInt() != 64 || ByteBuffer.wrap(png,20,4).getInt() != 32)
            throw new IllegalArgumentException("Choose a PNG cape, 64 x 32 pixels, up to 64 KB.");
        try (var image = NativeImage.read(png)) {
            if (image.getWidth() != 64 || image.getHeight() != 32) throw new IllegalArgumentException("Invalid cape dimensions.");
            byte[] pixels = new byte[SIZE];
            for (int y=0;y<32;y++) for(int x=0;x<64;x++) {
                int color=image.getPixel(x,y), offset=(y*64+x)*4;
                pixels[offset]=(byte)(color>>16); pixels[offset+1]=(byte)(color>>8);
                pixels[offset+2]=(byte)color; pixels[offset+3]=(byte)(color>>>24);
            }
            return pixels;
        }
    }
    public static byte[] readPng(Path path) throws Exception {
        if (Files.size(path)>65536) throw new IllegalArgumentException("PNG must be at most 64 KB.");
        try(var input=Files.newInputStream(path)) { return decode(input.readNBytes(65537)); }
    }
    public static byte[] load(Path path) throws Exception {
        try(var input=Files.newInputStream(path)) {byte[] pixels=input.readNBytes(MAX_ANIMATED+1); unpack(pixels); return pixels;}
    }
    public static void save(Path path, byte[] pixels) throws Exception {
        unpack(pixels);
        if (Files.isSymbolicLink(path) || Files.isSymbolicLink(path.getParent())) throw new IllegalArgumentException("Unsafe cape storage path.");
        Files.createDirectories(path.getParent());
        Path temporary=Files.createTempFile(path.getParent(),"cape-",".part");
        try {
            Files.write(temporary,pixels);
            try {Files.move(temporary,path,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);}
            catch(java.nio.file.AtomicMoveNotSupportedException error) {Files.move(temporary,path,StandardCopyOption.REPLACE_EXISTING);}
        } finally {Files.deleteIfExists(temporary);}
    }
}
