using System;
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
    public static void AttachDialog(Window window, Control content)
    {
        CancellationTokenSource? running = null;
        window.Opened += async (_, _) =>
        {
            running?.Cancel();
            var cancellation = new CancellationTokenSource();
            running = cancellation;
            try { await RevealAsync(content, cancellation.Token); }
            finally
            {
                if (ReferenceEquals(running, cancellation)) running = null;
                cancellation.Dispose();
            }
        };
        window.Closed += (_, _) => running?.Cancel();
    }

    // Use a dedicated wrapper so entrance motion cannot override hover or deletion effects.
    public static async Task RevealAsync(Control target, CancellationToken token, int delay = 0)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(240),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters =
                {
                    new Setter(Visual.OpacityProperty, 0d),
                    new Setter(TranslateTransform.YProperty, 10d)
                } },
                new KeyFrame { Cue = new Cue(1), Setters =
                {
                    new Setter(Visual.OpacityProperty, 1d),
                    new Setter(TranslateTransform.YProperty, 0d)
                } }
            }
        };
        target.SetCurrentValue(Visual.OpacityProperty, 0d);
        target.SetCurrentValue(Visual.RenderTransformProperty, new TranslateTransform(0, 0));
        try
        {
            if (delay > 0) await Task.Delay(delay, token);
            token.ThrowIfCancellationRequested();
            await animation.RunAsync(target, cancellationToken: token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { target.SetCurrentValue(Visual.OpacityProperty, 1d); }
    }
}
