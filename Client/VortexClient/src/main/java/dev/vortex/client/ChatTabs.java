package dev.vortex.client;

import java.util.ArrayDeque;
import java.util.IdentityHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.contents.TranslatableContents;

/**
 * Chat tabs (All / Private / Party) and search. They only filter what the chat box shows while it is open:
 * nothing is deleted, and closing the chat returns to All with no search.
 */
public final class ChatTabs {
    private ChatTabs() { }
    public enum Tab { ALL, PRIVATE, PARTY }
    private static Tab tab = Tab.ALL;
    private static String query = "";
    /** The tab to reopen with: closing the chat shows everything, opening it again returns to this tab. */
    private static Tab remembered = Tab.ALL;
    private static final boolean[] UNREAD = new boolean[Tab.values().length];
    private static final Pattern PLAYER_LINE = Pattern.compile("\\A<([A-Za-z0-9_]{3,16})> ");
    /** Lines this client added for the party, with the sender's name. By identity, so a server cannot fake a [Party] line. */
    private static final Map<Component, String> PARTY = new IdentityHashMap<>();
    private static final ArrayDeque<Component> PARTY_ORDER = new ArrayDeque<>();

    public static Tab tab() { return tab; }
    public static String query() { return query; }
    public static boolean unread(Tab value) { return UNREAD[value.ordinal()]; }
    static void markParty(Component line, String name) {
        PARTY.put(line, name); PARTY_ORDER.add(line);
        // Vanilla keeps 100 messages; remembering a few more than that is enough.
        while (PARTY_ORDER.size() > 256) PARTY.remove(PARTY_ORDER.poll());
    }
    public static boolean isParty(Component line) { return PARTY.containsKey(line); }
    public static boolean isPrivate(Component line) {
        return line.getContents() instanceof TranslatableContents translated &&
            (translated.getKey().equals("commands.message.display.incoming") || translated.getKey().equals("commands.message.display.outgoing"));
    }
    public static Tab category(Component line) { return isParty(line) ? Tab.PARTY : isPrivate(line) ? Tab.PRIVATE : Tab.ALL; }
    /** Whether a line is shown for a tab and a search; search ignores case and formatting. */
    public static boolean matches(Component line, Tab tab, String query) {
        if (tab != Tab.ALL && category(line) != tab) return false;
        return query.isEmpty() || line.getString().toLowerCase(Locale.ROOT).contains(query.toLowerCase(Locale.ROOT));
    }
    public static boolean accepts(Component line) { return matches(line, tab, query); }
    public static boolean filtering() { return tab != Tab.ALL || !query.isEmpty(); }
    /** A new message arrived (not a redraw): light the dot of its tab unless that tab is open. */
    public static void arrived(Component line) {
        EventAlerts.chat(line.getString());
        Tab category = category(line);
        if (category != Tab.ALL && category != tab) UNREAD[category.ordinal()] = true;
    }
    /** The player's name in a chat line, or "" when it cannot be told safely. */
    public static String name(Component line) {
        String party = PARTY.get(line);
        if (party != null) return party;
        if (line.getContents() instanceof TranslatableContents translated && translated.getArgs().length > 0) {
            Object first = translated.getArgs()[0];
            String candidate = first instanceof Component component ? component.getString() : String.valueOf(first);
            if (candidate.matches("[A-Za-z0-9_]{3,16}")) return candidate;
        }
        var matcher = PLAYER_LINE.matcher(line.getString());
        return matcher.find() ? matcher.group(1) : "";
    }
    public static void select(Tab value) {
        tab = value; remembered = value; UNREAD[value.ordinal()] = false; refresh();
    }
    public static void search(String value) {
        String next = value == null ? "" : value.strip();
        if (next.equals(query)) return;
        query = next; refresh();
    }
    public static void reset() {
        if (!filtering()) return;
        remembered = tab; tab = Tab.ALL; query = ""; refresh();
    }
    /** Called when the chat box opens. */
    public static void reopen() {
        if (tab == Tab.ALL && remembered != Tab.ALL) select(remembered);
    }
    /** Closing the chat box shows everything again, so the in-game chat never hides messages. */
    public static void tick(Minecraft mc) {
        if (filtering() && !(mc.gui.screen() instanceof net.minecraft.client.gui.screens.ChatScreen)) reset();
    }
    private static void refresh() {
        var mc = Minecraft.getInstance();
        if (mc != null && mc.gui != null) mc.gui.hud.getChat().rescaleChat();
    }
}
