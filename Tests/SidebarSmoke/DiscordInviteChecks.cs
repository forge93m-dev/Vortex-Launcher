using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.Views;

internal static class DiscordInviteChecks
{
    public static void Run(Action<int> pump, string output)
    {
        var ready = new InstanceModel { Name="Ready Fabric", Version="26.3", Loader="Fabric", IsInstalled=true };
        var wrong = new InstanceModel { Name="Other version", Version="26.2", Loader="Fabric", IsInstalled=true };
        var forge = new InstanceModel { Name="Forge", Version="26.3", Loader="Forge", IsInstalled=true };
        var missing = new InstanceModel { Name="Not installed", Version="26.3", Loader="Fabric", IsInstalled=false };
        var busy = new InstanceModel { Name="Running", Version="26.3", Loader="Fabric", IsInstalled=true, IsRunning=true };
        var invite = new DiscordServerInvite("26.3","mc.example.org:25565",DateTimeOffset.UtcNow.ToUnixTimeSeconds()+1800,"0123456789abcdef");
        var owner = new Window { Width=960, Height=600 }; owner.Show(); pump(50);
        var window = new DiscordInviteWindow(invite,new[] { ready,wrong,forge,missing,busy });
        var result = window.ShowDialog<InstanceModel?>(owner); pump(100);
        Check(!result.IsCompleted,"invitation never auto-launches or confirms on opening");
        var picker = window.GetVisualDescendants().OfType<ComboBox>().Single();
        Check(picker.Items.Count==1 && ReferenceEquals(picker.SelectedItem,ready),"only ready exact-version Fabric instance can be selected");
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==invite.Address),"server address visible before confirmation");
        var launch = window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Launch and join");
        var point=launch.TranslatePoint(new Point(),window)!.Value;
        Check(point.Y>=0 && point.Y+launch.Bounds.Height<=window.ClientSize.Height,"join confirmation fits inside the dialog");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output,"discord-server-invitation.png"));
        ready.IsRunning=true;
        launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump(50);
        Check(!result.IsCompleted,"instance becoming busy prevents confirmation");
        ready.IsRunning=false;
        launch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump(50);
        Check(result.IsCompletedSuccessfully && ReferenceEquals(result.Result,ready),"explicit confirmation returns only the chosen instance");
        var empty = new DiscordInviteWindow(invite,new[] { wrong,forge,missing,busy });
        var cancelled = empty.ShowDialog<InstanceModel?>(owner); pump(50);
        Check(!empty.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Launch and join").IsEnabled,"no compatible instance leaves Join disabled");
        empty.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump(20);
        Check(cancelled.IsCompletedSuccessfully && cancelled.Result==null,"cancel returns no launch target");
        var expired = new DiscordInviteWindow(invite with { Expires=DateTimeOffset.UtcNow.ToUnixTimeSeconds()-1 },new[] { ready });
        var expiredResult=expired.ShowDialog<InstanceModel?>(owner); pump(50);
        expired.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Launch and join").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pump(20);
        Check(!expiredResult.IsCompleted && expired.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("Invitation expired") == true),"expired invite cannot be confirmed");
        expired.Close(null); owner.Close(); pump(20);
        Console.WriteLine("DISCORD_INVITE_UI_OK (no Minecraft process or network connection)");
    }
    private static void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name); }
}
