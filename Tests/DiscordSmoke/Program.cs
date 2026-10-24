using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using VortexLauncher.Core.Network;

static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
static async Task Until(Func<bool> ready)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!ready()) await Task.Delay(10, timeout.Token);
}
Check(LauncherDiscordPresence.ApplicationId == "1547929448165281924", "supplied public application ID");
var vector = new DiscordServerInvite("26.3", "mc.example.org:25565", 2000001800, "0123456789abcdef");
Check(vector.Encode() == "vxi1.MjYuM3xtYy5leGFtcGxlLm9yZzoyNTU2NXx4MnF6OWt8MDEyMzQ1Njc4OWFiY2RlZg", "shared Java/.NET protocol vector");
Check(DiscordServerInvite.TryDecode(vector.Encode(), 2000000000, out var parsed) && parsed == vector, "Java-compatible bounded token decode");
Check(!DiscordServerInvite.TryDecode(vector.Encode(), 2000001800, out _) && !DiscordServerInvite.TryDecode(vector.Encode(), 1900000000, out _), "expiry and future bound");
Check(vector.Matches("26.3", "fabric") && !vector.Matches("26.2", "Fabric") && !vector.Matches("26.3", "Forge"), "exact version and Fabric compatibility filter");
foreach (string bad in new[] { "https://host", "user@host", "host/path", "host:0", "host:65536", "host:", "host --flag", "host\nnext", "[not-ip]" })
{
    try { DiscordServerInvite.NormalizeAddress(bad); throw new Exception("Unsafe address accepted"); } catch (FormatException) { Console.WriteLine("PASS unsafe address rejected"); }
}
Check(DiscordServerInvite.NormalizeAddress("[2001:db8::1]:25565") == "[2001:db8::1]:25565", "IPv6 route without DNS");
foreach (string version in new[] { "26.3", "26.2", "26.1.2", "1.21.11", "1.20" })
    Check(VortexLauncher.Core.Launch.ServerLaunchArguments.Build(version,"mc.example.org:25566").SequenceEqual(new[] { "--quickPlayMultiplayer", "mc.example.org:25566" }), "modern Quick Play arguments: " + version);
