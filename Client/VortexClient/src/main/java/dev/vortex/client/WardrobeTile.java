package dev.vortex.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.client.renderer.RenderPipelines;
import com.mojang.blaze3d.platform.InputConstants;

/** Compact visual choice; clipped rows cannot intercept header/footer clicks. */
final class WardrobeTile extends Button {
    private final UiMotion.Feedback motion;
    private final long sectionStarted;
    final int index;
    int clipTop,clipBottom;
    private final Identifier thumbnail;
    private final String builtinCape;
    private final java.util.function.BooleanSupplier selected;
    Runnable manage;
    boolean favorite;
    /** Small corner badge: PREVIEW for the item on the model, APPLIED for the one others see; empty for none. */
    java.util.function.Supplier<String> badge=()->"";
    WardrobeTile(int x,int y,int width,int index,String label,Identifier thumbnail,String builtinCape,
                 java.util.function.BooleanSupplier selected,Runnable choose,UiMotion.Feedback motion,long sectionStarted) {
        super(x,y,width,48,Component.literal(label),b->choose.run(),DEFAULT_NARRATION);
        this.index=index;this.thumbnail=thumbnail;this.builtinCape=builtinCape;this.selected=selected;
        this.motion=motion;this.sectionStarted=sectionStarted;
        setTooltip(net.minecraft.client.gui.components.Tooltip.create(Component.literal(label+" — click to preview. Saved tiles: ⋯ / right-click / R to manage.")));
    }
    @Override public boolean mouseClicked(MouseButtonEvent event,boolean twice){return event.y()>=clipTop && event.y()<clipBottom && super.mouseClicked(event,twice);}
    @Override public boolean isMouseOver(double x,double y){return y>=clipTop && y<clipBottom && super.isMouseOver(x,y);}
    @Override public void onPress(net.minecraft.client.input.InputWithModifiers input){motion.press();super.onPress(input);}
    @Override protected boolean isValidClickButton(net.minecraft.client.input.MouseButtonInfo button){return button.button()==InputConstants.MOUSE_BUTTON_LEFT || button.button()==InputConstants.MOUSE_BUTTON_RIGHT && manage!=null;}
    @Override public void onClick(MouseButtonEvent event,boolean twice){
        if(manage!=null && (event.button()==InputConstants.MOUSE_BUTTON_RIGHT || event.x()>=getRight()-17 && event.y()<getY()+18)){motion.press();manage.run();}
        else super.onClick(event,twice);
    }
    @Override public boolean keyPressed(net.minecraft.client.input.KeyEvent event){
        if(isFocused() && manage!=null && event.key()==com.mojang.blaze3d.platform.InputConstants.KEY_R){manage.run();return true;}
        return super.keyPressed(event);
    }
    @Override protected void extractContents(GuiGraphicsExtractor g,int mx,int my,float delta) {
        int x=getX(),y=getY(),w=getWidth();boolean chosen=selected.getAsBoolean();
        motion.frame(isHoveredOrFocused(),chosen);
        float reveal=VortexClient.config.uiAnimations?UiMotion.reveal((System.nanoTime()-Math.max(sectionStarted,motion.born))/1e6-Math.min(index,8)*12):1;
        UiMotion.begin(g,x,y,w,48,motion.scale()*(.985f+.015f*reveal),(1-reveal)*3);
        int background=ClientUi.mix(ClientUi.SURFACE,ClientUi.ACCENT_FAINT,motion.selected);
        background=ClientUi.mix(background,ClientUi.RAISED,motion.hover);
        ClientUi.panel(g,x,y,w,48,10,ClientUi.mix(ClientUi.PANEL,ClientUi.mix(ClientUi.BORDER,ClientUi.ACCENT,motion.selected*.8f+motion.hover*.2f),reveal),
            ClientUi.mix(ClientUi.PANEL,background,reveal));
        int tint=((int)(255*reveal)<<24)|0xFFFFFF;
        if(manage!=null)for(int dot=0;dot<3;dot++)g.fill(x+w-14+dot*4,y+7,x+w-12+dot*4,y+9,ClientUi.MUTED);
        if(favorite){g.fill(x+4,y+4,x+9,y+10,ClientUi.ACCENT);g.fill(x+4,y+10,x+6,y+12,ClientUi.ACCENT);g.fill(x+7,y+10,x+9,y+12,ClientUi.ACCENT);}
        if(thumbnail!=null)g.blit(RenderPipelines.GUI_TEXTURED,thumbnail,x+(w-20)/2,y+3,0,0,20,32,40,64,40,64,tint);
        else if(builtinCape!=null && CapeCatalog.texture(builtinCape)!=null) {
            int scale=CapeImages.width(builtinCape)/64;
            g.blit(RenderPipelines.GUI_TEXTURED,CapeCatalog.texture(builtinCape),x+(w-20)/2,y+3,12*scale,scale,20,32,10*scale,16*scale,64*scale,32*scale,tint);
        } else ClientUi.icon(g,builtinCape==null?"profile":"capes",x+(w-22)/2,y+7,22,ClientUi.mix(ClientUi.PANEL,ClientUi.MUTED,reveal));
        var font=UiFonts.body();
        String tag=badge.get();
        if(!tag.isEmpty() && w>=60){
            int tagColor=tag.equals("APPLIED")?0xFF86EFC4:ClientUi.ACCENT,tagWidth=font.width(tag)/2+8;
            ClientUi.pill(g,x+4,y+4,tagWidth,8,ClientUi.mix(ClientUi.PANEL,(tagColor&0xFFFFFF)|0x33000000,reveal));
            g.pose().pushMatrix();g.pose().translate(x+8,y+6);g.pose().scale(.5f,.5f);
            g.text(font,tag,0,0,ClientUi.mix(ClientUi.PANEL,tagColor,reveal),false);g.pose().popMatrix();
        }
        String label=font.plainSubstrByWidth(getMessage().getString(),w-8);
        g.text(font,label,x+(w-font.width(label))/2,y+36,ClientUi.mix(ClientUi.PANEL,chosen?ClientUi.TEXT:ClientUi.MUTED,reveal),false);
        g.pose().popMatrix();
    }
}
