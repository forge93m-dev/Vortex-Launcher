using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.VisualTree;
using VortexLauncher.Views;

internal static class NavigationMotionChecks
{
    internal static void Run(MainWindow window, SidebarFixture fixture, Action<int> pump, string output)
    {
        fixture.Select("Instances"); window.MouseMove(new Point(500, 120)); pump(250);
        foreach (var name in new[] { "libraryNav", "modpacksNav", "profilesNav", "settingsNav" })
        {
            var button = window.FindControl<Button>(name)!;
            var hover = Part(button, "NavHover");
            var bounds = button.Bounds;
            Check(Math.Abs(hover.Bounds.Height - button.Bounds.Height) < .01 && Math.Abs(hover.Bounds.Width - button.Bounds.Width) < .01, "vertical highlight fills the entire navigation strip");
            var indicator = Part(button, "NavIndicator");
            Check(indicator.Width == 3 && indicator.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left && indicator.Bounds.Height >= button.Bounds.Height / 2, "vertical marker sits on the rail item's left edge");
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
            var labelPosition = label.TranslatePoint(default, window)!.Value;
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); pump(35);
            Check(hover.Opacity > 0 && hover.Opacity < 1, "top tab hover has a visible intermediate frame: " + name);
            pump(180); Check(hover.Opacity > .99, "top tab hover reaches its highlighted state");
            Check(button.GetVisualDescendants().OfType<ContentPresenter>().All(c => c.Background is not ISolidColorBrush background || background.Color.A == 0), "hover has no second painted content layer");
            Check(button.Bounds == bounds && button.RenderTransform!.Value.IsIdentity && label.TranslatePoint(default, window)!.Value == labelPosition, "top tab labels and hit targets stay stationary");
            window.MouseDown(point, Avalonia.Input.MouseButton.Left); pump(30);
            Check(hover.RenderTransform!.Value.M11 > .96 && hover.RenderTransform.Value.M11 < 1, "press surface moves through an animated intermediate frame");
            pump(150);
            Check(Math.Abs(hover.Opacity - .55) < .01 && button.RenderTransform!.Value.IsIdentity, "top tab has separate pressed feedback without scaling");
            Check(Math.Abs(hover.RenderTransform!.Value.M11 - .96)<.001 && Math.Abs(hover.RenderTransform.Value.M22 - .9)<.001, "press gently compresses only the highlight, not the label");
            Check(button.GetVisualDescendants().OfType<ContentPresenter>().All(c => c.Background is not ISolidColorBrush background || background.Color.A == 0), "pressed state keeps only the single outer highlight");
            window.MouseUp(point, Avalonia.Input.MouseButton.Left); pump(40);
            Check(button.Classes.Contains("active"), "click still selects the intended tab");
            var pageName=name switch { "libraryNav"=>"instancesPage", "modpacksNav"=>"modpacksPage", "profilesNav"=>"accountsPage", _=>"settingsPage" };
            var page=window.FindControl<Grid>(pageName)!;
            if(name!="libraryNav") Check(page.IsEffectivelyVisible && page.Opacity>.81 && page.Opacity<1, "opening a new tab has a visible fade without a blank frame");
            Check(indicator.Opacity == 1 && window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("LibraryNav") && !b.Classes.Contains("active")).All(b => Part(b, "NavIndicator").Opacity == 0), "only the selected rail item has a visible marker");
            pump(220);
            Check(page.Opacity==1 && hover.RenderTransform!.Value.IsIdentity, "tab opening and press release both settle cleanly");
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, "navigation-full-height-" + name + ".png"));
            window.MouseMove(new Point(500, 120)); pump(200); Check(Math.Abs(hover.Opacity - .6) < .01, "selected tab keeps its subtle full-height highlight");
            fixture.Select(name == "libraryNav" ? "Settings" : "Instances"); pump(220);
            Check(hover.Opacity == 0, "inactive tab highlight fades out cleanly");
            Check(indicator.Opacity == 0, "outgoing underline disappears without a duplicate");
        }
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "navigation-animated.png"));
        fixture.Select("Instances"); pump(220);
        Console.WriteLine("NAVIGATION_MOTION_OK");
    }
    private static Border Part(Button button, string name) => button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
