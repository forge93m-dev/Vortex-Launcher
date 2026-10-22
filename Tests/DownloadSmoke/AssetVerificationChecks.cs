using VortexLauncher.Core.Launch;

internal static class AssetVerificationChecks
{
    // Explicit diagnostic: reads installed asset bytes and writes only the
    // verification ledger. No network, game process or asset modifications.
    internal static async Task ProfileAsync(string dataRoot)
    {
        string? previous = PlatformSupport.DataRootOverride;
        PlatformSupport.DataRootOverride = dataRoot;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var assetPaths = new SharedGameFiles.AssetPaths();
            var files = Directory.GetFiles(Path.Combine(SharedGameFiles.Assets, "objects"), "*", SearchOption.AllDirectories)
                .Select(path => (Path: assetPaths.Resolve(Path.GetFileName(path), false), Hash: Path.GetFileName(path), Size: new FileInfo(path).Length)).ToArray();
            long planning = timer.ElapsedMilliseconds;
            var verified = new AssetVerification();
            timer.Restart();
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 64 }, async (file, token) =>
            {
                if (!await DownloadCache.IsValidAsync(file.Path, file.Hash, file.Size, token))
                    throw new InvalidDataException("Asset verification failed; no verification ledger was published.");
                verified.RememberVerified(file.Path, file.Hash);
            });
            long fullCheck = timer.ElapsedMilliseconds;
            verified.Save();
            timer.Restart();
            var reloaded = new AssetVerification();
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 64 }, (file, _) =>
            {
                if (!reloaded.IsUnchanged(file.Path, file.Hash, file.Size)) throw new Exception("Verified asset was not reusable.");
                return ValueTask.CompletedTask;
            });
            Console.WriteLine($"REAL_LOCAL_ASSET_CHECK: files={files.Length}; bytes={files.Sum(file => file.Size)}; planning={planning}ms; full-check={fullCheck}ms; persisted-metadata-check={timer.ElapsedMilliseconds}ms; reused={reloaded.Hits}; network=0; assets-modified=0");
        }
        finally { PlatformSupport.DataRootOverride = previous; }
    }

    internal static async Task RunAsync(GameDownloader downloader, string instanceName, string manifestUrl)
    {
        string asset = Directory.GetFiles(Path.Combine(SharedGameFiles.Assets, "objects"), "*", SearchOption.AllDirectories)[0];
        string hash = Path.GetFileName(asset);
        byte[] original = File.ReadAllBytes(asset);
        long size = original.Length;
        var remembered = new AssetVerification();
        Check(remembered.IsUnchanged(asset, hash, size) && new AssetVerification().IsUnchanged(asset, hash, size),
            "asset verification survives a new cache object/launcher session without rereading asset content");
        Check(!remembered.IsUnchanged(asset, hash, size + 1) && !remembered.IsUnchanged(asset, new string('f', 40), size),
            "remembered assets still require the exact expected identity and size");
        string library = SharedGameFiles.LibrariesFor(Path.Combine(PlatformSupport.DataRoot, "instances", instanceName), "fixture")![0];
        Check(!remembered.IsUnchanged(library, hash, size), "persistent asset verification never bypasses executable library checks");
        DateTime written = File.GetLastWriteTimeUtc(asset);
        File.WriteAllBytes(asset, new byte[original.Length]);
        File.SetLastWriteTimeUtc(asset, written.AddSeconds(2));
        Check(!remembered.IsUnchanged(asset, hash, size), "same-size modified assets require a fresh checksum");
        await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { });
        Check(File.ReadAllBytes(asset).SequenceEqual(original), "ordinary preparation restores a changed asset rather than trusting its remembered size");
        var stale = new AssetVerification();
        stale.RememberVerified(asset, hash);
        DownloadCache.ForgetVerification(SharedGameFiles.Root);
        stale.Save();
        Check(!stale.IsUnchanged(asset, hash, size) && !new AssetVerification().IsUnchanged(asset, hash, size),
            "Repair resets disk verification and an old in-flight cache cannot restore it");
        await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { });
        File.WriteAllText(Path.Combine(SharedGameFiles.Root, ".verified-assets.json"), "broken metadata");
        Check(!new AssetVerification().IsUnchanged(asset, hash, size), "damaged verification metadata triggers full checks, not a broken install or a stale backup");
        await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { });
        Check(new AssetVerification().IsUnchanged(asset, hash, size), "successful verification safely rebuilds damaged verification metadata");
        string instance = Path.Combine(PlatformSupport.DataRoot, "instances", instanceName);
        using (var timing = new InstallationTiming(instance, "Fixture install"))
        { timing.Mark("Version lookup"); timing.Mark("Loader setup"); timing.Complete(); }
        string report = File.ReadAllText(Path.Combine(instance, ".vortex-installation-stages.txt"));
        Check(report.Contains("Version lookup:") && report.Contains("Loader setup:") && report.Contains("completed: True"),
            "whole-install timing includes metadata and loader phases, not just game downloads");
        using (var timing = new InstallationTiming(instance, "Interrupted fixture")) timing.Mark("Version lookup");
        Check(File.ReadAllText(Path.Combine(instance, ".vortex-installation-stages.txt")).Contains("completed: False"),
            "interrupted installations retain timing without claiming success");
        Console.WriteLine("ASSET_VERIFICATION_OK");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
