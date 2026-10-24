using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    private bool _reviewingDiscordInvitation;
    private async Task ReviewDiscordInvitationAsync(DiscordServerInvite invite)
    {
        if (_reviewingDiscordInvitation || _launcherClosed || !DiscordPresenceEnabled || !ReceiveDiscordInvites || IsInitializing) return;
        if (_runningGames > 0 || IsDownloading || IsGlobalLoading) { StatusMessage = "Server invitation skipped while Minecraft or another operation is running. Accept it again when ready."; return; }
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is not { } owner) return;
        _reviewingDiscordInvitation = true;
        try
        {
            if (!DiscordServerInvite.TryDecode(invite.Encode(), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out _)) return;
            var selected = await new DiscordInviteWindow(invite, Instances).ShowDialog<InstanceModel?>(owner);
            if (selected == null || _launcherClosed || !DiscordPresenceEnabled || !ReceiveDiscordInvites) return;
            if (_runningGames > 0 || IsDownloading || IsGlobalLoading || !Instances.Contains(selected) || !selected.IsInstalled || !selected.CanLaunch || !invite.Matches(selected.Version, selected.Loader) ||
                !DiscordServerInvite.TryDecode(invite.Encode(), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out _))
            { StatusMessage = "Invitation expired or the selected instance is no longer ready. Nothing was launched."; return; }
            await LaunchInstanceOnServerAsync(selected, invite.Address);
        }
        catch (Exception) { StatusMessage = "Could not open this server invitation. Nothing was launched."; }
        finally { _reviewingDiscordInvitation = false; }
    }
}
