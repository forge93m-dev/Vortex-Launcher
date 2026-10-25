import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

/** Event alerts: chat mentions, party changes (never your own actions), and the low health / hunger gates. */
public final class EventAlertsSmoke {
    private static Object make(Class<?> record, Object... values) throws Exception {
        var components = record.getRecordComponents(); var types = new Class<?>[components.length];
        for (int i = 0; i < types.length; i++) types[i] = components[i].getType();
        return record.getDeclaredConstructor(types).newInstance(values);
    }
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); }
    public static void run(ClassLoader target, Path root) throws Exception {
        Class<?> alerts = Class.forName("dev.vortex.client.EventAlerts", true, target);
        Class<?> snapshot = Class.forName("dev.vortex.client.PartyConnection$Snapshot", true, target);
        Class<?> member = Class.forName("dev.vortex.client.PartyConnection$Member", true, target);
        Class<?> marker = Class.forName("dev.vortex.client.PartyConnection$Marker", true, target);
        Class<?> waypoint = Class.forName("dev.vortex.client.PartyConnection$SharedWaypoint", true, target);
        Class<?> alert = Class.forName("dev.vortex.client.EventAlerts$Alert", true, target);
        Method mentions = alerts.getMethod("mentions", String.class, String.class);
        check((boolean) mentions.invoke(null, "<Alex> hi Steve!", "Steve"), "A whole-word mention is detected");
        check((boolean) mentions.invoke(null, "<Alex> steve, are you there", "Steve"), "Mentions ignore case");
        check(!(boolean) mentions.invoke(null, "<Steve> I am Steve", "Steve"), "Your own messages never alert you");
        check(!(boolean) mentions.invoke(null, "<Alex> hello Stevenson", "Steve"), "A longer name is not a mention");
        check(!(boolean) mentions.invoke(null, "<Alex> hello my_Steve1", "Steve"), "A name inside another word is not a mention");
        check(!(boolean) mentions.invoke(null, "Steve joined the game", "Steve"), "System lines are not mentions");
        check(!(boolean) mentions.invoke(null, "<Alex> hi", "ab") && !(boolean) mentions.invoke(null, null, "Steve") && !(boolean) mentions.invoke(null, "<Alex> hi Steve", null), "Missing or too short names never match");
        check(!(boolean) mentions.invoke(null, "<Alex> " + "x".repeat(3000) + " Steve", "Steve"), "Very long lines are ignored");

        Object me = make(member, "m1", "Steve", true), alex = make(member, "m2", "Alex", false), kim = make(member, "m3", "Kim", false);
        Object noMarkers = List.of();
        Object before = make(snapshot, List.of(me, alex), noMarkers, null, null);
        Object joined = make(snapshot, List.of(me, alex, kim), noMarkers, null, null);
        Method changes = alerts.getMethod("partyChanges", snapshot, snapshot, String.class);
        Method kind = alert.getMethod("kind"), title = alert.getMethod("title");
        var join = (List<?>) changes.invoke(null, before, joined, "Steve");
        check(join.size() == 1 && kind.invoke(join.get(0)).toString().equals("PARTY_JOIN") && title.invoke(join.get(0)).toString().contains("Kim"), "A friend joining is reported");
        var left = (List<?>) changes.invoke(null, joined, before, "Steve");
        check(left.size() == 1 && kind.invoke(left.get(0)).toString().equals("PARTY_LEAVE"), "A friend leaving is reported");
        check(((List<?>) changes.invoke(null, null, joined, "Steve")).isEmpty(), "The first snapshot after joining raises nothing");
        check(((List<?>) changes.invoke(null, joined, joined, "Steve")).isEmpty(), "An unchanged snapshot raises nothing");
        Object alsoMe = make(snapshot, List.of(alex), noMarkers, null, null);
        check(((List<?>) changes.invoke(null, alsoMe, before, "Steve")).isEmpty(), "Your own arrival is not announced");

        Object place = make(waypoint, "w1", "m2", "Alex", "Base", "srv", "overworld", 10, 64, 10, 0xFFAA00, null);
        Object mine = make(waypoint, "w2", "m1", "Steve", "Mine", "srv", "overworld", 1, 2, 3, 0xFFAA00, null);
        Object pinged = make(marker, "m2", "Alex", "srv", "overworld", 5, 70, 5, "Danger", "2099-01-01T00:00:00Z");
        Object mineMarker = make(marker, "m1", "Steve", "srv", "overworld", 6, 70, 6, "Look", "2099-01-01T00:00:00Z");
        Object shared = make(snapshot, List.of(me, alex), List.of(pinged, mineMarker), null, List.of(place, mine));
        var places = (List<?>) changes.invoke(null, before, shared, "Steve");
        check(places.size() == 2 && places.stream().allMatch(a -> { try { return kind.invoke(a).toString().equals("PARTY_PLACE"); } catch (Exception e) { return false; } }), "A friend's waypoint and ping are reported, yours are not");
        check(((List<?>) changes.invoke(null, shared, shared, "Steve")).isEmpty(), "The same waypoint and ping are not reported twice");

        Class<?> vitals = Class.forName("dev.vortex.client.EventAlerts$Vitals", true, target);
        Object gate = vitals.getDeclaredConstructor().newInstance();
        Method health = vitals.getMethod("health", float.class, float.class, int.class, long.class), hunger = vitals.getMethod("hunger", int.class, long.class), reset = vitals.getMethod("reset");
        check(!(boolean) health.invoke(gate, 20f, 20f, 30, 0L), "Full health is quiet");
        check((boolean) health.invoke(gate, 5f, 20f, 30, 1000L), "Dropping below 30% alerts once");
        check(!(boolean) health.invoke(gate, 4f, 20f, 30, 2000L) && !(boolean) health.invoke(gate, 3f, 20f, 30, 3000L), "Staying low does not repeat the alert");
        check(!(boolean) health.invoke(gate, 12f, 20f, 30, 4000L), "Recovering is quiet");
        check(!(boolean) health.invoke(gate, 5f, 20f, 30, 5000L), "A second dip within 10 seconds is held back");
        check(!(boolean) health.invoke(gate, 12f, 20f, 30, 6000L) && (boolean) health.invoke(gate, 5f, 20f, 30, 20000L), "A later dip alerts again");
        check(!(boolean) health.invoke(gate, 0f, 20f, 30, 40000L) && !(boolean) health.invoke(gate, 5f, 0f, 30, 40000L), "Death and a zero maximum never alert");
        check((boolean) health.invoke(gate, 3.9f, 20f, 10, 60000L) == false, "The 10% threshold is respected");
        reset.invoke(gate);
        check(!(boolean) hunger.invoke(gate, 18, 0L) && !(boolean) hunger.invoke(gate, 7, 1000L), "Hunger above 3 shanks is quiet");
        check((boolean) hunger.invoke(gate, 6, 2000L) && !(boolean) hunger.invoke(gate, 5, 3000L), "Low hunger alerts once");
        check(!(boolean) hunger.invoke(gate, 12, 4000L) && (boolean) hunger.invoke(gate, 6, 40000L), "Eating re-arms the hunger alert after its cooldown");
        System.out.println("EVENT_ALERTS_SMOKE_OK: mention detection, party join/leave/waypoint/ping changes ignoring your own actions, first snapshot silence, and low health/hunger gates with cooldowns");
    }
}
