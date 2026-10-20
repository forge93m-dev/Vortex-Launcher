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
    private static readonly StreamGeometry Far = Shape(0,380, 80,380, 80,305, 170,305, 170,230, 260,230, 260,160, 340,160, 340,255, 430,255, 430,320, 525,320, 525,265, 620,265, 620,190, 695,190, 695,245, 785,245, 785,310, 885,310, 885,250, 1000,250, 1000,600, 0,600);
    private static readonly StreamGeometry Middle = Shape(0,450, 120,450, 120,400, 225,400, 225,360, 335,360, 335,405, 455,405, 455,375, 575,375, 575,335, 690,335, 690,295, 800,295, 800,350, 900,350, 900,380, 1000,380, 1000,600, 0,600);
    private static readonly StreamGeometry Near = Shape(0,515, 90,515, 90,470, 205,470, 205,500, 345,500, 345,550, 510,550, 510,520, 660,520, 660,480, 780,480, 780,435, 880,435, 880,455, 1000,455, 1000,600, 0,600);
    private static readonly StreamGeometry River = Shape(430,600, 600,600, 645,535, 625,495, 695,460, 680,430, 735,390, 710,390, 650,430, 658,455, 565,495, 575,530);
    private static readonly StreamGeometry Tree = Shape(0,0, 20,0, 20,-20, 35,-20, 35,-55, 55,-55, 55,-95, 35,-95, 35,-125, -15,-125, -15,-95, -35,-95, -35,-55, -15,-55, -15,-20, 0,-20);
    private sealed record Palette(IBrush Sky, IBrush Far, IBrush Middle, IBrush Near, IBrush Water, IBrush Tree, IBrush Light);
    private static readonly Palette[] Palettes = {
        Make("#6DABA1", "#608B80", "#376D58", "#204D40", "#82C7BD", "#173D32", "#EBE9B5"),
        Make("#929BBE", "#737D9F", "#505E82", "#303E62", "#91B1D0", "#25364E", "#E2D7B7"),
        Make("#C8A27B", "#AF8065", "#96644B", "#634B3C", "#94B5AA", "#3D5949", "#F3DCAB")
    };
    static WorldCover() => AffectsRender<WorldCover>(SeedProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        uint hash = 2166136261;
        foreach (char c in Seed ?? "") hash = unchecked((hash ^ c) * 16777619);
        var p = Palettes[hash % (uint)Palettes.Length];
        // A wide hero must show the entire landscape, not a magnified strip of rock.
        double scaleX = Bounds.Width / 1000, scaleY = Bounds.Height / 600;
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(new Matrix(scaleX, 0, 0, scaleY, 0, 0)))
        {
            context.DrawRectangle(p.Sky, null, new Rect(0, 0, 1000, 600));
            context.DrawRectangle(p.Light, null, new Rect(780, 92, 70, 70));
            using (context.PushOpacity(0.20))
            {
                context.DrawRectangle(Brushes.White, null, new Rect(90, 115, 200, 18));
                context.DrawRectangle(Brushes.White, null, new Rect(170, 97, 80, 18));
                context.DrawRectangle(Brushes.White, null, new Rect(570, 160, 150, 14));
            }
            context.DrawGeometry(p.Far, null, Far);
            context.DrawGeometry(p.Middle, null, Middle);
            context.DrawGeometry(p.Near, null, Near);
            context.DrawGeometry(p.Water, null, River);
            using (context.PushTransform(Matrix.CreateTranslation(885, 455))) context.DrawGeometry(p.Tree, null, Tree);
            using (context.PushTransform(Matrix.CreateTranslation(180, 500))) context.DrawGeometry(p.Tree, null, Tree);
            using (context.PushTransform(new Matrix(.65, 0, 0, .65, 740, 480))) context.DrawGeometry(p.Tree, null, Tree);
        }
    }
    private static Palette Make(params string[] colors) => new(Brush.Parse(colors[0]), Brush.Parse(colors[1]), Brush.Parse(colors[2]), Brush.Parse(colors[3]), Brush.Parse(colors[4]), Brush.Parse(colors[5]), Brush.Parse(colors[6]));
    private static StreamGeometry Shape(params double[] coordinates)
    {
        var geometry = new StreamGeometry();
        using var drawing = geometry.Open();
        drawing.BeginFigure(new Point(coordinates[0], coordinates[1]), true);
        for (int i = 2; i < coordinates.Length; i += 2) drawing.LineTo(new Point(coordinates[i], coordinates[i + 1]));
        drawing.EndFigure(true);
        return geometry;
    }
}
