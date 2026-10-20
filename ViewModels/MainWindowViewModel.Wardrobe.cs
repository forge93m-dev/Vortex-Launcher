using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using VortexLauncher.Views;

namespace VortexLauncher.ViewModels;

public partial class MainWindowViewModel
{
    internal async Task OpenWardrobeAsync(Window owner)
    {
        if(_vortexSession==null){SelectTabCommand.Execute("Accounts");VortexAccountMessage="Sign in to Vortex to change your online skin and cape from the launcher.";return;}
        if(ActiveAccount.Username!=_vortexSession.Profile.Nickname){SelectTabCommand.Execute("Accounts");VortexAccountMessage="Select Use nickname for Minecraft first. The wardrobe publishes only to your Vortex-owned nickname.";return;}
        await new LauncherWardrobeWindow(_vortexSession,ActiveAccount.Uuid,VortexAccountApi).ShowDialog(owner);
    }
}
