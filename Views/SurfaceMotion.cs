using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;

namespace VortexLauncher.Views;

// Opt-in page/content entrances, with attach/context/visibility changes coalesced.
public sealed class SurfaceMotion : AvaloniaObject
{
    public static readonly AttachedProperty<string> KindProperty = AvaloniaProperty.RegisterAttached<SurfaceMotion, Control, string>("Kind", "");
    private static readonly AttachedProperty<bool> QueuedProperty = AvaloniaProperty.RegisterAttached<SurfaceMotion, Control, bool>("Queued");
    static SurfaceMotion() => KindProperty.Changed.AddClassHandler<Control>((control, _) =>
    {
        control.AttachedToVisualTree -= Attached;
        control.DetachedFromVisualTree -= Detached;
        control.PropertyChanged -= Changed;
        if (GetKind(control).Length == 0) { UiMotion.Cancel(control); return; }
        control.AttachedToVisualTree += Attached;
        control.DetachedFromVisualTree += Detached;
        control.PropertyChanged += Changed;
    });
    public static string GetKind(Control target) => target.GetValue(KindProperty);
    public static void SetKind(Control target, string value) => target.SetValue(KindProperty, value);
    private static void Attached(object? sender, VisualTreeAttachmentEventArgs e) { if (sender is Control target) Queue(target); }
    private static void Detached(object? sender, VisualTreeAttachmentEventArgs e) { if (sender is Control target) UiMotion.Cancel(target); }
    private static void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Control target) return;
        if (e.Property == Visual.IsVisibleProperty)
        {
            if (target.IsVisible) Queue(target); else UiMotion.Cancel(target);
        }
        else if (e.Property == StyledElement.DataContextProperty && GetKind(target) == "Content") Queue(target);
    }
    private static void Queue(Control target)
    {
        if (target.GetValue(QueuedProperty)) return;
        target.SetValue(QueuedProperty, true);
        Dispatcher.UIThread.Post(() =>
        {
            target.SetValue(QueuedProperty, false);
            if (target.GetVisualAncestors().OfType<Control>().Any(parent => parent.GetValue(QueuedProperty) || UiMotion.IsRunning(parent))) return;
            if (GetKind(target).Length > 0 && target.IsEffectivelyVisible && target.GetVisualRoot() != null) UiMotion.Reveal(target, page: GetKind(target) == "Page");
        }, DispatcherPriority.Background);
    }
}
