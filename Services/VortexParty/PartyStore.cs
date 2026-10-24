using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VortexParty;

public sealed class PartyError(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record MarkerRequest(string ServerKey, string Dimension, int X, int Y, int Z, string Kind);
public sealed record Marker(string MemberId, string Name, string ServerKey, string Dimension, int X, int Y, int Z, string Kind, DateTimeOffset ExpiresUtc);
public sealed record MemberView(string MemberId, string Name, bool Owner);
public sealed record Snapshot(MemberView[] Members, Marker[] Markers);
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
        public DateTimeOffset LastMarker;
        public Marker? Marker;
    }
    private sealed class Party(DateTimeOffset now)
    {
        public string Id { get; } = Secret(12);
        public string Invite { get; } = Secret(16);
        public DateTimeOffset Created { get; } = now;
        public List<Member> Members { get; } = new();
    }
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
        }
        parties.RemoveAll(p => !p.Members.Any(m => m.Owner) || now - p.Created >= TimeSpan.FromHours(6));
    }
    private static Snapshot View(Party party) => new(party.Members.Select(m => new MemberView(m.Id, m.Name, m.Owner)).ToArray(), party.Members.Where(m => m.Marker != null).Select(m => m.Marker!).ToArray());
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
            if (req.ServerKey == null || !Regex.IsMatch(req.ServerKey, "\\A[a-f0-9]{64}\\z") || req.Dimension == null || !Regex.IsMatch(req.Dimension, "\\A[a-z0-9_.-]+:[a-z0-9_./-]{1,80}\\z") ||
                req.X is < -30000000 or > 30000000 || req.Z is < -30000000 or > 30000000 || req.Y is < -2048 or > 2048 || req.Kind is not ("HERE" or "COME" or "DANGER"))
                throw new PartyError(400, "Invalid marker.");
            if (clock() - member.LastMarker < TimeSpan.FromSeconds(2)) throw new PartyError(429, "Wait two seconds between markers.");
            member.LastMarker = clock();
            member.Marker = new(member.Id, member.Name, req.ServerKey, req.Dimension, req.X, req.Y, req.Z, req.Kind, clock().AddSeconds(60));
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
