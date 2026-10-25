package dev.vortex.client;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Pattern;
import net.minecraft.client.Minecraft;
import net.minecraft.client.resources.sounds.SimpleSoundInstance;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundEvents;

/**
 * Short sounds and notices for things that happen while you are not looking at them: friends joining or leaving the
 * party, shared waypoints and pings, your name in chat, low health and low hunger. Nothing here sends anything anywhere.
 */
public final class EventAlerts {
    public enum Kind {
        PARTY_JOIN("Friend joins the party"), PARTY_LEAVE("Friend leaves the party"), PARTY_PLACE("Shared waypoint or ping"),
        MENTION("Your name in chat"), LOW_HEALTH("Low health"), LOW_HUNGER("Low hunger");
        public final String label;
        Kind(String label) { this.label = label; }
        public boolean on() {
            var c = VortexClient.config;
            return switch (this) {
                case PARTY_JOIN -> c.alertPartyJoin; case PARTY_LEAVE -> c.alertPartyLeave; case PARTY_PLACE -> c.alertPartyPlace;
                case MENTION -> c.alertMention; case LOW_HEALTH -> c.alertLowHealth; case LOW_HUNGER -> c.alertLowHunger;
            };
        }
        public void toggle() {
            var c = VortexClient.config;
            switch (this) {
                case PARTY_JOIN -> c.alertPartyJoin = !c.alertPartyJoin; case PARTY_LEAVE -> c.alertPartyLeave = !c.alertPartyLeave;
                case PARTY_PLACE -> c.alertPartyPlace = !c.alertPartyPlace; case MENTION -> c.alertMention = !c.alertMention;
                case LOW_HEALTH -> c.alertLowHealth = !c.alertLowHealth; case LOW_HUNGER -> c.alertLowHunger = !c.alertLowHunger;
            }
        }
    }
    public record Alert(Kind kind, String title, String body) { }

    private static final Pattern CHAT_LINE = Pattern.compile("\\A<([A-Za-z0-9_]{3,16})> (.*)\\z", Pattern.DOTALL);
    private static final Vitals VITALS = new Vitals();
    private static Object player, level;
    private EventAlerts() { }

    /** True when somebody else's chat line contains your name as a whole word. */
    public static boolean mentions(String line, String self) {
        if (line == null || self == null || self.length() < 3 || line.length() > 2048) return false;
        var match = CHAT_LINE.matcher(line);
        if (!match.matches() || match.group(1).equalsIgnoreCase(self)) return false;
        return Pattern.compile("(?<![A-Za-z0-9_])" + Pattern.quote(self) + "(?![A-Za-z0-9_])", Pattern.CASE_INSENSITIVE).matcher(match.group(2)).find();
    }

    /** What changed between two party snapshots, ignoring your own actions. The first snapshot after joining raises nothing. */
    public static List<Alert> partyChanges(PartyConnection.Snapshot before, PartyConnection.Snapshot after, String self) {
        var result = new ArrayList<Alert>();
        if (before == null || after == null) return result;
        Set<String> old = new LinkedHashSet<>(), now = new LinkedHashSet<>();
        for (var member : before.members()) old.add(member.name());
        for (var member : after.members()) now.add(member.name());
        for (String name : now) if (!old.contains(name) && !name.equalsIgnoreCase(self)) result.add(new Alert(Kind.PARTY_JOIN, name + " joined", "A friend joined your party."));
        for (String name : old) if (!now.contains(name) && !name.equalsIgnoreCase(self)) result.add(new Alert(Kind.PARTY_LEAVE, name + " left", "A friend left your party."));
        Set<String> places = new HashSet<>();
        for (var waypoint : before.waypointsOrEmpty()) places.add(waypoint.id());
        for (var waypoint : after.waypointsOrEmpty())
            if (!places.contains(waypoint.id()) && !waypoint.name().equalsIgnoreCase(self))
                result.add(new Alert(Kind.PARTY_PLACE, waypoint.name() + " shared a waypoint", waypoint.title()));
        Set<String> pings = new HashSet<>();
        for (var marker : before.markers()) pings.add(markerKey(marker));
        for (var marker : after.markers())
            if (!pings.contains(markerKey(marker)) && !marker.name().equalsIgnoreCase(self))
                result.add(new Alert(Kind.PARTY_PLACE, marker.name() + " pinged a location", marker.kind() + " at " + marker.x() + ", " + marker.y() + ", " + marker.z()));
        return result;
    }
    private static String markerKey(PartyConnection.Marker marker) {
        return marker.memberId() + "|" + marker.x() + "|" + marker.y() + "|" + marker.z() + "|" + marker.kind() + "|" + marker.expiresUtc();
    }

