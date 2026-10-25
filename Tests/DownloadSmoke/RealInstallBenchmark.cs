using System.Diagnostics;
using System.Text.Json;
using VortexLauncher.Core.Launch;

// Manual benchmark, never part of the default run: installs a real vanilla version from
// Mojang into a throwaway data folder and prints the downloader's own timing report.
// Usage: dotnet run --project Tests/DownloadSmoke -- --benchmark-install [version]
internal static class RealInstallBenchmark
{
    internal static async Task RunAsync(string? version, bool forge = false)
    {
        string root = Directory.CreateDirectory(Path.Combine(FixturePaths.TempRoot(), "vortex-install-benchmark-" + Guid.NewGuid().ToString("N"))).FullName;
        PlatformSupport.DataRootOverride = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        using var http = new HttpClient();
        using var manifest = JsonDocument.Parse(await http.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
        version ??= manifest.RootElement.GetProperty("latest").GetProperty("release").GetString()!;
        string url = manifest.RootElement.GetProperty("versions").EnumerateArray()
            .First(v => v.GetProperty("id").GetString() == version).GetProperty("url").GetString()!;
        Console.WriteLine($"Installing Minecraft {version} into {root}");
        var clock = Stopwatch.StartNew();
        await new GameDownloader().DownloadGameAsync("benchmark", version, url, (_, _) => { }, shareLibraries: !forge);
        Console.WriteLine($"MINECRAFT_FILES_SECONDS={clock.Elapsed.TotalSeconds:0.0}");
        Console.WriteLine(File.ReadAllText(Path.Combine(PlatformSupport.DataRoot, "instances", "benchmark", ".vortex-installation-timing.txt")));
        if (forge)
        {
            // Mirrors MainWindowViewModel.InstallInstanceAsync for Forge.
            var stage = Stopwatch.StartNew();
            string java = await new JavaRuntimeManager().EnsureJavaAsync(JavaResolver.GetRequiredJavaVersion(version));
            Console.WriteLine($"JAVA_SECONDS={stage.Elapsed.TotalSeconds:0.0}");
            var service = new VortexLauncher.Core.Network.ForgeService();
            string forgeVersion = (await service.GetPromotedForgeVersionAsync(version))!;
            stage.Restart();
            string lastStatus = "";
            var statusClock = Stopwatch.StartNew();
            bool installed = await service.InstallForgeProfileAsync(forgeVersion, version, Path.Combine(PlatformSupport.DataRoot, "instances", "benchmark"), java,
                (status, _) =>
                {
                    if (status == lastStatus) return;
                    Console.WriteLine($"  [{stage.Elapsed.TotalSeconds,6:0.0}s] {status}");
                    lastStatus = status;
                });
            Console.WriteLine($"FORGE {forgeVersion} installed={installed} FORGE_SECONDS={stage.Elapsed.TotalSeconds:0.0}");
        }
        clock.Stop();
        Console.WriteLine($"WALL_SECONDS={clock.Elapsed.TotalSeconds:0.0}");
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
