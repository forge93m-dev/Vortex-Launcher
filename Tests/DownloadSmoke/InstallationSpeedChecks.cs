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
        await ContentBatchChecks(folder, bytes, sha);
    }

    private static async Task ContentBatchChecks(string folder, byte[] bytes, string sha)
    {
        var handler = new FixtureHandler { DelayMs = 35 };
        using var http = new HttpClient(handler);
        const int files = 24;
        for (int i=0;i<files;i++) handler.Files[$"https://fixture.test/content-batch-{i}"] = bytes;
        async Task Download(int index, string batch, CancellationToken token) => await DownloadCache.EnsureAsync(
            $"https://fixture.test/content-batch-{index}", Path.Combine(folder,batch,index.ToString()),token,sha,bytes.Length,
            httpClient:http,cacheRoot:Path.Combine(folder,batch+"-cache"));
        var watch=Stopwatch.StartNew();
        for(int i=0;i<files;i++)await Download(i,"content-serial",default);
        long serial=watch.ElapsedMilliseconds; handler.Peak=0; watch.Restart();
        await DownloadBatch.RunAsync(Enumerable.Range(0,files), (i,t)=>Download(i,"content-parallel",t), default);
        long parallel=watch.ElapsedMilliseconds;
        Check(handler.Peak>1 && handler.Peak<=DownloadBatch.Concurrency,"content staging overlaps downloads but keeps at most eight transfers active");
        int requests=handler.Requests;
        await DownloadBatch.RunAsync(Enumerable.Range(0,files), (i,t)=>Download(i,"content-parallel",t), default);
        Check(handler.Requests==requests,"repeated content staging reuses verified files without network requests");
        Console.WriteLine($"CONTENT_BATCH_BENCHMARK: {files} x 4 KiB, simulated 35 ms latency; serial={serial} ms; parallel={parallel} ms; peak={handler.Peak}. Not an internet-speed prediction.");
        int active=0;
        try { await DownloadBatch.RunAsync(Enumerable.Range(0,24),async(i,t)=>{
            Interlocked.Increment(ref active);
            try { if(i==0){await Task.Delay(15,t);throw new IOException("Fixture staging failure");} await Task.Delay(100,t); }
            finally { Interlocked.Decrement(ref active); }
        },default);throw new Exception("Expected staging failure"); }
        catch(IOException){ }
        Check(active==0,"failed parallel staging waits for all workers before callers can clean temporary files");
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel(); int started=0;
        try { await DownloadBatch.RunAsync(Enumerable.Range(0,24),(i,t)=>{Interlocked.Increment(ref started);return Task.CompletedTask;},cancelled.Token);throw new Exception("Expected cancellation"); }
        catch(OperationCanceledException){ }
        Check(started==0,"cancelled content batch never starts a file transfer");

        string zip=Path.Combine(folder,"archive-verification.jar");
        using(var archive=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create))
        { using var entry=archive.CreateEntry("content.bin").Open(); entry.Write(bytes); }
        string zipHash=Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(zip))); long length=new FileInfo(zip).Length;
        long passes=DownloadCache.ArchiveValidationPasses;
        Check(await DownloadCache.IsValidAsync(zip,zipHash,length,default,true),"archive receives a full validation before being remembered");
        long checkedPasses=DownloadCache.ArchiveValidationPasses;
        for(int i=0;i<20;i++)if(!await DownloadCache.IsValidAsync(zip,zipHash,length,default,true))throw new Exception("Warm archive verification failed");
        Check(checkedPasses>passes && DownloadCache.ArchiveValidationPasses==checkedPasses,"unchanged verified archives are not repeatedly decompressed within the launcher session");
        DownloadCache.ForgetVerification(folder);
        Check(await DownloadCache.IsValidAsync(zip,zipHash,length,default,true) && DownloadCache.ArchiveValidationPasses>checkedPasses,"Repair invalidates archive verification and performs a fresh full check");
        byte[] damaged=await File.ReadAllBytesAsync(zip);damaged[0]=0;await File.WriteAllBytesAsync(zip,damaged);File.SetLastWriteTimeUtc(zip,DateTime.UtcNow.AddSeconds(10));
        Check(!await DownloadCache.IsValidAsync(zip,zipHash,length,default,true),"same-sized changed archive is not trusted by the fast path");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
