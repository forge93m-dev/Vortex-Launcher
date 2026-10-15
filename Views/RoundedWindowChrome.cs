using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

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
        void Update() => UpdateShape(shell, window.WindowState, window.ActualTransparencyLevel != WindowTransparencyLevel.None);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty || e.Property == TopLevel.ActualTransparencyLevelProperty) Update();
        }
        window.PropertyChanged += Changed;
        window.Closed += (_, _) => window.PropertyChanged -= Changed;
        Update();
    }
    internal static void UpdateShape(Border shell, WindowState state, bool transparent)
    {
        bool fillsScreen = state is WindowState.Maximized or WindowState.FullScreen;
        // Unsupported compositors get a solid theme-colored rectangle, never
        // black/white corner patches. Maximized windows fill their work area.
        shell.CornerRadius = new CornerRadius(!fillsScreen && transparent ? Radius : 0);
        shell.BorderThickness = new Thickness(fillsScreen ? 0 : 1);
    }
}

