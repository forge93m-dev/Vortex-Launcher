using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using VortexParty;

void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
void Error(int code, Action action, string text)
{
    try { action(); throw new Exception("FAIL: no rejection: " + text); }
    catch (PartyError ex) { Check(ex.Status == code, text); }
}
var now = DateTimeOffset.UtcNow;
var store = new PartyStore(() => now);
var owner = store.Create("Owner"); var friend = store.Join(owner.Invite, "Friend");
Check(owner.Token != friend.Token && owner.Token.Length == 64 && owner.Invite.Length == 32, "unpredictable invites and independent member sessions");
Check(store.Read(owner.Token).Members.Length == 2, "friends join the same group");
Error(401, () => store.Read(owner.Invite), "invite cannot authenticate session requests");
Error(409, () => store.Join(owner.Invite, "friend"), "duplicate names cannot impersonate an existing group member");
Error(400, () => store.Create("bad name"), "unsafe display names rejected");
var marker = new MarkerRequest(new string('a', 64), "minecraft:overworld", 10, 64, -20, "HERE");
store.Mark(friend.Token, marker);
Check(store.Read(owner.Token).Markers.Single().Name == "Friend", "only authenticated sender name is attached to a shared marker");
Error(429, () => store.Mark(friend.Token, marker), "marker spam is bounded");
var outsider = store.Create("Outside");
Check(store.Read(outsider.Token).Markers.Length == 0, "party markers do not leak into another party");
now = now.AddSeconds(3);
Error(400, () => store.Mark(friend.Token, marker with { X = int.MaxValue }), "out-of-world marker positions rejected");
Error(400, () => store.Mark(friend.Token, marker with { Kind = "COMMAND" }), "arbitrary marker payload rejected");
store.Mark(friend.Token, marker with { Kind = "DANGER" });
Check(store.Read(owner.Token).Markers.Length == 1, "each member has at most one active marker");
now = now.AddSeconds(61);
Check(store.Read(owner.Token).Markers.Length == 0, "markers expire after 60 seconds");
store.Mark(friend.Token, marker with { Kind = "PING" });
Check(store.Read(owner.Token).Markers.Single().Kind == "PING", "a ping marks one block for the party");
now = now.AddSeconds(31);
Check(store.Read(owner.Token).Markers.Length == 0, "pings expire after 30 seconds");

store.Chat(friend.Token, new ChatRequest("  meet at the farm  "));
var chat = store.Read(owner.Token).Chat;
Check(chat.Length == 1 && chat[0].Name == "Friend" && chat[0].Text == "meet at the farm", "party chat reaches the party with the sender's party name");
Check(store.Read(outsider.Token).Chat.Length == 0, "party chat does not leak into another party");
Error(429, () => store.Chat(friend.Token, new ChatRequest("again")), "party chat spam is bounded");
now = now.AddSeconds(1);
Error(400, () => store.Chat(friend.Token, new ChatRequest("")), "empty party message rejected");
Error(400, () => store.Chat(friend.Token, new ChatRequest(new string('x', 257))), "overlong party message rejected");
Error(400, () => store.Chat(friend.Token, new ChatRequest("line\nbreak")), "control characters cannot fake extra chat lines");
Error(400, () => store.Chat(friend.Token, new ChatRequest("abc‮dcba")), "bidi overrides cannot disguise a message");
for (int i = 0; i < 60; i++) { now = now.AddSeconds(1); store.Chat(owner.Token, new ChatRequest("msg " + i)); store.Read(friend.Token); store.Read(outsider.Token); }
chat = store.Read(owner.Token).Chat;
Check(chat.Length == PartyStore.ChatLimit && chat[^1].Text == "msg 59" && chat.Zip(chat.Skip(1)).All(p => p.First.Seq < p.Second.Seq), "chat keeps only the newest 50 messages, in order");
// Members keep polling (heartbeats) while the chat ages.
for (int i = 0; i < 6; i++) { now = now.AddMinutes(1); store.Read(owner.Token); store.Read(friend.Token); store.Read(outsider.Token); }
Check(store.Read(owner.Token).Chat.Length == 0, "party chat is forgotten after five minutes");

