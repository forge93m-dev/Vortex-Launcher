using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VortexParty;

public sealed class PartyError(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record MarkerRequest(string ServerKey, string Dimension, int X, int Y, int Z, string Kind);
public sealed record Marker(string MemberId, string Name, string ServerKey, string Dimension, int X, int Y, int Z, string Kind, DateTimeOffset ExpiresUtc);
public sealed record MemberView(string MemberId, string Name, bool Owner);
public sealed record ChatRequest(string Text);
public sealed record ChatMessage(long Seq, string MemberId, string Name, string Text, DateTimeOffset SentUtc);
public sealed record BlockPos(int X, int Y, int Z);
public sealed record WaypointRequest(string ServerKey, string Dimension, string Title, int X, int Y, int Z, int Color, BlockPos[]? Blocks);
public sealed record SharedWaypoint(string Id, string MemberId, string Name, string Title, string ServerKey, string Dimension, int X, int Y, int Z, int Color, BlockPos[] Blocks);
public sealed record Snapshot(MemberView[] Members, Marker[] Markers, ChatMessage[] Chat, SharedWaypoint[] Waypoints);
public sealed record Session(string PartyId, string Token, string Invite, Snapshot Snapshot);

public sealed class PartyStore
{
    private sealed class Member(string name, bool owner, DateTimeOffset now)
    {
        public string Id { get; } = Secret(12);
        public string Token { get; } = Secret(32);
        public string Name { get; } = name;
        public bool Owner { get; } = owner;
        public DateTimeOffset Seen = now;
        public DateTimeOffset LastMarker, LastChat, LastWaypoint;
        public Marker? Marker;
    }
    private sealed class Party(DateTimeOffset now)
    {
        public string Id { get; } = Secret(12);
        public string Invite { get; } = Secret(16);
        public DateTimeOffset Created { get; } = now;
        public List<Member> Members { get; } = new();
        public List<ChatMessage> Chat { get; } = new();
        public List<SharedWaypoint> Waypoints { get; } = new();
        public long NextSeq = 1;
    }
    // Chat is kept in memory only: the last 50 messages, each for at most five minutes.
    public const int ChatLimit = 50, WaypointLimit = 32, WaypointBlockLimit = 32;
    public static readonly TimeSpan ChatLifetime = TimeSpan.FromMinutes(5);
    private readonly object gate = new();
    private readonly List<Party> parties = new();
    private readonly Func<DateTimeOffset> clock;
    public PartyStore() : this(() => DateTimeOffset.UtcNow) { }
    public PartyStore(Func<DateTimeOffset> clock) { this.clock = clock; }
    private static string Secret(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    private static string Name(string? name)
    {
        name = name?.Trim();
        if (name == null || !Regex.IsMatch(name, "\\A[A-Za-z0-9_]{3,16}\\z")) throw new PartyError(400, "Use a player name with 3–16 letters, digits or underscores.");
        return name;
    }
    private void Cleanup()
    {
        var now = clock();
        foreach (var party in parties)
        {
            party.Members.RemoveAll(m => now - m.Seen >= TimeSpan.FromMinutes(2));
            foreach (var member in party.Members)
                if (member.Marker?.ExpiresUtc <= now) member.Marker = null;
            party.Chat.RemoveAll(c => now - c.SentUtc >= ChatLifetime);
            // Places shared by someone who left go with them.
            party.Waypoints.RemoveAll(w => party.Members.All(m => m.Id != w.MemberId));
        }
        parties.RemoveAll(p => !p.Members.Any(m => m.Owner) || now - p.Created >= TimeSpan.FromHours(6));
    }
    private static Snapshot View(Party party) => new(party.Members.Select(m => new MemberView(m.Id, m.Name, m.Owner)).ToArray(),
        party.Members.Where(m => m.Marker != null).Select(m => m.Marker!).ToArray(), party.Chat.ToArray(), party.Waypoints.ToArray());
    private static bool Position(int x, int y, int z) => x is >= -30000000 and <= 30000000 && z is >= -30000000 and <= 30000000 && y is >= -2048 and <= 2048;
    private static bool Place(string? serverKey, string? dimension) =>
        serverKey != null && Regex.IsMatch(serverKey, "\\A[a-f0-9]{64}\\z") && dimension != null && Regex.IsMatch(dimension, "\\A[a-z0-9_.-]+:[a-z0-9_./-]{1,80}\\z");
    // Plain one-line text: trimmed, 1..max characters, no control or bidi-override characters.
    private static string Text(string? text, int max, string what)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > max || text.Any(c => char.IsControl(c) || c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069'))
            throw new PartyError(400, $"Invalid {what}.");
        return text;
    }
    public Session Create(string name)
    {
        name = Name(name);
        lock (gate)
        {
            Cleanup();
            if (parties.Count >= 256) throw new PartyError(503, "Party service is full. Try later.");
            var party = new Party(clock()); var member = new Member(name, true, clock());
            party.Members.Add(member); parties.Add(party);
            return new(party.Id, member.Token, party.Invite, View(party));
        }
    }
    public Session Join(string invite, string name)
    {
        name = Name(name);
        lock (gate)
        {
            Cleanup();
            var party = parties.FirstOrDefault(p => p.Invite == invite) ?? throw new PartyError(404, "Invite is invalid or expired.");
            if (party.Members.Count >= 8) throw new PartyError(409, "This party is full (8 players).");
            if (party.Members.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new PartyError(409, "That name is already in the party.");
            var member = new Member(name, false, clock()); party.Members.Add(member);
            return new(party.Id, member.Token, party.Invite, View(party));
        }
    }
    private (Party Party, Member Member) Find(string token)
    {
        Cleanup();
        if (token.Length != 64) throw new PartyError(401, "Party session expired. Join again.");
        foreach (var party in parties)
        {
            var member = party.Members.FirstOrDefault(m => m.Token == token);
            if (member != null) { member.Seen = clock(); return (party, member); }
        }
        throw new PartyError(401, "Party session expired. Join again.");
    }
    public Snapshot Read(string token) { lock (gate) { return View(Find(token).Party); } }
    public Snapshot Mark(string token, MarkerRequest req)
    {
        lock (gate)
        {
            var (party, member) = Find(token);
            if (!Place(req.ServerKey, req.Dimension) || !Position(req.X, req.Y, req.Z) || req.Kind is not ("HERE" or "COME" or "DANGER" or "PING"))
                throw new PartyError(400, "Invalid marker.");
            if (clock() - member.LastMarker < TimeSpan.FromSeconds(2)) throw new PartyError(429, "Wait two seconds between markers.");
            member.LastMarker = clock();
            // A ping points at a block for a moment; the other markers last a minute.
            member.Marker = new(member.Id, member.Name, req.ServerKey, req.Dimension, req.X, req.Y, req.Z, req.Kind, clock().AddSeconds(req.Kind == "PING" ? 30 : 60));
            return View(party);
        }
    }
    public Snapshot Chat(string token, ChatRequest req)
    {
        lock (gate)
        {
            var (party, member) = Find(token);
            string text = Text(req.Text, 256, "message");
            if (clock() - member.LastChat < TimeSpan.FromMilliseconds(400)) throw new PartyError(429, "You are sending messages too fast.");
            member.LastChat = clock();
            party.Chat.Add(new(party.NextSeq++, member.Id, member.Name, text, clock()));
            if (party.Chat.Count > ChatLimit) party.Chat.RemoveRange(0, party.Chat.Count - ChatLimit);
            return View(party);
        }
    }
    public Snapshot Share(string token, WaypointRequest req)
    {
        lock (gate)
        {
            var (party, member) = Find(token);
            var blocks = req.Blocks ?? [];
            if (!Place(req.ServerKey, req.Dimension) || !Position(req.X, req.Y, req.Z) || req.Color is < 0 or > 0xFFFFFF ||
                blocks.Length > WaypointBlockLimit || blocks.Any(b => b == null || !Position(b.X, b.Y, b.Z)) || blocks.Distinct().Count() != blocks.Length)
                throw new PartyError(400, "Invalid waypoint.");
            string title = Text(req.Title, 32, "waypoint name");
            if (clock() - member.LastWaypoint < TimeSpan.FromSeconds(1)) throw new PartyError(429, "Wait a second between shared waypoints.");
            if (party.Waypoints.Count >= WaypointLimit) throw new PartyError(409, "This party already shares 32 waypoints. Remove one first.");
            member.LastWaypoint = clock();
            party.Waypoints.Add(new(Secret(12), member.Id, member.Name, title, req.ServerKey, req.Dimension, req.X, req.Y, req.Z, req.Color, blocks));
            return View(party);
        }
    }
    // The author or the party owner may remove a shared waypoint.
    public Snapshot Unshare(string token, string id)
    {
        lock (gate)
        {
            var (party, member) = Find(token);
            var waypoint = party.Waypoints.FirstOrDefault(w => w.Id == id) ?? throw new PartyError(404, "This waypoint is no longer shared.");
            if (waypoint.MemberId != member.Id && !member.Owner) throw new PartyError(403, "Only its author or the party owner can remove it.");
            party.Waypoints.Remove(waypoint);
            return View(party);
        }
    }
    public void Leave(string token)
    {
        lock (gate)
        {
            var (party, member) = Find(token);
            if (member.Owner) parties.Remove(party); else party.Members.Remove(member);
        }
    }
}
