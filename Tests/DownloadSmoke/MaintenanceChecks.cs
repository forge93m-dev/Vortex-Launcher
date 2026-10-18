using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;

static class MaintenanceChecks
{
    public static async Task RunAsync(string root, FixtureHandler handler, HttpClient client)
    {
        string work = Directory.CreateDirectory(Path.Combine(root, "maintenance")).FullName;
        var manager = new InstanceManager(Path.Combine(work, "instances"));
        var original = new InstanceModel { Name = "Fixture Fabric", Version = "26.3", Loader = "Fabric", LoaderVersion = "0.19.5" };
        string manifest = JsonSerializer.Serialize(original);
        string zip = Path.Combine(work, "vortex.zip");
        Zip(zip, ("instance.json", manifest), ("mods/example.jar", "mod"), ("resourcepacks/a.zip", "pack"),
            ("shaderpacks/a.zip", "shader"), ("saves/World/level.dat", "world"), ("saves/World/region/r.dat", "region"));
        var preview = ArchivePreview.Inspect(zip);
        Check(preview.Loader == "Fabric" && preview.LoaderVersion == "0.19.5" && preview.Mods == 1 && preview.Worlds == 1 && preview.ResourcePacks == 1 && preview.ShaderPacks == 1, "preview counts content and preserves exact loader metadata");
        Check(!Directory.Exists(Path.Combine(work, "instances")) || !Directory.EnumerateDirectories(Path.Combine(work, "instances")).Any(), "preview does not extract or create instances");
        var imported = await manager.ImportArchiveAsync(zip, preview.Fingerprint);
        Check(imported.LoaderVersion == "0.19.5" && File.ReadAllText(Path.Combine(imported.InstancePath, "saves/World/level.dat")) == "world", "confirmed Vortex import preserves game content and Fabric version");
        int directories = Directory.GetDirectories(Path.Combine(work, "instances")).Length;
        Zip(zip, ("instance.json", manifest), ("mods/changed.jar", "changed"));
        await RejectAsync(() => manager.ImportArchiveAsync(zip, preview.Fingerprint), "changed archive is rejected after preview");
        Check(Directory.GetDirectories(Path.Combine(work, "instances")).Length == directories, "changed archive creates no destination");
        foreach (string path in new[] { "../outside.txt", "/absolute.txt", "mods/CON.jar", "mods/a.jar:evil", "mods/../bad.jar" })
        {
            string bad = Path.Combine(work, "bad-" + Guid.NewGuid().ToString("N") + ".zip");
            Zip(bad, ("instance.json", manifest), (path, "unsafe"));
            Reject(() => ArchivePreview.Inspect(bad), "unsafe archive rejected: " + path);
        }
        string duplicate = Path.Combine(work, "duplicate.zip");
        Zip(duplicate, ("instance.json", manifest), ("mods/A.jar", "a"), ("mods/a.jar", "b"));
        Reject(() => ArchivePreview.Inspect(duplicate), "portable case-colliding ZIP rejected");
        string linked = Path.Combine(work, "linked.zip");
        using (var archive = ZipFile.Open(linked, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("instance.json").Open())) writer.Write(manifest);
            var entry = archive.CreateEntry("mods/link.jar"); entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var output = new StreamWriter(entry.Open()); output.Write("../other-file");
        }
        Reject(() => ArchivePreview.Inspect(linked), "symlink entry rejected before extraction");
        string poly = Path.Combine(work, "poly.zip");
        Zip(poly, ("Pack/mmc-pack.json", "{\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"26.3\"},{\"uid\":\"net.fabricmc.fabric-loader\",\"version\":\"0.19.5\"}]}"),
            ("Pack/instance.cfg", "name=Poly fixture\n"), ("Pack/.minecraft/mods/test.jar", "poly-mod"), ("Pack/.minecraft/saves/Test/level.dat", "poly-world"));
        Check(ArchivePreview.Inspect(poly).Worlds == 1, "PolyMC preview uses the nested Minecraft content directory");
        var polyInstance = await manager.ImportArchiveAsync(poly);
        Check(polyInstance.Loader == "Fabric" && File.Exists(Path.Combine(polyInstance.InstancePath, "mods/test.jar")), "PolyMC import flattens only game content");
        string emptyPoly = Path.Combine(work, "poly-empty.zip");
        Zip(emptyPoly, ("mmc-pack.json", "{\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"26.3\"}]}"));
        Check(ArchivePreview.Inspect(emptyPoly).Mods == 0, "newly created empty PolyMC instance remains importable");
        string mrpack = Path.Combine(work, "test.mrpack");
        Zip(mrpack, ("modrinth.index.json", "{\"game\":\"minecraft\",\"name\":\"MR fixture\",\"dependencies\":{\"minecraft\":\"26.3\",\"fabric-loader\":\"0.19.5\"},\"files\":[]}"),
            ("overrides/options.txt", "base"), ("client-overrides/options.txt", "client"), ("overrides/mods/mod.jar", "jar"));
        var mr = await manager.ImportArchiveAsync(mrpack);
        Check(mr.LoaderVersion == "0.19.5" && File.ReadAllText(Path.Combine(mr.InstancePath, "options.txt")) == "client", "Modrinth overrides and exact loader are imported without swallowed errors");

