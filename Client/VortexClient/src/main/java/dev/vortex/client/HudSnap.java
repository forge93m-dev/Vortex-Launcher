package dev.vortex.client;

/** Closest screen/overlay alignment within five GUI pixels; no state or rendering. */
public final class HudSnap {
    private HudSnap(){ }
    public record Axis(int position,int guide){ }
    public static Axis axis(int value,int size,int screen,int[] lines){
        int max=Math.max(0,screen-size),best=Math.clamp(value,0,max),guide=-1,distance=6;
        int[] screenPositions={0,(screen-size)/2,screen-size};
        int[] screenLines={0,screen/2,screen};
        for(int i=0;i<3;i++){int candidate=screenPositions[i],delta=Math.abs(value-candidate);
            if(candidate>=0 && candidate<=max && delta<distance){best=candidate;guide=screenLines[i];distance=delta;}}
        for(int line:lines)for(int anchor:new int[]{0,size/2,size}){
            int candidate=line-anchor,delta=Math.abs(value-candidate);
            if(candidate>=0 && candidate<=max && delta<distance){best=candidate;guide=line;distance=delta;}
        }
        return new Axis(best,guide);
    }
}
