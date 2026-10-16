using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace VortexLauncher.Views;

internal static class UiMotion
{
    private sealed class State { public CancellationTokenSource? Running; }
    private static readonly ConditionalWeakTable<Control, State> States = new();
    private static readonly ConditionalWeakTable<Window, object> Dialogs = new();
    internal static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(220);
    internal static bool Enabled { get; set; } = true;
    internal static int ActiveCount { get; private set; }
    public static void AttachDialog(Window window, Control content)
    {
        if (Dialogs.TryGetValue(window, out _)) return;
        Dialogs.Add(window, new object());
        window.Opened += async (_, _) => await RevealAsync(content, default);
        window.Closed += (_, _) => Cancel(content);
    }
    internal static void Cancel(Control target)
    {
        if (States.TryGetValue(target, out var state)) state.Running?.Cancel();
    }
    internal static async void Reveal(Control target) => await RevealAsync(target, default);
    // Coalesce entrances, animate only opacity/transforms, and never move the
    // native window outline. Older cancelled work cannot reset a newer run.
    public static async Task RevealAsync(Control target, CancellationToken token, int delay = 0)
    {
        var state = States.GetOrCreateValue(target);
        state.Running?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        state.Running = cancellation;
        var rest = new TranslateTransform(0, 0);
        if (!Enabled || ActiveCount >= 24)
        {
            target.SetCurrentValue(Visual.OpacityProperty, 1d);
            target.SetCurrentValue(Visual.RenderTransformProperty, rest);
            state.Running = null;
            return;
        }
        ActiveCount++;
        try
        {
            var start = new TranslateTransform(0, 10);
            target.SetCurrentValue(Visual.OpacityProperty, 0d);
            target.SetCurrentValue(Visual.RenderTransformProperty, start);
            if (delay > 0) await Task.Delay(Math.Min(delay, 120), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var animation = new Animation
            {
                Duration = EnterDuration, Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 10d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } }
                }
            };
            await animation.RunAsync(target, cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            ActiveCount--;
            if (ReferenceEquals(state.Running, cancellation))
            {
                state.Running = null;
                target.SetCurrentValue(Visual.OpacityProperty, 1d);
                target.SetCurrentValue(Visual.RenderTransformProperty, rest);
            }
        }
    }
}
