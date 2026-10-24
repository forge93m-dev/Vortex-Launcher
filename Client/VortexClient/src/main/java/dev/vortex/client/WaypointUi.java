package dev.vortex.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Lightweight shared chrome, without another blur pass or layout animation. */
final class WaypointUi {
    /** Full-height input target with centered text and the vanilla text editing behavior. */
    static final class Input extends net.minecraft.client.gui.components.EditBox {
        Input(net.minecraft.client.gui.Font font,int x,int y,int width,net.minecraft.network.chat.Component label){super(font,x,y,width,26,label);setBordered(false);setTextShadow(false);}
        @Override public void extractWidgetRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){g.pose().pushMatrix();g.pose().translate(0,8);super.extractWidgetRenderState(g,mx,my,delta);g.pose().popMatrix();}
    }
    static void begin(GuiGraphicsExtractor g,int sw,int sh,int x,int y,int w,int h,String title,String subtitle,long opened){
        float reveal=UiMotion.progress(opened);
        g.fill(0,0,sw,sh,((int)(155*reveal)<<24)|0x08090D);
        UiMotion.begin(g,x,y,w,h,1,(1-reveal)*4);
        ClientUi.rounded(g,x,y+4,w,h,10,0x50000000);
        ClientUi.panel(g,x,y,w,h,9,0xFF30323B,0xFF111216);
        ClientUi.heading(g,title,x+16,y+14,w-100);
        ClientUi.text(g,subtitle,x+16,y+33,w-32,ClientUi.MUTED);
    }
    static void field(GuiGraphicsExtractor g,int x,int y,int w,int h,boolean focused){
        ClientUi.panel(g,x,y,w,h,5,focused?0xFFABB6CE:0xFF32343D,0xFF1B1D23);
    }
}