    /** Low-health and low-hunger detector: one alert per dip, re-armed after recovering, with a cooldown. */
    public static final class Vitals {
        private boolean lowHealth, lowHunger;
        private long lastHealth = Long.MIN_VALUE, lastHunger = Long.MIN_VALUE;
        public void reset() { lowHealth = lowHunger = false; lastHealth = lastHunger = Long.MIN_VALUE; }
        public boolean health(float health, float maximum, int percent, long nowMs) {
            if (!(maximum > 0) || !(health > 0)) { lowHealth = false; return false; }
            percent = Math.clamp(percent, 10, 50);
            if (health * 100f > maximum * (percent + 10)) lowHealth = false;
            if (health * 100f > maximum * percent || lowHealth) return false;
            if (lastHealth != Long.MIN_VALUE && nowMs - lastHealth < 10_000) return false;
            lowHealth = true; lastHealth = nowMs; return true;
        }
        public boolean hunger(int food, long nowMs) {
            if (food >= 10) lowHunger = false;
            if (food > 6 || lowHunger) return false;
            if (lastHunger != Long.MIN_VALUE && nowMs - lastHunger < 30_000) return false;
            lowHunger = true; lastHunger = nowMs; return true;
        }
    }

    public static void tick(Minecraft mc) {
        if (player != mc.player || level != mc.level) { VITALS.reset(); player = mc.player; level = mc.level; }
        var c = VortexClient.config;
        if (!c.eventAlertsEnabled || mc.player == null || mc.level == null || !mc.player.isAlive() || mc.player.isCreative() || mc.player.isSpectator()) return;
        long now = System.nanoTime() / 1_000_000;
        if (c.alertLowHealth && VITALS.health(mc.player.getHealth(), mc.player.getMaxHealth(), c.lowHealthPercent, now))
            fire(new Alert(Kind.LOW_HEALTH, "Low health", Math.round(mc.player.getHealth() / 2f * 10) / 10f + " hearts left"));
        if (c.alertLowHunger && VITALS.hunger(mc.player.getFoodData().getFoodLevel(), now))
            fire(new Alert(Kind.LOW_HUNGER, "Low hunger", "Time to eat."));
    }

    /** Called for every new chat line. */
    public static void chat(String line) {
        var mc = Minecraft.getInstance();
        if (!VortexClient.config.eventAlertsEnabled || !VortexClient.config.alertMention || mc.getUser() == null) return;
        String self = mc.getUser().getName();
        if (mentions(line, self)) fire(new Alert(Kind.MENTION, "Mentioned in chat", line.length() > 90 ? line.substring(0, 89) + "…" : line));
    }

    /** Called when a new party snapshot arrives. */
    public static void party(PartyConnection.Snapshot before, PartyConnection.Snapshot after) {
        var mc = Minecraft.getInstance();
        if (!VortexClient.config.eventAlertsEnabled || mc.getUser() == null) return;
        for (var alert : partyChanges(before, after, mc.getUser().getName())) fire(alert);
    }

    private static void fire(Alert alert) {
        Minecraft.getInstance().execute(() -> {
            var c = VortexClient.config;
            if (!c.eventAlertsEnabled || !alert.kind().on()) return;
            ClientNotifications.info("event-" + alert.kind().name().toLowerCase(Locale.ROOT), alert.title(), alert.body());
            if (c.eventSounds) sound(alert.kind(), c.eventVolume);
        });
    }

    /** Plays the sound of an event at a volume of 0-100 (also used by the settings screen to preview). */
    public static void sound(Kind kind, int volume) {
        float level = Math.clamp(volume, 0, 100) / 100f;
        if (level <= 0) return;
        SoundEvent event = switch (kind) {
            case PARTY_JOIN -> SoundEvents.EXPERIENCE_ORB_PICKUP; case PARTY_LEAVE -> SoundEvents.UI_TOAST_OUT;
            case PARTY_PLACE -> SoundEvents.UI_TOAST_IN; case MENTION -> SoundEvents.UI_TOAST_IN;
            case LOW_HEALTH -> SoundEvents.UI_TOAST_OUT; case LOW_HUNGER -> SoundEvents.UI_TOAST_OUT;
        };
        float pitch = switch (kind) {
            case PARTY_JOIN -> 1.4f; case PARTY_LEAVE -> .8f; case PARTY_PLACE -> 1.2f; case MENTION -> 1.7f; case LOW_HEALTH -> .55f; case LOW_HUNGER -> .75f;
        };
        Minecraft.getInstance().getSoundManager().play(SimpleSoundInstance.forUI(event, pitch, level));
    }
}
