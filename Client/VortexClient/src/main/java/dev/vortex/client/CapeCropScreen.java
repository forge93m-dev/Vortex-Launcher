package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;

/** Large live crop preview; only the small preview texture changes while editing. */
public final class CapeCropScreen extends Screen {
    private static final Identifier PREVIEW=Identifier.fromNamespaceAndPath("vortex_client","cape_crop_preview");
    private final Screen parent;
    private final CapeArtwork.Source source;
    private double zoom=1,panX,panY;
    private boolean fit=true,dirty=true;
    private boolean textureMode;
    private DynamicTexture texture;
    private boolean previewDragging;
    public CapeCropScreen(Screen parent,CapeArtwork.Source source) {super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Crop cape photo")); this.parent=parent; this.source=source; textureMode=source.width()==64 && source.height()==32;}
    public record Layout(int x,int y,int width,int height,int previewX,int previewY,int previewWidth,int previewHeight,int controlsX,int controlsWidth) { }
    public static Layout layout(int width,int height) {
        int w=Math.min(560,width-16),h=Math.min(360,height-16),x=(width-w)/2,y=(height-h)/2;
        int ph=Math.min(256,h-82),pw=ph*10/16,cx=x+pw+30;
        return new Layout(x,y,w,h,x+12,y+38,pw,ph,cx,w-pw-42);
    }
    @Override protected void init() {
        var l=layout(width,height);
        if(texture==null) {
            texture=new DynamicTexture(()->"Vortex photo crop",new com.mojang.blaze3d.platform.NativeImage(160,256,false));
            minecraft.getTextureManager().register(PREVIEW,texture);
        }
        dirty=true;
        int cx=l.controlsX(),cw=l.controlsWidth(),y=l.y();
        addRenderableWidget(new ClientUi.ActionButton(cx,y+38,cw,20,Component.literal(textureMode?"Mode: cape texture":fit?"Mode: fit whole photo":"Mode: fill / crop"),b->{
            if(textureMode) {textureMode=false;fit=true;} else if(fit) fit=false; else if(CapePixels.dimensions(source.width(),source.height())) textureMode=true; else fit=true;
            dirty=true; rebuildWidgets();
        }));
        addRenderableWidget(new ClientUi.Slider(cx,y+64,cw,"Zoom",1,4,zoom,v->String.format(java.util.Locale.ROOT,"%.2fx",v),v->{zoom=v; dirty=true;},()->{})).active=!textureMode;
        addRenderableWidget(new ClientUi.Slider(cx,y+104,cw,"Horizontal",-1,1,panX,v->Math.round(v*100)+"%",v->{panX=v; dirty=true;},()->{})).active=!textureMode;
        addRenderableWidget(new ClientUi.Slider(cx,y+144,cw,"Vertical",-1,1,panY,v->Math.round(v*100)+"%",v->{panY=v; dirty=true;},()->{})).active=!textureMode;
        int by=y+l.height()-30;
        addRenderableWidget(new ClientUi.ActionButton(l.x()+12,by,70,22,Component.literal("Reset"),b->{zoom=1;panX=panY=0;fit=true;textureMode=false;dirty=true;rebuildWidgets();}));
        addRenderableWidget(new ClientUi.ActionButton(l.x()+86,by,70,22,Component.literal("Cancel"),b->onClose()));
        addRenderableWidget(new ClientUi.ActionButton(l.x()+160,by,l.width()-172,22,Component.literal("Use image"),b->{
            minecraft.gui.setScreen(parent); Capes.acceptArtwork(source,zoom,panX,panY,fit,textureMode);
        },true));
    }
    @Override public void tick() {
        if(!dirty || texture==null) return; dirty=false;
        byte[] pixels=textureMode?CapeArtwork.textureFace(source):CapeArtwork.face(source,160,256,zoom,panX,panY,fit);
        var image=texture.getPixels();
        for(int y=0;y<256;y++) for(int x=0;x<160;x++) {
            int p=(y*160+x)*4; image.setPixel(x,y,0xFF000000|((pixels[p]&255)<<16)|((pixels[p+1]&255)<<8)|(pixels[p+2]&255));
        }
        texture.upload();
    }
    private void release() {if(texture!=null) {minecraft.getTextureManager().release(PREVIEW); texture=null;}}
    @Override public void removed() {release();}
    @Override public void onClose() {minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen() {return false;}
    @Override public boolean isInGameUi() {return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta) {if(isInGameUi()) minecraft.gui.hud.extractDeferredSubtitles(); else extractPanorama(g,delta);}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        var l=layout(width,height); g.fill(0,0,width,height,0xA008090F);
        ClientUi.panel(g,l.x(),l.y(),l.width(),l.height(),10,ClientUi.BORDER,ClientUi.PANEL);
        ClientUi.heading(g,"Photo to HD cape",l.x()+12,l.y()+12,l.width()-24);
        if(texture!=null) g.blit(RenderPipelines.GUI_TEXTURED,PREVIEW,l.previewX(),l.previewY(),0,0,l.previewWidth(),l.previewHeight(),160,256,160,256,0xFFFFFFFF);
        if(l.height()>280) ClientUi.text(g,"1024x512 atlas / 160x256 artwork",l.controlsX(),l.y()+194,l.controlsWidth(),ClientUi.MUTED);
        super.extractRenderState(g,mx,my,delta);
    }
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical) {
        var l=layout(width,height);
        if(textureMode || x<l.previewX() || x>l.previewX()+l.previewWidth() || y<l.previewY() || y>l.previewY()+l.previewHeight() || !Double.isFinite(vertical)) return super.mouseScrolled(x,y,horizontal,vertical);
        zoom=Math.clamp(zoom*Math.pow(1.12,Math.clamp(vertical,-10,10)),1,4); dirty=true; rebuildWidgets(); return true;
    }
    @Override public boolean mouseDragged(MouseButtonEvent event,double dx,double dy) {
        var l=layout(width,height);
        if(!previewDragging || event.button()!=0) return super.mouseDragged(event,dx,dy);
        panX=Math.clamp(panX-dx/l.previewWidth()*2/zoom,-1,1); panY=Math.clamp(panY-dy/l.previewHeight()*2/zoom,-1,1);
        dirty=true; return true;
    }
    @Override public boolean mouseClicked(MouseButtonEvent event,boolean doubleClick) {
        var l=layout(width,height);
        if(!textureMode && event.button()==0 && event.x()>=l.previewX() && event.x()<l.previewX()+l.previewWidth() && event.y()>=l.previewY() && event.y()<l.previewY()+l.previewHeight()) {
            previewDragging=true; setDragging(true); return true;
        }
        return super.mouseClicked(event,doubleClick);
    }
    @Override public boolean mouseReleased(MouseButtonEvent event) {
        if(previewDragging && event.button()==0) {previewDragging=false;setDragging(false);rebuildWidgets();return true;}
        return super.mouseReleased(event);
    }
}
