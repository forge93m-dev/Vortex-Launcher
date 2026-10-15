using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VortexLauncher.Views;

// ClipToBounds in Avalonia 11.0.6 is rectangular: a rounded Border alone
// does not clip the backgrounds/images of its children to the same corners.
public sealed class RoundedClip : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<RoundedClip, Border, bool>("IsEnabled");
    static RoundedClip() => IsEnabledProperty.Changed.AddClassHandler<Border>((border, e) =>
    {
        border.PropertyChanged -= OnBorderChanged;
        if (GetIsEnabled(border))
        {
            border.PropertyChanged += OnBorderChanged;
            Update(border);
        }
        else border.ClearValue(Visual.ClipProperty);
    });
    public static bool GetIsEnabled(Border border) => border.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Border border, bool value) => border.SetValue(IsEnabledProperty, value);
    private static void OnBorderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is Border border && (e.Property == Visual.BoundsProperty || e.Property == Border.CornerRadiusProperty)) Update(border);
    }
    private static void Update(Border border)
    {
        // Current launcher shells/cards use one uniform corner radius.
        double radius = System.Math.Min(border.CornerRadius.TopLeft, System.Math.Min(border.Bounds.Width, border.Bounds.Height) / 2);
        if (radius <= 0) { border.Clip = new RectangleGeometry(new Rect(border.Bounds.Size)); return; }
        double width = border.Bounds.Width, height = border.Bounds.Height;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            var arc = new Size(radius, radius);
            drawing.BeginFigure(new Point(radius, 0), true);
            drawing.LineTo(new Point(width - radius, 0)); drawing.ArcTo(new Point(width, radius), arc, 0, false, SweepDirection.Clockwise);
            drawing.LineTo(new Point(width, height - radius)); drawing.ArcTo(new Point(width - radius, height), arc, 0, false, SweepDirection.Clockwise);
            drawing.LineTo(new Point(radius, height)); drawing.ArcTo(new Point(0, height - radius), arc, 0, false, SweepDirection.Clockwise);
            drawing.LineTo(new Point(0, radius)); drawing.ArcTo(new Point(radius, 0), arc, 0, false, SweepDirection.Clockwise);
            drawing.EndFigure(true);
        }
        border.Clip = geometry;
    }
}

