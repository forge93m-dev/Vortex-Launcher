package dev.vortex.client;
import org.joml.Matrix4fc;
import org.joml.Vector4f;

public final class WaypointProjection {
    public record Pin(int x,int y,boolean edge,double angle){}
    public static Pin project(double dx,double dy,double dz,Matrix4fc matrix,int width,int height){
        var clip=new Vector4f((float)dx,(float)dy,(float)dz,1).mul(matrix);
        if(!Float.isFinite(clip.x) || !Float.isFinite(clip.y) || !Float.isFinite(clip.w) || width<40 || height<40)return null;
        double divisor=Math.max(.0001,Math.abs(clip.w)),x=clip.x/divisor,y=-clip.y/divisor;
        boolean front=clip.w>0,edge=!front || Math.abs(x)>1 || Math.abs(y)>1;
        int cx=width/2,cy=height/2;
        if(!edge)return new Pin((int)Math.round(cx+x*cx),(int)Math.round(cy+y*cy),false,0);
        if(Math.abs(x)+Math.abs(y)<.0001)y=1;
        double px=x*cx,py=y*cy,scale=Math.min((cx-24)/Math.max(.0001,Math.abs(px)),(cy-30)/Math.max(.0001,Math.abs(py)));
        return new Pin((int)Math.round(cx+px*scale),(int)Math.round(cy+py*scale),true,Math.atan2(py,px));
    }
    public static String distance(double value){return value>=1000?String.format(java.util.Locale.ROOT,"%.1f km",value/1000):Math.round(value)+" m";}
}
