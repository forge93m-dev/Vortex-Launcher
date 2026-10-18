using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    public string LauncherVersionLabel => $"Vortex Launcher {_updateService.CurrentVersion} · {PlatformSupport.RuntimeId}";
    public string UpdateSource => _currentSettings.UpdateManifestUrl;
    public string LastLauncherUpdateResult { get; } = VortexLauncher.Core.Network.LauncherUpdateInstaller.ReadResultMessage(Environment.GetCommandLineArgs());
    [ObservableProperty] private bool _isCheckingUpdates;
    [ObservableProperty] private string _launcherUpdateStatus = "Updates have not been checked yet.";
    [ObservableProperty] private string _launcherUpdateNotes = "";

    [RelayCommand] private async Task CheckLauncherUpdatesAsync()
    {
        if (IsCheckingUpdates || IsInstallingUpdate) return;
        IsCheckingUpdates = true;
        LauncherUpdateStatus = "Checking for updates...";
        try
        {
            var result = await _updateService.CheckForUpdateAsync(UpdateSource);
            _availableUpdate = result.Update;
            UpdateAvailable = result.Update != null;
            LauncherUpdateStatus = result.Message;
            LauncherUpdateNotes = result.Update?.Notes ?? "";
            if (UpdateAvailable) StatusMessage = $"Update available: Vortex Launcher {result.Update!.Version}";
        }
        catch (Exception ex) { LauncherUpdateStatus = "Could not check for updates: " + ex.Message; }
        finally { IsCheckingUpdates = false; }
    }

    [RelayCommand] private async Task InstallLauncherUpdateAsync()
    {
        var update = _availableUpdate;
        if (update == null || IsInstallingUpdate || IsDownloading || IsGlobalLoading) return;
        if (_runningGames > 0 || Instances.Any(i => i.IsRunning || i.IsInstalling))
        {
            LauncherUpdateStatus = "Close Minecraft and finish instance tasks before updating the launcher.";
            StatusMessage = LauncherUpdateStatus; return;
        }
        try { update.RequireVerifiedPackage(PlatformSupport.RuntimeId); }
        catch (Exception ex) { ShowUpdateFailure(ex); return; }
        if (!await TaskDialog.ConfirmAsync("Launcher update", $"Install Vortex Launcher {update.Version} for {PlatformSupport.RuntimeId}?\n\n{update.Notes}\n\nThe download is checked against the publisher's SHA-256 checksum. The launcher will close, replace only its application files, keep a backup and restart. Worlds, mods and settings are preserved.\n\nDeveloper builds open the downloaded package folder for manual replacement instead.", "Download & update")) return;
        if (IsInstallingUpdate || IsDownloading || IsGlobalLoading || Instances.Any(i => i.IsRunning || i.IsInstalling)) return;
        using var cancellation = new CancellationTokenSource();
        using var operation = DownloadCenter.Begin($"Launcher update: {update.Version}", cancellation.Cancel);
        try
        {
            IsInstallingUpdate = true; IsGlobalLoading = true;
            LoadingTitle = "Updating Vortex Launcher";
            _installationCancellation = cancellation; CanCancelInstallation = true;
            var progress = new TransferProgress(snapshot => { operation.Report(snapshot); UpdateTransferProgress(snapshot); });
            bool restarting = await _updateService.InstallAsync(update, cancellation.Token, progress);
            LauncherUpdateStatus = restarting ? "Restarting to finish the launcher update..." : "Verified package opened. Replace the application, then launch it again.";
            StatusMessage = LauncherUpdateStatus;
            operation.Complete(LauncherUpdateStatus);
            if (restarting && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { operation.Cancelled(); LauncherUpdateStatus = "Update cancelled. The installed launcher was not changed."; }
        catch (Exception ex) { operation.Fail(ex); ShowUpdateFailure(ex); }
        finally
        { IsInstallingUpdate = false; IsGlobalLoading = false; CanCancelInstallation = false; _installationCancellation = null; TransferDetails = ""; }
    }

    private void ShowUpdateFailure(Exception ex)
    {
        LauncherUpdateStatus = "Could not install update: " + ex.Message;
        StatusMessage = LauncherUpdateStatus;
        TaskDialog.ShowIssue(new LaunchIssue("Launcher update failed", ex.Message,
            "Check the update source and publisher package. For an unsigned macOS test build, ask the publisher for a correctly signed app; do not disable Gatekeeper."), ex.ToString());
    }
}
