using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VortexLauncher.Core.Theme;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

// The Appearance settings: accent swatches, palettes (including one light), list density and a live preview.
internal static class AppearanceChecks
{
    private static double Luminance(Color c)
    {
        static double Channel(byte value) { double v = value / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
    private static double Contrast(string a, string b)
    {
        double x = Luminance(Color.Parse(a)), y = Luminance(Color.Parse(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    internal static void Run(MainWindow window, SidebarFixture fixture, Action<int> pump, string output)
    {
        window.Width = 1280; window.Height = 900; fixture.Select("Settings"); pump(250);
        window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 0; pump(150);
        var swatches = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("AccentSwatch")).ToList();
        Check(swatches.Count == 8 && swatches.All(b => b.Bounds.Width >= 28 && b.Background is ISolidColorBrush), "the Appearance page offers eight accent colors as round swatches");
        Check(window.FindControl<Button>("densityComfortable")!.Classes.Contains("active") && !window.FindControl<Button>("densityCompact")!.Classes.Contains("active"),
            "the current list density is marked");
        var preview = window.FindControl<Border>("appearancePreview")!;
        Check(preview.IsEffectivelyVisible && preview.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Survival") && preview.GetVisualDescendants().OfType<Button>().Any(b => (b.Content as string) == "Play"),
            "a live preview card shows the chosen look");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "appearance-settings.png"));

        // Density changes the row paddings that long lists use.
        var resources = Application.Current!.Resources;
        ThemeManager.SetDensity(true);
        Check((Thickness)resources["RowPadding"]! == new Thickness(8, 3) && (Thickness)resources["RowPaddingMedium"]! == new Thickness(8, 6) && (Thickness)resources["RowPaddingTall"]! == new Thickness(8, 8), "compact density halves the vertical padding of list rows");
        ThemeManager.SetDensity(false);
        Check((Thickness)resources["RowPadding"]! == new Thickness(8, 6) && (Thickness)resources["RowPaddingMedium"]! == new Thickness(8, 12) && (Thickness)resources["RowPaddingTall"]! == new Thickness(8, 16), "comfortable density restores the usual padding");

        // Every built-in palette keeps text readable; exactly one is light.
        var presets = ThemePresetViewModel.BuiltIn();
        Check(presets.Count == presets.Select(p => p.Name).Distinct().Count() && presets.Count(p => p.IsLight) == 1 && presets.Single(p => p.IsLight).Name == "Light", "palette names are unique and exactly one palette is light");
        foreach (var preset in presets)
        {
            Check(Contrast(preset.Text, preset.Background) >= 7 && Contrast(preset.Text, preset.Panel) >= 7 && Contrast(preset.Subtext, preset.Background) >= 4.5 && Contrast(preset.Subtext, preset.Panel) >= 4.5,
                $"palette {preset.Name}: text and secondary text stay readable on its background and panels");
            Check(preset.IsLight == (Luminance(Color.Parse(preset.Background)) > 0.5), $"palette {preset.Name}: only a bright background is marked light");
        }

        // A light palette switches the built-in controls too, and going back restores the dark look.
        var light = presets.Single(p => p.IsLight);
        ThemeManager.SetTheme(light.Name, light.HexColor, light.Background, light.Sidebar, light.Panel, light.Border, light.Text, light.Subtext, light.IsLight); pump(120);
        Check(Application.Current.RequestedThemeVariant == ThemeVariant.Light && Equals(resources["ThemeName"], "Light"), "choosing the light palette switches the control theme to light");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "appearance-light.png"));
        fixture.Select("Instances"); pump(150);
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "library-light.png"));
        var status = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("HubStatus")).GetVisualDescendants().OfType<TextBlock>().First();
        Check(status.Foreground is ISolidColorBrush good && Contrast("#" + good.Color.ToString().Substring(3), light.Background) >= 4.5, "the green status chip stays readable on the light palette");
        var createLight = new CreateInstanceWindow(new CreateFixture()); createLight.Show(); pump(60);
        createLight.CaptureRenderedFrame()!.Save(Path.Combine(output, "create-light.png")); createLight.Close();
        var importLight = new ImportPreviewWindow(new VortexLauncher.Core.Launch.ArchivePreview("f", "CurseForge", "Better Adventures", "1.21.1", "NeoForge", "21.1.172", 84, 3, 2, 1, 20, 5_000_000, 87), true, 100L << 30);
        importLight.Show(); pump(60); importLight.CaptureRenderedFrame()!.Save(Path.Combine(output, "import-light.png")); importLight.Close();
        var dark = presets[0];
        ThemeManager.SetTheme(dark.Name, dark.HexColor, dark.Background, dark.Sidebar, dark.Panel, dark.Border, dark.Text, dark.Subtext, dark.IsLight); pump(120);
        Check(Application.Current.RequestedThemeVariant == ThemeVariant.Dark, "choosing a dark palette switches the control theme back to dark");
        Console.WriteLine("APPEARANCE_OK");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
