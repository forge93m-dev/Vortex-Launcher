using VortexLauncher.Core.Management;

// The read-only look at an instance used by the workspace overview and its sidebar counts.
internal static class InstanceContentChecks
{
    internal static void Run(string testRoot)
    {
        string root = Directory.CreateDirectory(Path.Combine(testRoot, "content-summary")).FullName;
        Check(InstanceContentSummary.Read(Path.Combine(root, "missing")) == InstanceContentSummary.Empty && InstanceContentSummary.Read("") == InstanceContentSummary.Empty,
            "an instance folder that does not exist counts as empty");
        string instance = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
        Check(InstanceContentSummary.Read(instance).Mods == 0 && InstanceContentSummary.Read(instance).Worlds == 0, "a fresh instance holds nothing");

        string mods = Directory.CreateDirectory(Path.Combine(instance, "mods")).FullName;
        foreach (string name in new[] { "sodium-0.6.jar", "Lithium-0.12.jar", "iris-1.7.jar", "old-mod.jar.disabled", "readme.txt", "notes.jar.bak" }) File.WriteAllText(Path.Combine(mods, name), "x");
        Directory.CreateDirectory(Path.Combine(mods, "config-folder.jar"));
        string packs = Directory.CreateDirectory(Path.Combine(instance, "resourcepacks")).FullName;
        File.WriteAllText(Path.Combine(packs, "faithful.zip"), "x"); File.WriteAllText(Path.Combine(packs, "notes.txt"), "x"); Directory.CreateDirectory(Path.Combine(packs, "unpacked"));
        string shaders = Directory.CreateDirectory(Path.Combine(instance, "shaderpacks")).FullName;
        File.WriteAllText(Path.Combine(shaders, "bsl.zip"), "x");
        string saves = Directory.CreateDirectory(Path.Combine(instance, "saves")).FullName;
        foreach (var (name, days) in new[] { ("Old world", 20), ("Hardcore", 3), ("New world", 0) })
        {
            string level = Path.Combine(Directory.CreateDirectory(Path.Combine(saves, name)).FullName, "level.dat");
            File.WriteAllText(level, "x"); File.SetLastWriteTime(level, DateTime.Now.AddDays(-days));
        }
        Directory.CreateDirectory(Path.Combine(saves, "not a world"));
        Directory.CreateDirectory(Path.Combine(saves, "newest-without-level"));

        var summary = InstanceContentSummary.Read(instance);
        Check(summary.Mods == 3 && summary.DisabledMods == 1, "mods are counted by their .jar files; a disabled one is counted separately");
        Check(summary.ModNames.SequenceEqual(new[] { "iris-1.7", "Lithium-0.12", "sodium-0.6" }), "mod names drop the extension and are sorted without regard to case");
        Check(summary.ResourcePacks == 2 && summary.ShaderPacks == 1, "resource and shader packs count zip files and unpacked folders, not other files");
        Check(summary.Worlds == 3 && summary.RecentWorlds.Select(w => w.Name).SequenceEqual(new[] { "New world", "Hardcore", "Old world" }),
            "only folders with a level.dat are worlds, and the most recently played come first");
        Check(InstanceContentSummary.DisplayName("a-b.jar.disabled") == "a-b" && InstanceContentSummary.DisplayName("plain") == "plain", "file names are shown without .jar or .disabled");

        for (int i = 0; i < 5; i++)
        {
            string level = Path.Combine(Directory.CreateDirectory(Path.Combine(saves, "extra" + i)).FullName, "level.dat");
            File.WriteAllText(level, "x");
        }
        var many = InstanceContentSummary.Read(instance);
        Check(many.Worlds == 8 && many.RecentWorlds.Count == 3, "the overview lists at most the three newest worlds while the count stays exact");
        Console.WriteLine("INSTANCE_CONTENT_OK");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
