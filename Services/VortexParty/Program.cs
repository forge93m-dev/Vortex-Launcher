using VortexParty;

var builder = WebApplication.CreateBuilder(args);
// Explicitly opt into a public bind at deployment; never open the user's LAN by default.
if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5278");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4096);
builder.Logging.ClearProviders(); // No invite/session/coordinate data in request logs.
builder.Services.AddSingleton<PartyStore>();
var app = builder.Build();
var gates = new Dictionary<string, (DateTimeOffset Start, int Count)>();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    bool allowed;
    lock (gates)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in gates.Where(p => now - p.Value.Start > TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray()) gates.Remove(key);
        string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var item = gates.GetValueOrDefault(ip, (Start: now, Count: 0));
        allowed = item.Count < 300 && (gates.ContainsKey(ip) || gates.Count < 4096);
        if (allowed) gates[ip] = (item.Start, item.Count + 1);
    }
    if (!allowed) { ctx.Response.StatusCode = 429; return; }
    await next(ctx);
});
string Token(HttpRequest req) => req.Headers.Authorization.ToString() is var value && value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : "";
IResult Run(Func<object?> action)
{
    try { return Results.Json(action()); }
    catch (PartyError error) { return Results.Json(new { error = error.Message }, statusCode: error.Status); }
}
app.MapGet("/health", () => Results.Json(new { service = "Vortex Party", protocol = 2 }));
app.MapPost("/v1/parties", (CreateRequest req, PartyStore store) => Run(() => store.Create(req.Name)));
app.MapPost("/v1/join", (JoinRequest req, PartyStore store) => Run(() => store.Join(req.Invite, req.Name)));
app.MapGet("/v1/session", (HttpRequest req, PartyStore store) => Run(() => store.Read(Token(req))));
app.MapPost("/v1/markers", (MarkerRequest body, HttpRequest req, PartyStore store) => Run(() => store.Mark(Token(req), body)));
app.MapPost("/v1/chat", (ChatRequest body, HttpRequest req, PartyStore store) => Run(() => store.Chat(Token(req), body)));
app.MapPost("/v1/waypoints", (WaypointRequest body, HttpRequest req, PartyStore store) => Run(() => store.Share(Token(req), body)));
app.MapDelete("/v1/waypoints/{id:regex(^[a-f0-9]{{24}}$)}", (string id, HttpRequest req, PartyStore store) => Run(() => store.Unshare(Token(req), id)));
app.MapDelete("/v1/session", (HttpRequest req, PartyStore store) => Run(() => { store.Leave(Token(req)); return new { left = true }; }));
app.Run();

record CreateRequest(string Name);
record JoinRequest(string Invite, string Name);
