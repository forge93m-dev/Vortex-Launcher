using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;

namespace VortexLauncher.Views;

public partial class WindowTitleBar : UserControl
{
    private Window? _window;
    public WindowTitleBar() => InitializeComponent();
    public WindowTitleBar(Window window) : this()
    {
        _window = window;
        CaptionTitle.Bind(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
        ChromeMaximize.Bind(IsVisibleProperty, new Binding(nameof(Window.CanResize)) { Source = window });
    }

    public static void Apply(Window window)
    {
        if (window.Content is not Control content) return;
        window.Content = null;
        window.SystemDecorations = SystemDecorations.None;
        window.ExtendClientAreaToDecorationsHint = false;
        // Preserve the existing dialog's usable content height.
        if (!double.IsNaN(window.Height)) window.Height += 40;
        if (window.MinHeight > 0) window.MinHeight += 40;
        var layout = new Grid { RowDefinitions = new RowDefinitions("40,*") };
        layout.Children.Add(new WindowTitleBar(window));
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        var shell = new Border { Child = layout, CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), ClipToBounds = true };
        shell.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ThemeBackgroundBrush"));
        shell.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeBorderBrush"));
        window.Content = shell;
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) { if (_window != null) _window.WindowState = WindowState.Minimized; }
    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void OnClose(object? sender, RoutedEventArgs e) => _window?.Close();
    private void ToggleMaximize()
    {
        if (_window is not { CanResize: true }) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void OnCaptionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Control source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any())) return;
        if (e.ClickCount == 2) ToggleMaximize();
        else _window.BeginMoveDrag(e);
        e.Handled = true;
    }
}
