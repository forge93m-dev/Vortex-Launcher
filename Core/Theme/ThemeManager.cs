using System;
using Avalonia;
using Avalonia.Media;
using System.Linq;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace VortexLauncher.Core.Theme;

public static class ThemeManager
{
    public static void SetTheme(string name, string accent, string background, string sidebar,
        string panel, string border, string text, string subtext)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        resources["ThemeName"] = name;
        var backgroundColor = Parse(background, Colors.Black);
        resources["ThemeBackgroundBrush"] = new SolidColorBrush(backgroundColor);
        // Translucent panels laid over cover art.
        resources["ThemeGlassBrush"] = new SolidColorBrush(new Color(0xD9, backgroundColor.R, backgroundColor.G, backgroundColor.B));
        resources["ThemeSidebarBrush"] = new SolidColorBrush(Parse(sidebar, Colors.Black));
        resources["ThemePanelBrush"] = new SolidColorBrush(Parse(panel, Colors.Black));
        resources["ThemeBorderBrush"] = new SolidColorBrush(Parse(border, Colors.Gray));
        resources["ThemeTextBrush"] = new SolidColorBrush(Parse(text, Colors.White));
        resources["ThemeSubtextBrush"] = new SolidColorBrush(Parse(subtext, Colors.Gray));
        SetAccentColor(accent);
    }

    public static void SetAccentColor(string hexColor)
    {
        if (Color.TryParse(hexColor, out var color))
        {
            SetAccentColor(color);
        }
    }

    public static void SetAccentColor(Color color)
    {
        if (Application.Current is null) return;

        var resources = Application.Current.Resources;
        resources["AccentColor"] = color;
        resources["AccentBrush"] = new SolidColorBrush(color);

        // Derived resources keep hover states, selection backgrounds and text
        // contrast synchronized with the selected accent.
        var hover = new Color(
            color.A,
            (byte)Math.Min(255, color.R + 24),
            (byte)Math.Min(255, color.G + 24),
            (byte)Math.Min(255, color.B + 24));
        var soft = new Color(38, color.R, color.G, color.B);
        bool useDarkText = color.R * 299 + color.G * 587 + color.B * 114 > 150000;

        resources["AccentHoverBrush"] = new SolidColorBrush(hover);
        resources["AccentSoftBrush"] = new SolidColorBrush(soft);
        resources["AccentForegroundBrush"] = new SolidColorBrush(useDarkText ? Colors.Black : Colors.White);

        // Keep Fluent toggles, checkboxes and sliders in sync with our custom controls.
        foreach (var theme in Application.Current.Styles.OfType<FluentTheme>())
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                if (!theme.Palettes.TryGetValue(variant, out var palette))
                    theme.Palettes[variant] = palette = new ColorPaletteResources();
                palette.Accent = color;
            }
        }
    }

    private static Color Parse(string value, Color fallback)
        => Color.TryParse(value, out var color) ? color : fallback;
}