        string clientRoot = Directory.CreateDirectory(Path.Combine(work, "client-instance")).FullName;
        var instance = new InstanceModel { InstancePath = clientRoot, Version = "26.3", Loader = "Fabric", LoaderVersion = "0.19.5" };
        string mods = Directory.CreateDirectory(Path.Combine(clientRoot, "mods")).FullName;
        string old = Path.Combine(mods, "old-client.jar.disabled");
        Zip(old, ("fabric.mod.json", "{\"id\":\"vortex_client\",\"version\":\"0.1.3\"}"));
        byte[] previous = File.ReadAllBytes(old);
        File.WriteAllText(Path.Combine(mods, "unrelated.jar"), "untouched");
        Directory.CreateDirectory(Path.Combine(clientRoot, "config"));
        File.WriteAllText(Path.Combine(clientRoot, "config/vortex-client.json"), "settings");
        var service = new VortexClientService();
        string? backup = await service.UpdateAsync(instance);
        string updated = Path.Combine(mods, $"vortex-client-26.3-fabric-{VortexClientService.ClientVersion}.jar.disabled");
        Check(backup != null && File.ReadAllBytes(backup).SequenceEqual(previous) && File.Exists(updated) && !File.Exists(old), "client update backs up the original JAR and preserves disabled state");
        Check(File.ReadAllText(Path.Combine(mods, "unrelated.jar")) == "untouched" && File.ReadAllText(Path.Combine(clientRoot, "config/vortex-client.json")) == "settings", "client update preserves other mods and client settings");
        Check(await service.UpdateAsync(instance) == null, "identical bundled client does not create another backup");
        Zip(Path.Combine(mods, "second-client.jar"), ("fabric.mod.json", "{\"id\":\"vortex_client\",\"version\":\"0.1.3\"}"));
        await RejectAsync(() => service.UpdateAsync(instance), "duplicate client copies block the update");
        instance.IsRunning = true;
        await RejectAsync(() => service.UpdateAsync(instance), "running game blocks client mutation");
        instance.IsRunning = false;
        string cancellationRoot = Directory.CreateDirectory(Path.Combine(work, "cancel-client")).FullName;
        var cancelledInstance = new InstanceModel { InstancePath = cancellationRoot, Version = "26.3", Loader = "Fabric" };
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await RejectAsync(() => service.UpdateAsync(cancelledInstance, cancellation.Token), "cancelled update publishes no client JAR");
        Check(!Directory.Exists(Path.Combine(cancellationRoot, "mods")), "pre-cancelled client update leaves instance unchanged");

        string libUrl = "https://fixture.test/repair.jar";
        string validJar = Path.Combine(work, "valid.jar"); Zip(validJar, ("test.class", "valid class"));
        handler.Files[libUrl] = File.ReadAllBytes(validJar);
        string corrupt = Path.Combine(work, "corrupt.jar"); File.WriteAllText(corrupt, "broken jar");
        string cache = Path.Combine(work, "jar-cache");
        await DownloadCache.EnsureAsync(libUrl, corrupt, httpClient: client, cacheRoot: cache, validateArchive: true);
        Check(await DownloadCache.IsValidAsync(corrupt, null, null, default, true), "repair redownloads an unreadable hashless loader library");
        handler.Files["https://fixture.test/broken-repair.jar"] = Encoding.UTF8.GetBytes("not a jar");
        await RejectAsync(() => DownloadCache.EnsureAsync("https://fixture.test/broken-repair.jar", Path.Combine(work, "no.jar"), httpClient: client, cacheRoot: cache, validateArchive: true), "invalid library download cannot be published");

        var warnings = CrashAnalyzer.Diagnose(new[] { "mixin-0.8.7.jar", "WARNING: sun.misc.Unsafe", "Realms failed to parse SignedJWT: 0", "InvalidCredentialsException: Status: 401", "Caused by: MinecraftClientHttpException[type=HTTP_ERROR, status=401]" });
        Check(!warnings.Title.Contains("Mixin", StringComparison.OrdinalIgnoreCase) && warnings.Explanation.Contains("does not establish"), "warnings and first classpath JAR do not become a false crash diagnosis");
        Check(CrashAnalyzer.Diagnose(new[] { "UnsupportedClassVersionError: class file version 69.0" }).Explanation.Contains("Java 25"), "Java mismatch identifies the required version");
        Check(CrashAnalyzer.Diagnose(new[] { "OutOfMemoryError: Java heap space" }).Title == "Not enough memory", "memory error has an actionable diagnosis");
        Check(CrashAnalyzer.Diagnose(new[] { "Mixin apply failed mod.example.json" }).Title == "Mod transformation failed", "real mixin failure is distinguished from library version notices");
        Check(CrashAnalyzer.Diagnose(new[] { "ZipException: invalid LOC header" }).CanRepair, "damaged game component suggests repair");
        Reject(() => InstanceRepair.ValidateTarget(instance), "repair refuses unrelated folders outside this launcher's instance root");
        Console.WriteLine("MAINTENANCE_CHECKS_OK");
    }

    private static void Zip(string path, params (string Path, string Content)[] entries)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, content) in entries) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(content); }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException) { rejected = true; }
        Check(rejected, message);
    }
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false; try { await action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or OperationCanceledException) { rejected = true; }
        Check(rejected, message);
    }
}
