using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;

namespace VortexLauncher.Views;

public sealed class DiscordInviteWindow : Window
{
    public DiscordInviteWindow(DiscordServerInvite invite, IEnumerable<InstanceModel> instances)
    {
        Title = "Vortex · Server invitation"; Width = 560; Height = 400;
        MinWidth = 480; MinHeight = 360; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Application.Current?.FindResource("ThemeBackgroundBrush") as IBrush ?? Brush.Parse("#10141A");
        var choices = instances.Where(i => invite.Matches(i.Version, i.Loader) && i.IsInstalled && i.CanLaunch).ToArray();
        var picker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(),
            ItemTemplate = new FuncDataTemplate<InstanceModel>((instance, _) => new TextBlock
            { Text = instance == null ? "" : instance.Name + " · " + instance.Version + " / " + instance.Loader, TextTrimming = TextTrimming.CharacterEllipsis })
        };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Text = choices.Length == 0 ? "No ready Fabric instance for this version. Close this dialog, create/install one, then accept the invitation again."
                : "This invitation is not proof of the sender's identity. Only connect to a server you trust. Matching Minecraft versions does not guarantee matching mods." };
        var launch = new Button { Content = "Launch and join", IsEnabled = choices.Length > 0 };
        launch.Classes.Add("PrimaryBtn");
        var cancel = new Button { Content = "Cancel" }; cancel.Classes.Add("SecondaryBtn");
        cancel.Click += (_, _) => Close(null);
        launch.Click += (_, _) =>
        {
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= invite.Expires) { message.Text = "Invitation expired. Ask your friend for a new one."; launch.IsEnabled = false; return; }
            if (picker.SelectedItem is not InstanceModel selected || !selected.IsInstalled || !selected.CanLaunch) { message.Text = "This instance is no longer ready. Cancel and try again."; return; }
            Close(selected);
        };
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Play together", FontSize = 24, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = invite.Address, FontSize = 17, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Minecraft " + invite.Version + " · Fabric · This invitation does not install a modpack", FontSize = 12, TextWrapping = TextWrapping.Wrap },
                picker, message,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, launch } }
            }
        };
        WindowTitleBar.Apply(this);
    }
}
