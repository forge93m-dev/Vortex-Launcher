package dev.vortex.client;

import java.util.Objects;

/** One cape at a time: fade out, swap invisibly, fade in. No texture allocation or IO. */
public final class CapeFade<T> {
    private T displayed,target;
    private boolean seen,out;
    private double visibility=1;
    public void observe(T next) {
        if(!seen){seen=true;displayed=target=next;return;}
        if(Objects.equals(target,next))return;
        target=next;
        if(Objects.equals(displayed,target)){out=false;return;}
        if(displayed==null){displayed=target;visibility=0;out=false;}
        else out=true;
    }
    public void step(double seconds,boolean animate) {
        if(!animate){displayed=target;visibility=1;out=false;return;}
        double remaining=Double.isFinite(seconds)?Math.clamp(seconds,0,.1):0;
        if(out){
            double used=Math.min(remaining,visibility*.16);
            visibility=Math.max(0,visibility-used/.16);remaining-=used;
            if(visibility<=1e-6){displayed=target;visibility=displayed==null?1:0;out=false;}
        }
        if(!out)visibility=Math.min(1,visibility+remaining/.22);
    }
    public T displayed(){return displayed;}
    public boolean retains(T texture){return texture!=null && (Objects.equals(texture,displayed)||Objects.equals(texture,target));}
    public float opacity(){return (float)(visibility*visibility*(3-2*visibility));}
}
