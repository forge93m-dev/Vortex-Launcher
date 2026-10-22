using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Network;
using VortexLauncher.Core.Diagnostics;

internal static class SharedGameFilesChecks
{
    internal static async Task RunAsync(GameDownloader downloader, FixtureHandler handler, string source, string manifestUrl)
    {
        var original = SharedGameFiles.Read(source)!;
        var originals = SharedGameFiles.LibrariesFor(source, "fixture")!;
        string instance = Path.Combine(PlatformSupport.DataRoot, "instances", "shared-second");
        var stamps = Directory.GetFiles(SharedGameFiles.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => File.GetLastWriteTimeUtc(p));
        int requests = handler.Requests;
        var timer = Stopwatch.StartNew();
        await downloader.DownloadGameAsync("shared-second", "fixture", manifestUrl, (_, _) => { });
        timer.Stop();
        Check(handler.Requests == requests + 1, "another instance reuses shared game files without downloading them again");
        Check(stamps.All(p => File.GetLastWriteTimeUtc(p.Key) == p.Value) &&
            originals.SequenceEqual(SharedGameFiles.LibrariesFor(instance, "fixture")!) &&
            !Directory.Exists(Path.Combine(instance, "assets")) && !Directory.EnumerateFiles(Path.Combine(instance, "libraries"), "*", SearchOption.AllDirectories).Any(),
            "another instance references the same files; assets and libraries are not copied or rewritten");
        Console.WriteLine($"SHARED_WARM_FIXTURE: {timer.ElapsedMilliseconds} ms; {stamps.Count} existing shared files; zero game-file requests. Not an internet-speed prediction.");

        string loaderUrl = "https://fixture.test/fabric-loader-0.99.0.jar";
        byte[] helper = Jar(("fabric.mod.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"shared-helper\",\"version\":\"1.0.0\"}")));
        handler.Files[loaderUrl] = Jar(("fabric.mod.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"fabricloader\",\"version\":\"0.99.0\",\"jars\":[{\"file\":\"libs/helper.jar\"}]}")), ("libs/helper.jar", helper));
        var profile = new FabricProfile { Libraries = new() { new LibraryItem { Name = "net.fabricmc:fabric-loader:0.99.0", Downloads = new() { Artifact = new() { Url = loaderUrl } } } } };
        await downloader.DownloadLoaderLibrariesAsync(profile, instance, (_, _) => { }, "https://fixture.test/");
        Check(SharedGameFiles.LibrariesFor(instance, "fixture")!.Length == originals.Length + 1 &&
            SharedGameFiles.LibrariesFor(source, "fixture")!.Length == originals.Length,
            "loader libraries belong to the selected instance's classpath, not every shared JAR");
        Check(InstanceRepair.ResolveInstalledLoaderVersion(new() { InstancePath = instance, Version = "fixture", Loader = "Fabric" }) == "0.99.0",
            "repair resolves the selected loader from shared files");
        Directory.CreateDirectory(Path.Combine(instance, "mods"));
        File.WriteAllBytes(Path.Combine(instance, "mods", "dependent.jar"), Jar(("fabric.mod.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"id\":\"dependent\",\"version\":\"1.0.0\",\"depends\":{\"shared-helper\":\"*\"}}"))));
        Check(!(await new ModPreflightService().CheckAsync(instance, "fixture", "Fabric", "0.99.0")).HasErrors,
            "mod checks recognize dependencies bundled inside the selected shared loader");
        await downloader.DownloadLoaderLibrariesAsync(new FabricProfile { Libraries = new() }, instance, (_, _) => { }, "https://fixture.test/");
        Check(SharedGameFiles.LibrariesFor(instance, "fixture")!.Length == originals.Length, "switching loader profiles removes stale classpath entries without deleting another instance's files");
        Check((await new ModPreflightService().CheckAsync(instance, "fixture", "Fabric", "0.99.0")).HasErrors,
            "mod checks do not borrow a dependency from an unselected shared loader");

        string indexUrl = "https://fixture.test/other-index.json";
        byte[] index = JsonSerializer.SerializeToUtf8Bytes(new { objects = new { } });
        handler.Files[indexUrl] = index;
        string secondManifest = "https://fixture.test/other-version.json";
        byte[] clientBytes = handler.Files["https://fixture.test/client.jar"];
        handler.Files[secondManifest] = JsonSerializer.SerializeToUtf8Bytes(new {
            downloads = new { client = new { url = "https://fixture.test/client.jar", sha1 = Sha(clientBytes), size = clientBytes.Length } },
            assetIndex = new { id = "fixture", url = indexUrl, sha1 = Sha(index), size = index.Length }, libraries = Array.Empty<object>() });
        await downloader.DownloadGameAsync("shared-other", "other", secondManifest, (_, _) => { });
        string other = Path.Combine(PlatformSupport.DataRoot, "instances", "shared-other");
        Check(SharedGameFiles.IndexFor(other, "other") != original.AssetIndex &&
            SharedGameFiles.IndexFor(source, "fixture") == original.AssetIndex &&
            SharedGameFiles.LibrariesFor(other, "other")!.Length == 0,
            "versions with the same asset-index ID coexist and do not inherit unrelated shared libraries");
        await downloader.DownloadGameAsync("shared-forge", "fixture", manifestUrl, (_, _) => { }, shareLibraries: false);
        string forge = Path.Combine(PlatformSupport.DataRoot, "instances", "shared-forge");
        Check(SharedGameFiles.LibrariesFor(forge, "fixture") == null &&
            Directory.GetFiles(Path.Combine(forge, "libraries"), "*.jar", SearchOption.AllDirectories).Length == originals.Length &&
            SharedGameFiles.AssetsFor(forge) == SharedGameFiles.Assets,
            "Forge keeps installer-compatible private libraries while sharing assets");
        var expand = typeof(GameLauncher).GetMethod("ExpandForgeArgument", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        string expanded = (string)expand.Invoke(null, new object[] { "${assets_root}|${assets_index_name}", forge,
            Path.Combine(forge, "libraries"), Path.Combine(forge, "natives"), "fixture", "fixture-forge" })!;
        Check(expanded == SharedGameFiles.Assets.Replace('\\', '/') + "|" + original.AssetIndex,
            "Forge launch arguments select the exact shared assets and index");
        Check(SharedGameFiles.Library(loaderUrl, "loader.jar", new string('a', 40)) !=
            SharedGameFiles.Library(loaderUrl, "loader.jar", new string('b', 40)),
            "changed expected library content cannot overwrite another version's shared input");

        string metadata = Path.Combine(source, ".vortex-shared-game.json");
        string before = File.ReadAllText(metadata);
        using (var cancel = new CancellationTokenSource())
        {
            bool stopped = false;
            try { await downloader.DownloadGameAsync(Path.GetFileName(source), "other", secondManifest,
                (_, percent) => { if (percent == 15) cancel.Cancel(); }, cancel.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped && File.ReadAllText(metadata) == before, "cancelled preparation does not publish a partial shared layout");
        }
        var unsafeLayout = new SharedGameFiles.Layout { Version = "fixture", Libraries = new() { "libraries/../../foreign.jar" } };
        bool rejected = false;
        try { SharedGameFiles.Save(instance, unsafeLayout); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "shared metadata rejects paths escaping its owned storage");
        string otherMetadata = Path.Combine(other, ".vortex-shared-game.json");
        File.WriteAllText(otherMetadata, "broken");
        File.WriteAllText(otherMetadata + ".bak", "also broken");
        await downloader.DownloadGameAsync("shared-other", "other", secondManifest, (_, _) => { });
        Check(SharedGameFiles.Read(other)!.Version == "other", "installation rebuilds damaged shared metadata without discarding game content");
        string legacy = Path.Combine(PlatformSupport.DataRoot, "instances", "legacy-layout");
        Check(SharedGameFiles.AssetsFor(legacy) == Path.Combine(legacy, "assets") && SharedGameFiles.LibrariesFor(legacy, "fixture") == null,
            "instances without shared metadata retain their legacy launch layout");
        string dataRoot = PlatformSupport.DataRoot;
        try
        {
            PlatformSupport.DataRootOverride = Path.Combine(dataRoot, "relocated-launcher");
            string relocated = Path.Combine(PlatformSupport.DataRoot, "instances", "portable");
            SharedGameFiles.Save(relocated, original);
            Check(SharedGameFiles.LibrariesFor(relocated, "fixture")!.All(p => p.StartsWith(SharedGameFiles.Root + Path.DirectorySeparatorChar)),
                "relative shared metadata follows the launcher when its folder is moved");
        }
        finally { PlatformSupport.DataRootOverride = dataRoot; }
        Console.WriteLine("SHARED_GAME_FILES_OK");
    }
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes));
    private static byte[] Jar(params (string Name, byte[] Bytes)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files)
            { using var entry = archive.CreateEntry(file.Name).Open(); entry.Write(file.Bytes); }
        return stream.ToArray();
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
