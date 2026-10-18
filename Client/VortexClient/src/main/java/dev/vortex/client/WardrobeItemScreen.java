package dev.vortex.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Explicit, labeled collection actions; opening this panel never applies a cosmetic. */
public final class WardrobeItemScreen extends Screen {
    private final Screen parent;
    private final WardrobeGallery.Item item;
    private final java.util.function.BooleanSupplier canDelete;
    private String name,message="Changes affect only this saved collection item.";
    private boolean favorite,confirmDelete,busy;
    public WardrobeItemScreen(Screen parent,WardrobeGallery.Item item,java.util.function.BooleanSupplier canDelete) {
        super(Minecraft.getInstance(),UiFonts.body(),Component.literal("Saved design"));
        this.parent=parent;this.item=item;this.canDelete=canDelete;name=item.label();favorite=item.favorite();
    }
    @Override protected void init() {
        int w=Math.min(360,width-24),x=(width-w)/2,y=(height-208)/2;
        var input=new EditBox(font,x+12,y+42,w-24,24,Component.literal("Design name"));
        input.setMaxLength(40);input.setTextShadow(false);input.setValue(name);input.setResponder(v->{name=v;confirmDelete=false;});
        input.active=!busy;addRenderableWidget(input);
        addRenderableWidget(new ClientUi.ActionButton(x+12,y+74,w-24,23,Component.literal(favorite?"Favorite: YES":"Favorite: NO"),b->{favorite=!favorite;confirmDelete=false;rebuildWidgets();})).active=!busy;
        var delete=addRenderableWidget(new ClientUi.ActionButton(x+12,y+103,w-24,23,Component.literal(confirmDelete?"Confirm removal":"Remove from collection"),b->{
            if(!canDelete.getAsBoolean()){message="Change the applied design before removing it.";return;}
            if(!confirmDelete){confirmDelete=true;message="Remove this item? A recoverable copy will be kept.";rebuildWidgets();return;}
            edit(true);
        }));delete.active=!busy && canDelete.getAsBoolean();
        int half=(w-30)/2;
        addRenderableWidget(new ClientUi.ActionButton(x+12,y+153,half,24,Component.literal("Cancel"),b->onClose())).active=!busy;
        addRenderableWidget(new ClientUi.ActionButton(x+18+half,y+153,half,24,Component.literal("Save"),b->edit(false),true)).active=!busy;
        if(!canDelete.getAsBoolean())message="Applied design: change it before removing it.";
    }
    private void edit(boolean delete) {
        if(busy || delete && !canDelete.getAsBoolean())return;
        busy=true;rebuildWidgets();
        WardrobeGallery.edit(item.entry(),name,favorite,delete,error->{
            busy=false;
            if(error.isEmpty()){if(minecraft.gui.screen()==this)minecraft.gui.setScreen(parent);}
            else {message=error;confirmDelete=false;if(minecraft.gui.screen()==this)rebuildWidgets();}
        });
    }
    @Override public void onClose(){if(!busy)minecraft.gui.setScreen(parent);}
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return minecraft.level!=null;}
    @Override public void extractBackground(GuiGraphicsExtractor g,int mx,int my,float delta){if(isInGameUi())minecraft.gui.hud.extractDeferredSubtitles();else extractPanorama(g,delta);}
    @Override public void extractRenderState(GuiGraphicsExtractor g,int mx,int my,float delta){
        int w=Math.min(360,width-24),x=(width-w)/2,y=(height-208)/2;
        g.fill(0,0,width,height,0xA007090D);ClientUi.panel(g,x,y,w,208,8,ClientUi.BORDER,ClientUi.PANEL);
        ClientUi.heading(g,"Saved design",x+12,y+14,w-24);
        ClientUi.text(g,message,x+12,y+133,w-24,ClientUi.MUTED);
        ClientUi.text(g,"Right-click a saved tile or press R to manage it.",x+12,y+187,w-24,ClientUi.MUTED);
        super.extractRenderState(g,mx,my,delta);
    }
}
