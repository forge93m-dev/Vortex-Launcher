using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using VortexLauncher.Core.Theme;
using VortexLauncher.Models;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

internal static class GameLibraryChecks
{
    internal static void Run(MainWindow window, SidebarFixture fixture, Action<int> pump, string output)
    {
        ThemeManager.SetTheme("Midnight", "#A78BFA", "#15161C", "#1D1F27", "#252833", "#2E3140", "#ECEDF3", "#A0A4B3");
        var list = window.FindControl<InstanceTileGrid>("instanceCards")!;
        var preview = window.FindControl<ContentControl>("librarySelection")!;
        var switcher = Switcher(window);
        var browser = window.FindControl<Border>("libraryBrowser")!;
        var navs = new[] { ("libraryNav", "Instances"), ("modpacksNav", "Modpacks"), ("profilesNav", "Accounts"), ("settingsNav", "Settings") };
        foreach (var (name, tab) in navs)
        {
            var button = window.FindControl<Button>(name)!;
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
            Check(label.IsEffectivelyVisible && label.Opacity == 1 && button.Focusable, "rail navigation labels are always visible: " + name);
            var before = button.Bounds;
            Move(window, button); pump(180);
            Check(button.Bounds == before, "navigation hover does not resize/reflow: " + name);
            Click(window, button); pump(60);
            Check(fixture.ActiveTab == tab && button.Classes.Contains("active"), "rail navigation routes to " + tab);
        }
        var profiles = window.FindControl<Button>("profilesNav")!;
        profiles.Focus(NavigationMethod.Tab); pump(30);
        window.KeyPress(Key.Return, RawInputModifiers.None, PhysicalKey.Enter, "\r"); window.KeyRelease(Key.Return, RawInputModifiers.None, PhysicalKey.Enter, "\r"); pump(50);
        Check(fixture.ActiveTab == "Accounts", "keyboard Enter activates the new navigation");
        fixture.Select("Instances"); pump(100);
        Click(window,window.FindControl<Button>("downloadCenterButton")!); pump(30);
        Check(fixture.DownloadsOpenCount==1, "persistent download button routes to the shared downloads command");
        Check(fixture.Library.SelectedInstance == fixture.Instances[0] && preview.Content == fixture.Instances[0] && preview.IsEffectivelyVisible && !browser.IsEffectivelyVisible, "first instance is selected and featured on the cover automatically");
        Click(window, switcher); pump(80);
        Check(switcher.IsChecked == true && browser.IsEffectivelyVisible, "instance switcher opens the full collection over the cover");
        list.SelectedIndex = 1; pump(80);
        Check(fixture.Library.SelectedInstance == fixture.Instances[1] && preview.Content == fixture.Instances[1], "list selection updates the cover and metadata");
        var selectedRow=list.GetVisualDescendants().OfType<Border>().Single(b=>b.Classes.Contains("LibraryTile") && ReferenceEquals(b.DataContext, fixture.Instances[1]));
        Click(window,selectedRow); pump(60);
        Check(switcher.IsChecked == false && !browser.IsEffectivelyVisible && preview.IsEffectivelyVisible && preview.Content == fixture.Instances[1], "choosing a tile closes the collection and features it on the cover");
        Click(window, switcher); pump(60);
        list.Focus();pump(20);
        window.KeyPress(Key.Right,RawInputModifiers.None,PhysicalKey.ArrowRight, ""); window.KeyRelease(Key.Right,RawInputModifiers.None,PhysicalKey.ArrowRight, ""); pump(40);
        Check(list.SelectedIndex==2 && preview.Content==fixture.Instances[2] && browser.IsEffectivelyVisible, $"arrow keys select an instance and keep the collection open (index={list.SelectedIndex})");
        window.KeyPress(Key.Escape,RawInputModifiers.None,PhysicalKey.Escape, ""); window.KeyRelease(Key.Escape,RawInputModifiers.None,PhysicalKey.Escape, ""); pump(40);
        Check(switcher.IsChecked == false && !browser.IsEffectivelyVisible, "Escape closes the collection and returns to the cover");
        list.SelectedIndex=1; pump(40);
        var play = Play(window);
        Check(play.CommandParameter == fixture.Instances[1], "Play targets the featured instance, not the first row");
        Click(window, play); pump(50);
        Check(fixture.LaunchCount == 1 && fixture.LastLaunched == fixture.Instances[1], "featured Play retains the launch command and selected target");
        fixture.Instances[1].IsRunning = true; pump(30); Check(!Play(window).IsEnabled, "running instances cannot be launched twice");
        fixture.Instances[1].IsRunning = false;
        fixture.Instances[1].HasInterruptedOperation = true; pump(30); Check(!Play(window).IsEnabled, "interrupted instances remain guarded");
        fixture.Instances[1].HasInterruptedOperation = false;
        var star = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Toggle favorite");
        Click(window, star); pump(100);
        Check(fixture.Instances[1].IsFavorite && fixture.Library.SelectedInstance == fixture.Instances[1], "pinning reorders the collection without changing the featured instance");
        Click(window, switcher); pump(60);
        Click(window, window.FindControl<Button>("favoritesFilter")!); pump(80);
        Check(fixture.Library.VisibleInstances.Count == 1 && fixture.Library.SelectedInstance == fixture.Instances[1], "favorites filter retains the selected favorite");
        fixture.Library.SearchQuery = "nothing matches"; pump(80);
        Check(!fixture.Library.HasSelection && preview.Content == null && !preview.IsEffectivelyVisible && window.FindControl<StackPanel>("libraryEmptyState")!.IsEffectivelyVisible, "no matches clears the old preview and shows a useful empty state");
        switcher.IsChecked = false; pump(40);
        Check(browser.IsEffectivelyVisible, "the collection stays open while nothing can be featured on the cover");
        Check(!Play(window).IsEnabled, "PLAY is disabled when no instance is featured");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "game-library-no-results.png"));
        fixture.Library.ClearFiltersCommand.Execute(null); fixture.Instances[1].IsFavorite = false; pump(80);
        fixture.Library.SelectedInstance = fixture.Instances[0]; pump(80);
        Check(!browser.IsEffectivelyVisible && preview.IsEffectivelyVisible, "clearing filters returns to the cover");
        foreach (var size in new[] { new Size(960,600), new Size(1280,800), new Size(1600,1000) })
        {
            window.Width=size.Width; window.Height=size.Height; pump(180);
            foreach (var name in navs.Select(n=>n.Item1).Concat(new[] { "wardrobeNav", "activePlayerCard", "instanceSwitcher", "downloadCenterButton" }))
                Fits(window, window.FindControl<Control>(name)!, name + " fits " + size);
            Fits(window, Play(window), "featured Play fits " + size);
            Check(Play(window).Bounds.Width >= 240 && Play(window).Bounds.Height >= 56, "PLAY is the largest action in the hub at " + size);
            var clientButton = window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="featuredClientButton");
            Check(clientButton.IsEffectivelyVisible && clientButton.CommandParameter==fixture.Library.SelectedInstance && clientButton.Content?.ToString()?.Contains("Vortex Client")==true,"client install/update action is visible on the selected instance at " + size);
            Fits(window,clientButton,"client install/update action fits " + size);
            int beforeClientAction=fixture.ClientActionCount;
            Click(window,clientButton);pump(25);
            Check(fixture.ClientActionCount==beforeClientAction+1 && fixture.ClientActionTarget==fixture.Library.SelectedInstance,"prominent client button routes to the selected instance");
            fixture.Library.SelectedInstance!.IsRunning=true;pump(25);
            Check(!clientButton.IsEnabled,"client installation stays disabled while Minecraft is running");
            fixture.Library.SelectedInstance.IsRunning=false;pump(25);
            var card = window.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="selectedInstanceBar");
            Fits(window, card, "cover title card fits " + size);
            var playBar = window.FindControl<Border>("playBar")!;
            Fits(window, playBar, "play bar fits " + size);
            Check(playBar.Bounds.Height < 110 && preview.Bounds.Height >= 300, "the cover takes most of the window above a compact play bar at " + size);
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"game-library-{(int)size.Width}.png"));
            switcher.IsChecked = true; pump(40);
            list.GetVisualDescendants().OfType<ScrollViewer>().First().Offset=default; pump(180);
            Check(list.Bounds.Height >= 150, "the opened collection leaves room for browsing at " + size);
            Check(list.Bounds.Width > 800 && list.GetVisualDescendants().OfType<Border>().Where(b=>b.Classes.Contains("LibraryTile")).All(b=>b.Bounds.Height == 156), "responsive collection spans the window with compact tiles at " + size);
            var thumbnails = list.GetVisualDescendants().OfType<Border>().Where(b=>b.Classes.Contains("InstanceThumbnail")).ToArray();
            Check(thumbnails.Length > 0 && thumbnails.All(b=>b.Bounds.Width == 48 && b.Bounds.Height == 48), "instance thumbnails remain square at " + size);
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"game-library-browser-{(int)size.Width}.png"));
            switcher.IsChecked = false; pump(60);
        }
        window.Width=1280; window.Height=800; fixture.Library.SelectedInstance=fixture.Instances[2]; pump(120);
        Check(preview.Content==fixture.Instances[2], "long instance names keep the correct featured target");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output,"game-library-long-name.png"));
        fixture.Instances[2].CoverImagePath=Path.Combine(output,"does-not-exist.png"); pump(120);
        Check(window.GetVisualDescendants().OfType<WorldCover>().Any(c=>c.IsEffectivelyVisible && ReferenceEquals(c.DataContext, fixture.Instances[2])), "missing custom covers retain original built-in artwork");
        fixture.Instances[2].CoverImagePath=Path.Combine(output,"game-library-1280.png"); pump(300);
        var images = window.GetVisualDescendants().OfType<VortexLauncher.Converters.AsyncImage>().ToArray();
        Check(images.Any(i=>i.DecodeWidth==1600 && i.Source!=null) && images.Any(i=>i.DecodeWidth==128 && i.Source!=null), "custom cover fills the hub cover and the switcher thumbnail");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output,"game-library-custom-cover.png"));
        fixture.Instances[2].CoverImagePath=""; fixture.Library.SelectedInstance=fixture.Instances[0]; pump(80);
        SelectionModelChecks();
        window.CaptureRenderedFrame()!.Save(Path.Combine(output,"game-library-preview.png"));
        Console.WriteLine("GAME_LIBRARY_UI_OK");
    }
    private static void SelectionModelChecks()
    {
        var a=new InstanceModel {Name="Alpha",Version="26.3",Loader="Fabric"};
        var b=new InstanceModel {Name="Beta",Version="1.20.1",Loader="Forge"};
        var source=new ObservableCollection<InstanceModel>{a,b};
        using var library=new InstanceLibraryViewModel(source);
        library.SelectedInstance=b; a.IsFavorite=true;
        Check(library.SelectedInstance==b, "model preserves selection across favorite sorting");
        library.SearchQuery="fabric"; Check(library.SelectedInstance==a, "filter selects a visible instance rather than a hidden one");
        a.Loader="Quilt"; Check(library.IsEmpty && !library.HasSelection, "live loader changes reconcile selection");
        library.ClearFiltersCommand.Execute(null); library.SelectedInstance=b; source.Remove(b);
        Check(library.SelectedInstance==a, "deleting the selection moves the preview to a surviving instance");
        source.Clear(); Check(library.SelectedInstance==null && library.IsEmpty, "clearing the library clears selection");
        using(library.DeferRefresh()){source.Add(b);source.Add(a);}
        Check(library.HasSelection && library.VisibleInstances.Count==2, "batched startup load restores a valid selection");
    }
    internal static Button Play(Window window) => window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name == "featuredPlayButton");
    internal static ToggleButton Switcher(Window window) => window.FindControl<ToggleButton>("instanceSwitcher")!;
    private static void Fits(Window window,Control control,string label)
    {
        var p=control.TranslatePoint(default,window)!.Value;
        Check(p.X>=-.5 && p.Y>=-.5 && p.X+control.Bounds.Width<=window.Bounds.Width+.5 && p.Y+control.Bounds.Height<=window.Bounds.Height+.5, label);
    }
    private static void Move(Window window,Control control) => window.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)!.Value);
    private static void Click(Window window,Control control){control.BringIntoView();Avalonia.Threading.Dispatcher.UIThread.RunJobs();Move(window,control); var p=control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)!.Value; window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);}
    private static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
}
