using System.Net;
using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;

// Microsoft sign-in against a fake Microsoft: the device-code flow, Xbox and Minecraft steps, every refusal the player can meet,
// the encrypted token store and the in-memory session. Nothing here talks to the real services.
internal static class MicrosoftAuthChecks
{
    private const string ClientId = "11111111-2222-3333-4444-555555555555";
    private const string Uuid32 = "069a79f444e94726a5befca90e38aaf5";
    private const string McToken = "mc.access-token_0123456789ABCDEF";

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Urls = new(), Bodies = new();
        public string? ProfileAuthorization;
        public Queue<string> DeviceErrors = new();          // answers of the token endpoint during device-code polling
        public string? XstsError, McLoginStatus, ProfileStatus; public bool NoGame; public string ProfileName = "Steve_2"; public string ProfileId = Uuid32;
        public bool RefreshFails; public string? RefreshReturnsNewToken = "refresh-2";
        public string DeviceVerification = "https://www.microsoft.com/link";

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString(), body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (this) { Urls.Add(url); Bodies.Add(body); }
            switch (url)
            {
                case MicrosoftAuthService.DeviceCodeUrl:
                    return Json(new { device_code = "device-1", user_code = "ABCD1234", verification_uri = DeviceVerification, expires_in = 900, interval = 1 });
                case MicrosoftAuthService.TokenUrl when body.Contains("device_code"):
                    return DeviceErrors.Count > 0 && DeviceErrors.Dequeue() is { Length: > 0 } error && error != "ok"
                        ? Json(new { error }, HttpStatusCode.BadRequest)
                        : Json(new { access_token = "ms-access", refresh_token = "refresh-1", expires_in = 3600 });
                case MicrosoftAuthService.TokenUrl when body.Contains("refresh_token"):
                    return RefreshFails ? Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest)
                        : Json(RefreshReturnsNewToken == null ? new { access_token = "ms-access-2", expires_in = 3600 } : (object)new { access_token = "ms-access-2", refresh_token = RefreshReturnsNewToken, expires_in = 3600 });
                case MicrosoftAuthService.XboxUserUrl:
                    return Json(new { Token = "xbl-token", DisplayClaims = new { xui = new[] { new { uhs = "hash123" } } } });
                case MicrosoftAuthService.XstsUrl:
                    return XstsError != null ? Json(new { XErr = long.Parse(XstsError) }, HttpStatusCode.Unauthorized)
                        : Json(new { Token = "xsts-token", DisplayClaims = new { xui = new[] { new { uhs = "hash123" } } } });
                case MicrosoftAuthService.MinecraftLoginUrl:
                    return McLoginStatus == "403" ? Json(new { error = "forbidden" }, HttpStatusCode.Forbidden) : Json(new { access_token = McToken, expires_in = 86400 });
                case MicrosoftAuthService.EntitlementsUrl:
                    return Json(new { items = NoGame ? Array.Empty<object>() : new[] { new { name = "game_minecraft" }, new { name = "product_minecraft" } } });
                case MicrosoftAuthService.ProfileUrl:
                    ProfileAuthorization = request.Headers.Authorization?.ToString();
                    return ProfileStatus == "404" ? Json(new { error = "NOT_FOUND" }, HttpStatusCode.NotFound) : Json(new { id = ProfileId, name = ProfileName });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private sealed class XorProtector : ISecretProtector
    {
        public bool Broken;
        public byte[]? Protect(byte[] data) => Broken ? null : data.Select(b => (byte)(b ^ 0x5A)).Reverse().ToArray();
        public byte[]? Unprotect(byte[] data) => Broken ? null : data.Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    private static async Task<string?> CodeOf(Func<Task> action)
    {
        try { await action(); return null; } catch (MicrosoftAuthException ex) { return ex.Code; }
    }

    private static MicrosoftAuthService Service(Fake fake, Func<DateTimeOffset>? now = null) => new(ClientId, new HttpClient(fake), now);

    internal static async Task RunAsync(string testRoot)
    {
        string root = Directory.CreateDirectory(Path.Combine(testRoot, "microsoft")).FullName;

        Check(MicrosoftAuthService.IsValidClientId(ClientId) && !MicrosoftAuthService.IsValidClientId("") && !MicrosoftAuthService.IsValidClientId("not a guid") &&
              !MicrosoftAuthService.IsValidClientId("00000000-0000-0000-0000-000000000000"), "an Azure client ID must be a real GUID");
        Check(MicrosoftAuthService.IsTrustedVerificationUri("https://www.microsoft.com/link") && MicrosoftAuthService.IsTrustedVerificationUri("https://login.live.com/oauth20_remoteconnect.srf") &&
              !MicrosoftAuthService.IsTrustedVerificationUri("http://www.microsoft.com/link") && !MicrosoftAuthService.IsTrustedVerificationUri("https://microsoft.com.evil.example/link") &&
              !MicrosoftAuthService.IsTrustedVerificationUri("https://user@www.microsoft.com/link") && !MicrosoftAuthService.IsTrustedVerificationUri("https://www.microsoft.com:8443/link") &&
              !MicrosoftAuthService.IsTrustedVerificationUri("javascript:alert(1)") && !MicrosoftAuthService.IsTrustedVerificationUri(null), "only Microsoft's own https pages are ever offered for opening");

        // The whole happy path, with the polling rules (pending, slow down, then approved).
        var fake = new Fake { DeviceErrors = new(new[] { "authorization_pending", "authorization_pending", "slow_down", "ok" }) };
        var service = Service(fake);
        var code = await service.StartAsync(CancellationToken.None);
        Check(code.UserCode == "ABCD1234" && code.Value == "device-1" && code.VerificationUri == "https://www.microsoft.com/link" && code.IntervalSeconds == 1, "the device code, the short user code and Microsoft's page are read");
        Check(fake.Bodies[0].Contains("client_id=" + ClientId) && fake.Bodies[0].Contains("XboxLive.signin") && fake.Bodies[0].Contains("offline_access"), "the sign-in asks for the Xbox permission and a refresh token");
        var delays = new List<int>();
        var tokens = await service.WaitAsync(code, CancellationToken.None, (span, _) => { delays.Add((int)span.TotalSeconds); return Task.CompletedTask; });
        Check(tokens.AccessToken == "ms-access" && tokens.RefreshToken == "refresh-1", "polling ends with the Microsoft tokens once the code is approved");
        Check(delays.SequenceEqual(new[] { 1, 1, 1, 6 }), "polling waits the interval Microsoft gave and 5 seconds longer after \"slow down\"");
        var session = await service.CompleteAsync(tokens, CancellationToken.None);
        Check(session.Uuid == "069a79f4-44e9-4726-a5be-fca90e38aaf5" && session.Name == "Steve_2" && session.AccessToken == McToken && session.RefreshToken == "refresh-1", "the Minecraft session carries the dashed UUID, the name, the token and the refresh token");
        Check(fake.Bodies.Any(b => b.Contains("\"RpsTicket\":\"d=ms-access\"")) && fake.Bodies.Any(b => b.Contains("rp://api.minecraftservices.com/")) && fake.Bodies.Any(b => b.Contains("XBL3.0 x=hash123;xsts-token")),
            "the Xbox, XSTS and Minecraft steps send the expected tickets");
        Check(fake.ProfileAuthorization == "Bearer " + McToken, "the profile is read with the Minecraft token");
        string[] official = { "login.microsoftonline.com", "user.auth.xboxlive.com", "xsts.auth.xboxlive.com", "api.minecraftservices.com" };
        Check(fake.Urls.All(u => u.StartsWith("https://", StringComparison.Ordinal) && official.Contains(new Uri(u).Host)), "only the official Microsoft, Xbox and Minecraft hosts are contacted");

        // Every refusal the player can meet is a clear message with a stable code.
        Check(await CodeOf(() => Service(new Fake { DeviceErrors = new(new[] { "authorization_declined" }) }).WaitAsync(code, CancellationToken.None, (_, _) => Task.CompletedTask)) == "declined", "declining the code in the browser is reported");
        Check(await CodeOf(() => Service(new Fake { DeviceErrors = new(new[] { "expired_token" }) }).WaitAsync(code, CancellationToken.None, (_, _) => Task.CompletedTask)) == "expired", "an expired code is reported");
        var clock = DateTimeOffset.UtcNow;
        Check(await CodeOf(() => Service(new Fake(), () => clock.AddMinutes(30)).WaitAsync(code with { ExpiresAt = clock.AddMinutes(1) }, CancellationToken.None, (_, _) => Task.CompletedTask)) == "expired", "polling stops by itself when the code's time is over");
        Check(await CodeOf(() => new MicrosoftAuthService("", new HttpClient(new Fake())).StartAsync(CancellationToken.None)) == "client-id", "without a client ID nothing is requested");
        foreach (var (xerr, expected) in new[] { ("2148916233", "no-xbox"), ("2148916238", "child"), ("2148916235", "region"), ("2148916236", "adult"), ("2148916299", "xsts") })
            Check(await CodeOf(() => Service(new Fake { XstsError = xerr }).CompleteAsync(tokens, CancellationToken.None)) == expected, "Xbox error " + xerr + " becomes a plain explanation (" + expected + ")");
        var refused = await Assert(() => Service(new Fake { McLoginStatus = "403" }).CompleteAsync(tokens, CancellationToken.None));
        Check(refused is { Code: "forbidden" } && refused.Message.Contains("approved"), "a Minecraft refusal of an unapproved app explains that Mojang must approve it");
        Check(await CodeOf(() => Service(new Fake { NoGame = true }).CompleteAsync(tokens, CancellationToken.None)) == "no-game", "an account without Minecraft: Java Edition is refused");
        Check(await CodeOf(() => Service(new Fake { ProfileStatus = "404" }).CompleteAsync(tokens, CancellationToken.None)) == "not-found", "an owner without a Java profile is told to choose a name");
        Check(await CodeOf(() => Service(new Fake { ProfileName = "bad name!" }).CompleteAsync(tokens, CancellationToken.None)) == "profile" &&
              await CodeOf(() => Service(new Fake { ProfileId = "zz" }).CompleteAsync(tokens, CancellationToken.None)) == "profile", "an implausible profile (bad name or id) is never trusted");

        // Renewing from the stored refresh token.
        var renew = new Fake();
        var renewed = await Service(renew).RefreshSessionAsync("refresh-1", CancellationToken.None);
        Check(renewed.RefreshToken == "refresh-2" && renewed.AccessToken == McToken, "renewing returns a new session and the newest refresh token");
        var keepsOld = await Service(new Fake { RefreshReturnsNewToken = null }).RefreshSessionAsync("refresh-1", CancellationToken.None);
        Check(keepsOld.RefreshToken == "refresh-1", "when Microsoft sends no new refresh token the old one is kept");
        Check(await CodeOf(() => Service(new Fake { RefreshFails = true }).RefreshAsync("refresh-1", CancellationToken.None)) == "signed-out" &&
              await CodeOf(() => Service(new Fake()).RefreshAsync("", CancellationToken.None)) == "signed-out", "a refused or missing refresh token means signing in again");

        // Stored tokens: encrypted at rest, never plain.
        string config = Directory.CreateDirectory(Path.Combine(root, "config")).FullName;
        var protector = new XorProtector();
        var store = new MicrosoftTokenStore(config, protector);
        Check(store.CanPersist && store.Save(session.Uuid, "refresh-secret-value") && store.Get(session.Uuid) == "refresh-secret-value", "a refresh token round-trips through the store");
        string disk = File.ReadAllText(Path.Combine(config, "microsoft-tokens.json"));
        Check(!disk.Contains("refresh-secret-value") && disk.Contains(session.Uuid), "the token file holds only encrypted text");
        store.Remove(session.Uuid);
        Check(store.Get(session.Uuid) == null, "a removed token is gone");
        protector.Broken = true;
        Check(!store.Save(session.Uuid, "x") && store.Get(session.Uuid) == null, "when protection fails nothing is written in the clear");
        protector.Broken = false;
        Check(!store.Save("not-a-uuid", "x") && !store.Save(session.Uuid, ""), "only real UUIDs and non-empty tokens are stored");
        if (OperatingSystem.IsWindows())
        {
            var dpapi = new WindowsSecretProtector(); byte[] plain = Encoding.UTF8.GetBytes("windows secret");
            byte[]? sealedBytes = dpapi.Protect(plain);
            Check(sealedBytes != null && !sealedBytes.SequenceEqual(plain) && dpapi.Unprotect(sealedBytes)!.SequenceEqual(plain), "on Windows the secret is sealed for the current user and opens again");
        }

        // The session manager: memory cache, renewal before expiry, and a forgotten sign-in.
        var manager = Fake2(config, out var live, out var now);
        manager.Remember(session);
        Check((await manager.GetAsync(session.Uuid, ClientId, CancellationToken.None)).AccessToken == McToken && live.Urls.Count == 0, "a fresh session is served from memory without any request");
        now.Value = session.ExpiresAt.AddMinutes(-3);
        var again = await manager.GetAsync(session.Uuid, ClientId, CancellationToken.None);
        Check(live.Urls.Count > 0 && again.RefreshToken == "refresh-2" && store.Get(session.Uuid) == "refresh-2", "a session about to expire is renewed and the new refresh token is stored");
        var wrong = Fake2(config, out _, out var wrongNow, uuidOfProfile: "11111111111111111111111111111111");
        wrong.Remember(session); wrongNow.Value = session.ExpiresAt.AddMinutes(-1);
        Check(await CodeOf(() => wrong.GetAsync(session.Uuid, ClientId, CancellationToken.None)) == "account", "a stored sign-in that belongs to another Microsoft account is refused");
        var gone = Fake2(Directory.CreateDirectory(Path.Combine(root, "empty")).FullName, out _, out _);
        Check(await CodeOf(() => gone.GetAsync(session.Uuid, ClientId, CancellationToken.None)) == "signed-out", "an account with no stored sign-in has to sign in again");
        var dead = Fake2(config, out var deadFake, out var deadNow); deadFake.RefreshFails = true;
        dead.Remember(session); deadNow.Value = session.ExpiresAt.AddMinutes(-1);
        Check(await CodeOf(() => dead.GetAsync(session.Uuid, ClientId, CancellationToken.None)) == "signed-out" && store.Get(session.Uuid) == null, "a sign-in Microsoft no longer accepts is forgotten");

        // Accounts on disk never hold tokens.
        var accounts = new AccountManager(Path.Combine(root, "accounts"));
        accounts.SaveAccounts(new List<AccountModel> { new("Steve") { IsActive = true }, new("Steve_2") { Uuid = session.Uuid, IsMicrosoft = true } });
        string accountFile = File.ReadAllText(Path.Combine(root, "accounts", "accounts.json"));
        var loaded = accounts.LoadAccounts();
        Check(loaded.Single(a => a.IsMicrosoft).Uuid == session.Uuid && !loaded.Single(a => a.Username == "Steve").IsMicrosoft && !accountFile.Contains("token", StringComparison.OrdinalIgnoreCase),
            "the account list remembers which players are Microsoft accounts and contains no token");

        // The game gets the token in a separate argument list.
        Check(GameLauncher.AuthArguments(McToken).SequenceEqual(new[] { "--accessToken", McToken, "--userType", "msa" }), "a Microsoft session is passed to the game as --accessToken with --userType msa");
        Check(GameLauncher.ValidAccessToken(McToken) && !GameLauncher.ValidAccessToken("short") && !GameLauncher.ValidAccessToken(McToken + "\n--username evil") && !GameLauncher.ValidAccessToken(McToken + " x") && !GameLauncher.ValidAccessToken(new string('a', 9000)),
            "a token with spaces, line breaks or an odd length never reaches the argument file");
        Console.WriteLine("MICROSOFT_AUTH_OK");

        MicrosoftSessionManager Fake2(string configDirectory, out Fake handler, out Clock clockValue, string? uuidOfProfile = null)
        {
            handler = new Fake { ProfileId = uuidOfProfile ?? Uuid32 };
            clockValue = new Clock { Value = DateTimeOffset.UtcNow };
            var clockRef = clockValue; var handlerRef = handler;
            return new MicrosoftSessionManager(new MicrosoftTokenStore(configDirectory, new XorProtector()),
                _ => new MicrosoftAuthService(ClientId, new HttpClient(handlerRef), () => clockRef.Value), () => clockRef.Value);
        }
    }

    private sealed class Clock { public DateTimeOffset Value; }

    private static async Task<MicrosoftAuthException?> Assert(Func<Task> action)
    {
        try { await action(); return null; } catch (MicrosoftAuthException ex) { return ex; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
