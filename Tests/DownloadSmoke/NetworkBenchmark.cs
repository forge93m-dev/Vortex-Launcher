using System.Diagnostics;
using System.Net;
using System.Text.Json;

// Manual benchmark: downloads a version's assets straight into memory (no disk, no hashing)
// with several client settings, to separate the network limit from launcher overhead.
// Usage: dotnet run --project Tests/DownloadSmoke -- --benchmark-network [version]
internal static class NetworkBenchmark
{
    internal static async Task RunAsync(string version)
    {
        using var meta = new HttpClient();
        using var manifest = JsonDocument.Parse(await meta.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
        string url = manifest.RootElement.GetProperty("versions").EnumerateArray().First(v => v.GetProperty("id").GetString() == version).GetProperty("url").GetString()!;
        using var versionJson = JsonDocument.Parse(await meta.GetStringAsync(url));
        using var index = JsonDocument.Parse(await meta.GetStringAsync(versionJson.RootElement.GetProperty("assetIndex").GetProperty("url").GetString()!));
        var assets = index.RootElement.GetProperty("objects").EnumerateObject()
            .Select(o => o.Value.GetProperty("hash").GetString()!).Distinct()
            .Select(h => $"https://resources.download.minecraft.net/{h[..2]}/{h}").ToArray();
        Console.WriteLine($"{version}: {assets.Length} assets");
        foreach (var (name, http2, multi, parallel) in new[] { ("HTTP/2 one connection", true, false, 64), ("HTTP/2 many connections", true, true, 64), ("HTTP/1.1", false, false, 64), ("HTTP/1.1", false, false, 128), ("HTTP/2 many connections", true, true, 128) })
        {
            using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = parallel, EnableMultipleHttp2Connections = multi, PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
            { DefaultRequestVersion = http2 ? HttpVersion.Version20 : HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            long bytes = 0;
            var clock = Stopwatch.StartNew();
            await Parallel.ForEachAsync(assets, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (asset, token) =>
            {
                byte[] data = await client.GetByteArrayAsync(asset, token);
                Interlocked.Add(ref bytes, data.Length);
            });
            Console.WriteLine($"{name,-24} x{parallel,-4} {clock.Elapsed.TotalSeconds,6:0.00}s  {bytes / 1048576d / clock.Elapsed.TotalSeconds,6:0.0} MB/s");
        }
    }
}
