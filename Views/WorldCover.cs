using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VortexLauncher.Views;

// Original, code-native cover art. No screenshots, downloads, animation loop or
// per-frame geometry construction. Custom images are drawn on top of this fallback.
public sealed class WorldCover : Control
{
    public static readonly StyledProperty<string> SeedProperty = AvaloniaProperty.Register<WorldCover, string>(nameof(Seed), "Vortex");
    public string Seed { get => GetValue(SeedProperty); set => SetValue(SeedProperty, value); }
    private sealed record Palette(IBrush Sky, IBrush Far, IBrush Middle, IBrush Near, IBrush Tree, IBrush Trunk, IBrush Sun);
    // Soft pastel landscapes: rounded hills, a round sun and two round trees.
    private static readonly Palette[] Palettes = {
        Make("#9CC3F0", "#86AEDD", "#6FB38A", "#4E9A6E", "#3E7F5A", "#5C4636", "#FFE7A8"),
        Make("#B9B3E8", "#9C93D6", "#7FA6C9", "#6A8FB8", "#4F6E9A", "#4A3F5C", "#FFE3C2"),
        Make("#F3CFA6", "#E3AE7E", "#C99266", "#A9774F", "#7E5A3B", "#5A3F2C", "#FFF1C9"),
        Make("#A6DCE8", "#7EC4D6", "#5FB39A", "#468F7A", "#2F6E5C", "#4B3B2E", "#FFF4B8"),
        Make("#F1B9CF", "#DD95B3", "#B78AC0", "#946FA6", "#6E5285", "#4A3550", "#FFE6EE")
    };
    static WorldCover() => AffectsRender<WorldCover>(SeedProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        uint hash = 2166136261;
        foreach (char c in Seed ?? "") hash = unchecked((hash ^ c) * 16777619);
        var p = Palettes[hash % (uint)Palettes.Length];
        // Uniform fill keeps the sun and trees round on wide heroes and square thumbnails.
        double scale = Math.Max(Bounds.Width / 1000, Bounds.Height / 600);
        double offsetX = (Bounds.Width - 1000 * scale) / 2, offsetY = (Bounds.Height - 600 * scale) / 2;
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY)))
        {
            context.DrawRectangle(p.Sky, null, new Rect(0, 0, 1000, 600));
            context.DrawEllipse(p.Sun, null, new Point(760, 150), 60, 60);
            using (context.PushOpacity(0.35))
            {
                context.DrawRectangle(Brushes.White, null, new Rect(150, 120, 180, 36), 18, 18);
                context.DrawRectangle(Brushes.White, null, new Rect(560, 214, 120, 28), 14, 14);
            }
            context.DrawEllipse(p.Far, null, new Point(250, 470), 420, 190);
            context.DrawEllipse(p.Far, null, new Point(820, 450), 380, 180);
            context.DrawEllipse(p.Middle, null, new Point(620, 610), 560, 210);
            context.DrawEllipse(p.Near, null, new Point(120, 650), 420, 190);
            DrawTree(context, p, 841, 480, 38);
            DrawTree(context, p, 230, 492, 28);
        }
    }
    private static void DrawTree(DrawingContext context, Palette p, double x, double ground, double radius)
    {
        context.DrawRectangle(p.Trunk, null, new Rect(x - radius * .16, ground - radius * 1.6, radius * .32, radius * 1.6), radius * .16, radius * .16);
        context.DrawEllipse(p.Tree, null, new Point(x, ground - radius * 1.9), radius, radius);
    }
    private static Palette Make(params string[] colors) => new(Brush.Parse(colors[0]), Brush.Parse(colors[1]), Brush.Parse(colors[2]), Brush.Parse(colors[3]), Brush.Parse(colors[4]), Brush.Parse(colors[5]), Brush.Parse(colors[6]));
}
