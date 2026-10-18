using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Accounts;
using VortexLauncher.Models;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    internal VortexAccountClient VortexAccountApi { get; set; } = new();
    internal VortexSessionStore VortexAccountStore { get; set; } = new();
    private VortexSession? _vortexSession;
    [ObservableProperty] private string _vortexLogin = "";
    [ObservableProperty] [property: JsonIgnore] private string _vortexPassword = "";
    [ObservableProperty] [property: JsonIgnore] private string _vortexPasswordConfirmation = "";
    [ObservableProperty] private string _vortexNickname = "";
    [ObservableProperty] private bool _isVortexRegistration;
    [ObservableProperty] private bool _isVortexAccountBusy;
    [ObservableProperty] private bool _hasVortexAccount;
    [ObservableProperty] private bool _rememberVortexAccount = true;
    [ObservableProperty] private string _vortexAccountMessage = "Vortex accounts are separate from local Minecraft player names.";
    [ObservableProperty] private string _vortexAccountLabel = "Not signed in";
    public bool CanRememberVortexAccount => VortexAccountStore.CanRemember;
    public string VortexAuthAction => IsVortexRegistration ? "Create account" : "Sign in";
    partial void OnIsVortexRegistrationChanged(bool value) => OnPropertyChanged(nameof(VortexAuthAction));
    [RelayCommand] private void ToggleVortexRegistration()
    {
        if (IsVortexAccountBusy) return;
        IsVortexRegistration = !IsVortexRegistration;VortexPassword = VortexPasswordConfirmation = "";
        VortexAccountMessage = IsVortexRegistration ? "Use a unique password (15+ characters). Password recovery is not available in this beta." : "Sign in to your Vortex account. Local profiles stay unchanged.";
    }
    [RelayCommand] private async Task AuthenticateVortexAsync()
    {
        if (IsVortexAccountBusy || HasVortexAccount) return;
        string password=VortexPassword,confirmation=VortexPasswordConfirmation;
        VortexPassword=VortexPasswordConfirmation="";
        if(IsVortexRegistration && password!=confirmation){VortexAccountMessage="Passwords do not match.";return;}
        IsVortexAccountBusy=true;
        try
        {
            var session=await VortexAccountApi.AuthenticateAsync(VortexLogin.Trim(),password,IsVortexRegistration?VortexNickname.Trim():null,_lifetime.Token);
            _vortexSession=session;HasVortexAccount=true;VortexAccountLabel=session.Profile.Login;VortexNickname=session.Profile.Nickname;
            VortexAccountMessage=await RememberSessionAsync(session);
        }
        catch(OperationCanceledException){VortexAccountMessage="Sign-in cancelled.";}
        catch(Exception error){VortexAccountMessage=AccountError(error);}
        finally{IsVortexAccountBusy=false;}
    }
    private async Task<string> RememberSessionAsync(VortexSession session)
    {
        if(!RememberVortexAccount || !VortexAccountStore.CanRemember)
        {
            try{await Task.Run(VortexAccountStore.Clear);}catch{return "Signed in for this session, but an older saved sign-in could not be removed. Check protected storage before sharing this computer.";}
            return "Signed in for this launcher session. Your local player names are unchanged.";
        }
        try{await Task.Run(()=>VortexAccountStore.Save(session));return "Signed in. Select Use nickname for Minecraft, then launch. Wardrobe Apply automatically syncs skins and capes online. Your password is not saved.";}
        catch{return "Signed in for this session only. Protected storage could not be updated; your password is not saved.";}
    }
    internal async Task RestoreVortexSessionAsync()
    {
        if(IsVortexAccountBusy || HasVortexAccount)return;IsVortexAccountBusy=true;
        try
        {
            var session=await Task.Run(VortexAccountStore.Load,_lifetime.Token);if(session==null)return;
            if(session.Expires<=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()){VortexAccountMessage="Your saved session expired. Sign in again.";return;}
            VortexAccountMessage="Checking your saved Vortex session…";
            var profile=await VortexAccountApi.MeAsync(session,_lifetime.Token);
            _vortexSession=session with {Profile=profile};HasVortexAccount=true;VortexAccountLabel=profile.Login;VortexNickname=profile.Nickname;
            VortexAccountMessage="Signed in to Vortex. Local player names are unchanged.";
        }
        catch(OperationCanceledException){ }
        catch(Exception error){VortexAccountMessage=AccountError(error);}
        finally{IsVortexAccountBusy=false;}
    }
    [RelayCommand] private async Task RenameVortexNicknameAsync()
    {
        if(IsVortexAccountBusy || _vortexSession==null)return;IsVortexAccountBusy=true;
        try
        {
            var profile=await VortexAccountApi.RenameAsync(_vortexSession,VortexNickname.Trim(),_lifetime.Token);
            _vortexSession=_vortexSession with {Profile=profile};VortexNickname=profile.Nickname;
            VortexAccountMessage="Vortex nickname updated. Your account ID has not changed. "+await RememberSessionAsync(_vortexSession);
        }
        catch(Exception error){VortexAccountMessage=AccountError(error);}
        finally{IsVortexAccountBusy=false;}
    }
    [RelayCommand] private void UseVortexNickname()
    {
        if(_vortexSession==null || IsVortexAccountBusy || IsInitializing)return;
        string name=_vortexSession.Profile.Nickname;
        var profile=Accounts.FirstOrDefault(a=>a.Username.Equals(name,StringComparison.Ordinal));
        if(profile==null && Accounts.Any(a=>a.Username.Equals(name,StringComparison.OrdinalIgnoreCase))){VortexAccountMessage="A local name with different capitalization already exists. Select or edit it in Saved players.";return;}
        profile??=new AccountModel(name);var previousLastUsed=profile.LastUsedUtc;profile.LastUsedUtc=DateTimeOffset.UtcNow;
        if(CommitProfiles(Accounts.Contains(profile)?Accounts.ToList():Accounts.Append(profile).ToList(),profile))
            VortexAccountMessage="Nickname selected for the next launch. This is not Microsoft authentication or proof of ownership on an offline server.";
        else profile.LastUsedUtc=previousLastUsed;
    }
    [RelayCommand] private async Task SignOutVortexAsync()
    {
        if(IsVortexAccountBusy)return;IsVortexAccountBusy=true;bool revoked=true,cleared=true;
        try
        {
            if(_vortexSession!=null)try{await VortexAccountApi.LogoutAsync(_vortexSession,_lifetime.Token);}catch{revoked=false;}
            try{await Task.Run(VortexAccountStore.Clear);}catch{cleared=false;}
            _vortexSession=null;HasVortexAccount=false;VortexAccountLabel="Not signed in";VortexPassword=VortexPasswordConfirmation="";
            VortexAccountMessage=!cleared?"Signed out here, but saved sign-in could not be removed. Check protected storage before sharing this computer."
                :revoked?"Signed out. Your local player names and worlds are unchanged.":"Signed out on this computer. Server revocation could not be confirmed; the session expires automatically.";
        }
        finally{IsVortexAccountBusy=false;}
    }
    private static string AccountError(Exception error)=>error is InvalidOperationException or UnauthorizedAccessException
        ?error.Message:"The account operation could not finish. Local profiles and game files were not changed.";
}
