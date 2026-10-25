package dev.vortex.client;

import java.util.Locale;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.Component;

/**
 * Party chat inside the normal chat box. A line starting with "@p " (or "@party ") goes only to the party
 * service and never to the Minecraft server; party lines are shown locally with a [Party] tag.
 */
public final class PartyChat {
    private PartyChat() { }
    static final int TEXT = 0xF3F4F7, NOTE = 0xA0A4B3;
    private static int tag() { return ClientUi.ACCENT & 0xFFFFFF; }
    private static int nameColor() { return ClientUi.ACCENT_PALE & 0xFFFFFF; }
    /** The party text of a chat line, or null when the line is ordinary chat or a command. */
    public static String partyText(String line) {
        if (line == null) return null;
        String lower = line.toLowerCase(Locale.ROOT);
        for (String prefix : new String[] {"@party", "@p"})
            if (lower.equals(prefix) || lower.startsWith(prefix + " ")) return line.substring(prefix.length()).strip();
        return null;
    }
    /** Called by the chat screen before sending; true means the line was handled here and must not reach the server. */
    public static boolean intercept(String line) {
        String text = partyText(line);
        // On the Party tab every plain line is a party line; commands still go to the server.
        if (text == null && ChatTabs.tab() == ChatTabs.Tab.PARTY && line != null && !line.startsWith("/")) text = line.strip();
        if (text == null) return false;
        if (text.isEmpty()) { local("Type your party message after @p, for example: @p meet at the base"); return true; }
        PartyFeature.chat(text);
        return true;
    }
    static void show(String name, String text) {
        var line = Component.literal("[Party] ").withColor(tag()).append(Component.literal(name + ": ").withColor(nameColor())).append(Component.literal(text).withColor(TEXT));
        ChatTabs.markParty(line, name);
        add(line);
    }
    static void local(String text) {
        var line = Component.literal("[Party] ").withColor(tag()).append(Component.literal(text).withColor(NOTE));
        ChatTabs.markParty(line, "");
        add(line);
    }
    private static void add(Component line) {
        var mc = Minecraft.getInstance();
        if (mc == null || mc.gui == null) return;
        mc.gui.hud.getChat().addClientSystemMessage(line);
    }
}
