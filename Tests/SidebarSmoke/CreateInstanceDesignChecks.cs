using Avalonia.Controls;
using Avalonia.VisualTree;
using VortexLauncher.ViewModels;
using VortexLauncher.Views;

// The new-instance window: loader tiles instead of a drop-down, one-click version chips, a live preview panel.
internal static class CreateInstanceDesignChecks
{
    internal static void Run(CreateInstanceWindow window, Action<int> pump)
    {
        var data = (CreateFixture)window.DataContext!;
        var tiles = window.FindControl<ListBox>("loaderTiles")!;
        Check(tiles.ItemCount == 3 && ReferenceEquals(tiles.SelectedItem, data.SelectedLoaderOption) && ((LoaderOption)tiles.SelectedItem!).Name == "Fabric",
            "loaders are shown as tiles and the current one is selected");
        tiles.SelectedIndex = 2; pump(20);
        Check(data.SelectedLoaderOption.Name == "Forge", "picking a loader tile updates the instance's loader");
        tiles.SelectedIndex = 0; pump(20);
        Check(data.SelectedLoaderOption.Name == "Vanilla", "tiles can be switched back and forth");

        var chips = window.FindControl<ListBox>("quickVersions")!;
        Check(chips.ItemCount == 2, "the newest versions are offered as chips");
        chips.SelectedIndex = 1; pump(20);
        chips.SelectedIndex = 0; pump(20);
        Check(data.SelectedVersion == "26.3", "clicking a version chip selects that version");

        Check(new LoaderOption("Fabric").Tagline == "light, fast" && new LoaderOption("NeoForge").Tagline == "modern" && new LoaderOption("Vanilla").Tagline == "no mods",
            "each loader tile carries a short description");
        var checking = new LoaderOption("Forge", false, "Forge (checking...)");
        Check(checking.Tagline == "checking" && !checking.IsAvailable, "a loader being checked says so instead of its description");
        checking.DisplayName = "Forge (unavailable)";
        Check(checking.Tagline == "unavailable", "a loader that is not available for this version says so");
        checking.DisplayName = "Forge";
        Check(checking.Tagline == "classic", "the description returns once the loader is available");

        var preview = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Check(preview.Contains("PREVIEW") && preview.Contains("Survival") && preview.Contains("Minecraft 26.3 · Fabric"),
            "the side panel previews the new instance's name and configuration");
        Check(preview.Contains("Light and fast. The best choice for Vortex Client."), "the chosen loader's hint is shown under the tiles");
        Console.WriteLine("CREATE_INSTANCE_DESIGN_OK");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
