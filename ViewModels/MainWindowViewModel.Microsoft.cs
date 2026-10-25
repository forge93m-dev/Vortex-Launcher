using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;

namespace VortexLauncher.ViewModels;

/// <summary>Signing in with a Microsoft account: the player enters a short code on microsoft.com, so no password ever reaches the launcher.</summary>
public partial class MainWindowViewModel
{
    internal MicrosoftSessionManager _microsoftSessions = new(new MicrosoftTokenStore());
    /// <summary>Tests replace this to talk to a fake Microsoft instead of the real one.</summary>
    internal Func<string, MicrosoftAuthService>? MicrosoftServiceFactory { get; set; }
    internal Action<string>? OpenBrowser { get; set; }
    private CancellationTokenSource? _microsoftCancellation;

    [ObservableProperty] private string _microsoftClientId = "";
    [ObservableProperty] private string _microsoftStatus = "";
    [ObservableProperty] private string _microsoftCode = "";
    [ObservableProperty] private string _microsoftPageUrl = "";
    [ObservableProperty] private bool _isMicrosoftBusy;

    public bool HasMicrosoftCode => MicrosoftCode.Length > 0;
    public bool CanStartMicrosoftSignIn => !IsMicrosoftBusy && !IsInitializing;
    public string MicrosoftHelp =>
        "Sign in with a Microsoft account that owns Minecraft: Java Edition to play on online-mode servers. " +
        "This needs the Application (client) ID of your own free Azure app (personal accounts, public client), " +
        "and the app must be approved for Minecraft by Mojang. You enter the code in your browser; the launcher never sees your password.";

    partial void OnMicrosoftCodeChanged(string value) => OnPropertyChanged(nameof(HasMicrosoftCode));
    partial void OnIsMicrosoftBusyChanged(bool value) => OnPropertyChanged(nameof(CanStartMicrosoftSignIn));

    [RelayCommand]
    private async Task SignInMicrosoftAsync()
    {
        if (IsMicrosoftBusy || IsInitializing) return;
        string clientId = MicrosoftClientId.Trim();
        if (!MicrosoftAuthService.IsValidClientId(clientId))
        {
            MicrosoftStatus = "Enter the Application (client) ID of your Azure app first. It looks like 1a2b3c4d-1111-2222-3333-444455556666.";
            return;
        }
        IsMicrosoftBusy = true;
        using var cancellation = new CancellationTokenSource();
        _microsoftCancellation = cancellation;
        try
        {
            var service = MicrosoftServiceFactory?.Invoke(clientId) ?? new MicrosoftAuthService(clientId);
            MicrosoftStatus = "Asking Microsoft for a sign-in code...";
            var code = await service.StartAsync(cancellation.Token);
            MicrosoftCode = code.UserCode;
            MicrosoftPageUrl = code.VerificationUri;
            MicrosoftStatus = $"Enter the code on {new Uri(code.VerificationUri).Host}. Waiting for you...";
            OpenMicrosoftPage();
            var tokens = await service.WaitAsync(code, cancellation.Token);
            MicrosoftStatus = "Signing in to Xbox and Minecraft...";
            AddMicrosoftAccount(await service.CompleteAsync(tokens, cancellation.Token));
        }
        catch (OperationCanceledException) { MicrosoftStatus = "Sign-in cancelled."; }
        catch (MicrosoftAuthException ex) { MicrosoftStatus = ex.Message; }
        finally
        {
            MicrosoftCode = ""; MicrosoftPageUrl = "";
            _microsoftCancellation = null;
            IsMicrosoftBusy = false;
        }
    }

    [RelayCommand] private void CancelMicrosoftSignIn() => _microsoftCancellation?.Cancel();

    [RelayCommand]
    private void OpenMicrosoftPage()
    {
        if (!MicrosoftAuthService.IsTrustedVerificationUri(MicrosoftPageUrl)) return;
        try
        {
            if (OpenBrowser != null) OpenBrowser(MicrosoftPageUrl);
            else Process.Start(new ProcessStartInfo { FileName = MicrosoftPageUrl, UseShellExecute = true });
        }
        catch (Exception) { /* The code is shown on screen; the player can open the page by hand. */ }
    }

    /// <summary>Adds the signed-in player (or updates the same player) and makes it the active profile.</summary>
    internal void AddMicrosoftAccount(MicrosoftAuthService.Session session)
    {
        _microsoftSessions.Remember(session);
        var account = Accounts.FirstOrDefault(a => a.IsMicrosoft && string.Equals(a.Uuid, session.Uuid, StringComparison.OrdinalIgnoreCase))
            ?? new AccountModel(session.Name) { Uuid = session.Uuid, IsMicrosoft = true };
        account.Username = session.Name;
        var list = Accounts.Contains(account) ? Accounts.ToList() : Accounts.Append(account).ToList();
        MicrosoftStatus = CommitProfiles(list, account)
            ? $"Signed in as {session.Name}." + (_microsoftSessions.CanPersist ? "" : " This system cannot protect the sign-in, so you will need to sign in again the next time the launcher starts.")
            : "The profile could not be saved: " + ProfileError;
    }
}
