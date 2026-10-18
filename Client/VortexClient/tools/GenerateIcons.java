import java.awt.*;
import java.awt.geom.*;
import java.awt.image.BufferedImage;
import java.nio.file.*;
import javax.imageio.ImageIO;

/** Original vector paths rasterized with antialiasing. Runtime requires no AWT. */
public class GenerateIcons {
    static Graphics2D g;
    static void line(double... points) {
        var p = new Path2D.Double(); p.moveTo(points[0], points[1]);
        for (int i=2;i<points.length;i+=2) p.lineTo(points[i], points[i+1]); g.draw(p);
    }
    static void circle(double x,double y,double r) { g.draw(new Ellipse2D.Double(x-r,y-r,r*2,r*2)); }
    static void bar(double x,double y,double w,double h) { g.fill(new RoundRectangle2D.Double(x,y,w,h,2,2)); }
    public static void main(String[] args) throws Exception {
        Path dir = Path.of(args[0]); Files.createDirectories(dir);
        for (String name : args.length > 1 ? new String[]{args[1]} : new String[]{"fps","zoom","armor","sprint","coordinates","crosshair","effects","ping","speed","clock","tps","freelook","fullbright","block_info","chat","search","settings","brand","modules","hud","keybinds","globe","profile","exit","back","language","accessibility","world","realms","lan","trophy","statistics","feedback","bug","report","social","credits","reset","help","capes","play"}) {
            if(name.equals("brand")) {
                Files.copy(Path.of("Assets/Branding/vortex-ingame-white-v2.png"),dir.resolve("brand.png"),StandardCopyOption.REPLACE_EXISTING);
                Files.writeString(dir.resolve("brand.png.mcmeta"), "{\"texture\":{\"blur\":true,\"clamp\":true}}\n");
                continue;
            }
            int resolution=96;
            var image = new BufferedImage(resolution,resolution,BufferedImage.TYPE_INT_ARGB);
            g=image.createGraphics(); g.scale(resolution/24.0,resolution/24.0);
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING,RenderingHints.VALUE_ANTIALIAS_ON);
            g.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL,RenderingHints.VALUE_STROKE_PURE);
            g.setColor(Color.WHITE);g.setStroke(new BasicStroke(1.65f,BasicStroke.CAP_ROUND,BasicStroke.JOIN_ROUND));
            switch(name) {
                case "compact_shield" -> {line(6,3,18,3,18,13,12,21,6,13,6,3);line(9,8,15,8);line(12,5,12,11);}
                case "enchantment_hints" -> {line(3,4,10,5,12,7,14,5,21,4,21,19,14,20,12,22,10,20,3,19,3,4);line(12,7,12,22);line(6,9,9,10);line(15,10,18,9);}
                case "death_waypoints" -> {circle(12,10,8);circle(9,9,1);circle(15,9,1);line(9,17,9,21,15,21,15,17);line(12,18,12,21);}
                case "shulker_preview" -> {g.draw(new RoundRectangle2D.Double(3,5,18,15,3,3));line(3,11,21,11);line(8,11,8,20);line(16,11,16,20);circle(9,8,0.5);circle(15,8,0.5);}
                case "entity_hearts" -> {var p=new Path2D.Double();p.moveTo(12,21);p.curveTo(-5,10,3,-1,12,7);p.curveTo(21,-1,29,10,12,21);g.draw(p);line(5,12,9,12,11,9,13,15,15,12,19,12);}
                case "waypoints" -> {var p=new Path2D.Double();p.moveTo(12,22);p.curveTo(8,17,5,12,5,8);p.curveTo(5,-1,19,-1,19,8);p.curveTo(19,12,16,17,12,22);g.draw(p);circle(12,8,2.4);line(2,21,6,21);line(18,21,22,21);}
                case "chest_search" -> {line(3,11,3,5,18,5,18,9);line(3,9,12,9);line(3,11,3,20,11,20);line(8,9,8,12,10,12,10,9);circle(15,15,4);line(18,18,22,22);}
                case "chat" -> {line(4,4,20,4,20,16,11,16,6,21,6,16,4,16,4,4);line(8,8,16,8);line(8,12,14,12);}
                case "modules" -> {for(int yy=4;yy<=14;yy+=10)for(int xx=4;xx<=14;xx+=10)g.draw(new RoundRectangle2D.Double(xx,yy,6,6,2,2));}
                case "hud" -> {g.draw(new RoundRectangle2D.Double(3,4,18,13,2,2));line(8,21,16,21);line(12,17,12,21);line(7,8,10,8);line(15,12,18,12);}
                case "keybinds" -> {g.draw(new RoundRectangle2D.Double(2,6,20,12,2,2));for(int yy=9;yy<=12;yy+=3)for(int xx=5;xx<=19;xx+=3)bar(xx,yy,1,1);line(7,15,17,15);}
                case "globe" -> {circle(12,12,9);g.draw(new Ellipse2D.Double(8,3,8,18));line(3,12,21,12);}
                case "profile" -> {circle(12,7,4);var p=new Path2D.Double();p.moveTo(4,21);p.curveTo(4,12,20,12,20,21);g.draw(p);}
                case "exit" -> {line(10,4,4,4,4,20,10,20);line(9,12,21,12);line(17,8,21,12,17,16);}
                case "back" -> {line(14,5,7,12,14,19);line(7,12,21,12);}
                case "fps" -> {g.draw(new RoundRectangle2D.Double(3,4,18,14,2,2));line(5,14,9,10,12,12,17,7);line(9,21,15,21);}
                case "ping" -> {bar(4,16,3,4);bar(10.5,11,3,9);bar(17,5,3,15);}
                case "tps" -> {line(2,12,7,12,10,4,14,20,17,12,22,12);}
                case "search" -> {circle(10.5,10.5,6);line(15,15,20,20);}
                case "zoom" -> {circle(10.5,10.5,7);line(16,16,21,21);line(7,10.5,14,10.5);line(10.5,7,10.5,14);}
                case "armor" -> {line(8,4,4,6,2,10,6,12,7,9,7,20,17,20,17,9,18,12,22,10,20,6,16,4);line(8,4,10,7,14,7,16,4);}
                case "sprint" -> {line(5,6,11,12,5,18);line(13,6,19,12,13,18);}
                case "coordinates" -> {var p=new Path2D.Double();p.moveTo(12,22);p.curveTo(9,18,4,13,4,9);p.curveTo(4,-1,20,-1,20,9);p.curveTo(20,13,15,18,12,22);g.draw(p);circle(12,9,2.5);}
                case "crosshair" -> {line(12,3,12,8);line(12,16,12,21);line(3,12,8,12);line(16,12,21,12);circle(12,12,1);}
                case "effects" -> {line(9,3,15,3);line(10,3,10,8,6,12,6,19,18,19,18,12,14,8,14,3);line(7,14,17,14);}
                case "speed" -> {line(4,8,16,8);line(2,12,13,12);line(5,16,16,16);line(16,4,21,12,16,20);}
                case "clock" -> {circle(12,12,8.5);line(12,6,12,12,16,14);}
                case "freelook" -> {var p=new Path2D.Double();p.moveTo(2,12);p.curveTo(7,3,17,3,22,12);p.curveTo(17,21,7,21,2,12);g.draw(p);circle(12,12,3);}
                case "fullbright" -> {circle(12,12,4);for(int i=0;i<8;i++){double a=i*Math.PI/4;line(12+7*Math.cos(a),12+7*Math.sin(a),12+10*Math.cos(a),12+10*Math.sin(a));}}
                case "block_info" -> {line(12,3,21,8,21,17,12,22,3,17,3,8,12,3);line(3,8,12,13,21,8);line(12,13,12,22);}
                case "settings" -> {circle(12,12,3);circle(12,12,7);for(int i=0;i<8;i++){double a=i*Math.PI/4;line(12+7*Math.cos(a),12+7*Math.sin(a),12+9*Math.cos(a),12+9*Math.sin(a));}}
                case "language" -> {line(3,5,13,5);line(8,2,8,5);line(11,5,9,10,4,14);line(5,8,11,13);line(13,21,17,10,21,21);line(15,17,19,17);}
                case "accessibility" -> {circle(12,4,2);line(3,8,12,10,21,8);line(12,10,12,15);line(12,15,7,22);line(12,15,17,22);}
                case "world" -> {line(4,6,20,6);line(4,12,20,12);line(4,18,20,18);circle(8,6,2);circle(16,12,2);circle(10,18,2);}
                case "realms" -> {var p=new Path2D.Double();p.moveTo(6,18);p.curveTo(-1,18,0,9,6,9);p.curveTo(6,0,19,0,19,9);p.curveTo(25,9,25,18,18,18);p.closePath();g.draw(p);line(10,12,12,10,14,12);line(12,10,12,16);}
                case "lan" -> {circle(12,18,1);g.draw(new Arc2D.Double(7,12,10,10,35,110,Arc2D.OPEN));g.draw(new Arc2D.Double(3,8,18,18,40,100,Arc2D.OPEN));line(12,3,12,6);}
                case "trophy" -> {line(7,3,17,3,17,11,14,15,10,15,7,11,7,3);line(7,5,3,5,3,9,7,11);line(17,5,21,5,21,9,17,11);line(12,15,12,21);line(7,21,17,21);}
                case "statistics" -> {line(3,3,3,21,22,21);bar(7,12,3,6);bar(12,8,3,10);bar(17,4,3,14);}
                case "feedback" -> {line(3,4,21,4,21,16,10,16,5,21,5,16,3,16,3,4);line(8,10,16,10);line(12,6,12,14);}
                case "bug" -> {g.draw(new RoundRectangle2D.Double(7,6,10,15,5,5));line(9,3,10,6);line(15,3,14,6);line(12,10,12,20);for(int yy=9;yy<=17;yy+=4){line(3,yy,7,yy);line(17,yy,21,yy);}}
                case "report" -> {line(12,2,21,6,20,15,12,22,4,15,3,6,12,2);line(12,7,12,13);circle(12,17,.5);}
                case "social" -> {circle(9,7,3);circle(18,8,2.5);var p=new Path2D.Double();p.moveTo(2,21);p.curveTo(2,12,16,12,16,21);g.draw(p);p=new Path2D.Double();p.moveTo(17,14);p.curveTo(21,14,22,17,22,20);g.draw(p);}
                case "credits" -> {line(5,2,15,2,20,7,20,22,5,22,5,2);line(15,2,15,7,20,7);line(9,12,16,12);line(9,16,16,16);}
                case "reset" -> {g.draw(new Arc2D.Double(4,4,16,16,-40,285,Arc2D.OPEN));line(3,3,3,10,10,10);}
                case "help" -> {circle(12,12,10);g.draw(new Arc2D.Double(8,5,8,8,-90,270,Arc2D.OPEN));line(12,12,12,14);circle(12,18,.5);}
                case "capes" -> {line(8,3,16,3,21,21,12,18,3,21,8,3);line(8,3,12,7,16,3);}
                case "play" -> {line(8,4,20,12,8,20,8,4);}
                default -> throw new IllegalArgumentException("Unknown icon: " + name);
            }
            g.dispose();ImageIO.write(image,"png",dir.resolve(name+".png").toFile());
            Files.writeString(dir.resolve(name+".png.mcmeta"), "{\"texture\":{\"blur\":true,\"clamp\":true}}\n");
        }
    }
}
