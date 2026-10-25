using Avalonia.Controls;
using Avalonia.VisualTree;
using VortexLauncher.Views;

// The CurseForge key field in the launcher Settings: present, hidden while typing, and explained.
internal static class CurseForgeSettingsChecks
{
    internal static void Run(MainWindow window, SidebarFixture fixture, Action<int> pump)
    {
        window.Width = 1280; window.Height = 800; fixture.Select("Settings"); pump(250);
        window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 2; pump(120);
        var box = window.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.Name == "curseForgeKeyBox");
        Check(box != null && box.PasswordChar == '•', "the CurseForge API key field exists and hides what is typed");
        Check(box!.Watermark!.ToString()!.Contains("optional"), "the key is optional: other packs import without it");
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("console.curseforge.com") == true && t.Text.Contains("stays on this computer")), "the Settings page says where to get the key and that it stays on this computer");
        fixture.Select("Instances"); pump(60);
        Console.WriteLine("CURSEFORGE_SETTINGS_OK");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
