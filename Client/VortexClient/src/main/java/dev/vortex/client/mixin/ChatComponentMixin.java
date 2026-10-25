package dev.vortex.client.mixin;

import java.util.List;
import dev.vortex.client.ChatAccess;
import dev.vortex.client.ChatPresentation;
import dev.vortex.client.ChatTabs;
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
public abstract class ChatComponentMixin implements ChatAccess {
    @Shadow @Final private List<GuiMessage> allMessages;
    @Shadow @Final private List<GuiMessage.Line> trimmedMessages;
    @Shadow private int chatScrollbarPos;
    @Shadow public abstract void rescaleChat();
    @Unique private final ChatPresentation vortex$chat = new ChatPresentation();
    @Unique private int vortex$count = 1;
    @Unique private int vortex$options = -1;

    @Inject(method = "addMessageToDisplayQueue", at = @At("HEAD"), cancellable = true)
    private void vortex$prepare(GuiMessage message, CallbackInfo ci) {
        // Tabs and search hide lines from the open chat box only; the message itself stays stored.
        if (!ChatTabs.accepts(message.content())) { ci.cancel(); return; }
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
    private void vortex$prune(GuiMessage message, CallbackInfo ci) { vortex$chat.retain(allMessages); ChatTabs.arrived(message.content()); }
    @Override public net.minecraft.network.chat.Component vortex$messageAtRow(int row) {
        int index = row + chatScrollbarPos;
        return row < 0 || index < 0 || index >= trimmedMessages.size() ? null : trimmedMessages.get(index).parent().content();
    }
    /**
     * 1.21.11 lines do not link to their message: count message ends from the newest line instead
     * (the port build swaps this in; with repeat collapsing on it can point one message off).
     */
    @Unique private net.minecraft.network.chat.Component vortex$legacyMessage(int index) {
        int ends = 0;
        for (int i = 0; i <= index; i++) if (trimmedMessages.get(i).endOfEntry()) ends++;
        int seen = 0;
        for (var message : allMessages) if (ChatTabs.accepts(message.content()) && ++seen == Math.max(1, ends)) return message.content();
        return null;
    }
    @Override public int vortex$matchingCount() {
        int count = 0;
        for (var message : allMessages) if (ChatTabs.accepts(message.content())) count++;
        return count;
    }
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
