using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using VortexLauncher.Models;
using VortexLauncher.Views;

internal static class ChromeAndHoverChecks
{
    internal static void Run(Action<int> pump, string output)
    {
        WindowsRegionChecks.Run();
        // The headless backend has no OS compositor. Exercise the exact rounded
        // geometry on an alpha surface, then test the unsupported-backend fallback.
        var shell = new Border { BorderBrush = Brushes.Lime, Child = new Border { Background = Brushes.Magenta } };
        var host = new Window { Width = 240, Height = 140, Content = shell };
        RoundedWindowChrome.Attach(host, shell);
        Check(host.Background is ISolidColorBrush { Color.A: 0 } && host.TransparencyLevelHint.SequenceEqual(new[] { WindowTransparencyLevel.Transparent }), "custom chrome requests per-pixel transparency without blur/acrylic");
        Check(host.TransparencyBackgroundFallback is ISolidColorBrush fallback && shell.Background is ISolidColorBrush background && fallback.Color == background.Color, "unsupported transparency falls back to the theme, not a black/white patch");
        host.TransparencyBackgroundFallback = Brushes.Transparent; // Alpha test surface only.
        host.Show(); pump(50);
        RoundedWindowChrome.UpdateShape(shell, WindowState.Normal, transparent: true); pump(40);
        using (var frame = host.CaptureRenderedFrame()!)
        {
            frame.Save(Path.Combine(output, "rounded-alpha-corners.png"));
            Check(new[] { (0, 0), (frame.PixelSize.Width - 1, 0), (0, frame.PixelSize.Height - 1), (frame.PixelSize.Width - 1, frame.PixelSize.Height - 1) }
                .All(p => Alpha(frame, p.Item1, p.Item2) == 0), "all four outer corners are genuinely transparent even with an opaque child background");
            Check(Alpha(frame, frame.PixelSize.Width / 2, frame.PixelSize.Height / 2) == 255, "rounded window body remains fully opaque");
            Check(new[] { (3, 4), (frame.PixelSize.Width - 4, 4), (3, frame.PixelSize.Height - 5), (frame.PixelSize.Width - 4, frame.PixelSize.Height - 5) }
                .All(p => Green(frame, p.Item1, p.Item2) > 100), "opaque content does not overwrite the curved border at any corner");
        }
        RoundedWindowChrome.UpdateShape(shell, WindowState.Maximized, transparent: true);
        Check(shell.CornerRadius == new CornerRadius(0) && shell.BorderThickness == new Thickness(0), "maximized chrome removes rounded gaps and the outer border");
        RoundedWindowChrome.UpdateShape(shell, WindowState.Normal, transparent: true);
        Check(shell.CornerRadius == new CornerRadius(12) && shell.BorderThickness == new Thickness(1), "restored windows return to a subtle 12-pixel corner radius");
        RoundedWindowChrome.UpdateShape(shell, WindowState.Normal, transparent: false);
        Check(shell.CornerRadius == new CornerRadius(0), "a compositor without transparency uses a solid rectangle rather than fake dark corners");
        host.Close();

        var fixture = new SidebarFixture(36);
        var window = new MainWindow { DataContext = fixture, Width = 960, Height = 600 };
        window.Show(); pump(400);
        GameLibraryChecks.Switcher(window).IsChecked = true; pump(450);
        Check(window.Content is Border root && RoundedClip.GetIsEnabled(root) && root.Clip != null, "main window uses the rounded content clip");
        var scroller = window.FindControl<InstanceTileGrid>("instanceCards")!.GetVisualDescendants().OfType<ScrollViewer>().First();
        var card = VisibleCards(window, scroller).OrderBy(c => c.TranslatePoint(default, scroller)!.Value.Y).First();
        var before = card.TranslatePoint(default, scroller)!.Value;
        Hover(window, card, pump);
        var top = card.TranslatePoint(default, scroller)!.Value;
        Check(card.IsPointerOver && Math.Abs(before.Y-top.Y)<.1 && top.Y>=-.5, "row hover never lifts or clips the top of the collection (over " + card.IsPointerOver + ", before " + before.Y + ", top " + top.Y + ")");
        Check(card.Bounds.Height==156 && card.GetVisualDescendants().OfType<Border>().Any(b=>RoundedClip.GetIsEnabled(b) && b.Clip!=null), "compact cover thumbnails retain an explicit rounded clip");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "library-hover-first-row.png"));
        window.MouseMove(new Point(700, 80)); pump(120);
        scroller.Offset = new Vector(0, scroller.Extent.Height); pump(250);
        var last = VisibleCards(window, scroller).Single(c => (c.DataContext as InstanceModel)?.Name == "Instance 35");
        Hover(window, last, pump);
        var bottom = last.TranslatePoint(new Point(last.Bounds.Width, last.Bounds.Height), scroller)!.Value;
        Check(last.IsPointerOver && bottom.Y <= scroller.Viewport.Height + .5 && bottom.X <= scroller.Viewport.Width + .5, "last-row hover stays inside the scroll viewport");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "library-hover-last-row.png"));
        window.Close(); fixture.Library.Dispose();
        Console.WriteLine("CHROME_HOVER_CHECKS_OK");
    }
    private static IEnumerable<Border> Cards(Window window) => window.GetVisualDescendants().OfType<Border>().Where(c => c.Classes.Contains("InstanceCard"));
    private static IEnumerable<Border> VisibleCards(Window window, ScrollViewer scroller) => Cards(window).Where(card =>
    {
        var point = card.TranslatePoint(default, scroller)!.Value;
        return card.IsEffectivelyVisible && point.X >= -0.5 && point.Y >= -0.5 && point.X < scroller.Viewport.Width && point.Y < scroller.Viewport.Height;
    });
    private static void Hover(Window window, Border card, Action<int> pump)
    {
        var point = card.TranslatePoint(new Point(24, 24), window)!.Value;
        window.MouseMove(point); pump(250);
    }
    private static byte Alpha(Bitmap bitmap, int x, int y)
        => Channel(bitmap, x, y, 3);
    private static byte Green(Bitmap bitmap, int x, int y)
        => Channel(bitmap, x, y, 1);
    private static byte Channel(Bitmap bitmap, int x, int y, int channel)
    {
        // Avalonia 11.0.6 CopyPixels ignores non-zero sourceRect offsets.
        int stride = bitmap.PixelSize.Width * 4, size = stride * bitmap.PixelSize.Height;
        IntPtr pixel = Marshal.AllocHGlobal(size);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pixel, size, stride); return Marshal.ReadByte(pixel, y * stride + x * 4 + channel); }
        finally { Marshal.FreeHGlobal(pixel); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
