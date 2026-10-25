using System.IO.Compression;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;

// NeoForge: which versions belong to which Minecraft version, safe installer extraction, an already finished install,
// repair's version lookup and the pack formats that name NeoForge. The installer itself is not run here (it needs the network).
internal static class NeoForgeChecks
{
    private static void Zip(string path, params (string Path, string Content)[] entries)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, content) in entries) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(content); }
    }

    internal static async Task RunAsync(string testRoot)
    {
        string work = Directory.CreateDirectory(Path.Combine(testRoot, "neoforge")).FullName;

        Check(ForgeService.NeoForgePrefix("1.21.1") == "21.1." && ForgeService.NeoForgePrefix("1.21") == "21.0." && ForgeService.NeoForgePrefix("1.20.4") == "20.4." &&
              ForgeService.NeoForgePrefix("1.20.2") == "20.2." && ForgeService.NeoForgePrefix("1.21.11") == "21.11.", "Minecraft 1.x versions map to NeoForge's 21.1.x style prefix");
        Check(ForgeService.NeoForgePrefix("26.1") == "26.1.0." && ForgeService.NeoForgePrefix("26.1.2") == "26.1.2." && ForgeService.NeoForgePrefix("26.3") == "26.3.0.", "Minecraft 26.x versions map to NeoForge's 26.1.0.x style prefix");
        Check(ForgeService.NeoForgePrefix("1.20.1") == null && ForgeService.NeoForgePrefix("1.20") == null && ForgeService.NeoForgePrefix("1.19.4") == null && ForgeService.NeoForgePrefix("1.12.2") == null,
            "Minecraft versions NeoForge never published (1.20.1 and older) are reported as unavailable");
        Check(ForgeService.NeoForgePrefix("") == null && ForgeService.NeoForgePrefix("24w14a") == null && ForgeService.NeoForgePrefix("1.21.x") == null && ForgeService.NeoForgePrefix("../1.21") == null, "snapshots and odd version text are never mapped");

        var all = new[] { "21.1.9", "21.1.172", "21.1.80", "21.1.173-beta", "21.0.5", "21.11.4", "26.1.0.0-alpha.1+snapshot-1", "26.1.0.19-beta", "26.1.0.3-beta", "26.1.2.124", "26.1.2.9", "21.1.9/../x", "21.1.1+evil" };
        var forOneTwentyOneOne = ForgeService.NeoForgeVersionsFor("1.21.1", all);
        Check(forOneTwentyOneOne.SequenceEqual(new[] { "21.1.172", "21.1.80", "21.1.9", "21.1.173-beta" }), "NeoForge builds compare by number (172 beats 80), stable builds come before betas, and unsafe names are dropped");
        Check(ForgeService.NeoForgeVersionsFor("26.1", all).SequenceEqual(new[] { "26.1.0.19-beta", "26.1.0.3-beta" }), "alpha snapshots are never offered and a beta is used only when no stable build exists");
        Check(ForgeService.NeoForgeVersionsFor("26.1.2", all).First() == "26.1.2.124" && ForgeService.NeoForgeVersionsFor("1.20.1", all).Count == 0, "the newest stable build is first and unsupported versions have none");
        Check(ForgeService.IsSafeNeoForgeVersion("21.1.172") && ForgeService.IsSafeNeoForgeVersion("26.1.0.19-beta") &&
              !ForgeService.IsSafeNeoForgeVersion("") && !ForgeService.IsSafeNeoForgeVersion("../21.1.1") && !ForgeService.IsSafeNeoForgeVersion("21.1/1") &&
              !ForgeService.IsSafeNeoForgeVersion("21.1.1?x=1") && !ForgeService.IsSafeNeoForgeVersion(new string('1', 41)), "only plain version text may reach a download address or a folder name");

        // Installer contents never leave the libraries folder.
        string libraries = Directory.CreateDirectory(Path.Combine(work, "libs")).FullName;
        string good = Path.Combine(work, "good-installer.jar");
        Zip(good, ("version.json", "{\"id\":\"neoforge-21.1.172\",\"libraries\":[]}"), ("maven/net/neoforged/neoforge/21.1.172/neoforge-21.1.172-universal.jar", "jar"), ("data/readme.txt", "x"));
        string? versionJson = ForgeService.ExtractNeoForgeInstaller(good, libraries, CancellationToken.None);
        Check(versionJson != null && versionJson.Contains("neoforge-21.1.172") && File.Exists(Path.Combine(libraries, "net", "neoforged", "neoforge", "21.1.172", "neoforge-21.1.172-universal.jar")),
            "the installer's version profile is read and its bundled libraries are placed under libraries");
        string evil = Path.Combine(work, "evil-installer.jar");
        Zip(evil, ("version.json", "{}"), ("maven/../../outside.jar", "x"));
        bool refused = false;
        try { ForgeService.ExtractNeoForgeInstaller(evil, libraries, CancellationToken.None); } catch (InvalidDataException) { refused = true; }
        Check(refused && !File.Exists(Path.Combine(work, "outside.jar")) && !File.Exists(Path.Combine(Path.GetDirectoryName(libraries)!, "outside.jar")), "an installer that tries to write outside the libraries folder is refused");

        // A finished install is recognised without any network use; a bad version never reaches the network.
        string instance = Directory.CreateDirectory(Path.Combine(work, "instance")).FullName;
        string profileDir = Directory.CreateDirectory(Path.Combine(instance, "versions", "neoforge-21.1.172")).FullName;
        string libs = Path.Combine(instance, "libraries");
        string client = Path.Combine(libs, "net", "neoforged", "neoforge", "21.1.172", "neoforge-21.1.172-client.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(client)!);
        string dep = Path.Combine(libs, "org", "example", "lib", "1.0", "lib-1.0.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(dep)!);
        File.WriteAllText(client, "client"); File.WriteAllText(dep, "dep");
        File.WriteAllText(Path.Combine(profileDir, "neoforge-21.1.172.json"),
            "{\"id\":\"neoforge-21.1.172\",\"libraries\":[{\"name\":\"org.example:lib:1.0\",\"downloads\":{\"artifact\":{\"path\":\"org/example/lib/1.0/lib-1.0.jar\",\"url\":\"https://example.invalid/lib.jar\"}}}]}");
        var stages = new List<string>();
        bool ready = await new ForgeService().InstallNeoForgeProfileAsync("21.1.172", "1.21.1", instance, "java-that-does-not-exist", (s, _) => stages.Add(s));
        Check(ready && stages.Any(s => s.Contains("NeoForge setup ready")), "an instance whose NeoForge is already installed is recognised without downloading or running anything");
        bool badVersion = false;
        try { await new ForgeService().InstallNeoForgeProfileAsync("../evil", "1.21.1", instance, "java", (_, _) => { }); } catch (InvalidOperationException) { badVersion = true; }
        Check(badVersion, "a NeoForge version with path or address characters is refused before any download");

        // Repair finds the installed NeoForge version from the libraries folder.
        var model = new InstanceModel { Name = "Neo", Version = "1.21.1", Loader = "NeoForge", InstancePath = instance };
        Check(InstanceRepair.ResolveInstalledLoaderVersion(model) == "21.1.172", "repair reads the installed NeoForge version from the libraries folder");
        model.Version = "1.21.4";
        Check(InstanceRepair.ResolveInstalledLoaderVersion(model) == "", "a NeoForge build for another Minecraft version is not mistaken for this instance's");

        // Pack formats.
        string mrpack = Path.Combine(work, "neo.mrpack");
        Zip(mrpack, ("modrinth.index.json", "{\"game\":\"minecraft\",\"name\":\"Neo pack\",\"dependencies\":{\"minecraft\":\"1.21.1\",\"neoforge\":\"21.1.172\"},\"files\":[]}"));
        var preview = ArchivePreview.Inspect(mrpack);
        Check(preview.Loader == "NeoForge" && preview.LoaderVersion == "21.1.172" && preview.Version == "1.21.1", "a Modrinth .mrpack that needs NeoForge is recognised");
        Console.WriteLine("NEOFORGE_OK");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
