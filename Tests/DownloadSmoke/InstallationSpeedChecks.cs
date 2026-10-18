using System.Diagnostics;
using System.Security.Cryptography;
using VortexLauncher.Core.Launch;

internal static class InstallationSpeedChecks
{
    internal static async Task RunAsync(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "installation-speed")).FullName;
        byte[] bytes = new byte[4096];
        Random.Shared.NextBytes(bytes);
        string source = Path.Combine(folder, "source");
        await File.WriteAllBytesAsync(source, bytes);
        // Warm the shared pool before comparing managed allocation volumes.
        await DownloadCache.CopyVerifiedFileAsync(source, Path.Combine(folder, "warmup"), default);
        async Task<long> Copies(bool legacy)
        {
            long before = GC.GetTotalAllocatedBytes(true);
            for (int i = 0; i < 128; i++)
            {
                string target = Path.Combine(folder, (legacy ? "old-" : "pooled-") + i);
                if (legacy)
                {
                    await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, true);
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, true);
                    await input.CopyToAsync(output, 256 * 1024);
                }
                else await DownloadCache.CopyVerifiedFileAsync(source, target, default);
                if (!(await File.ReadAllBytesAsync(target)).SequenceEqual(bytes)) throw new Exception("Copy changed file bytes");
            }
            return GC.GetTotalAllocatedBytes(true) - before;
        }
        long oldAllocations = await Copies(true), pooledAllocations = await Copies(false);
        Check(pooledAllocations < oldAllocations / 2, "tiny-file copies allocate less than half the previous managed memory and preserve exact bytes");
        Console.WriteLine($"COPY_ALLOCATION_BENCHMARK: 128 x 4 KiB; old={oldAllocations / 1048576d:0.00} MiB; pooled={pooledAllocations / 1048576d:0.00} MiB");

        var handler = new FixtureHandler { DelayMs = 20 };
        using var client = new HttpClient(handler);
        string sha = Convert.ToHexString(SHA1.HashData(bytes));
        const int count = 256;
        for (int i = 0; i < count; i++) handler.Files[$"https://fixture.test/speed-{i}"] = bytes;
        async Task<(long ms, int peak)> Download(int concurrency, string name)
        {
            handler.Peak = 0;
            var timer = Stopwatch.StartNew();
            await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = concurrency }, async (i, token) =>
            {
                await DownloadCache.EnsureAsync($"https://fixture.test/speed-{i}", Path.Combine(folder, name, i.ToString()), token,
                    sha, bytes.Length, httpClient: client, cacheRoot: Path.Combine(folder, name + "-cache"));
            });
            return (timer.ElapsedMilliseconds, handler.Peak);
        }
        var baseline = await Download(24, "baseline");
        int adaptive = GameDownloader.TransferConcurrency(count);
        var optimized = await Download(adaptive, "optimized");
        Check(adaptive == 64 && optimized.peak > baseline.peak && optimized.peak <= 64 && GameDownloader.TransferConcurrency(32) == 24,
            "large asset sets use a bounded 64-transfer queue; small sets retain the lighter queue");
        int before = handler.Requests;
        await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = adaptive }, async (i, token) =>
            await DownloadCache.EnsureAsync($"https://fixture.test/speed-{i}", Path.Combine(folder, "optimized", i.ToString()), token,
                sha, bytes.Length, httpClient: client, cacheRoot: Path.Combine(folder, "optimized-cache")));
        Check(handler.Requests == before, "repeating the optimized batch makes no network requests");
        Console.WriteLine($"QUEUE_BENCHMARK: 256 x 4 KiB, simulated 20 ms latency; 24 transfers={baseline.ms} ms; 64 transfers={optimized.ms} ms; peak={optimized.peak}. Not an internet-speed prediction.");
        Console.WriteLine("INSTALLATION_SPEED_OK");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
