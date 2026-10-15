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
        // The headless backend has no OS compositor. Exercise the exact rounded
        // geometry on an alpha surface, then test the unsupported-backend fallback.
        var shell = new Border { Child = new Border { Background = Brushes.Magenta } };
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
        window.Show(); pump(550);
        Check(window.Content is Border root && RoundedClip.GetIsEnabled(root) && root.Clip != null, "main window uses the rounded content clip");
        var scroller = window.FindControl<ScrollViewer>("instanceCardsScroll")!;
        // ItemsRepeater may keep recycled containers parked at -10000. They are
        // not on-screen cards and cannot be selected as hover targets.
        var card = VisibleCards(window, scroller).OrderBy(c => c.TranslatePoint(default, scroller)!.Value.Y)
            .ThenBy(c => c.TranslatePoint(default, scroller)!.Value.X).First();
        Hover(window, card, pump);
        var top = card.TranslatePoint(default, scroller)!.Value;
        Console.WriteLine($"Hover geometry: pointer={card.IsPointerOver}, top={top}, viewport={scroller.Viewport}, offset={scroller.Offset}, margin={((Border)card.Parent!).Margin}");
        Check(card.IsPointerOver && top.Y >= 1.5 && top.X >= -0.5, $"first-row hover lift stays below the scroll viewport edge (pointer={card.IsPointerOver}, top={top})");
        Check(card.Bounds.Size == new Size(272, 154) && RoundedClip.GetIsEnabled(card) && card.Clip != null, "card dimensions stay compact and opaque covers share the rounded clip");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "library-hover-first-row.png"));
        window.MouseMove(new Point(700, 80)); pump(220);
        scroller.Offset = new Vector(0, scroller.Extent.Height); pump(250);
        var last = VisibleCards(window, scroller).Single(c => (c.DataContext as InstanceModel)?.Name == "Instance 35");
        Hover(window, last, pump);
        var bottom = last.TranslatePoint(new Point(last.Bounds.Width, last.Bounds.Height), scroller)!.Value;
        Check(last.IsPointerOver && bottom.Y <= scroller.Viewport.Height + 0.5 && bottom.X <= scroller.Viewport.Width + 0.5, "last-row and right-edge card animations remain inside the viewport");
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
    {
        // Avalonia 11.0.6 CopyPixels ignores non-zero sourceRect offsets.
        int stride = bitmap.PixelSize.Width * 4, size = stride * bitmap.PixelSize.Height;
        IntPtr pixel = Marshal.AllocHGlobal(size);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pixel, size, stride); return Marshal.ReadByte(pixel, y * stride + x * 4 + 3); }
        finally { Marshal.FreeHGlobal(pixel); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}

