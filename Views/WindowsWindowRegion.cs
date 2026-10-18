using System;
using System.Runtime.InteropServices;

namespace VortexLauncher.Views;

// Cut out actual HWND corners for both opaque and nominally transparent renderers.
internal static class WindowsWindowRegion
{
    internal static bool Apply(IntPtr hwnd, bool rounded, double scale)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return false;
        if (!rounded) return SetWindowRgn(hwnd, IntPtr.Zero, true) != 0;
        if (!GetWindowRect(hwnd, out var bounds)) return false;
        if (!GetClientRect(hwnd, out var client)) return false;
        var origin = new Point();
        if (!ClientToScreen(hwnd, ref origin)) return false;
        int left = origin.X - bounds.Left, top = origin.Y - bounds.Top;
        int width = client.Right - client.Left, height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0 || !double.IsFinite(scale) || scale <= 0) return false;
        int diameter = (int)Math.Clamp(Math.Round(RoundedWindowChrome.Radius * 2 * scale), 1, Math.Min(width, height));
        // Win32 can retain an invisible resize frame. Clip the actual Avalonia
        // client surface, not that frame (otherwise the visible corners stay square).
        IntPtr region = CreateRoundRectRgn(left, top, left + width + 1, top + height + 1, diameter, diameter);
        if (region == IntPtr.Zero) return false;
        // Successful SetWindowRgn transfers ownership to Windows; it also disposes
        // the previous region. Delete only the region that Windows rejected.
        if (SetWindowRgn(hwnd, region, true) != 0) return true;
        DeleteObject(region);
        return false;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
}
