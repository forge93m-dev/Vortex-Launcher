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
    public record Image(int width,int height,byte[] rgba) { }
    public static boolean hd(byte[] data) {return data!=null && data.length>=12 && ByteBuffer.wrap(data).getInt()==0x56435032;}
    private CapePixels() { }
    public static String id(byte[] pixels) throws Exception {
        check(pixels); return (hd(pixels)?"hd:":"custom:") + HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(pixels));
    }
    public static void check(byte[] pixels) {
        if(hd(pixels)) {
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
    public static Image unpack(byte[] data) throws Exception {
        check(data); if(!hd(data)) return new Image(64,32,data);
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
        try(var input=Files.newInputStream(path)) {byte[] pixels=input.readNBytes(MAX_PACKED+1); unpack(pixels); return pixels;}
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
