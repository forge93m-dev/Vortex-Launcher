using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using VortexLauncher.Core.Accounts;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class VortexAccountChecks
{
    public static void Run(Action<int> pump,string output)
    {
        var root=Directory.CreateDirectory(Path.Combine(output,"vortex-accounts")).FullName;
        var identity=new VortexIdentity("11111111-1111-4111-8111-111111111111","fixture","FixturePlayer");
        var session=new VortexSession(identity,new string('a',64),DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds());
        var store=new VortexSessionStore(Path.Combine(root,"session.dpapi"));
        Check(!VortexAccountClient.ValidLogin("fixture\n") && !VortexAccountClient.ValidNickname("fixture\n") && !VortexAccountClient.ValidToken(null),"names reject trailing controls and missing tokens");
        Check(!session.ToString().Contains(session.Token),"session diagnostics redact bearer token");
        if(store.CanRemember)
        {
            store.Save(session);var bytes=File.ReadAllBytes(Path.Combine(root,"session.dpapi"));
            Check(!Encoding.UTF8.GetString(bytes).Contains(session.Token),"remembered session contains no plaintext token");
            Check(store.Load()==session,"Windows protected session round trips");
            store.Clear();Check(store.Load()==null,"sign out removes only fixture session");
            // Test-only corrupt input is created with .NET, never user's credential storage.
            File.WriteAllBytes(Path.Combine(root,"session.dpapi"),new byte[]{1,2,3,4});
            try{store.Load();throw new Exception("Corrupt fixture accepted");}catch(IOException){}
            Check(File.ReadAllBytes(Path.Combine(root,"session.dpapi")).SequenceEqual(new byte[]{1,2,3,4}),"unreadable session is preserved");
            store.Clear();
        }
        var handler=new FixtureHandler(session);
        var api=new VortexAccountClient(new HttpClient(handler));
        VortexSession? authenticated=null;
        Wait(Task.Run(async()=>authenticated=await api.AuthenticateAsync("fixture","Synthetic password 472!","FixturePlayer")),pump);
        Check(authenticated==session,"validated registration response becomes a Vortex session");
        string? wardrobeToken=null;Wait(Task.Run(async()=>wardrobeToken=await api.WardrobeSessionAsync(session,identity.Id)),pump);
        Check(wardrobeToken==new string('c',64) && wardrobeToken!=session.Token,"game receives a separate wardrobe capability, not the account session");
        handler.Mode="wrong-id";
        ExpectFailure(()=>api.MeAsync(session),pump,"different identity is rejected");
        handler.Mode="redirect";ExpectFailure(()=>api.MeAsync(session),pump,"account service redirects are rejected");
        handler.Mode="large";ExpectFailure(()=>api.MeAsync(session),pump,"oversized account responses are rejected");
        handler.Mode="error";
        try{Wait(Task.Run(()=>api.AuthenticateAsync("fixture","Synthetic password 472!",null)),pump);throw new Exception("Fixture error accepted");}
        catch(InvalidOperationException error){Check(!error.Message.Contains("fixture-secret"),"server error bodies are not exposed");}
        handler.Mode="ok";
        handler.Mode="throttled";
        try{Wait(Task.Run(()=>api.AuthenticateAsync("fixture","Synthetic password 472!",null)),pump);throw new Exception("Rate limit fixture accepted");}
        catch(InvalidOperationException error){Check(error.Message.Contains("2 min 5 sec"),"rate limit displays server wait time");}
        handler.Mode="ok";
        var accounts=new AccountManager(Path.Combine(root,"config"));
        var original=new AccountModel("LegacyPlayer"){IsActive=true};accounts.SaveAccounts(new(){original});
        var vm=new MainWindowViewModel(new InstanceManager(Path.Combine(root,"instances")),new SettingsManager(Path.Combine(root,"config")),accounts,checkUpdates:false)
        {VortexAccountApi=api,VortexAccountStore=store};
        Wait(vm.InitializeAsync(),pump);string originalId=vm.ActiveAccount.Uuid;
        vm.VortexLogin="fixture";vm.VortexPassword="Synthetic password 472!";
        Wait(vm.AuthenticateVortexCommand.ExecuteAsync(null),pump);
        Check(vm.HasVortexAccount && vm.VortexPassword=="" && vm.VortexPasswordConfirmation=="","sign in clears password fields");
        Check(vm.Accounts.Count==1 && vm.ActiveAccount.Uuid==originalId,"sign in does not claim or replace a local Minecraft profile");
        vm.VortexNickname="ChangedPlayer";Wait(vm.RenameVortexNicknameCommand.ExecuteAsync(null),pump);
        Check(vm.ActiveAccount.Uuid==originalId && vm.ActiveAccount.Username=="LegacyPlayer","Vortex nickname rename leaves game identity unchanged");
        vm.UseVortexNicknameCommand.Execute(null);
        Check(vm.Accounts.Count==2 && vm.ActiveAccount.Username=="ChangedPlayer" && vm.Accounts.Any(a=>a.Uuid==originalId),"explicit nickname selection preserves legacy local player");
        Wait(vm.SignOutVortexCommand.ExecuteAsync(null),pump);
        Check(!vm.HasVortexAccount && vm.Accounts.Count==2 && store.Load()==null,"sign out preserves saved players and clears credential");
        vm.SelectTabCommand.Execute("Accounts");
        var host=new MainWindow {DataContext=vm,Width=1100,Height=760};host.Show();pump(500);
        host.FindControl<Expander>("vortexAccountExpander")!.IsExpanded=true;pump(200);
        var passwordBoxes=host.GetVisualDescendants().OfType<TextBox>().Where(b=>b.PasswordChar=='●').ToList();
        Check(passwordBoxes.Count==2,"sign in and registration password controls are masked");
        host.CaptureRenderedFrame()!.Save(Path.Combine(output,"vortex-account-sign-in.png"));
        vm.ToggleVortexRegistrationCommand.Execute(null);pump(150);
        host.CaptureRenderedFrame()!.Save(Path.Combine(output,"vortex-account-register.png"));
        host.Close();vm.CancelStartup();vm.Library.Dispose();
        Console.WriteLine("VORTEX_ACCOUNT_LAUNCHER_OK");
    }
    private static void Wait(Task task,Action<int> pump)
    {
        var clock=Stopwatch.StartNew();while(!task.IsCompleted && clock.ElapsedMilliseconds<20000)pump(10);
        if(!task.IsCompleted)throw new Exception("Account fixture timed out");task.GetAwaiter().GetResult();
    }
    private static void ExpectFailure(Func<Task> action,Action<int> pump,string message)
    {try{Wait(Task.Run(action),pump);throw new Exception("Unsafe fixture response accepted");}catch(InvalidOperationException){Check(true,message);}}
    private static void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text);}
    private sealed class FixtureHandler(VortexSession session):HttpMessageHandler
    {
        public string Mode="ok";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(Mode=="redirect")return new(HttpStatusCode.Redirect){Headers={Location=new Uri("https://other.invalid/")}};
            if(Mode=="large")return new(HttpStatusCode.OK){Content=new StringContent(new string('x',17000))};
            if(Mode=="error")return new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{\"error\":\"fixture-secret\"}")};
            if(Mode=="throttled")return new(HttpStatusCode.TooManyRequests){Content=new StringContent("{\"error\":\"rate_limited\",\"retry_after\":125}"),Headers={RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(125))}};
            string route=request.RequestUri!.Segments.Last();
            if(route=="wardrobe-session"){
                Check(request.Headers.Authorization?.Parameter==session.Token,"only launcher requests wardrobe capability with account bearer");
                return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new {token=new string('c',64),expires=session.Expires}))};
            }
            if(route is "me" or "nickname" or "logout")Check(request.Headers.Authorization?.Parameter==session.Token,"authenticated operation uses bearer header");
            var profile=Mode=="wrong-id"?session.Profile with {Id="22222222-2222-4222-8222-222222222222"}:session.Profile;
            if(route=="nickname")
            {
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                profile=profile with {Nickname=body.RootElement.GetProperty("nickname").GetString()!};
            }
            object response=route=="logout"?new {signed_out=true}:new {profile=new {id=profile.Id,login=profile.Login,nickname=profile.Nickname},token=session.Token,expires=session.Expires};
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(response))};
        }
    }
}
