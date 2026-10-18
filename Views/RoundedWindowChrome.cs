using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;

namespace VortexLauncher.Views;

internal static class RoundedWindowChrome
{
    internal const double Radius = 12;
    internal static void Attach(Window window, Border shell)
    {
        window.Background = Brushes.Transparent;
        // Per-pixel transparency only; no Acrylic, blur or full-window opacity.
        window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        window.Bind(TopLevel.TransparencyBackgroundFallbackProperty, new DynamicResourceExtension("ThemeBackgroundBrush"));
        shell.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ThemeBackgroundBrush"));
        shell.ClipToBounds = true;
        RoundedClip.SetIsEnabled(shell, true);
        // The outer clip alone lets rectangular child backgrounds paint over
        // the curved border. Clip the content to the INNER contour as well.
        var content = shell.Child;
        shell.Child = null;
        var innerClip = new Border { Name = "windowChromeContentClip", Child = content };
        RoundedClip.SetIsEnabled(innerClip, true);
        shell.Child = innerClip;
        bool nativeRegion = false, updating = false, closed = false;
        (Size Size, double Scale, WindowState State, bool Transparent)? lastNativeShape = null;
        void Update()
        {
            if (updating || closed) return; // SetWindowRgn synchronously sends window-position messages.
            updating = true;
            try
            {
                bool transparent = window.ActualTransparencyLevel != WindowTransparencyLevel.None;
                bool normal = window.WindowState is not (WindowState.Maximized or WindowState.FullScreen);
                var shape = (window.ClientSize, window.RenderScaling, window.WindowState, transparent);
                if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle && handle.HandleDescriptor == "HWND" && handle.Handle != IntPtr.Zero)
                {
                    // Transparent is only the renderer's reported capability. Some
                    // Win32 swapchain/compositor paths still paint opaque corners.
                    // Always clip the HWND, even when transparency reports success.
                    if (normal && lastNativeShape != shape)
                    {
                        if (WindowsWindowRegion.Apply(handle.Handle, true, window.RenderScaling)) { nativeRegion = true; lastNativeShape = shape; }
                    }
                    else if (!normal && nativeRegion)
                    {
                        if (WindowsWindowRegion.Apply(handle.Handle, false, window.RenderScaling)) { nativeRegion = false; lastNativeShape = null; }
                    }
                }
                UpdateShape(shell, window.WindowState, transparent || nativeRegion);
            }
            finally { updating = false; }
        }
        void Opened(object? sender, EventArgs e) { lastNativeShape = null; Update(); }
        void ScalingChanged(object? sender, EventArgs e)
        {
            lastNativeShape = null; Update();
            // Recheck after Win32 finishes the DPI transition's physical resize.
            Dispatcher.UIThread.Post(() => { lastNativeShape = null; Update(); });
        }
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty || e.Property == TopLevel.ActualTransparencyLevelProperty || e.Property == Visual.BoundsProperty) Update();
        }
        window.PropertyChanged += Changed;
        window.Opened += Opened;
        window.ScalingChanged += ScalingChanged;
        window.Closed += (_, _) => { closed = true; window.PropertyChanged -= Changed; window.Opened -= Opened; window.ScalingChanged -= ScalingChanged; };
        Update();
    }
    internal static void UpdateShape(Border shell, WindowState state, bool transparent)
    {
        bool fillsScreen = state is WindowState.Maximized or WindowState.FullScreen;
        // transparent also includes the successful native Windows-region fallback.
        // Other unsupported compositors retain a solid theme-colored rectangle.
        shell.CornerRadius = new CornerRadius(!fillsScreen && transparent ? Radius : 0);
        shell.BorderThickness = new Thickness(fillsScreen ? 0 : 1);
        if (shell.Child is Border { Name: "windowChromeContentClip" } innerClip)
            innerClip.CornerRadius = new CornerRadius(Math.Max(0, shell.CornerRadius.TopLeft - shell.BorderThickness.Left));
    }
}
