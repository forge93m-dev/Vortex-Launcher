using VortexLauncher.Core.Accounts;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;

// Explicit opt-in: synthetic public account, isolated session store, no real player data.
internal static class VortexAccountLiveChecks
{
    public static void Run(Action<int> pump,string output)
    {
        var root=Directory.CreateDirectory(Path.Combine(output,"live-accounts")).FullName;
        var suffix=Guid.NewGuid().ToString("N")[..10];var login="qa_"+suffix;
        var nickname="QA_"+suffix;var password=Guid.NewGuid().ToString("N")+"!Test";
        var api=new VortexAccountClient();var store=new VortexSessionStore(Path.Combine(root,"session.dpapi"));
        var accounts=new AccountManager(Path.Combine(root,"config"));
        accounts.SaveAccounts(new(){new AccountModel("FixturePlayer"){IsActive=true}});
        MainWindowViewModel Create()=>new(new InstanceManager(Path.Combine(root,"instances")),new SettingsManager(Path.Combine(root,"config")),accounts,checkUpdates:false){VortexAccountApi=api,VortexAccountStore=store};
        var vm=Create();MainWindowViewModel? restored=null;VortexSession? registered=null;
        void Wait(Task task){var clock=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted && clock.ElapsedMilliseconds<30000)pump(10);if(!task.IsCompleted)throw new Exception("Live account check timed out");task.GetAwaiter().GetResult();}
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
        try
        {
            Wait(vm.InitializeAsync());var original=vm.ActiveAccount.Uuid;
            // Register through the actual API; expose only exact synthetic cleanup coordinates.
            Wait(Task.Run(async()=>registered=await api.AuthenticateAsync(login,password,nickname)));
            Console.WriteLine("ACCOUNT_FIXTURE:"+System.Text.Json.JsonSerializer.Serialize(new {id=registered!.Profile.Id,login}));
            Wait(api.LogoutAsync(registered!));
            vm.VortexLogin=login;vm.VortexPassword=password;Wait(vm.AuthenticateVortexCommand.ExecuteAsync(null));
            Check(vm.HasVortexAccount && vm.VortexPassword=="","live launcher sign-in clears password");
            Check(vm.ActiveAccount.Uuid==original && vm.Accounts.Count==1,"live sign-in preserves Minecraft profile");
            Check(store.Load()!=null,"live session is saved in isolated protected storage");
            restored=Create();Wait(restored.InitializeAsync());Wait(restored.RestoreVortexSessionAsync());
            Check(restored.HasVortexAccount && restored.VortexAccountLabel==login,"new launcher restores and verifies live session");
            var saved=store.Load()!;
            Wait(restored.SignOutVortexCommand.ExecuteAsync(null));
            Check(!restored.HasVortexAccount && store.Load()==null,"live sign-out clears protected session");
            bool denied=false;try{Wait(api.MeAsync(saved));}catch(UnauthorizedAccessException){denied=true;}
            Check(denied,"live sign-out revokes server token");
            vm.VortexPassword="Wrong fixture password!";Wait(vm.SignOutVortexCommand.ExecuteAsync(null));
            vm.VortexPassword="Wrong fixture password!";Wait(vm.AuthenticateVortexCommand.ExecuteAsync(null));
            Check(!vm.HasVortexAccount && vm.VortexPassword=="","wrong password cannot authenticate");
            Console.WriteLine("VORTEX_ACCOUNT_LIVE_LAUNCHER_OK");
        }
        finally
        {
            Wait(vm.SignOutVortexCommand.ExecuteAsync(null));
            if(restored!=null){Wait(restored.SignOutVortexCommand.ExecuteAsync(null));restored.CancelStartup();restored.Library.Dispose();}
            vm.CancelStartup();vm.Library.Dispose();
        }
    }
}
