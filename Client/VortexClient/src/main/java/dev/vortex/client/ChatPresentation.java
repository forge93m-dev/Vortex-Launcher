package dev.vortex.client;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Objects;
import net.minecraft.ChatFormatting;
import net.minecraft.client.multiplayer.chat.GuiMessage;
import net.minecraft.network.chat.Component;

/** Display-only state: original messages, signatures, tags and reporting history stay intact. */
public final class ChatPresentation {
    private static final DateTimeFormatter TIME = DateTimeFormatter.ofPattern("HH:mm");
    private final IdentityHashMap<GuiMessage, String> times = new IdentityHashMap<>();
    private GuiMessage previous;
    private int repeats;

    public void resetGroup() { previous = null; repeats = 0; }
    public void clear() { resetGroup(); times.clear(); }
    public void retain(List<GuiMessage> history) { times.keySet().removeIf(message -> !containsIdentity(history, message)); }
    private static boolean containsIdentity(List<GuiMessage> history, GuiMessage message) {
        for (var entry : history) if (entry == message) return true;
        return false;
    }
    public GuiMessage previous() { return previous; }
    public int prepare(GuiMessage message, boolean collapse) {
        times.computeIfAbsent(message, ignored -> TIME.format(LocalTime.now()));
        boolean same = collapse && previous != null && message != previous &&
            message.addedTime() >= previous.addedTime() && (long)message.addedTime() - previous.addedTime() <= 1200 &&
            message.source() == previous.source() && Objects.equals(message.tag(), previous.tag()) &&
            (message.signature() == null) == (previous.signature() == null) && message.content().equals(previous.content());
        repeats = same ? Math.min(100, repeats + 1) : 1;
        previous = message;
        return repeats;
    }
    public Component decorate(GuiMessage message, boolean timestamps, int count) {
        var result = Component.empty();
        String time = times.get(message);
        if (timestamps && time != null) result.append(Component.literal("[" + time + "] ").withStyle(ChatFormatting.GRAY));
        result.append(message.content());
        if (count > 1) result.append(Component.literal(" ×" + count).withStyle(ChatFormatting.GRAY));
        return result;
    }
}