var place = new WaypointRequest(new string('a', 64), "minecraft:overworld", "Iron farm", 100, 64, -50, 0x7DD3FC, [new BlockPos(100, 63, -50), new BlockPos(101, 63, -50)]);
store.Share(friend.Token, place);
var shared = store.Read(owner.Token).Waypoints;
Check(shared.Length == 1 && shared[0].Name == "Friend" && shared[0].Title == "Iron farm" && shared[0].Blocks.Length == 2, "shared waypoint and its blocks reach the party");
Check(store.Read(outsider.Token).Waypoints.Length == 0, "shared waypoints do not leak into another party");
now = now.AddSeconds(2);
Error(400, () => store.Share(friend.Token, place with { Title = "" }), "unnamed shared waypoint rejected");
Error(400, () => store.Share(friend.Token, place with { Color = -1 }), "invalid shared waypoint colour rejected");
Error(400, () => store.Share(friend.Token, place with { Blocks = Enumerable.Range(0, 33).Select(i => new BlockPos(i, 64, 0)).ToArray() }), "shared block selections are bounded");
Error(400, () => store.Share(friend.Token, place with { Blocks = [new BlockPos(1, 1, 1), new BlockPos(1, 1, 1)] }), "duplicate shared blocks rejected");
var third = store.Join(owner.Invite, "Third");
Error(403, () => store.Unshare(third.Token, shared[0].Id), "members cannot remove someone else's waypoint");
Error(404, () => store.Unshare(third.Token, "000000000000000000000000"), "removing an unknown waypoint is reported");
store.Unshare(owner.Token, shared[0].Id);
Check(store.Read(friend.Token).Waypoints.Length == 0, "the party owner can remove any shared waypoint");
now = now.AddSeconds(2);
store.Share(friend.Token, place);
store.Leave(friend.Token);
Check(store.Read(owner.Token).Waypoints.Length == 0, "places shared by someone who left are removed with them");
store.Leave(third.Token);
Check(store.Read(owner.Token).Members.Length == 1, "leaving removes membership and markers");
for (int i = 0; i < 7; i++) store.Join(owner.Invite, "Player" + i);
Error(409, () => store.Join(owner.Invite, "Extra"), "groups are bounded at eight members");
store.Leave(owner.Token);
Error(404, () => store.Join(owner.Invite, "NewFriend"), "owner leaving closes the invite and party");
var expiry = store.Create("Expiry"); now = now.AddMinutes(2);
Error(401, () => store.Read(expiry.Token), "abandoned membership expires without keeping location history");

// Real HTTP integration on an ephemeral loopback port, no user files or public bind.
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
string url = $"http://127.0.0.1:{port}";
string service = Path.GetFullPath(args[0]);
var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add(service); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(url);
using var process = Process.Start(start)!;
var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
try
{
    using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
    bool ready = false;
    for (int i = 0; i < 100 && !process.HasExited; i++)
    {
        try { ready = (await http.GetAsync("/health")).IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { }
        await Task.Delay(50);
    }
    Check(ready, "isolated loopback service starts");
    var created = await (await http.PostAsJsonAsync("/v1/parties", new { name = "HttpOwner" })).Content.ReadFromJsonAsync<Session>();
    var joined = await (await http.PostAsJsonAsync("/v1/join", new { invite = created!.Invite, name = "HttpFriend" })).Content.ReadFromJsonAsync<Session>();
    Check(joined!.PartyId == created!.PartyId, "real HTTP create/join handshake");
    Check((await http.GetAsync("/v1/session")).StatusCode == HttpStatusCode.Unauthorized, "HTTP reads require member credentials");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joined.Token);
    Check((await http.PostAsJsonAsync("/v1/markers", marker)).IsSuccessStatusCode, "real HTTP marker publication");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
    var json = await http.GetStringAsync("/v1/session");
    Check(json.Contains("HttpFriend") && !json.Contains(created.Token) && !json.Contains(joined.Token) && !json.Contains(created.Invite), "snapshot returns markers but never session credentials or invite codes");
    Check((await http.PostAsJsonAsync("/v1/chat", new { text = "hello party" })).IsSuccessStatusCode, "real HTTP party chat");
    var sharedResponse = await http.PostAsJsonAsync("/v1/waypoints", new { serverKey = new string('a', 64), dimension = "minecraft:overworld", title = "Base", x = 1, y = 64, z = 2, color = 0xBDA5FF, blocks = new[] { new { x = 1, y = 63, z = 2 } } });
    var sharedSnapshot = await sharedResponse.Content.ReadFromJsonAsync<Snapshot>();
    Check(sharedResponse.IsSuccessStatusCode && sharedSnapshot!.Chat.Single().Text == "hello party" && sharedSnapshot.Waypoints.Single().Title == "Base", "real HTTP shared waypoint and chat in the snapshot");
    Check((await http.DeleteAsync("/v1/waypoints/" + sharedSnapshot!.Waypoints.Single().Id)).IsSuccessStatusCode, "real HTTP shared waypoint removal");
    Check((await http.DeleteAsync("/v1/waypoints/..%2Fsession")).StatusCode == HttpStatusCode.NotFound, "waypoint ids cannot reach other routes");
    var malformed = await http.PostAsync("/v1/markers", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
    Check(malformed.StatusCode == HttpStatusCode.BadRequest, "malformed HTTP payload is rejected");
    var oversized = await http.PostAsync("/v1/parties", new StringContent(new string('x', 5000), System.Text.Encoding.UTF8, "application/json"));
    Check(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge, "HTTP request body size is bounded");
    if (args.Length >= 3)
    {
        var java = new ProcessStartInfo(args[1]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-classpath", args[2], "PartyTransportSmoke", url }) java.ArgumentList.Add(arg);
        using var child = Process.Start(java)!;
        var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(deadline.Token); }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        Console.Write(await output);
        Check(child.ExitCode == 0, "actual Java client transport works with service: " + (child.ExitCode == 0 ? "OK" : await error));
    }
    Console.WriteLine("PARTY_SMOKE_OK");
}
finally
{
    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync(); await stdout; await stderr;
}
