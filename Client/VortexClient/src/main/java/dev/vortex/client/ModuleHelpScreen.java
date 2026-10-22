package dev.vortex.client;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
public final class ModuleHelpScreen extends Screen {
    private final long opened = System.nanoTime();
    private final Screen parent;private final ClientModule module;private int scroll;
    public ModuleHelpScreen(Screen parent,ClientModule module){super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Settings explained"));this.parent=parent;this.module=module;}
    private int w(){return Math.min(520,width-16);}private int h(){return Math.min(360,height-16);}private int x(){return (width-w())/2;}private int y(){return (height-h())/2;}
    @Override protected void init(){addRenderableWidget(new ClientUi.ActionButton(x()+w()-92,y()+h()-34,80,24,Component.literal("Back"),b->onClose(),true));}
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical){if(Double.isFinite(vertical)){scroll=(int)Math.clamp(scroll-vertical*3,0,Math.max(0,font.split(Component.literal(SettingsHelp.describe(module)),w()-32).size()-(h()-90)/12));return true;}return false;}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){ClientUi.beginPage(g,width,height,x(),y(),w(),h(),"Settings explained","help",opened);var lines=font.split(Component.literal(SettingsHelp.describe(module)),w()-32);int capacity=(h()-90)/12;scroll=Math.clamp(scroll,0,Math.max(0,lines.size()-capacity));for(int i=0;i<capacity && scroll+i<lines.size();i++)g.text(font,lines.get(scroll+i),x()+16,y()+43+i*12,ClientUi.TEXT,false);if(lines.size()>capacity)ClientUi.text(g,"Scroll for more",x()+16,y()+h()-26,w()-120,ClientUi.MUTED);super.extractRenderState(g,mx,my,delta);g.pose().popMatrix();}
    @Override public void onClose(){minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(isInGameUi())minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
}
