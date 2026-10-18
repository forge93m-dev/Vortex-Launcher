package dev.vortex.client.mixin;
import com.mojang.blaze3d.platform.InputConstants;
import dev.vortex.client.ChestSearch;
import dev.vortex.client.VortexClient;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.AbstractContainerMenu;
import net.minecraft.world.inventory.Slot;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.*;

@Mixin(AbstractContainerScreen.class)
public abstract class ChestSearchMixin extends Screen {
    @Shadow protected int leftPos;
    @Shadow protected int topPos;
    @Shadow @Final protected int imageWidth;
    @Shadow @Final protected int imageHeight;
    @Shadow @Final protected AbstractContainerMenu menu;
    @Unique private EditBox vortex$search;
    @Unique private ChestSearch.Layout vortex$layout;
    @Unique private final ChestSearch.State vortex$state=new ChestSearch.State();
    protected ChestSearchMixin(Component title){super(title);}
    @Unique private boolean vortex$enabled(){return vortex$search!=null && VortexClient.config.chestSearchEnabled && ChestSearch.storageSlots(menu)>0;}
    @Inject(method="init()V",at=@At("TAIL"))
    private void vortex$init(CallbackInfo callback){
        if(!VortexClient.config.chestSearchEnabled || ChestSearch.storageSlots(menu)==0)return;
        String previous=vortex$search==null?"":vortex$search.getValue();
        vortex$layout=ChestSearch.layout(leftPos,topPos,imageWidth,imageHeight,height);var l=vortex$layout;
        vortex$search=new EditBox(font,l.x(),l.y(),l.width(),l.height(),Component.literal("Search this container"));
        vortex$search.setMaxLength(128);vortex$search.setHint(Component.literal(l.compact()?"Search…":"Search items…  Ctrl+F"));vortex$search.setTextShadow(false);vortex$search.setBordered(!l.compact());
        vortex$search.setTooltip(net.minecraft.client.gui.components.Tooltip.create(Component.literal("All words must match. -word excludes. Filters: #food #armor #weapon #tool #block #enchanted #damaged, ench:mending, dur:<25%. Example: #tool ench:efficiency -gold dur:>=50")));
        vortex$search.setResponder(vortex$state::query);vortex$search.setValue(previous);addWidget(vortex$search);
    }
    @Inject(method="containerTick()V",at=@At("TAIL"))
    private void vortex$refresh(CallbackInfo callback){if(vortex$enabled())vortex$state.changed();}
    @Inject(method="extractSlot(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/world/inventory/Slot;II)V",at=@At("TAIL"))
    private void vortex$slot(GuiGraphicsExtractor g,Slot slot,int mx,int my,CallbackInfo callback){
        if(!vortex$enabled() || !vortex$state.active() || slot.index<0 || slot.index>=ChestSearch.storageSlots(menu) || !slot.hasItem())return;
        vortex$state.refresh(menu);int x=slot.x,y=slot.y;
        if(!vortex$state.matches(slot.index))g.fill(x,y,x+16,y+16,0xA8000000);
        else {int color=0xFF96DBA2;g.fill(x-1,y-1,x+17,y,color);g.fill(x-1,y+16,x+17,y+17,color);g.fill(x-1,y,x,y+16,color);g.fill(x+16,y,x+17,y+16,color);}
    }
    @Inject(method="extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V",at=@At("TAIL"))
    private void vortex$field(GuiGraphicsExtractor g,int mx,int my,float delta,CallbackInfo callback){
        if(!vortex$enabled())return;vortex$state.refresh(menu);var l=vortex$layout;
        if(l.compact())g.fill(l.x()-2,l.y()-1,l.x()+l.width()+2,l.y()+l.height()+1,0xEE202228);
        vortex$search.extractRenderState(g,mx,my,delta);
        if(!l.compact() && vortex$state.active())g.text(font,vortex$state.found()+"/"+vortex$state.total(),l.x()+l.width()+5,l.y()+5,0xFFE3E7EB,true);
    }
    @Inject(method="keyPressed(Lnet/minecraft/client/input/KeyEvent;)Z",at=@At("HEAD"),cancellable=true)
    private void vortex$key(KeyEvent event,CallbackInfoReturnable<Boolean> callback){
        if(!vortex$enabled())return;
        if(event.hasControlDown() && event.key()==InputConstants.KEY_F){setFocused(vortex$search);vortex$search.setFocused(true);callback.setReturnValue(true);return;}
        if(!vortex$search.isFocused())return;
        if(event.key()==InputConstants.KEY_ESCAPE){vortex$search.setValue("");vortex$search.setFocused(false);setFocused(null);}
        else if(event.key()==InputConstants.KEY_RETURN || event.key()==InputConstants.KEY_NUMPADENTER){vortex$search.setFocused(false);setFocused(null);}
        else vortex$search.keyPressed(event);
        // Typing must never trigger inventory-close, hotbar-swap or drop shortcuts.
        callback.setReturnValue(true);
    }
    @Inject(method="mouseClicked(Lnet/minecraft/client/input/MouseButtonEvent;Z)Z",at=@At("HEAD"),cancellable=true)
    private void vortex$click(MouseButtonEvent event,boolean twice,CallbackInfoReturnable<Boolean> callback){
        if(!vortex$enabled())return;
        if(vortex$search.isMouseOver(event.x(),event.y())){setFocused(vortex$search);vortex$search.setFocused(true);vortex$search.mouseClicked(event,twice);callback.setReturnValue(true);}
        else {vortex$search.setFocused(false);if(getFocused()==vortex$search)setFocused(null);}
    }
    @Inject(method="mouseDragged(Lnet/minecraft/client/input/MouseButtonEvent;DD)Z",at=@At("HEAD"),cancellable=true)
    private void vortex$drag(MouseButtonEvent event,double dx,double dy,CallbackInfoReturnable<Boolean> callback){if(vortex$enabled() && vortex$search.isFocused()){vortex$search.mouseDragged(event,dx,dy);callback.setReturnValue(true);}}
    @Inject(method="mouseReleased(Lnet/minecraft/client/input/MouseButtonEvent;)Z",at=@At("HEAD"),cancellable=true)
    private void vortex$release(MouseButtonEvent event,CallbackInfoReturnable<Boolean> callback){if(vortex$enabled() && vortex$search.isFocused()){vortex$search.mouseReleased(event);callback.setReturnValue(true);}}
}