Check(VortexLauncher.Core.Launch.ServerLaunchArguments.Build("1.12.2", "[2001:db8::1]:25566").SequenceEqual(new[] { "--server", "[2001:db8::1]", "--port", "25566" }), "legacy join arguments preserve IPv6 brackets and port");
foreach (string page in new[] { "Instances", "Modpacks", "Accounts", "Settings", "Wardrobe", "Instance" })
{
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(LauncherDiscordPresence.Payload(page, "vortex", 123, "fixture")));
    var root = json.RootElement; var rpcArgs = root.GetProperty("args"); var activity = rpcArgs.GetProperty("activity");
    Check(activity.GetProperty("state").GetString() == LauncherDiscordPresence.PageState(page), "mapped section: " + page);
    Check(activity.EnumerateObject().Select(x => x.Name).Order().SequenceEqual(new[] { "assets", "details", "state", "timestamps", "type" }), "privacy allowlist");
    Check(rpcArgs.GetProperty("pid").GetInt32() == Environment.ProcessId && activity.GetProperty("assets").GetProperty("large_image").GetString() == "vortex", "local PID and portal asset key");
}
int connections = 0;
var fake = new Fake();
using (var presence = new LauncherDiscordPresence(_ => { Interlocked.Increment(ref connections); return Task.FromResult<Stream>(fake); }))
{
    await Task.Delay(400); Check(connections == 0, "default disabled does not connect");
    presence.Update(true, "Instances", "vortex", false);
    await Until(() => presence.Status.StartsWith("Connected"));
    Check(fake.Handshake && fake.Activities.Count == 1, "handshake and acknowledged activity");
    fake.Enqueue(3, new { ping = "fixture" }); await Until(() => fake.Pongs > 0);
    Check(fake.Pongs == 1, "idle ping/pong and fragmented frames");
    for (int i = 0; i < 100; i++) presence.Update(true, "Settings", "vortex", false);
    await Task.Delay(400); Check(fake.Activities.Count == 1, "rapid section changes are coalesced");
    // Exercise an update with a pending, fragmented idle read after the rate-limit interval.
    await Task.Delay(TimeSpan.FromSeconds(15));
    await Until(() => fake.Activities.Count == 2 && presence.Status.StartsWith("Connected"));
    Check(fake.Activities.Last() == "Changing launcher settings", "latest section reaches Discord after rate limit");
    presence.Update(true, "Settings", "vortex", true);
    await Until(() => fake.Closed); Check(presence.Status.StartsWith("Paused"), "game handoff closes launcher activity");
    presence.Update(false, "Settings", "vortex", false);
    Check(presence.Status == "Disabled", "disable status");
}
var rejected = new Fake { Reject = true };
using (var presence = new LauncherDiscordPresence(_ => Task.FromResult<Stream>(rejected)))
{
    presence.Update(true, "Settings", "vortex", false);
    await Until(() => presence.Status.Contains("rejected"));
    Check(!presence.Status.StartsWith("Connected"), "Discord rejection never reports connected");
}
var receiver = new Fake(); int received = 0;
using (var presence = new LauncherDiscordPresence(_ => Task.FromResult<Stream>(receiver)))
{
    presence.InvitationReceived += invite => { if (invite.Address == "mc.example.org") Interlocked.Increment(ref received); };
    presence.Update(true, "Instances", "vortex", false, true);
    await Until(() => presence.Status.Contains("invitations ready"));
    Check(receiver.Subscribed, "ACTIVITY_JOIN subscription is acknowledged");
    receiver.Enqueue(1, new { cmd = "DISPATCH", evt = "ACTIVITY_JOIN", data = new { secret = "malformed" } });
    await Task.Delay(100); Check(received == 0, "malformed invite does not open any UI");
    string secret = new DiscordServerInvite("26.3", "mc.example.org", DateTimeOffset.UtcNow.ToUnixTimeSeconds()+1800, "0123456789abcdef").Encode();
    receiver.Enqueue(1, new { cmd = "DISPATCH", evt = "ACTIVITY_JOIN", data = new { secret } });
    await Until(() => received == 1);
    for (int i=0;i<20;i++) receiver.Enqueue(1, new { cmd = "DISPATCH", evt = "ACTIVITY_JOIN", data = new { secret } });
    await Task.Delay(300); Check(received == 1, "valid invitation delivered once; modal flood bounded");
    presence.Update(false, "Instances", "vortex", false, true);
    await Until(() => receiver.Closed); Check(presence.Status == "Disabled", "receiver closes when sharing is disabled");
}
var deniedSubscription = new Fake { RejectSubscribe = true };
using (var presence = new LauncherDiscordPresence(_ => Task.FromResult<Stream>(deniedSubscription)))
{
    presence.Update(true, "Instances", "vortex", false, true);
    await Until(() => presence.Status.Contains("invitations unavailable"));
    Check(presence.Status.StartsWith("Connected"), "subscription rejection preserves plain activity sharing");
}
int attempts = 0;
using (var presence = new LauncherDiscordPresence(_ => { Interlocked.Increment(ref attempts); throw new IOException("absent"); }))
{
    presence.Update(true, "Instances", "vortex", false);
    await Until(() => presence.Status.Contains("unavailable")); await Task.Delay(500);
    Check(attempts == 1, "missing Discord is recoverable with retry backoff");
}
foreach (byte[] bytes in new[] { new byte[3], Header(1, 65537), Header(5, 0), Header(1, -1) })
{
    try { await LauncherDiscordPresence.ReadFrameAsync(new MemoryStream(bytes), CancellationToken.None); throw new Exception("invalid frame accepted"); }
    catch (IOException) { Console.WriteLine("PASS malformed frame rejected"); }
}
Console.WriteLine("DISCORD_LAUNCHER_SMOKE_OK (fake transport; actual Discord was not contacted)");
static byte[] Header(int op, int size) { byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(bytes, op); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), size); return bytes; }

sealed class Fake : Stream
{
    readonly Channel<byte> incoming = Channel.CreateUnbounded<byte>();
    public readonly ConcurrentQueue<string> Activities = new();
    public volatile bool Handshake, Closed, Subscribed;
    public bool Reject, RejectSubscribe;
    public int Pongs;
    public void Enqueue(int op, object payload)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload), header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, op); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length);
        foreach (byte b in header.Concat(data)) incoming.Writer.TryWrite(b);
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        try { byte b = await incoming.Reader.ReadAsync(token); buffer.Span[0] = b; return 1; }
        catch (ChannelClosedException) { return 0; }
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        var bytes = buffer.ToArray(); int op = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != bytes.Length - 8) throw new IOException("fragmented write");
        if (op == 4) { Interlocked.Increment(ref Pongs); return ValueTask.CompletedTask; }
        using var json = JsonDocument.Parse(bytes.AsMemory(8)); var root = json.RootElement;
        if (op == 0)
        {
            Handshake = root.GetProperty("client_id").GetString() == LauncherDiscordPresence.ApplicationId;
            Enqueue(1, new { evt = "READY" });
        }
        else
        {
            if (root.GetProperty("cmd").GetString() == "SUBSCRIBE")
            {
                Subscribed = root.GetProperty("evt").GetString() == "ACTIVITY_JOIN";
                Enqueue(1, new { cmd = "SUBSCRIBE", nonce = root.GetProperty("nonce").GetString(), evt = RejectSubscribe ? "ERROR" : (string?)null });
                return ValueTask.CompletedTask;
            }
            Activities.Enqueue(root.GetProperty("args").GetProperty("activity").GetProperty("state").GetString()!);
            Enqueue(1, new { cmd = "SET_ACTIVITY", nonce = root.GetProperty("nonce").GetString(), evt = Reject ? "ERROR" : (string?)null });
        }
        return ValueTask.CompletedTask;
    }
    protected override void Dispose(bool disposing) { Closed = true; incoming.Writer.TryComplete(); base.Dispose(disposing); }
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long l) => throw new NotSupportedException();
}
