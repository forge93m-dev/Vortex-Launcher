using Avalonia.Controls;
using Avalonia.VisualTree;
using VortexLauncher.Core.Launch;
using VortexLauncher.Views;

// The import review window: what the pack is, what it holds, what the launcher fetches and what blocks the import.
internal static class ImportPreviewChecks
{
    private static string[] Texts(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    internal static void Run(ImportPreviewWindow window, Action<int> pump)
    {
        pump(30);
        var texts = Texts(window);
        Check(texts.Contains("Survival") && texts.Contains("PolyMC") && texts.Contains("Minecraft 26.3") && texts.Contains("Fabric 0.19.5"),
            "the review names the pack and shows its format, Minecraft version and loader as chips");
        Check(texts.Contains("12") && texts.Contains("mods") && texts.Contains("resource packs") && texts.Contains("shader packs") && texts.Contains("world"),
            "the review counts mods, resource packs, shader packs and worlds");
        Check(texts.Contains("Everything is inside the pack") && texts.Contains("The pack includes a world"), "a pack without downloads says everything is inside it, and mentions its world");
        Check(window.FindControl<Button>("ImportButton")!.IsEnabled && !window.Blocked, "a normal pack can be imported");
        Check(texts.Any(t => t.StartsWith("Free disk space: 200.0 GB")), "the free disk space is shown");
        Check(window.FindControl<TextBlock>("LogoLetter")!.Text == "S", "the pack's icon carries its first letter");

        // A CurseForge pack needs the user's own key.
        var curse = new ArchivePreview("f", "CurseForge", "Better Adventures", "1.21.1", "NeoForge", "21.1.172", 84, 3, 2, 0, 20, 5_000_000, 87);
        var noKey = new ImportPreviewWindow(curse, false, 100L << 30);
        noKey.Show(); pump(30);
        Check(noKey.Blocked && !noKey.FindControl<Button>("ImportButton")!.IsEnabled && Texts(noKey).Contains("CurseForge key needed"),
            "a CurseForge pack without a key explains what is missing and cannot be imported yet");
        Check(Texts(noKey).Contains("NeoForge 21.1.172") && Texts(noKey).Contains("The launcher fetches 87 files itself") && Texts(noKey).Contains("Some files may need a manual download"),
            "a CurseForge pack says how many files the launcher fetches and warns about manual downloads");
        noKey.Close();
        var withKey = new ImportPreviewWindow(curse, true, 100L << 30);
        withKey.Show(); pump(30);
        Check(!withKey.Blocked && withKey.FindControl<Button>("ImportButton")!.IsEnabled && Texts(withKey).Contains("Your CurseForge key is set"), "with a key the same pack can be imported");
        withKey.Close();

        // Not enough room: needed is the unpacked size plus 20% headroom.
        var big = new ArchivePreview("g", "Modrinth", "Huge", "26.3", "Fabric", "0.19.5", 10, 0, 0, 0, 5, 10L << 30, 0);
        Check(ImportPreviewWindow.Required(big) == (long)((10L << 30) * 1.2), "the space an import needs is its unpacked size plus headroom");
        var tight = new ImportPreviewWindow(big, true, 2L << 30);
        tight.Show(); pump(30);
        Check(tight.Blocked && !tight.FindControl<Button>("ImportButton")!.IsEnabled && Texts(tight).Contains("Not enough disk space"), "an import that does not fit on the disk is blocked and says why");
        tight.Close();
        var unknown = new ImportPreviewWindow(big, true, null);
        unknown.Show(); pump(30);
        Check(!unknown.Blocked && Texts(unknown).Any(t => t.StartsWith("Needed disk space: about 12.0 GB")), "when the free space cannot be read the import is not blocked");
        unknown.Close();

        Check(ImportPreviewWindow.Size(2048) == "2 KB" && ImportPreviewWindow.Size(5L << 20) == "5 MB" && ImportPreviewWindow.Size(3L << 30) == "3.0 GB", "sizes are shown in KB, MB or GB");
        Console.WriteLine("IMPORT_PREVIEW_OK");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
