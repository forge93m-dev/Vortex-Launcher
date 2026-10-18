using System.Runtime.InteropServices;
using VortexLauncher.Views;

internal static class WindowsRegionChecks
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: Win32 corner region requires Windows"); return; }
        // Hidden, fixture-only HWNDs. No launcher settings, game windows or user apps.
        foreach (uint style in new[] { 0x80000000u, 0x80040000u })
        {
            IntPtr hwnd = CreateWindowEx(0, "STATIC", "Vortex corner regression (hidden)", style, 0, 0, 300, 180, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Check(hwnd != IntPtr.Zero, "hidden Win32 corner fixture created");
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                Check(WindowsWindowRegion.Apply(hwnd, true, 1), "opaque HWND accepts real rounded clipping");
                Check(GetWindowRgn(hwnd, region) > 0, "rounded region belongs to the HWND, not only to a painted border");
                var origin = new Point(); ClientToScreen(hwnd, ref origin); GetWindowRect(hwnd, out var outer); GetClientRect(hwnd, out var client);
                int x = origin.X - outer.Left, y = origin.Y - outer.Top;
                Check(!PtInRegion(region, x, y) && !PtInRegion(region, x + client.Right - 1, y) && !PtInRegion(region, x, y + client.Bottom - 1) && !PtInRegion(region, x + client.Right - 1, y + client.Bottom - 1), "all four client corners are genuinely excluded, including invisible resize frames");
                Check(PtInRegion(region, x + client.Right / 2, y + client.Bottom / 2), "native clipping preserves the opaque window center");
                Check(WindowsWindowRegion.Apply(hwnd, true, 2) && GetWindowRgn(hwnd, region) > 0 && !PtInRegion(region, x + 12, y), "native corner radius follows 200-percent DPI");
                Check(SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 420, 240, 0x0004 | 0x0010), "hidden fixture resizes without activation");
                Check(WindowsWindowRegion.Apply(hwnd, true, 1.5) && GetWindowRgn(hwnd, region) > 0, "rounded clipping refreshes after resize and DPI changes");
                GetClientRect(hwnd, out client); Check(PtInRegion(region, x + client.Right - 3, y + client.Bottom / 2), "resized right edge is not clipped to the old window width");
                Check(WindowsWindowRegion.Apply(hwnd, false, 1) && GetWindowRgn(hwnd, region) == 0, "maximized/fullscreen path removes the custom native region");
                int before = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
                for (int i = 0; i < 200; i++) CheckQuiet(WindowsWindowRegion.Apply(hwnd, true, i % 2 == 0 ? 1 : 1.5));
                int after = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
                Check(after - before <= 2, "replacing native regions does not leak GDI objects");
            }
            finally { if (region != IntPtr.Zero) DeleteObject(region); DestroyWindow(hwnd); }
        }
        Console.WriteLine("WINDOWS_REGION_OK (hidden HWNDs, no user windows touched)");
    }
    private static void Check(bool value, string text) { if (!value) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
    private static void CheckQuiet(bool value) { if (!value) throw new Exception("Native region replacement failed"); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extended, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
}
