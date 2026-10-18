using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace VortexLauncher.Views;

public static class PopupMotion
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(PopupMotion));
    private static readonly ConditionalWeakTable<Control, State> States = new();
    private sealed class State { public CancellationTokenSource? Running; }

    static PopupMotion()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.GetNewValue<bool>())
            {
                // Fluent sets popup border resources as local template values;
                // bind at that same priority so the user's theme wins there too.
                if (control is Border border)
                {
                    border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ThemePanelBrush"));
                    border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeBorderBrush"));
                    border.CornerRadius = new CornerRadius(12);
                    border.BorderThickness = new Thickness(1);
                    border.Padding = new Thickness(5);
                }
                control.AttachedToVisualTree += OnAttached;
                control.DetachedFromVisualTree += OnDetached;
            }
            else
            {
                control.AttachedToVisualTree -= OnAttached;
                control.DetachedFromVisualTree -= OnDetached;
                if (States.TryGetValue(control, out var state)) state.Running?.Cancel();
            }
        });
    }

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    private static async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control) return;
        var state = States.GetOrCreateValue(control);
        state.Running?.Cancel();
        var cancellation = new CancellationTokenSource();
        state.Running = cancellation;
        try { await UiMotion.RevealAsync(control, cancellation.Token); }
        finally
        {
            if (ReferenceEquals(state.Running, cancellation)) state.Running = null;
            cancellation.Dispose();
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control && States.TryGetValue(control, out var state)) state.Running?.Cancel();
    }
}
