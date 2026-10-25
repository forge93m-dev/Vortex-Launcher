package dev.vortex.client.mixin;

import dev.vortex.client.ChatOverlay;
import dev.vortex.client.PartyChat;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.ChatScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(ChatScreen.class)
public abstract class ChatScreenMixin extends Screen {
    @Shadow protected EditBox input;
    protected ChatScreenMixin(Component title) { super(title); }
    /** "@p ..." lines, or any non-command line on the Party tab, go to the party service only; never to the Minecraft server. */
    @Inject(method = "handleChatInput(Ljava/lang/String;Z)V", at = @At("HEAD"), cancellable = true)
    private void vortex$partyChat(String message, boolean addToHistory, CallbackInfo callback) {
        if (PartyChat.intercept(message)) callback.cancel();
    }
    @Inject(method = "init()V", at = @At("TAIL"))
    private void vortex$tabs(CallbackInfo callback) {
        for (var widget : ChatOverlay.widgets(width, height)) addRenderableWidget(widget);
    }
    @Inject(method = "extractRenderState", at = @At("TAIL"))
    private void vortex$overlay(GuiGraphicsExtractor g, int mx, int my, float delta, CallbackInfo callback) {
        ChatOverlay.render(g, width, height, mx, my);
    }
    @Inject(method = "mouseClicked", at = @At("HEAD"), cancellable = true)
    private void vortex$menu(MouseButtonEvent event, boolean doubleClick, CallbackInfoReturnable<Boolean> callback) {
        if (ChatOverlay.mouseClicked(event.x(), event.y(), event.button(), width, height, input)) callback.setReturnValue(true);
    }
    @Inject(method = "keyPressed", at = @At("HEAD"), cancellable = true)
    private void vortex$keys(KeyEvent event, CallbackInfoReturnable<Boolean> callback) {
        if (ChatOverlay.returnsFocus(getFocused(), event.key())) { vortex$typeHere(); callback.setReturnValue(true); return; }
        if (ChatOverlay.keyPressed(event.key(), input)) callback.setReturnValue(true);
    }
    @Inject(method = "mouseClicked", at = @At("RETURN"))
    private void vortex$keepTyping(MouseButtonEvent event, boolean doubleClick, CallbackInfoReturnable<Boolean> callback) {
        if (ChatOverlay.returnsFocus(getFocused(), -1)) vortex$typeHere();
    }
    @org.spongepowered.asm.mixin.Unique
    private void vortex$typeHere() {
        if (getFocused() != null) getFocused().setFocused(false);
        setFocused(input);
    }
}
