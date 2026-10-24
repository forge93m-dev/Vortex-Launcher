package dev.vortex.client;
import org.joml.Matrix4fc;
import org.joml.Vector4f;

public final class WaypointProjection {
    public record Pin(int x,int y,boolean edge,double angle){}
    public static Pin project(double dx,double dy,double dz,Matrix4fc matrix,int width,int height){
        var clip=new Vector4f((float)dx,(float)dy,(float)dz,1).mul(matrix);
        if(!Float.isFinite(clip.x) || !Float.isFinite(clip.y) || !Float.isFinite(clip.w) || width<40 || height<40)return null;
        if(clip.w<=.0001f)return null;
        double divisor=clip.w,x=clip.x/divisor,y=-clip.y/divisor;
        if(Math.abs(x)>=1 || Math.abs(y)>=1)return null;
        double cx=width/2.0,cy=height/2.0;
        return new Pin(Math.clamp((int)Math.round(cx+x*cx),0,width-1),Math.clamp((int)Math.round(cy+y*cy),0,height-1),false,0);
    }
    public static String distance(double value){return value>=1000?String.format(java.util.Locale.ROOT,"%.1f km",value/1000):Math.round(value)+" m";}
}
