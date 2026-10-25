using System;
using Avalonia;
using Avalonia.Media;
using System.Linq;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace VortexLauncher.Core.Theme;

public static class ThemeManager
{
    /// <summary>"Compact" halves the vertical padding of long lists (mods, files, catalog results); "Comfortable" restores it.</summary>
    public static void SetDensity(bool compact)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        resources["RowPadding"] = compact ? new Thickness(8, 3) : new Thickness(8, 6);
        resources["RowPaddingMedium"] = compact ? new Thickness(8, 6) : new Thickness(8, 12);
        resources["RowPaddingTall"] = compact ? new Thickness(8, 8) : new Thickness(8, 16);
    }

    /// <summary>Light palettes switch the built-in controls (text boxes, drop-downs) to their light look as well.</summary>
    public static void SetVariant(bool light)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        // Pastel "good" greens read well on dark panels but vanish on light ones.
        app.Resources["StatusGoodBrush"] = new SolidColorBrush(Color.Parse(light ? "#17794F" : "#86EFC4"));
        app.Resources["StatusGoodSoftBrush"] = new SolidColorBrush(Color.Parse(light ? "#2617794F" : "#296EE7B7"));
    }

    public static void SetTheme(string name, string accent, string background, string sidebar,
        string panel, string border, string text, string subtext, bool light = false)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        resources["ThemeName"] = name;
        SetVariant(light);
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
