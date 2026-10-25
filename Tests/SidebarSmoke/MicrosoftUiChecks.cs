using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

// Microsoft sign-in in the launcher itself: the Profiles page section, and the whole flow against a fake Microsoft.
internal static class MicrosoftUiChecks
{
    private const string ClientId = "11111111-2222-3333-4444-555555555555";

    private sealed class FakeMicrosoft : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString(); lock (Urls) Urls.Add(url);
            string body = request.Content == null ? "" : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            object answer = url switch
            {
                MicrosoftAuthService.DeviceCodeUrl => new { device_code = "d", user_code = "WXYZ9876", verification_uri = "https://www.microsoft.com/link", expires_in = 900, interval = 1 },
                MicrosoftAuthService.TokenUrl => body.Contains("device_code") ? new { access_token = "ms", refresh_token = "refresh-ui", expires_in = 3600 } : new { access_token = "ms2", refresh_token = "refresh-ui-2", expires_in = 3600 },
                MicrosoftAuthService.XboxUserUrl => new { Token = "xbl", DisplayClaims = new { xui = new[] { new { uhs = "h" } } } },
                MicrosoftAuthService.XstsUrl => new { Token = "xsts", DisplayClaims = new { xui = new[] { new { uhs = "h" } } } },
                MicrosoftAuthService.MinecraftLoginUrl => new { access_token = "mc.token-0123456789ABCDEFGH", expires_in = 86400 },
                MicrosoftAuthService.EntitlementsUrl => new { items = new[] { new { name = "game_minecraft" } } },
                MicrosoftAuthService.ProfileUrl => new { id = "069a79f444e94726a5befca90e38aaf5", name = "Alex_Ms" },
                _ => new { error = "unexpected" }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Rot : ISecretProtector
    {
        public byte[]? Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x33)).ToArray();
        public byte[]? Unprotect(byte[] data) => Protect(data);
    }

    internal static void Run(Action<int> pump, string output)
    {
        var root = Path.Combine(output, "microsoft-ui");
        var accounts = new AccountManager(Path.Combine(root, "config"));
        accounts.SaveAccounts(new() { new AccountModel("Steve") { IsActive = true } });
        var vm = new MainWindowViewModel(new InstanceManager(Path.Combine(root, "instances")), new SettingsManager(Path.Combine(root, "config")), accounts, checkUpdates: false);
        var init = vm.InitializeAsync(); var clock = Stopwatch.StartNew();
        while (!init.IsCompleted && clock.ElapsedMilliseconds < 15000) pump(10);
        init.GetAwaiter().GetResult();
        var store = new MicrosoftTokenStore(Path.Combine(root, "config"), new Rot());
        var fake = new FakeMicrosoft();
        vm._microsoftSessions = new MicrosoftSessionManager(store, id => new MicrosoftAuthService(id, new HttpClient(fake)));
        vm.MicrosoftServiceFactory = id => new MicrosoftAuthService(id, new HttpClient(fake));
        string? opened = null; vm.OpenBrowser = url => opened = url;

        var host = new MainWindow { DataContext = vm, Width = 1100, Height = 800 };
        host.Show(); pump(300);
        vm.ActiveTab = "Accounts"; pump(300);
        var expander = host.FindControl<Expander>("microsoftAccountExpander");
        Check(expander != null && host.FindControl<TextBox>("microsoftClientIdBox") != null && host.FindControl<Button>("microsoftSignInButton") != null,
            "the Profiles page has a Microsoft account section with the app ID box and the sign-in button");
        expander!.IsExpanded = true; pump(150);
        host.CaptureRenderedFrame()!.Save(Path.Combine(output, "microsoft-section.png"));

        vm.MicrosoftClientId = "";
        vm.SignInMicrosoftCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Check(vm.MicrosoftStatus.Contains("Application (client) ID") && vm.Accounts.Count == 1 && fake.Urls.Count == 0, "signing in without an app ID explains what is missing and contacts nobody");
        vm.MicrosoftClientId = "nonsense";
        vm.SignInMicrosoftCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Check(vm.Accounts.Count == 1 && fake.Urls.Count == 0, "an app ID that is not a GUID is refused before any request");

        vm.MicrosoftClientId = ClientId;
        var signIn = vm.SignInMicrosoftCommand.ExecuteAsync(null);
        clock.Restart(); bool codeShown = false;
        while (!signIn.IsCompleted && clock.ElapsedMilliseconds < 20000) { pump(20); codeShown |= vm.MicrosoftCode == "WXYZ9876" && vm.HasMicrosoftCode; }
        signIn.GetAwaiter().GetResult(); pump(100);
        Check(codeShown, "while waiting, the short code is shown to the player");
        Check(opened == "https://www.microsoft.com/link", "Microsoft's page is opened for the player");
        var ms = vm.Accounts.Single(a => a.IsMicrosoft);
        Check(ms.Username == "Alex_Ms" && ms.Uuid == "069a79f4-44e9-4726-a5be-fca90e38aaf5" && ms.IsActive && ReferenceEquals(vm.ActiveAccount, ms) && ms.KindLabel == "Microsoft account" && ms.KindChip == "MICROSOFT ACCOUNT",
            "a signed-in Microsoft player becomes the active profile with their real name and UUID");
        Check(!vm.HasMicrosoftCode && !vm.IsMicrosoftBusy && vm.MicrosoftStatus.StartsWith("Signed in as Alex_Ms"), "the code disappears and the status says who signed in");
        var saved = accounts.LoadAccounts();
        Check(saved.Single(a => a.IsMicrosoft).Uuid == ms.Uuid && !saved.Single(a => a.Username == "Steve").IsMicrosoft, "the Microsoft flag survives a save and reload of the profile list");
        Check(store.Get(ms.Uuid) == "refresh-ui", "the refresh token is stored encrypted, not in the profile list");
        host.CaptureRenderedFrame()!.Save(Path.Combine(output, "microsoft-signed-in.png"));

        // The launch path gets a token from the session manager without asking again.
        int requests = fake.Urls.Count;
        var session = vm._microsoftSessions.GetAsync(ms.Uuid, ClientId, CancellationToken.None).GetAwaiter().GetResult();
        Check(session.AccessToken == "mc.token-0123456789ABCDEFGH" && fake.Urls.Count == requests, "launching with the account uses the fresh session without another sign-in");

        // Names of Microsoft players come from Minecraft; removing one forgets the sign-in.
        vm.EditAccountCommand.Execute(ms);
        Check(vm.EditingAccount == null && vm.ProfileError.Contains("cannot be edited"), "a Microsoft player's name cannot be edited");
        vm.PendingDeleteAccount = ms; vm.ConfirmDeleteAccountCommand.Execute(null);
        Check(vm.Accounts.All(a => !a.IsMicrosoft) && store.Get(ms.Uuid) == null && accounts.LoadAccounts().All(a => !a.IsMicrosoft), "removing a Microsoft player also removes the stored sign-in");

        // Cancelling stops waiting and leaves everything as it was.
        vm.MicrosoftServiceFactory = id => new MicrosoftAuthService(id, new HttpClient(new SlowMicrosoft()));
        var cancelled = vm.SignInMicrosoftCommand.ExecuteAsync(null);
        clock.Restart();
        while (!vm.HasMicrosoftCode && clock.ElapsedMilliseconds < 10000) pump(20);
        vm.CancelMicrosoftSignInCommand.Execute(null);
        clock.Restart();
        while (!cancelled.IsCompleted && clock.ElapsedMilliseconds < 10000) pump(20);
        cancelled.GetAwaiter().GetResult();
        Check(vm.MicrosoftStatus == "Sign-in cancelled." && !vm.IsMicrosoftBusy && vm.Accounts.All(a => !a.IsMicrosoft), "cancelling a sign-in changes nothing");
        host.Close();
        Console.WriteLine("MICROSOFT_UI_OK");
    }

    // A Microsoft that never approves the code.
    private sealed class SlowMicrosoft : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object answer = request.RequestUri!.ToString() == MicrosoftAuthService.DeviceCodeUrl
                ? new { device_code = "d", user_code = "SLOW0001", verification_uri = "https://www.microsoft.com/link", expires_in = 900, interval = 1 }
                : new { error = "authorization_pending" };
            return Task.FromResult(new HttpResponseMessage(request.RequestUri.ToString() == MicrosoftAuthService.DeviceCodeUrl ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
                { Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json") });
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
