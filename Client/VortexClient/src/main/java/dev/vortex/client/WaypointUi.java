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
        // Same soft glass window as the rest of the client: grows slightly into place.
        UiMotion.begin(g,x,y,w,h,.96f+.04f*reveal,(1-reveal)*4);
        ClientUi.rounded(g,x-2,y+5,w+4,h,12,0x40000000);
        ClientUi.rounded(g,x,y,w,h,12,ClientUi.GLASS);
        ClientUi.heading(g,title,x+16,y+14,w-100);
        ClientUi.text(g,subtitle,x+16,y+33,w-32,ClientUi.MUTED);
    }
    /** Label pill height and the gap between it and the pin head. */
    static final int LABEL_HEIGHT=14,PIN_HEIGHT=25;
    /**
     * Top-left corner of a marker's label, centred above the pin and kept inside the screen.
     * When there is no room above (the pin is near the top edge) the label moves below the pin.
     */
    static int[] labelPosition(int pinX,int pinY,int labelWidth,int screenWidth,int screenHeight){
        int x=Math.clamp(pinX-labelWidth/2,3,Math.max(3,screenWidth-labelWidth-3));
        int above=pinY-PIN_HEIGHT-4-LABEL_HEIGHT;
        int y=above>=3?above:Math.min(pinY+4,screenHeight-LABEL_HEIGHT-3);
        return new int[]{x,y};
    }
    /** The full name shows only near the crosshair or close by, so many markers never clutter the view. */
    static boolean showsName(int pinX,int pinY,int screenWidth,int screenHeight,double meters){
        return meters<48 || Math.abs(pinX-screenWidth/2)<70 && Math.abs(pinY-screenHeight/2)<60;
    }
    /**
     * A map pin standing on the projected point: a coloured head with a dark ring, a short tail to the block,
     * and a glass label with a colour dot, the name and the distance.
     */
    static void marker(GuiGraphicsExtractor g,net.minecraft.client.gui.Font font,int x,int y,int rgb,String name,String distance,boolean full,int screenWidth,int screenHeight){
        int color=0xFF000000|rgb,ring=0xE610151C;
        int headY=y-PIN_HEIGHT;
        // A teardrop: the dark round head narrows smoothly into a point that touches the target block.
        // A smooth texture, not GUI-pixel rows: rows would show as steps at GUI scale 3 and 4.
        ClientUi.pin(g,x-8,headY-1,ring);
        ClientUi.rounded(g,x-6,headY+1,13,13,6,color);
        // The first letter of the name tells markers apart at a glance; dark on light colours, light on dark.
        String letter=initial(name);
        int luminance=((rgb>>16&255)*299+(rgb>>8&255)*587+(rgb&255)*114)/1000;
        g.text(font,letter,x-font.width(letter)/2+1,headY+4,luminance>140?0xFF10151C:0xFFF3F4F7,false);
        String title=full?font.plainSubstrByWidth(name,112):"";
        int textWidth=(title.isEmpty()?0:font.width(title)+6)+font.width(distance);
        int labelWidth=textWidth+20;
        int[] at=labelPosition(x,y,labelWidth,screenWidth,screenHeight);
        ClientUi.pill(g,at[0],at[1],labelWidth,LABEL_HEIGHT,0xD910151C);
        ClientUi.rounded(g,at[0]+5,at[1]+4,6,6,3,color);
        int tx=at[0]+14;
        if(!title.isEmpty()){g.text(font,title,tx,at[1]+3,0xFFF3F4F7,false);tx+=font.width(title)+6;}
        g.text(font,distance,tx,at[1]+3,title.isEmpty()?0xFFF3F4F7:0xFFB6BCC9,false);
    }
    /** Upper-case first letter or digit of a name; "?" when it has none. */
    static String initial(String name){
        if(name!=null)for(int i=0;i<name.length();){int c=name.codePointAt(i);if(Character.isLetterOrDigit(c))return new String(Character.toChars(Character.toUpperCase(c)));i+=Character.charCount(c);}
        return "?";
    }
    static void field(GuiGraphicsExtractor g,int x,int y,int w,int h,boolean focused){
        ClientUi.rounded(g,x,y,w,h,Math.min(12,h/2),focused?ClientUi.ACCENT:0xFF2E3140);
        ClientUi.rounded(g,x+1,y+1,w-2,h-2,Math.min(11,h/2-1),0xFF252833);
    }
}
