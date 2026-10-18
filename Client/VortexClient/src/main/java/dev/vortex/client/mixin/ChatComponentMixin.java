package dev.vortex.client.mixin;

import java.util.List;
import dev.vortex.client.ChatPresentation;
import dev.vortex.client.VortexClient;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.components.ChatComponent;
import net.minecraft.client.multiplayer.chat.GuiMessage;
import net.minecraft.util.FormattedCharSequence;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ChatComponent.class)
public abstract class ChatComponentMixin {
    @Shadow @Final private List<GuiMessage> allMessages;
    @Shadow @Final private List<GuiMessage.Line> trimmedMessages;
    @Shadow private int chatScrollbarPos;
    @Shadow public abstract void rescaleChat();
    @Unique private final ChatPresentation vortex$chat = new ChatPresentation();
    @Unique private int vortex$count = 1;
    @Unique private int vortex$options = -1;

    @Inject(method = "addMessageToDisplayQueue", at = @At("HEAD"))
    private void vortex$prepare(GuiMessage message, CallbackInfo ci) {
        var config = VortexClient.config;
        GuiMessage previous = vortex$chat.previous();
        vortex$count = vortex$chat.prepare(message, config.chatEnabled && config.chatCollapseRepeats);
        if (vortex$count > 1) {
            int removed = 0;
            while (!trimmedMessages.isEmpty() && trimmedMessages.getFirst().parent() == previous) {
                trimmedMessages.removeFirst(); removed++;
            }
            // Vanilla adds the replacement lines and adjusts a scrolled viewport afterwards.
            if (chatScrollbarPos > 0) chatScrollbarPos = Math.max(0, chatScrollbarPos - removed);
        }
    }
    @Redirect(method = "addMessageToDisplayQueue", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/multiplayer/chat/GuiMessage;splitLines(Lnet/minecraft/client/gui/Font;I)Ljava/util/List;"))
    private List<FormattedCharSequence> vortex$decorate(GuiMessage message, Font font, int width) {
        var config = VortexClient.config;
        if (!config.chatEnabled) return message.splitLines(font, width);
        var display = new GuiMessage(message.addedTime(), vortex$chat.decorate(message, config.chatTimestamps, vortex$count),
            message.signature(), message.source(), message.tag());
        return display.splitLines(font, width);
    }
    @Inject(method = "refreshTrimmedMessages", at = @At("HEAD"))
    private void vortex$refresh(CallbackInfo ci) { vortex$chat.resetGroup(); }
    @Inject(method = "addMessageToQueue", at = @At("TAIL"))
    private void vortex$prune(GuiMessage message, CallbackInfo ci) { vortex$chat.retain(allMessages); }
    @Inject(method = "clearMessages", at = @At("TAIL"))
    private void vortex$clear(boolean recent, CallbackInfo ci) { vortex$chat.clear(); }
    @Inject(method = "restoreState", at = @At("HEAD"))
    private void vortex$restore(CallbackInfo ci) { vortex$chat.clear(); }
    @Inject(method = "tick", at = @At("TAIL"))
    private void vortex$settings(CallbackInfo ci) {
        var c = VortexClient.config;
        int options = (c.chatEnabled ? 1 : 0) | (c.chatTimestamps ? 2 : 0) | (c.chatCollapseRepeats ? 4 : 0);
        if (options != vortex$options) { vortex$options = options; rescaleChat(); }
    }
}
