import java.awt.image.BufferedImage;
import java.nio.file.*;
import javax.imageio.ImageIO;

/** Original code-native 64x32 Minecraft cape UV textures; no runtime generator. */
public class GenerateCapes {
    public static void main(String[] args) throws Exception {
        Path folder=Path.of(args[0]); Files.createDirectories(folder);
        String[] names={"vortex","aurora","ember"};
        int[] colors={0xB8A5FF,0x65E0CA,0xFF9864};
        for(int n=0;n<names.length;n++) {
            var image=new BufferedImage(64,32,BufferedImage.TYPE_INT_ARGB);
            for(int y=0;y<17;y++) for(int x=0;x<22;x++) image.setRGB(x,y,0xFF111521);
            for(int start:new int[]{1,12}) for(int y=0;y<16;y++) for(int x=0;x<10;x++) {
                int color=0x181D2B;
                if(x==0 || x==9 || y==15) color=shade(colors[n],.55);
                if(n==0 && y>=3 && y<=11 && (x==1+(y-3)/2 || x==8-(y-3)/2)) color=colors[n];
                if(n==1 && Math.abs(y-(4+x/2))<2) color=shade(colors[n],.65+.035*x);
                if(n==2 && y>=5 && x>=2 && x<=7 && (x+y)%4<2) color=shade(colors[n],.5+.03*y);
                if(y==13 && x>=2 && x<=7) color=colors[n];
                image.setRGB(start+x,1+y,0xFF000000|color);
            }
            ImageIO.write(image,"PNG",folder.resolve(names[n]+".png").toFile());
        }
    }
    static int shade(int color,double factor) {
        return Math.min(255,(int)(((color>>16)&255)*factor))<<16 |
            Math.min(255,(int)(((color>>8)&255)*factor))<<8 | Math.min(255,(int)((color&255)*factor));
    }
}
