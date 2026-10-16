using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using VortexLauncher.Views;

internal static class WorkspaceMotionChecks
{
    internal static void Run(MainWindow window, SidebarFixture fixture, Action<int> pump, string output)
    {
        foreach(var name in new[]{"instancesPage","modpacksPage","accountsPage","settingsPage"})
            Check(SurfaceMotion.GetKind(window.FindControl<Grid>(name)!)=="Page", "shared entrances are enabled for " + name);
        fixture.Select("Settings"); pump(300);
        var tabs=window.FindControl<TabControl>("SettingsTabs")!;
        Check(tabs.TabStripPlacement==Avalonia.Controls.Dock.Left && tabs.ItemCount==4, "settings use separate appearance, game, Java and update workspaces");
        foreach(var dimensions in new[]{new Size(960,600), new Size(1280,800)})
        {
            window.Width=dimensions.Width;window.Height=dimensions.Height;
            for(int i=0;i<4;i++)
            {
                tabs.SelectedIndex=i;pump(300);
                Check(tabs.SelectedContent is Control content && content.IsEffectivelyVisible, "settings category is visible: "+i);
                window.CaptureRenderedFrame()!.Save(Path.Combine(output,$"workspace-settings-{i}-{(int)dimensions.Width}.png"));
            }
            fixture.Select("Accounts");pump(300);
            var addProfile=window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"+  Add profile"));
            var profileBottom=addProfile.TranslatePoint(new Point(addProfile.Bounds.Width,addProfile.Bounds.Height),window)!.Value;
            Check(profileBottom.Y<window.Bounds.Height-42 && profileBottom.X<window.Bounds.Width,"profile creation stays above the footer at "+dimensions);
            window.CaptureRenderedFrame()!.Save(Path.Combine(output,$"workspace-profiles-{(int)dimensions.Width}.png"));
            fixture.Select("Modpacks");pump(300);window.CaptureRenderedFrame()!.Save(Path.Combine(output,$"workspace-discover-{(int)dimensions.Width}.png"));
            fixture.Select("Settings");pump(300);
        }
        window.Width=1280;window.Height=800;fixture.Select("Instances");pump(350);
        var play=GameLibraryChecks.Play(window);
        Check(play.Transitions?.OfType<TransformOperationsTransition>().Any(t=>t.Duration.TotalMilliseconds>=100)==true,"Play has a real non-zero transform transition");
        var center=play.TranslatePoint(new Point(play.Bounds.Width/2,play.Bounds.Height/2),window)!.Value;
        window.MouseMove(center);pump(260);
        Check(play.RenderTransform!.Value.M11>1.01, "Play visibly scales up on hover");
        window.MouseDown(center,Avalonia.Input.MouseButton.Left);pump(220);
        Check(play.RenderTransform!.Value.M11<1,"pressed buttons have a separate compressed state");
        window.MouseUp(center,Avalonia.Input.MouseButton.Left);window.MouseMove(new Point(20,20));pump(280);
        Check(Math.Abs(play.RenderTransform!.Value.M11-1)<.002,"hover/press animation returns cleanly to rest");
        var target=new Border { Width=160,Height=40,Background=Brushes.SlateBlue };
        var host=new Window {Width=300,Height=150,Content=target};host.Show();pump(30);
        var first=UiMotion.RevealAsync(target,default,100);var replacement=UiMotion.RevealAsync(target,default);
        pump(450);
        Check(first.IsCompletedSuccessfully && replacement.IsCompletedSuccessfully && target.Opacity==1 && Math.Abs(target.RenderTransform!.Value.M32)<.01,"rapid replacement coalesces entrances without stale opacity/translation");
        UiMotion.Enabled=false;var disabled=UiMotion.RevealAsync(target,default);
        Check(disabled.IsCompletedSuccessfully && target.Opacity==1,"reduced-motion preference skips entrances immediately");UiMotion.Enabled=true;
        var children=Enumerable.Range(0,80).Select(_=>new Border {Height=3}).ToArray();
        var stack=new StackPanel();foreach(var child in children)stack.Children.Add(child);host.Content=stack;pump(30);
        var jobs=children.Select(c=>UiMotion.RevealAsync(c,default)).ToArray();
        Check(UiMotion.ActiveCount<=24,"simultaneous animation work is bounded even for a large batch");pump(450);
        Check(jobs.All(t=>t.IsCompletedSuccessfully),"batched animations complete without persistent loops");
        host.Close();pump(80);
        Console.WriteLine("WORKSPACE_MOTION_UI_OK");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
}
