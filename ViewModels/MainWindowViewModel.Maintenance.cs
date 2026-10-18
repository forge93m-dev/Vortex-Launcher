using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    private DownloadsWindow? _downloadsWindow;
    [RelayCommand] private void ShowDownloads()
    {
        if (_downloadsWindow != null) { _downloadsWindow.Activate(); return; }
        _downloadsWindow = new DownloadsWindow();
        _downloadsWindow.Closed += (_, _) => _downloadsWindow = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            _downloadsWindow.Show(owner);
        else _downloadsWindow.Show();
    }

    [RelayCommand] private async Task RepairInstanceAsync(InstanceModel? instance)
    {
        if (instance == null || !instance.CanChangeFiles || IsGlobalLoading || IsDownloading) return;
        try
        {
            string loaderVersion = await Task.Run(() => { InstanceRepair.ValidateTarget(instance); return InstanceRepair.ResolveInstalledLoaderVersion(instance); });
            if (!await TaskDialog.ConfirmAsync("Repair instance", $"Repair {instance.Name}?\n\nMinecraft files and assets will be checked against their manifest checksums. Loader libraries will be checked and missing or unreadable files downloaded again.\n\nLoader: {instance.Loader} {loaderVersion}\nWorlds, user mods, resource packs, screenshots and settings will not be removed or changed. Vortex Client is updated separately.", "Repair instance")) return;
            if (!instance.CanChangeFiles || IsGlobalLoading || IsDownloading) return;
            instance.LoaderVersion = loaderVersion;
            await InstallInstanceAsync(instance, repair: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Repair could not start: {ex.Message}";
            TaskDialog.ShowIssue(new LaunchIssue("Repair could not start", ex.Message, "Check the instance path and loader version. No game files were changed."), ex.ToString(), instance.InstancePath);
        }
    }

    [RelayCommand] private async Task UpdateVortexClientAsync(InstanceModel? instance)
    {
        if (instance == null || !instance.CanChangeFiles || IsGlobalLoading || IsDownloading) return;
        DownloadOperation? operation = null;
        try
        {
            string installed = await Task.Run(() => VortexClientService.InstalledVersion(instance));
            if (!await TaskDialog.ConfirmAsync("Update Vortex Client", $"{instance.Name}\nInstalled: {installed}\nAvailable in this launcher: {VortexClientService.ClientVersion}\n\nOnly Minecraft {VortexClientService.MinecraftVersion} with Fabric 0.19.5+ is supported.\n\nThe previous JAR will be backed up in .vortex-client-backups. Your client settings and other mods are preserved. A disabled JAR will stay disabled.", "Install / update")) return;
            if (!instance.CanChangeFiles || IsGlobalLoading || IsDownloading) return;
            instance.IsInstalling = true;
            IsGlobalLoading = true;
            LoadingTitle = "Updating Vortex Client";
            using var cancellation = new CancellationTokenSource();
            _installationCancellation = cancellation;
            CanCancelInstallation = true;
            operation = DownloadCenter.Begin($"Vortex Client: {instance.Name}", cancellation.Cancel);
            operation.Report("Validating client and creating backup");
            string? backup = await Task.Run(() => new VortexClientService().UpdateAsync(instance, cancellation.Token));
            instance.VortexClientEnabled = !VortexClientService.InstalledVersion(instance).EndsWith("(disabled)", StringComparison.Ordinal);
            SaveInstanceMetadata(instance);
            StatusMessage = $"Vortex Client {VortexClientService.ClientVersion} is installed.";
            operation.Complete(backup == null ? "Current bundled client is installed." : "Updated successfully. Backup: " + backup);
        }
        catch (OperationCanceledException)
        {
            operation?.Cancelled(); StatusMessage = "Client update cancelled. The existing JAR is unchanged.";
        }
        catch (Exception ex)
        {
            operation?.Fail(ex); StatusMessage = "Client update failed: " + ex.Message;
            TaskDialog.ShowIssue(new LaunchIssue("Client update failed", ex.Message, "Close Minecraft, verify compatibility and check that only one Vortex Client copy exists in Mods."), ex.ToString(), instance.InstancePath);
        }
        finally
        {
            operation?.Dispose();
            if (operation != null) { instance.IsInstalling = false; IsGlobalLoading = false; CanCancelInstallation = false; _installationCancellation = null; }
        }
    }
}
