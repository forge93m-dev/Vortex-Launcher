using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VortexLauncher.Views;

internal static class MotionRegressionChecks
{
    internal static void Run(Action<int> pump)
    {
        var transform = new ScaleTransform(.93, .93);
        var child = new Border { Height = 40, Background = Brushes.White };
        var parent = new Border { Child = child, RenderTransform = transform, Opacity = .72 };
        var host = new Window { Width = 300, Height = 160, Content = parent };
        host.Show(); pump(60);
        var first = UiMotion.RevealAsync(parent, default);
        Check(ReferenceEquals(parent.RenderTransform, transform), "entrances never replace an existing layout/hover transform");
        pump(40);
        Check(parent.Opacity >= .64, "an incoming surface never flashes to invisible");
        var before = parent.Opacity;
        var replacement = UiMotion.RevealAsync(parent, default);
        Check(ReferenceEquals(first, replacement), "duplicate requests share the same running animation");
        Check(parent.Opacity >= before - .02, "repeated requests never restart from black");
        var nested = UiMotion.RevealAsync(child, default);
        Check(nested.IsCompletedSuccessfully && child.Opacity == 1, "nested content does not compound its parent's entrance");
        pump(400);
        Check(first.IsCompletedSuccessfully && replacement.IsCompletedSuccessfully, "replaced entrances finish without pending work");
        Check(Math.Abs(parent.Opacity - .72) < .001 && ReferenceEquals(parent.RenderTransform, transform), "completion preserves bound opacity and original transforms");
        UiMotion.Enabled = false;
        var disabled = UiMotion.RevealAsync(parent, default);
        Check(disabled.IsCompletedSuccessfully && Math.Abs(parent.Opacity - .72) < .001 && ReferenceEquals(parent.RenderTransform, transform), "reduced motion also preserves existing visual state");
        UiMotion.Enabled = true;
        var cancelled = UiMotion.RevealAsync(parent, default);
        pump(30); UiMotion.Cancel(parent); pump(40);
        Check(cancelled.IsCompletedSuccessfully && Math.Abs(parent.Opacity - .72) < .001, "cancellation restores base opacity without a forced opaque frame");
        var interrupted = UiMotion.RevealAsync(parent, default);
        pump(30); UiMotion.Enabled = false; pump(40);
        Check(interrupted.IsCompletedSuccessfully && UiMotion.ActiveCount == 0 && Math.Abs(parent.Opacity - .72) < .001, "disabling motion stops in-flight work and preserves the visual state");
        UiMotion.Enabled = true;
        host.Close(); pump(40);
        Console.WriteLine("MOTION_REGRESSION_OK");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
