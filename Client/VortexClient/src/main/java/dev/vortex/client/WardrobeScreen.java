package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.core.ClientAsset;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.player.PlayerSkin;
import net.minecraft.client.resources.DefaultPlayerSkin;

/** One wardrobe for skins and capes. Local skin controls never imply online publication. */
public final class WardrobeScreen extends Screen {
    private final Screen parent;
    private boolean skinTab=true,slim,vanillaSkin;
    private byte[] draftPixels;
    private ClientAsset.Texture draftTexture;
    private boolean skinDirty;
    private String draftCape;
    private byte[] draftCapePixels;
    private int draftGeneration;
    private WardrobePreview preview;
    private final WardrobePreview.Camera camera=new WardrobePreview.Camera();
    public boolean retainsCape(ClientAsset.Texture texture){return camera.capeFade.retains(CapeCatalog.canonical(texture));}
    private boolean wasBusy;
    private boolean modelChosen;
    private SkinPixels.Selection shownSkin;
    private java.util.UUID shownProfile;
    private final java.util.List<WardrobeTile> tiles=new java.util.ArrayList<>();
    private final java.util.Map<String,net.minecraft.resources.Identifier> thumbnails=new java.util.HashMap<>();
    private WardrobeGallery.Result gallery=new WardrobeGallery.Result(java.util.List.of(),java.util.List.of(),"");
    private boolean galleryLoaded,galleryLoading,selectionBusy;
    private long galleryRevision=-1;
    private int scroll;
    private long sectionStarted=System.nanoTime(),navigationFrame=System.nanoTime();
    private float navigationPosition;
    private final java.util.Map<String,UiMotion.Feedback> tileMotions=new java.util.HashMap<>();
    public WardrobeScreen(Screen parent) {this(Minecraft.getInstance(),UiFonts.body(),parent);}
    WardrobeScreen(Minecraft client,net.minecraft.client.gui.Font font,Screen parent){super(client,font,Component.literal("Wardrobe"));this.parent=parent;}
    public record Layout(int x,int y,int width,int height,int sidebar,int previewWidth) {
        public int listX(){return x+sidebar+12;}
        public int previewX(){return x+width-previewWidth-12;}
        public int listWidth(){return previewX()-listX()-12;}
        public int rowHeight(){return Math.min(28,(height-124)/5);}
        public int columns(){return listWidth()>=260?3:listWidth()>=170?2:1;}
        public int tileWidth(){return (listWidth()-(columns()-1)*6)/columns();}
        public int galleryTop(){return y+100;}
        public int galleryBottom(){return y+height-65;}
    }
    public static Layout layout(int width,int height) {
        int w=Math.min(720,width-16),h=Math.min(380,height-16);
        int side=w<440?64:92,pw=w<440?108:Math.min(280,w*40/100);
        return new Layout((width-w)/2,(height-h)/2,w,h,side,pw);
    }
    private void button(int x,int y,int w,int h,String text,Runnable press,boolean primary) {
        addRenderableWidget(new ClientUi.ActionButton(x,y,w,h,Component.literal(text),b->press.run(),primary));
    }
    @Override protected void init() {
        LocalSkins.tick(minecraft);
        if(draftCapePixels!=null && CapeImages.asset(draftCape)==null)Capes.prepareDraft(draftCapePixels);
        tiles.clear();
        var l=layout(width,height);int x=l.x(),y=l.y(),h=l.height();
        wasBusy=LocalSkins.busy()||Capes.busy();shownProfile=LocalSkins.profile(minecraft);
        shownSkin=LocalSkins.saved();if(!modelChosen && shownSkin!=null)slim=shownSkin.slim();
        if(!skinDirty)try{selectedSkinId=shownSkin==null?"":CosmeticCollection.skinId(shownSkin);}catch(Exception unavailable){selectedSkinId="";}
        button(x+8,y+49,l.sidebar()-16,24,"Skin",()->section(true),skinTab);
        button(x+8,y+79,l.sidebar()-16,24,"Cape",()->section(false),!skinTab);
        button(x+8,y+h-66,l.sidebar()-16,24,"Access",()->minecraft.gui.setScreen(CapeScreen.access(this)),false);
        button(x+8,y+h-36,l.sidebar()-16,24,"Back",this::onClose,false);
        if(skinTab) initSkin(l);else initCape(l);
        if(skinTab)button(x+8,y+109,l.sidebar()-16,23,"Retry sync",()->{if(skinDirty){LocalSkins.message="Apply or cancel the preview before sharing online.";return;}LocalSkins.publish();},false);
        if(!skinTab)button(x+8,y+109,l.sidebar()-16,23,"Retry sync",()->{
            if(draftCape!=null && !draftCape.equals(VortexClient.config.selectedCape)){Capes.message="Apply or cancel the preview before sharing online.";return;}
            if(!VortexClient.config.capesEnabled && !Capes.enabled(true))return;Capes.publish();
        },false);
        buildTiles(l);
        // The model starts under the "Preview" caption so the head never covers the text.
        preview=addRenderableWidget(new WardrobePreview(l.previewX(),y+70,l.previewWidth(),h-125,this::appearance,camera));
        int half=(l.previewWidth()-4)/2;
        button(l.previewX(),y+h-49,half,21,"Front",preview::front,false);
        button(l.previewX()+half+4,y+h-49,half,21,"Back",preview::back,false);
        if(!galleryLoading && (!galleryLoaded || galleryRevision!=CosmeticCollection.revision()))refreshGallery();
    }
    private void section(boolean skin){
        camera.focusCape(!skin);
        if(skinTab!=skin){skinTab=skin;sectionStarted=System.nanoTime();scroll=0;rebuildWidgets();}
    }
    private void refreshGallery() {
        galleryLoading=true;long requested=CosmeticCollection.revision();
        WardrobeGallery.refresh(result->{galleryLoading=false;
            gallery=result;galleryLoaded=true;galleryRevision=requested;
            var keep=new java.util.HashSet<String>();
            for(var item:gallery.skins())keep.add("skin/"+item.entry().id());
            for(var item:gallery.capes())keep.add("cape/"+item.entry().id());
            var iterator=thumbnails.entrySet().iterator();
            while(iterator.hasNext()){var entry=iterator.next();if(!keep.contains(entry.getKey())){minecraft.getTextureManager().release(entry.getValue());iterator.remove();}}
            if(minecraft.gui.screen()==this)rebuildWidgets();
        });
    }
    private void initSkin(Layout l) {
        int x=l.listX(),y=l.y()+49,w=l.listWidth();
        button(x,y,w,22,"Add skin…",()->{int request=++draftGeneration;var profile=LocalSkins.profile(minecraft);LocalSkins.choose(slim,pixels->{
            if(minecraft.gui.screen()!=this || request!=draftGeneration || !java.util.Objects.equals(profile,LocalSkins.profile(minecraft)))return;
            try {draftPixels=pixels;draftTexture=LocalSkins.draft(pixels);vanillaSkin=false;skinDirty=true;selectedSkinId=CosmeticCollection.skinId(new SkinPixels.Selection(true,slim,pixels));LocalSkins.message="Preview only. Apply or Cancel below.";rebuildWidgets();}
            catch(Exception error){LocalSkins.message="Could not display this skin.";}
        });},false);
        button(x,y+26,w,20,slim?"Arms: slim":"Arms: classic",()->{++draftGeneration;modelChosen=true;slim=!slim;skinDirty=true;rebuildWidgets();},false);
        int half=(w-4)/2,footer=l.y()+l.height()-58;
        button(x,footer,half,26,"Apply",()->{
            if(selectionBusy || LocalSkins.busy() || Capes.busy()){LocalSkins.message="Wait for the current image operation.";return;}
            byte[] pixels=draftPixels;
            if(pixels==null && LocalSkins.saved()!=null)pixels=LocalSkins.saved().rgba();
            int request=++draftGeneration;var profile=LocalSkins.profile(minecraft);
            LocalSkins.apply(pixels,slim,!vanillaSkin,success->{if(success && java.util.Objects.equals(profile,LocalSkins.profile(minecraft))){WardrobeAutoSync.skinApplied(profile);if(request==draftGeneration){skinDirty=false;modelChosen=false;vanillaSkin=false;draftPixels=null;draftTexture=null;LocalSkins.discardDraft();if(minecraft.gui.screen()==this)rebuildWidgets();}}});
        },true);
        button(x+half+4,footer,w-half-4,26,"Cancel",this::cancelSkin,false);
    }
    private void initCape(Layout l) {
        int addWidth=(l.listWidth()-4)/2;
        button(l.listX(),l.y()+49,addWidth,24,"Add cape…",Capes::choosePng,false);
        button(l.listX()+addWidth+4,l.y()+49,l.listWidth()-addWidth-4,24,"Animated…",Capes::chooseAnimated,false);
        int y=l.y()+l.height()-58,w=l.listWidth(),half=(w-4)/2;
        button(l.listX(),y,half,26,"Apply",()->{
            if(CapeCatalog.custom(pendingCape()) && (selectionBusy || LocalSkins.busy() || Capes.busy())){Capes.message="Wait for the current image operation.";return;}
            String chosen=pendingCape();int request=++draftGeneration;var profile=LocalSkins.profile(minecraft);
            Capes.applyDraft(chosen,draftCapePixels,success->{if(success && java.util.Objects.equals(profile,LocalSkins.profile(minecraft))){WardrobeAutoSync.capeApplied(profile,chosen);if(request==draftGeneration){draftCape=null;draftCapePixels=null;if(minecraft.gui.screen()==this)rebuildWidgets();}}});
        },true);
        button(l.listX()+half+4,y,w-half-4,26,"Cancel",this::cancelCape,false);
    }
    private String pendingCape(){return draftCape==null?VortexClient.config.selectedCape:draftCape;}
    private void cancelSkin(){if(LocalSkins.busy()){LocalSkins.message="Wait for the current skin operation.";return;}++draftGeneration;skinDirty=false;draftPixels=null;draftTexture=null;vanillaSkin=false;modelChosen=false;LocalSkins.discardDraft();LocalSkins.message="Preview cancelled. Applied skin unchanged.";if(minecraft!=null)rebuildWidgets();}
    private void cancelCape(){++draftGeneration;draftCape=null;draftCapePixels=null;Capes.message="Preview cancelled. Applied cape unchanged.";if(minecraft!=null)rebuildWidgets();}
    private void previewBuiltinCape(String id){if(!CapeCatalog.valid(id) || CapeCatalog.custom(id))return;++draftGeneration;draftCape=id;draftCapePixels=null;Capes.message="Preview only. Apply or Cancel below.";if(minecraft!=null)rebuildWidgets();}
    private void previewVanillaSkin(){++draftGeneration;vanillaSkin=true;skinDirty=true;draftPixels=null;draftTexture=null;selectedSkinId="";LocalSkins.discardDraft();LocalSkins.message="Vanilla preview. Apply or Cancel below.";if(minecraft!=null)rebuildWidgets();}
    public void stageCape(byte[] pixels) {
        if(!java.util.Objects.equals(shownProfile,LocalSkins.profile(minecraft)))return;
        try{if(Capes.prepareDraft(pixels)){++draftGeneration;draftCape=CapePixels.id(pixels);draftCapePixels=pixels;Capes.message="Preview only. Apply or Cancel below.";rebuildWidgets();}}
        catch(Exception error){Capes.message="Could not display this cape. Applied cape unchanged.";}
    }
    private net.minecraft.resources.Identifier thumbnail(WardrobeGallery.Item item) {
        String key=item.entry().kind().name().toLowerCase(java.util.Locale.ROOT)+"/"+item.entry().id();
        if(thumbnails.containsKey(key))return thumbnails.get(key);
        var image=new com.mojang.blaze3d.platform.NativeImage(40,64,false);
        net.minecraft.client.renderer.texture.DynamicTexture texture=null;
        try {
            for(int y=0;y<64;y++)for(int x=0;x<40;x++){int p=(y*40+x)*4;byte[] b=item.thumbnail();
                image.setPixel(x,y,((b[p+3]&255)<<24)|((b[p]&255)<<16)|((b[p+1]&255)<<8)|(b[p+2]&255));}
            var id=net.minecraft.resources.Identifier.fromNamespaceAndPath("vortex_client","wardrobe/"+Integer.toHexString(System.identityHashCode(this))+"/"+key);
            texture=new net.minecraft.client.renderer.texture.DynamicTexture(()->"Vortex wardrobe thumbnail",image);
            minecraft.getTextureManager().register(id,texture);thumbnails.put(key,id);return id;
        }catch(Exception error){if(texture!=null)texture.close();else image.close();return null;}
    }
    private WardrobeTile tile(Layout l,String label,net.minecraft.resources.Identifier thumb,String cape,java.util.function.BooleanSupplier selected,Runnable choose) {
        int index=tiles.size();
        String key=(skinTab?"skin/":"cape/")+(thumb!=null?thumb.toString():cape!=null?cape:label);
        var motion=tileMotions.computeIfAbsent(key,k->new UiMotion.Feedback());
        var tile=new WardrobeTile(l.listX()+index%l.columns()*(l.tileWidth()+6),0,l.tileWidth(),index,label,thumb,cape,selected,()->{if(!skinTab || !LocalSkins.busy())choose.run();},motion,sectionStarted);
        tile.badge=()->{boolean applied=applied(cape,label);return selected.getAsBoolean()&&!applied?"PREVIEW":applied?"APPLIED":"";};
        tiles.add(addWidget(tile));return tile;
    }
    /** Built-in capes and the Vanilla tiles; gallery tiles get their own check through {@link #managed}. */
    private boolean applied(String cape,String label){
        if(cape!=null)return cape.equals(VortexClient.config.selectedCape);
        if(skinTab && label.equals("Vanilla"))return LocalSkins.saved()==null || !LocalSkins.saved().enabled();
        return false;
    }
    private boolean isApplied(WardrobeGallery.Item item){
        if(item.entry().kind()==CosmeticCollection.Kind.CAPE)return item.entry().id().equals(VortexClient.config.selectedCape.replace(':','_'));
        try{var saved=LocalSkins.saved();return saved!=null && saved.enabled() && item.entry().id().equals(CosmeticCollection.skinId(saved));}catch(Exception ignored){return true;}
    }
    private void managed(WardrobeTile tile,WardrobeGallery.Item item){
        tile.favorite=item.favorite();
        var chosen=tile.badge;
        tile.badge=()->isApplied(item)?"APPLIED":chosen.get();tile.manage=()->minecraft.gui.setScreen(new WardrobeItemScreen(this,item,()->!isApplied(item)));
    }
    private void buildTiles(Layout l) {
        if(skinTab) {
            tile(l,"Vanilla",null,null,()->vanillaSkin || draftTexture==null && (LocalSkins.saved()==null || !LocalSkins.saved().enabled()),this::previewVanillaSkin);
            for(var item:gallery.skins())managed(tile(l,item.label(),thumbnail(item),null,()->skinSelected(item),()->selectSkin(item)),item);
        } else {
            for(String id:CapeCatalog.IDS)tile(l,id.equals("none")?"Vanilla":Character.toUpperCase(id.charAt(0))+id.substring(1),null,id,()->id.equals(pendingCape()),()->previewBuiltinCape(id));
            for(var item:gallery.capes())managed(tile(l,item.label(),thumbnail(item),null,()->item.entry().id().equals(pendingCape().replace(':','_')),()->selectCape(item)),item);
        }
        scroll=Math.clamp(scroll,0,maximumScroll(l));positionTiles(l);
    }
    private boolean skinSelected(WardrobeGallery.Item item) {
        if(vanillaSkin || !skinDirty && (LocalSkins.saved()==null || !LocalSkins.saved().enabled()))return false;
        return item.entry().id().equals(selectedSkinId);
    }
    private String selectedSkinId="";
    private void selectSkin(WardrobeGallery.Item item) {
        if(selectionBusy || LocalSkins.busy()){LocalSkins.message="Wait for the current skin operation.";return;}
        selectionBusy=true;var target=LocalSkins.profile(minecraft);int request=++draftGeneration;
        WardrobeGallery.skin(item.entry(),skin->{selectionBusy=false;
            if(skin==null || request!=draftGeneration || minecraft.gui.screen()!=this || !java.util.Objects.equals(target,LocalSkins.profile(minecraft)))return;
            try {draftPixels=skin.rgba();draftTexture=LocalSkins.draft(draftPixels);slim=skin.slim();modelChosen=true;vanillaSkin=false;
                skinDirty=true;selectedSkinId=item.entry().id();LocalSkins.message="Preview only. Apply or Cancel below.";rebuildWidgets();}
            catch(Exception error){LocalSkins.message="Could not select this saved skin.";}
        });
    }
    private void selectCape(WardrobeGallery.Item item){
        if(selectionBusy)return;
        selectionBusy=true;var profile=LocalSkins.profile(minecraft);int request=++draftGeneration;
        WardrobeGallery.cape(item.entry(),pixels->{selectionBusy=false;if(pixels!=null && request==draftGeneration && minecraft.gui.screen()==this && java.util.Objects.equals(profile,LocalSkins.profile(minecraft)))stageCape(pixels);});
    }
    private int maximumScroll(Layout l){return Math.max(0,((tiles.size()+l.columns()-1)/l.columns())*54-6-(l.galleryBottom()-l.galleryTop()));}
    private void positionTiles(Layout l){for(var tile:tiles){tile.setY(l.galleryTop()+tile.index/l.columns()*54-scroll);
        tile.clipTop=l.galleryTop();tile.clipBottom=l.galleryBottom();tile.visible=tile.getY()+48>tile.clipTop && tile.getY()<tile.clipBottom;}}
    @Override public boolean mouseScrolled(double x,double y,double horizontal,double vertical) {
        var l=layout(width,height);
        if(Double.isFinite(vertical) && x>=l.listX() && x<l.listX()+l.listWidth() && y>=l.galleryTop() && y<l.galleryBottom()){
            scroll=(int)Math.clamp(scroll-vertical*54,0,maximumScroll(l));positionTiles(l);return true;}
        return super.mouseScrolled(x,y,horizontal,vertical);
    }
    private PlayerSkin appearance() {
        var base=minecraft.player==null?DefaultPlayerSkin.get(minecraft.getUser().getProfileId()):LocalSkins.original(Capes.original(minecraft.player.getUUID(),minecraft.player.getSkin()));
        if(LocalSkins.saved()==null)base=AccountSkinTextures.skin(LocalSkins.profile(minecraft),base);
        if(!vanillaSkin) {
            if(draftTexture!=null)base=LocalSkins.patch(base,draftTexture,slim);
            else if(LocalSkins.saved()!=null && LocalSkins.saved().enabled()) {
                base=LocalSkins.skin(LocalSkins.profile(minecraft),base);
                base=new PlayerSkin(base.body(),base.cape(),base.elytra(),slim?net.minecraft.world.entity.player.PlayerModelType.SLIM:net.minecraft.world.entity.player.PlayerModelType.WIDE,base.secure());
            }
        }
        return CapeCatalog.patch(base,pendingCape());
    }
    @Override public void tick() {
        boolean busy=LocalSkins.busy()||Capes.busy();var id=LocalSkins.profile(minecraft);
        if(!java.util.Objects.equals(id,shownProfile)){++draftGeneration;skinDirty=false;draftCape=null;draftCapePixels=null;draftPixels=null;draftTexture=null;LocalSkins.discardDraft();vanillaSkin=false;modelChosen=false;}
        if(busy!=wasBusy || !java.util.Objects.equals(id,shownProfile) || shownSkin!=LocalSkins.saved())rebuildWidgets();
        if(!galleryLoading && galleryRevision!=CosmeticCollection.revision())refreshGallery();
    }
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public boolean isPauseScreen(){return false;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta) {
        if(isInGameUi())minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);
    }
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta) {
        var l=layout(width,height);g.fill(0,0,width,height,0xA00C0D12);
        ClientUi.rounded(g,l.x()-2,l.y()+5,l.width()+4,l.height(),12,0x40000000);
        ClientUi.rounded(g,l.x(),l.y(),l.width(),l.height(),12,ClientUi.GLASS);
        ClientUi.heading(g,"Wardrobe",l.listX(),l.y()+17,l.width()-l.sidebar()-25);
        ClientUi.icon(g,"brand",l.x()+18,l.y()+13,26,ClientUi.TEXT);
        g.fill(l.x()+l.sidebar(),l.y()+10,l.x()+l.sidebar()+1,l.y()+l.height()-10,ClientUi.BORDER);
        long now=System.nanoTime();navigationPosition=ClientUi.approach(navigationPosition,skinTab?0:1,(now-navigationFrame)/1e9);navigationFrame=now;
        ClientUi.rounded(g,l.x()+4,l.y()+53+Math.round(navigationPosition*30),3,16,1,ClientUi.ACCENT);
        ClientUi.rounded(g,l.previewX(),l.y()+42,l.previewWidth(),l.height()-66,12,0xFF1D1F27);
        // The preview is labeled with what it shows, so a draft is never mistaken for the applied look.
        ClientUi.eyebrow(g,"Preview · drag to turn",l.previewX()+10,l.y()+50,l.previewWidth()-20);
        String shown=skinTab?(vanillaSkin?"Vanilla skin":skinDirty?"New skin":"Your skin"):VortexMenuChrome.capeLabel(pendingCape());
        ClientUi.text(g,shown,l.previewX()+10,l.y()+60,l.previewWidth()-20,ClientUi.TEXT);
        if(skinTab)ClientUi.text(g,"Apply automatically syncs with your Vortex account",l.listX(),l.y()+83,l.listWidth(),ClientUi.MUTED);
        if(!skinTab){
            var publication=Capes.status(draftCape!=null && !draftCape.equals(VortexClient.config.selectedCape));
            int color=switch(publication.state()){case PUBLISHED->0xFF86EFC4;case UNCONFIRMED->0xFFFDA4AF;default->ClientUi.ACCENT;};
            ClientUi.rounded(g,l.listX(),l.y()+84,4,4,2,color);
            ClientUi.text(g,publication.label(),l.listX()+9,l.y()+81,l.listWidth()-9,color);
            if(mx>=l.listX() && mx<l.listX()+l.listWidth() && my>=l.y()+78 && my<l.y()+98)
                g.setTooltipForNextFrame(font,Component.literal(publication.detail()),mx,my);
        }
        positionTiles(l);
        g.enableScissor(l.listX(),l.galleryTop(),l.listX()+l.listWidth(),l.galleryBottom());
        for(var tile:tiles)if(tile.visible)tile.extractRenderState(g,mx,my,delta);
        g.disableScissor();
        int max=maximumScroll(l);
        if(max>0){int vh=l.galleryBottom()-l.galleryTop(),thumb=Math.max(12,vh*vh/(vh+max));
            ClientUi.rounded(g,l.previewX()-8,l.galleryTop(),3,vh,1,ClientUi.TRACK);
            ClientUi.rounded(g,l.previewX()-8,l.galleryTop()+scroll*(vh-thumb)/max,3,thumb,1,ClientUi.ACCENT);}
        String status=skinTab?LocalSkins.message:Capes.busy()?Capes.progressMessage:Capes.message;
        if(!gallery.notice().isEmpty())status=gallery.notice();
        ClientUi.text(g,status,l.listX(),l.y()+l.height()-21,l.width()-l.sidebar()-24,ClientUi.MUTED);
        super.extractRenderState(g,mx,my,delta);
    }
    @Override public void onClose(){++draftGeneration;LocalSkins.discardDraft();minecraft.gui.setScreen(parent);}
    @Override public void removed(){for(var id:thumbnails.values())minecraft.getTextureManager().release(id);thumbnails.clear();}
}
