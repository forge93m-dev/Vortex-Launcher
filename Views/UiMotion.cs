using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace VortexLauncher.Views;

internal static class UiMotion
{
    private sealed class State
    {
        public CancellationTokenSource? Running;
        public Task? Completion;
    }
    private static readonly ConditionalWeakTable<Control, State> States = new();
    private static readonly ConditionalWeakTable<Window, object> Dialogs = new();
    private static readonly HashSet<Control> Active = new();
    internal static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(140);
    internal static readonly TimeSpan PageDuration = TimeSpan.FromMilliseconds(180);
    private static bool _enabled = true;
    internal static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) foreach (var target in Active.ToArray()) Cancel(target);
        }
    }
    internal static int ActiveCount => Active.Count;
    internal static bool IsRunning(Control target) =>
        States.TryGetValue(target, out var state) && state.Running is { IsCancellationRequested: false };

    public static void AttachDialog(Window window, Control content)
    {
        if (Dialogs.TryGetValue(window, out _)) return;
        Dialogs.Add(window, new object());
        window.Opened += (_, _) => Reveal(content);
        window.Closed += (_, _) => Cancel(content);
    }
    internal static void Cancel(Control target)
    {
        if (States.TryGetValue(target, out var state)) state.Running?.Cancel();
    }
    // Pages slide a short way in from the right while they fade up.
    internal static async void Reveal(Control target, bool page = false) =>
        await RevealCoreAsync(target, default, 0, page, page ? new Rise(0, PageSlide, 1, .82, PageDuration) : null);
    internal const double PageSlide = 14;

    // Only one subtle opacity animation per visual branch. Never overwrite the
    // base opacity/binding or a transform used by layout, clipping or hover.
    public static Task RevealAsync(Control target, CancellationToken token, int delay = 0)
        => RevealCoreAsync(target, token, delay, page: false);

    // Entrance for surfaces opened on demand (the instance collection, a newly featured
    // cover): fades in from fromOpacity while rising distance px into place. Targets that
    // already carry a transform only fade, so layout and hover transforms stay untouched.
    public static Task RiseAsync(Control target, CancellationToken token, double distance, double fromOpacity = 0, int delay = 0, int duration = 240)
        => RevealCoreAsync(target, token, delay, page: false, new Rise(distance, 0, 1, fromOpacity, TimeSpan.FromMilliseconds(duration)));

    // Popups and menus grow slightly from fromScale while they fade in.
    public static Task GrowAsync(Control target, CancellationToken token, double fromScale = .96, double fromOpacity = .4, int duration = 160)
        => RevealCoreAsync(target, token, 0, page: false, new Rise(0, 0, fromScale, fromOpacity, TimeSpan.FromMilliseconds(duration)));

    private sealed record Rise(double DistanceY, double DistanceX, double FromScale, double FromOpacity, TimeSpan Duration);

    // A one-shot ring that swells and fades from behind a pressed button. The ring
    // must carry its own ScaleTransform; nothing else is touched.
    public static async void Pulse(Control ring)
    {
        if (!_enabled || ring.RenderTransform is not ScaleTransform || !ring.IsEffectivelyVisible) return;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(460), Easing = new CubicEaseOut(), FillMode = FillMode.None,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, .45), new Setter(ScaleTransform.ScaleXProperty, 1d), new Setter(ScaleTransform.ScaleYProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(ScaleTransform.ScaleXProperty, 1.12), new Setter(ScaleTransform.ScaleYProperty, 1.3) } }
            }
        };
        Active.Add(ring);
        try { await animation.RunAsync(ring); }
        finally { Active.Remove(ring); }
    }

    private static Task RevealCoreAsync(Control target, CancellationToken token, int delay, bool page, Rise? rise = null)
    {
        if (!_enabled || token.IsCancellationRequested || !target.IsEffectivelyVisible)
            return Task.CompletedTask;
        var state = States.GetOrCreateValue(target);
        if (state.Running is { IsCancellationRequested: false } && state.Completion != null)
            return state.Completion; // Repeated binding/attach events must not restart a fade.
        if (target.GetVisualAncestors().OfType<Control>().Any(IsRunning)) return Task.CompletedTask;
        foreach (var child in Active.ToArray())
            if (child.GetVisualAncestors().Contains(target)) Cancel(child);
        if (Active.Count >= 24) return Task.CompletedTask;

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        state.Running = cancellation;
        Active.Add(target);
        state.Completion = RunAsync(target, state, cancellation, delay, page, rise);
        return state.Completion;
    }

    private static async Task RunAsync(Control target, State state, CancellationTokenSource cancellation, int delay, bool page, Rise? rise)
    {
        Transform? shift = null;
        try
        {
            if (delay > 0 && rise == null) await Task.Delay(Math.Min(delay, 60), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            double opacity = target.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(1d);
            var animation = new Animation
            {
                Duration = rise?.Duration ?? (page ? PageDuration : EnterDuration), Easing = new CubicEaseOut(), FillMode = FillMode.None,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, opacity * (rise?.FromOpacity ?? (page ? .82 : .94))) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, opacity) } }
                }
            };
            if (rise != null && delay > 0)
            {
                // Staggered entrances hold their starting frame while they wait, so nothing
                // shows at full strength and then jumps back.
                animation.Delay = TimeSpan.FromMilliseconds(Math.Min(delay, 400));
                animation.FillMode = FillMode.Backward;
            }
            // A temporary transform owned by this entrance; Avalonia animates it through
            // the target's RenderTransform, and it is removed again when the entrance ends.
            if (rise != null && target.RenderTransform == null)
            {
                if (rise.FromScale < 1)
                {
                    target.RenderTransform = shift = new ScaleTransform();
                    animation.Children[0].Setters.Add(new Setter(ScaleTransform.ScaleXProperty, rise.FromScale));
                    animation.Children[0].Setters.Add(new Setter(ScaleTransform.ScaleYProperty, rise.FromScale));
                    animation.Children[1].Setters.Add(new Setter(ScaleTransform.ScaleXProperty, 1d));
                    animation.Children[1].Setters.Add(new Setter(ScaleTransform.ScaleYProperty, 1d));
                }
                else if (rise.DistanceY > 0 || rise.DistanceX > 0)
                {
                    target.RenderTransform = shift = new TranslateTransform();
                    animation.Children[0].Setters.Add(new Setter(TranslateTransform.XProperty, rise.DistanceX));
                    animation.Children[0].Setters.Add(new Setter(TranslateTransform.YProperty, rise.DistanceY));
                    animation.Children[1].Setters.Add(new Setter(TranslateTransform.XProperty, 0d));
                    animation.Children[1].Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
                }
            }
            await animation.RunAsync(target, cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (shift != null && ReferenceEquals(target.RenderTransform, shift)) target.RenderTransform = null;
            if (ReferenceEquals(state.Running, cancellation))
            {
                state.Running = null;
                state.Completion = null;
                Active.Remove(target);
            }
            cancellation.Dispose();
        }
    }
}
