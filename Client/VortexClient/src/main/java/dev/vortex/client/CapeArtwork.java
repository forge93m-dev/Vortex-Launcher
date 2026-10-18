package dev.vortex.client;

import java.nio.file.Files;
import java.nio.file.Path;

/** Headless-safe local photo import. Never uploads the original file or metadata. */
public final class CapeArtwork {
    private static final int[] BACKGROUND={16,18,24};
    public record Source(int width,int height,byte[] rgba) { }
    private CapeArtwork() { }
    public static Source read(Path path) throws Exception {
        if(Files.size(path)>8*1024*1024) throw new IllegalArgumentException("Choose a PNG/JPEG up to 8 MB.");
        try(var input=Files.newInputStream(path)) {return decode(input.readNBytes(8*1024*1024+1));}
    }
    public static Source decode(byte[] bytes) throws Exception {
        if(bytes.length<8 || bytes.length>8*1024*1024 || !((bytes[0]&255)==137 && bytes[1]==80 && bytes[2]==78 && bytes[3]==71 || (bytes[0]&255)==255 && (bytes[1]&255)==216 && (bytes[2]&255)==255))
            throw new IllegalArgumentException("Choose a PNG or JPEG image up to 8 MB.");
        // ImageIO decoding is headless-safe; unlike FileDialog it creates no AWT window.
        // An explicit memory stream prevents ImageIO from creating disk-cache files.
        try(var input=new javax.imageio.stream.MemoryCacheImageInputStream(new java.io.ByteArrayInputStream(bytes))) {
            var readers=javax.imageio.ImageIO.getImageReaders(input);
            if(!readers.hasNext()) throw new IllegalArgumentException("Cannot decode this PNG/JPEG image.");
            var reader=readers.next();
            try {
                reader.setInput(input,true,true);
                int width=reader.getWidth(0),height=reader.getHeight(0);
                if(width<1 || height<1 || width>4096 || height>4096 || (long)width*height>8_388_608)
                    throw new IllegalArgumentException("Image must be at most 4096 pixels per side and 8 megapixels.");
                var image=reader.read(0); byte[] rgba=new byte[width*height*4]; int[] row=new int[width];
                for(int y=0;y<height;y++) {
                    image.getRGB(0,y,width,1,row,0,width);
                    for(int x=0;x<width;x++) {
                        int p=(y*width+x)*4,c=row[x]; rgba[p]=(byte)(c>>16); rgba[p+1]=(byte)(c>>8); rgba[p+2]=(byte)c; rgba[p+3]=(byte)(c>>>24);
                    }
                }
                image.flush(); return new Source(width,height,rgba);
            } finally {reader.dispose();}
        }
    }
    public static byte[] face(Source source,int width,int height,double zoom,double panX,double panY,boolean fit) {
        if(source.width()<1 || source.height()<1 || source.rgba().length!=(long)source.width()*source.height()*4 || width<1 || height<1 || width>320 || height>512)
            throw new IllegalArgumentException("Invalid photo preview.");
        if(!Double.isFinite(zoom) || !Double.isFinite(panX) || !Double.isFinite(panY)) throw new IllegalArgumentException("Invalid crop.");
        double scale=(fit?Math.min((double)width/source.width(),(double)height/source.height()):Math.max((double)width/source.width(),(double)height/source.height()))*Math.clamp(zoom,1,4);
        byte[] face=new byte[width*height*4];
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            double sx=(x+.5-width/2.0)/scale+source.width()/2.0-.5+Math.clamp(panX,-1,1)*source.width()/2.0;
            double sy=(y+.5-height/2.0)/scale+source.height()/2.0-.5+Math.clamp(panY,-1,1)*source.height()/2.0;
            int p=(y*width+x)*4;
            for(int channel=0;channel<3;channel++) {
                double value=BACKGROUND[channel];
                if(sx>=-.5 && sx<source.width()-.5 && sy>=-.5 && sy<source.height()-.5) {
                    int ax=(int)Math.floor(sx),ay=(int)Math.floor(sy); double fx=sx-ax,fy=sy-ay;
                    value=0;
                    for(int yy=0;yy<2;yy++) for(int xx=0;xx<2;xx++) {
                        int offset=(Math.clamp(ay+yy,0,source.height()-1)*source.width()+Math.clamp(ax+xx,0,source.width()-1))*4;
                        double alpha=(source.rgba()[offset+3]&255)/255.0;
                        value+=((source.rgba()[offset+channel]&255)*alpha+BACKGROUND[channel]*(1-alpha))*(xx==0?1-fx:fx)*(yy==0?1-fy:fy);
                    }
                }
                face[p+channel]=(byte)Math.clamp((int)Math.round(value),0,255);
            }
            face[p+3]=(byte)255;
        }
        return face;
    }
    public static byte[] compose(Source source,double zoom,double panX,double panY,boolean fit) throws Exception {
        byte[] face=face(source,160,256,zoom,panX,panY,fit),atlas=new byte[1024*512*4];
        for(int y=0;y<256;y++) {
            System.arraycopy(face,y*160*4,atlas,((y+16)*1024+16)*4,160*4);
            System.arraycopy(face,y*160*4,atlas,((y+16)*1024+192)*4,160*4);
        }
        // Copy edge colors to the one-unit thickness and top/bottom faces.
        for(int y=0;y<256;y++) for(int x=0;x<16;x++) {
            System.arraycopy(face,y*160*4,atlas,((y+16)*1024+x)*4,4);
            System.arraycopy(face,(y*160+159)*4,atlas,((y+16)*1024+176+x)*4,4);
        }
        for(int y=0;y<16;y++) for(int x=0;x<160;x++) {
            System.arraycopy(face,x*4,atlas,(y*1024+16+x)*4,4);
            System.arraycopy(face,(255*160+x)*4,atlas,(y*1024+176+x)*4,4);
        }
        return CapePixels.pack(new CapePixels.Image(1024,512,atlas));
    }
    public static byte[] texture(Source source) throws Exception {
        if(!CapePixels.dimensions(source.width(),source.height())) throw new IllegalArgumentException("Cape texture must be 64x32 up to 1024x512.");
        return source.width()==64 ? source.rgba().clone() : CapePixels.pack(new CapePixels.Image(source.width(),source.height(),source.rgba()));
    }
    public static byte[] textureFace(Source source) {
        if(!CapePixels.dimensions(source.width(),source.height())) throw new IllegalArgumentException("Invalid cape texture dimensions.");
        int scale=source.width()/64,w=10*scale,h=16*scale; byte[] rgba=new byte[w*h*4];
        for(int y=0;y<h;y++) System.arraycopy(source.rgba(),((y+scale)*source.width()+12*scale)*4,rgba,y*w*4,w*4);
        return face(new Source(w,h,rgba),160,256,1,0,0,false);
    }
}
