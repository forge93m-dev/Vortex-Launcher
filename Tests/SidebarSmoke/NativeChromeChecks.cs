using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using VortexLauncher.Views;

internal static class NativeChromeChecks
{
    internal static void Run()
    {
        // Native platform, but never Show(): no production VM, files or visible windows.
        var window = new Window { Width = 640, Height = 400, Content = new Border() };
        WindowTitleBar.Apply(window);
        var hwnd = window.TryGetPlatformHandle()!.Handle;
        var impl = (ITopLevelImpl)typeof(TopLevel).GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(window)!;
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            foreach (var transparency in new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None, WindowTransparencyLevel.Transparent })
            {
                var callback = (Action<WindowTransparencyLevel>)typeof(ITopLevelImpl).GetProperty("TransparencyLevelChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(impl)!;
                callback(transparency);
                if (window.ActualTransparencyLevel != transparency) throw new Exception("Transparency callback did not reach Avalonia");
                if (GetWindowRgn(hwnd, region) <= 0) throw new Exception("Normal HWND lost clipping when renderer reported " + transparency);
                var origin = new Point(); ClientToScreen(hwnd, ref origin); GetWindowRect(hwnd, out var outer); GetClientRect(hwnd, out var client);
                int x = origin.X - outer.Left, y = origin.Y - outer.Top;
                if (PtInRegion(region, x, y) || PtInRegion(region, x + client.Right - 1, y) || PtInRegion(region, x, y + client.Bottom - 1) || PtInRegion(region, x + client.Right - 1, y + client.Bottom - 1))
                    throw new Exception("Native region includes a square client corner");
                if (!PtInRegion(region, x + client.Right / 2, y + client.Bottom / 2)) throw new Exception("Native region removed window center");
                if (window.RenderScaling == 1 && (!PtInRegion(region, x + 3, y + 4) || !PtInRegion(region, x + client.Right - 4, y + 4) || !PtInRegion(region, x + 3, y + client.Bottom - 5) || !PtInRegion(region, x + client.Right - 4, y + client.Bottom - 5)))
                    throw new Exception("Native region clips the visible curved stroke");
                Console.WriteLine("PASS: hidden Avalonia HWND excludes all four corners with reported " + transparency);
            }
        }
        finally { DeleteObject(region); window.Close(); }
        Console.WriteLine("NATIVE_CHROME_OK");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
