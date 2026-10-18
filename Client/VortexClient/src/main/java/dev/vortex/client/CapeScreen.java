package dev.vortex.client;

import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;

public final class CapeScreen extends Screen {
    private final Screen parent;
    private String code = "";
    private boolean wasBusy, accessPage, accessOnly;
    private UUID shownProfile;
    public CapeScreen(Screen parent) { super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Vortex Capes")); this.parent = parent; }
    public static CapeScreen access(Screen parent) {var screen=new CapeScreen(parent);screen.accessPage=screen.accessOnly=true;return screen;}
    public record Layout(int x, int y, int width, int height, int cardWidth) {
        public int previewWidth() { return Math.min(160, (width - 40) / 3); }
        public int listX() { return x + previewWidth() + 24; }
        public int listWidth() { return width - previewWidth() - 36; }
        public int rowStep() { return (height - 118) / 5; }
    }
    public static Layout layout(int width,int height) {
        int w = Math.min(540,Math.max(200,width-16)), h = Math.min(280,height-16);
        return new Layout((width-w)/2,(height-h)/2,w,h,(w-40)/5);
    }
    private void action(int x,int y,int w,int h,String title,Runnable press,boolean primary) {
        addRenderableWidget(new ClientUi.ActionButton(x,y,w,h,Component.literal(title),b->press.run(),primary));
    }
    @Override protected void init() {
        var l = layout(width,height); int x=l.x(), y=l.y(), w=l.width(), h=l.height();
        wasBusy = Capes.busy(); shownProfile = Capes.profile();
        action(x+w-60,y+10,48,22,"Back",this::onClose,false);
        if (accessPage) { initAccess(l); return; }
        action(x+w-146,y+10,80,22,"Online access",()->{accessPage=true;rebuildWidgets();},false);
        String[] names = {"Vanilla cape", "Vortex", "Aurora", "Ember", "My image"};
        for(int i=0;i<5;i++) {
            String id=i<4?CapeCatalog.IDS[i]:Capes.customPreview();
            action(l.listX(),y+44+i*l.rowStep(),l.listWidth(),l.rowStep()-3,names[i],()->{
                if(id.isEmpty()) Capes.choosePng(); else Capes.preview(id); rebuildWidgets();
            },!id.isEmpty() && id.equals(VortexClient.config.selectedCape));
        }
        action(x+12,y+h-80,l.previewWidth(),21,VortexClient.config.capesEnabled?"Capes: ON":"Capes: OFF",()->{
            Capes.enabled(!VortexClient.config.capesEnabled);rebuildWidgets();
        },false);
        int half=(w-28)/2;
        action(x+12,y+h-52,half,24,"Choose image…",Capes::choosePng,false);
        var save = new ClientUi.ActionButton(x+16+half,y+h-52,half,24,Component.literal("Save online"),b->Capes.publish(),true);
        save.active=!Capes.busy(); addRenderableWidget(save);
    }
    private void initAccess(Layout l) {
        int x=l.x(), y=l.y(), w=l.width(), h=l.height();
        if(AccountWardrobe.connected()){
            action(x+12,y+99,w-24,24,"Account connected — no activation code needed",()->{Capes.message="Return to Wardrobe. Apply automatically syncs your selection.";},true);
            action(x+12,y+h-52,w-24,24,"Back to wardrobe",this::onClose,false);return;
        }
        var input = new EditBox(font,x+12,y+99,w-24,22,Component.literal("Private activation code"));
        input.setMaxLength(64); input.setTextShadow(false); input.setHint(Component.literal("One-time invitation code"));
        input.setValue(code); input.setResponder(value->code=value); addRenderableWidget(input);
        action(x+12,y+128,(w-28)/2,24,"Activate profile",()->{
            String submitted=code; code=""; Capes.activate(submitted); rebuildWidgets();
        },true);
        action(x+16+(w-28)/2,y+128,(w-28)/2,24,"Copy profile ID",()->{
            UUID id=Capes.profile();
            if(id==null) Capes.message="Join the target world/server before copying its profile ID.";
            else { minecraft.keyboardHandler.setClipboard(id.toString()); Capes.message="Profile ID copied. Keep your invitation code private."; }
        },false);
        action(x+12,y+h-52,w-24,24,accessOnly?"Back to wardrobe":"Back to capes",this::onClose,false);
    }
    @Override public void tick() { if(wasBusy!=Capes.busy() || !java.util.Objects.equals(shownProfile,Capes.profile())) rebuildWidgets(); }
    @Override public boolean isInGameUi() { return minecraft.level!=null; }
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta) {
        if(isInGameUi()) minecraft.gui.hud.extractDeferredSubtitles(); else extractPanorama(g,delta);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        var l=layout(width,height); int x=l.x(),y=l.y(),w=l.width(),h=l.height();
        g.fill(0,0,width,height,0xA008090F);
        ClientUi.panel(g,x,y,w,l.height(),10,ClientUi.BORDER,ClientUi.PANEL);
        ClientUi.heading(g,accessPage?"Online access":"Capes",x+12,y+14,w-(accessPage?90:160));
        if(accessPage) {
            ClientUi.text(g,AccountWardrobe.connected()?"Vortex account wardrobe is connected.":"Legacy access. Vortex accounts need no code.",x+12,y+48,w-24,ClientUi.TEXT);
            ClientUi.text(g,AccountWardrobe.connected()?"Use your Vortex nickname or an owned legacy profile.":"For code-free access, sign in in the launcher.",x+12,y+65,w-24,ClientUi.MUTED);
            ClientUi.text(g,"Wardrobe Apply syncs automatically when signed in.",x+12,y+82,w-24,ClientUi.MUTED);
        } else {
            int ph=h-130;
            ClientUi.rounded(g,x+12,y+44,l.previewWidth(),ph,6,0xFF171E27);
            var texture=CapeCatalog.texture(VortexClient.config.selectedCape);
            if(texture==null) ClientUi.text(g,"Vanilla",x+21,y+44+ph/2,l.previewWidth()-18,ClientUi.MUTED);
            else {
                int scale=CapeImages.width(VortexClient.config.selectedCape)/64;
                int ch=Math.min(ph-12,(l.previewWidth()-16)*16/10), cw=ch*10/16;
                g.blit(RenderPipelines.GUI_TEXTURED,texture,x+12+(l.previewWidth()-cw)/2,y+44+(ph-ch)/2,
                    12*scale,scale,cw,ch,10*scale,16*scale,64*scale,32*scale,0xFFFFFFFF);
            }
        }
        ClientUi.text(g,Capes.busy()?Capes.progressMessage:Capes.message,x+12,y+h-20,w-24,ClientUi.ACCENT);
        super.extractRenderState(g,mx,my,delta);
    }
    @Override public void removed() { code=""; }
    @Override public boolean isPauseScreen() { return false; }
    @Override public void onClose() { code=""; if(accessPage && !accessOnly) {accessPage=false;rebuildWidgets();} else minecraft.gui.setScreen(parent); }
}
