using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VortexLauncher.Models;
using VortexLauncher.Views;

internal static class GridBrowsingChecks
{
    internal static void Run(Action<int> pump, string output)
    {
        var fixture = new SidebarFixture(500);
        var window = new MainWindow { DataContext = fixture, Width = 1280, Height = 800 };
        window.Show(); pump(250);
        var grid = window.FindControl<InstanceTileGrid>("instanceCards")!;
        var scroll = grid.GetVisualDescendants().OfType<ScrollViewer>().First();
        Border[] Tiles() => grid.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("LibraryTile")).ToArray();
        int count = Tiles().Length;
        Check(count > 5 && count < 80, "500 instances realize a bounded grid, not all artwork controls");
        Check(Tiles().Select(b => Math.Round(b.TranslatePoint(default, scroll)!.Value.X)).Distinct().Count() >= 3 &&
            Tiles().Select(b => Math.Round(b.TranslatePoint(default, scroll)!.Value.Y)).Distinct().Count() >= 2,
            "library wraps into multiple columns and rows instead of a horizontal strip");
        Check(scroll.Extent.Width <= scroll.Viewport.Width + 1 && scroll.Extent.Height > scroll.Viewport.Height,
            "only vertical scrolling is needed to browse the library");
        var tile = Tiles().First(b => ReferenceEquals(b.DataContext, fixture.Library.SelectedInstance));
        var play = tile.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Play instance");
        var label = play.GetVisualDescendants().OfType<TextBlock>().Single();
        Point center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), play)!.Value;
        Check(Math.Abs(center.Y - play.Bounds.Height / 2) < 2 && label.Bounds.Height <= play.Bounds.Height,
            "tile Play text is centered and not clipped by large theme padding");
        Point point = play.TranslatePoint(new Point(play.Bounds.Width / 2, play.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); pump(30);
        Check(fixture.LaunchCount == 1 && ReferenceEquals(fixture.LastLaunched, tile.DataContext), "Play on a tile launches that exact instance");
        grid.Focus();
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); pump(80);
        Check(grid.SelectedIndex == grid.Columns, "Down selects the next grid row, not the next tile");
        Point wheelPoint = grid.TranslatePoint(new Point(90, 90), window)!.Value;
        window.MouseWheel(wheelPoint, new Vector(0, -3), RawInputModifiers.None); pump(120);
        Check(scroll.Offset.Y > 0 && scroll.Offset.X == 0, "ordinary mouse wheel scrolls down over a tile without Shift or a horizontal gesture");
        window.MouseWheel(wheelPoint, new Vector(0, 3), RawInputModifiers.None); pump(120);
        Check(scroll.Offset.Y == 0, "mouse wheel scrolls back to the start");
        window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, ""); window.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.End, ""); pump(200);
        Check(grid.SelectedIndex == 499 && Tiles().Any(b => (b.DataContext as InstanceModel)?.Name == "Instance 499"), "End reaches and reveals the final instance");
        var lastTile = Tiles().Single(b => (b.DataContext as InstanceModel)?.Name == "Instance 499");
        var lastPosition = lastTile.TranslatePoint(default, scroll)!.Value;
        Check(lastPosition.Y >= -1 && lastPosition.Y + lastTile.Bounds.Height <= scroll.Viewport.Height + 1, "final instance is actually visible, not merely realized outside the viewport");
        Check(scroll.Offset.Y > 0 && scroll.Offset.X == 0, "keyboard navigation scrolls vertically without a hidden horizontal offset");
        Check(Math.Abs(scroll.Extent.Height - (Math.Ceiling(500.0 / grid.Columns) * 168 - 12)) < 1,
            "virtualized extent matches the exact row count without drifting or skipping items");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "collection-500-bottom.png"));
        scroll.Offset = default; pump(200);
        Check(Tiles().Any(b => (b.DataContext as InstanceModel)?.Name == "Instance 0"), "scrolling back restores the first instances");
        grid.SelectedIndex = 250; grid.ScrollIntoView(250); pump(120);
        var middleTile = Tiles().Single(b => (b.DataContext as InstanceModel)?.Name == "Instance 250");
        var middlePosition = middleTile.TranslatePoint(default, scroll)!.Value;
        Check(middlePosition.Y >= -1 && middlePosition.Y + middleTile.Bounds.Height <= scroll.Viewport.Height + 1,
            "jumping to a middle row reveals the correct tile without forcing an off-screen anchor");
        grid.SelectedIndex = 0; grid.ScrollIntoView(0); pump(120);
        fixture.Library.SelectedInstance = fixture.Instances[0]; pump(40);
        Check(Tiles().Count(b => b.Classes.Contains("selected")) == 1 &&
            ReferenceEquals(Tiles().Single(b => b.Classes.Contains("selected")).DataContext, fixture.Library.SelectedInstance),
            "recycled tiles retain exactly one correct selection highlight");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "collection-grid-1280.png"));
        window.Width = 960; window.Height = 600; pump(150);
        Check(grid.Columns >= 3 && Tiles().Length < 80 && scroll.Extent.Width <= scroll.Viewport.Width + 1,
            "minimum desktop window keeps three columns without horizontal scrolling");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "collection-grid-960.png"));
        window.Close(); fixture.Library.Dispose();
        Console.WriteLine("GRID_BROWSING_OK");
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
