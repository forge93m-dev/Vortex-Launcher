using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using VortexLauncher.Views;

internal static class AlignmentSettingsChecks
{
    private static readonly string[] Sections = { "settingsAppearance", "settingsGame", "settingsJava", "settingsUpdates" };
    internal static void Run(MainWindow window, SidebarFixture fixture, Action<int> pump, string output)
    {
        fixture.Select("Instances"); window.MouseMove(new Point(500,120)); pump(250);
        foreach(var size in new[]{new Size(960,600),new Size(1280,800)})
        {
            window.Width=size.Width; window.Height=size.Height; GameLibraryChecks.Switcher(window).IsChecked=true; pump(80);
            var search=window.FindControl<TextBox>("librarySearch")!;
            var watermark=search.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Name=="PART_Watermark");
            CenterY(watermark,search,"search watermark");
            Check(search.VerticalContentAlignment==Avalonia.Layout.VerticalAlignment.Center,"search input content uses vertical centering");
            var downloads=window.FindControl<Button>("downloadCenterButton")!;
            var downloadGlyph=downloads.GetVisualDescendants().OfType<PathIcon>().Single();
            var downloadLabel=downloads.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="Downloads");
            CenterY(downloadGlyph,downloads,"Downloads icon"); CenterY(downloadLabel,downloads,"Downloads label");
            foreach(var button in window.GetVisualDescendants().OfType<Button>().Where(b=>b.IsEffectivelyVisible && (b.Classes.Contains("HeroFavorite") || b.Classes.Contains("IconButton"))))
            {
                var glyph=button.GetVisualDescendants().OfType<Control>().SingleOrDefault(c=>c.Classes.Contains("CenteredGlyph"));
                if(glyph==null)continue;
                CenterY(glyph,button,"icon button vertical center");
                var point=glyph.TranslatePoint(new Point(glyph.Bounds.Width/2,glyph.Bounds.Height/2),button)!.Value;
                Check(Math.Abs(point.X-button.Bounds.Width/2)<=1,"icon button horizontal center");
            }
            window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(output,$"alignment-library-{(int)size.Width}.png"));
            GameLibraryChecks.Switcher(window).IsChecked=false; pump(40);
        }
        var favorite=window.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Toggle favorite");
        var wasFavorite=fixture.Library.SelectedInstance!.IsFavorite;
        favorite.Command!.Execute(favorite.CommandParameter); pump(40);
        Check(fixture.Library.SelectedInstance.IsFavorite!=wasFavorite && favorite.Content?.ToString()=="Favorite","labeled favorite action retains its command without an ambiguous standalone icon");
        favorite.Command.Execute(favorite.CommandParameter); pump(40);

        fixture.Select("Settings"); pump(250);
        var tabs=window.FindControl<TabControl>("SettingsTabs")!;
        var sections=Sections.Select(n=>window.FindControl<ScrollViewer>(n)!).ToArray();
        Check(sections.All(c=>c.GetVisualRoot()==window),"settings sections are mounted once, including hidden categories");
        Check(sections[0].GetVisualDescendants().OfType<ToggleSwitch>().Any(t => AutomationProperties.GetName(t) == "Share launcher activity"), "Discord opt-in is in Settings Appearance, not Profiles");
        Check(sections[0].GetVisualDescendants().OfType<ToggleSwitch>().Any(t => AutomationProperties.GetName(t) == "Receive server invitations"), "receiving invitations requires its own explicit opt-in");
        int attachments=0,detachments=0;
        foreach(var section in sections){section.AttachedToVisualTree+=(_,_)=>attachments++;section.DetachedFromVisualTree+=(_,_)=>detachments++;}
        var headers=tabs.Items.OfType<TabItem>().ToArray();
        for(int i=0;i<4;i++)
        {
            var header=headers[i];
            var point=header.TranslatePoint(new Point(header.Bounds.Width/2,header.Bounds.Height/2),window)!.Value;
            window.MouseMove(point); window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);pump(1);
            Check(tabs.SelectedIndex==i && sections[i].IsEffectivelyVisible && sections.Count(s=>s.IsEffectivelyVisible)==1,"settings mouse click switches the visible category without an animation wait");
        }
        var java=sections[2].GetVisualDescendants().OfType<TextBox>().Single(t=>t.AcceptsReturn);
        var original=java.Text; java.Text="-Xmx4G -Dfixture=true";pump(10);
        var watch=Stopwatch.StartNew();
        for(int i=0;i<24;i++){tabs.SelectedIndex=i%4;pump(1);}
        watch.Stop();
        Console.WriteLine($"SETTINGS_RETAINED_24_SWITCHES_MS={watch.Elapsed.TotalMilliseconds:F1}");
        Check(attachments==0 && detachments==0,"rapid settings navigation never detaches/rebuilds section controls");
        Check(java.Text=="-Xmx4G -Dfixture=true" && fixture.JvmArgs==java.Text,"editing settings survives rapid category changes");
        java.Text=original;pump(20);
        window.MouseMove(new Point(500,120));pump(220);
        foreach(var tab in headers.Where(t=>!t.IsSelected))
        {
            var chrome=tab.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="WorkspaceTabChrome");
            Check(chrome.Background is ISolidColorBrush brush && brush.Color.A==0,"inactive settings button never retains a stale highlighted background");
        }
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(output,"alignment-settings.png"));
        fixture.Select("Instances");pump(220);
        Console.WriteLine("ALIGNMENT_SETTINGS_OK; previews: "+output);
    }
    private static void CenterY(Control child,Control parent,string label)
    {
        var point=child.TranslatePoint(new Point(child.Bounds.Width/2,child.Bounds.Height/2),parent)!.Value;
        Check(Math.Abs(point.Y-parent.Bounds.Height/2)<=1,label+" is centered");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
}
