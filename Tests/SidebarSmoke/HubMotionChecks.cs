using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using VortexLauncher.Views;

internal static class HubMotionChecks
{
    internal static void Run(MainWindow window, SidebarFixture fixture, Action<int> pump, string output)
    {
        window.Width = 1280; window.Height = 800; fixture.Select("Instances"); window.MouseMove(new Point(500, 120)); pump(350);
        var switcher = GameLibraryChecks.Switcher(window);
        var browser = window.FindControl<Border>("libraryBrowser")!;
        var header = window.FindControl<Grid>("browserHeader")!;
        var chevron = switcher.GetVisualDescendants().OfType<PathIcon>().Single();

        switcher.IsChecked = true; pump(45);
        var tiles = Tiles(window);
        Check(browser.IsEffectivelyVisible && browser.Opacity == 1 && tiles.Length >= 3, "the collection opens at once and lays out its tiles");
        Check(header.Opacity < 1 && Shift(header) > 0, "the collection heading rises into place");
        Check(tiles[^1].Opacity == 0 && Shift(tiles[^1]) > 0, "later tiles wait their turn below their place without flashing first");
        Check(Math.Abs(chevron.RenderTransform!.Value.M11) < .99, "the switcher arrow turns through an intermediate frame");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "motion-collection-midframe.png"));
        pump(140);
        Check(tiles[0].Opacity > tiles[^1].Opacity, "tiles appear one after another, first to last");
        pump(400);
        Check(header.Opacity == 1 && header.RenderTransform == null && tiles.All(t => t.Opacity == 1 && t.RenderTransform == null), "heading and tiles settle exactly in place");
        Check(chevron.RenderTransform!.Value.M11 < -.99, "the switcher arrow ends pointing up while the collection is open");
        switcher.IsChecked = false; pump(80);

        fixture.Library.SelectedInstance = fixture.Instances[1]; pump(120);
        var cover = Part(window, "heroCover");
        var card = Part(window, "selectedInstanceBar");
        Check(cover.Opacity < 1 && cover.Opacity >= .44, "a newly featured cover cross-fades without flashing to black");
        Check(card.Opacity < 1 && Shift(card) > 0, "the title card rises after the cover");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "motion-cover-midframe.png"));
        pump(450);
        Check(cover.Opacity == 1 && card.Opacity == 1 && card.RenderTransform == null, "the featured cover and title card settle cleanly");

        var play = GameLibraryChecks.Play(window);
        var pulse = window.FindControl<Border>("playPulse")!;
        play.Command!.Execute(play.CommandParameter);
        play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); pump(70);
        Check(pulse.Opacity > 0 && pulse.RenderTransform is ScaleTransform { ScaleX: > 1 } && play.RenderTransform!.Value.IsIdentity, "pressing PLAY sends a ring out from behind the button without shrinking it");
        pump(600);
        Check(pulse.Opacity == 0 && pulse.RenderTransform is ScaleTransform { ScaleX: 1 }, "the PLAY ring fades away completely");

        fixture.Select("Settings"); pump(40);
        var settings = window.FindControl<Grid>("settingsPage")!;
        Check(settings.RenderTransform != null && settings.RenderTransform.Value.M31 > 0 && settings.RenderTransform.Value.M31 <= UiMotion.PageSlide, "a new section slides in a short way from the side");
        pump(320);
        Check(settings.RenderTransform == null && settings.Opacity == 1, "the section settles in place");
        fixture.Select("Instances"); pump(320);

        UiMotion.Enabled = false;
        switcher.IsChecked = true; pump(25);
        Check(browser.IsEffectivelyVisible && Tiles(window).All(t => t.Opacity == 1 && t.RenderTransform == null), "with interface animations off the collection opens instantly");
        switcher.IsChecked = false; fixture.Library.SelectedInstance = fixture.Instances[0]; pump(40);
        Check(Part(window, "selectedInstanceBar").Opacity == 1, "with interface animations off a new cover appears instantly");
        play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); pump(30);
        Check(pulse.Opacity == 0, "with interface animations off PLAY sends no ring");
        UiMotion.Enabled = true; pump(60);
        Check(UiMotion.ActiveCount == 0, "hub animations leave no pending work");
        Console.WriteLine("HUB_MOTION_OK");
    }
    private static Border[] Tiles(Window window) => window.FindControl<InstanceTileGrid>("instanceCards")!.GetVisualDescendants().OfType<Border>()
        .Where(b => b.Classes.Contains("LibraryTile")).OrderBy(b => b.Bounds.Y).ThenBy(b => b.TranslatePoint(default, window)!.Value.X).ToArray();
    private static double Shift(Control control) => control.RenderTransform?.Value.M32 ?? 0;
    private static Border Part(Window window, string name) => window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
