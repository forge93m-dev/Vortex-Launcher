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
    internal static async void Reveal(Control target, bool page = false) => await RevealCoreAsync(target, default, 0, page);

    // Only one subtle opacity animation per visual branch. Never overwrite the
    // base opacity/binding or a transform used by layout, clipping or hover.
    public static Task RevealAsync(Control target, CancellationToken token, int delay = 0)
        => RevealCoreAsync(target, token, delay, page: false);

    private static Task RevealCoreAsync(Control target, CancellationToken token, int delay, bool page)
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
        state.Completion = RunAsync(target, state, cancellation, delay, page);
        return state.Completion;
    }

    private static async Task RunAsync(Control target, State state, CancellationTokenSource cancellation, int delay, bool page)
    {
        try
        {
            if (delay > 0) await Task.Delay(Math.Min(delay, 60), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            double opacity = target.GetBaseValue(Visual.OpacityProperty).GetValueOrDefault(1d);
            var animation = new Animation
            {
                Duration = page ? PageDuration : EnterDuration, Easing = new CubicEaseOut(), FillMode = FillMode.None,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, opacity * (page ? .82 : .94)) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, opacity) } }
                }
            };
            await animation.RunAsync(target, cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
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
