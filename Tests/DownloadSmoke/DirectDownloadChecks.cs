using System.Net;
using System.Security.Cryptography;
using VortexLauncher.Core.Launch;

// Small checksummed files in the shared store are downloaded straight to their place,
// without the cache copy, while keeping the same verification and atomic publication.
internal static class DirectDownloadChecks
{
    private sealed class Handler(Func<byte[]> body) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body()) });
        }
    }

    internal static async Task RunAsync(string testRoot)
    {
        byte[] asset = RandomNumberGenerator.GetBytes(64 * 1024);
        string hash = Convert.ToHexString(SHA1.HashData(asset)).ToLowerInvariant();
        string url = $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";
        string shared = Path.Combine(SharedGameFiles.Root, "assets", "objects", hash[..2], hash);
        string outside = Path.Combine(testRoot, "instance-only", hash);

        Check(DownloadCache.IsSmallSharedFile(shared, hash, asset.Length, false), "small checksummed shared files take the direct path");
        Check(!DownloadCache.IsSmallSharedFile(outside, hash, asset.Length, false), "files outside the shared store keep the cache path");
        Check(!DownloadCache.IsSmallSharedFile(shared, null, asset.Length, false), "files without a checksum keep the cache path");
        Check(!DownloadCache.IsSmallSharedFile(shared, hash, null, false) && !DownloadCache.IsSmallSharedFile(shared, hash, DownloadCache.SmallFileLimit + 1L, false), "unknown or large sizes keep the resumable cache path");
        Check(!DownloadCache.IsSmallSharedFile(shared, hash, asset.Length, true), "archives that need validation keep the cache path");

        var previous = DownloadCache.DirectClientOverride;
        try
        {
            var good = new Handler(() => asset);
            DownloadCache.DirectClientOverride = new HttpClient(good);
            long before = DownloadCache.DirectSmallDownloads;
            long reported = 0;
            await DownloadCache.EnsureAsync(url, shared, default, hash, asset.Length, received: bytes => Interlocked.Add(ref reported, bytes));
            Check(File.ReadAllBytes(shared).SequenceEqual(asset) && DownloadCache.DirectSmallDownloads == before + 1 && good.Requests == 1, "a direct download writes the exact verified bytes");
            Check(reported == asset.Length, "a direct download reports its bytes to the progress");
            Check(!Directory.EnumerateFiles(Path.GetDirectoryName(shared)!, "*.part").Any(), "a direct download leaves no temporary file");
            Check(!Directory.Exists(DownloadCache.Root) || !Directory.EnumerateFiles(DownloadCache.Root).Any(f => f.Contains(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))))),
                "a direct download keeps no second copy or lock file in the cache");
            await DownloadCache.EnsureAsync(url, shared, default, hash, asset.Length);
            Check(good.Requests == 1, "an already verified shared file is not downloaded again");

            // Corrupted bytes of the right size: rejected after retries, nothing published.
            byte[] corrupted = asset.ToArray(); corrupted[^1] ^= 0x55;
            var bad = new Handler(() => corrupted);
            DownloadCache.DirectClientOverride = new HttpClient(bad);
            string badTarget = Path.Combine(SharedGameFiles.Root, "assets", "objects", "zz", hash);
            bool rejected = false;
            try { await DownloadCache.EnsureAsync(url, badTarget, default, hash, asset.Length); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(badTarget) && bad.Requests == 3, "a corrupted direct download is retried, then rejected without publishing a file");
            Check(!Directory.EnumerateFiles(Path.GetDirectoryName(badTarget)!, "*.part").Any(), "a rejected direct download leaves no temporary file");

            // A server sending more than the declared size is cut off.
            var oversized = new Handler(() => asset.Concat(new byte[16]).ToArray());
            DownloadCache.DirectClientOverride = new HttpClient(oversized);
            string bigTarget = Path.Combine(SharedGameFiles.Root, "assets", "objects", "yy", hash);
            rejected = false;
            try { await DownloadCache.EnsureAsync(url, bigTarget, default, hash, asset.Length); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(bigTarget), "a direct download larger than declared is rejected");

            // Cancellation stops at once and publishes nothing.
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            string cancelTarget = Path.Combine(SharedGameFiles.Root, "assets", "objects", "xx", hash);
            bool stopped = false;
            try { await DownloadCache.EnsureAsync(url, cancelTarget, cancelled.Token, hash, asset.Length); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped && !File.Exists(cancelTarget), "a cancelled direct download publishes nothing");
        }
        finally
        {
            DownloadCache.DirectClientOverride = previous;
            // Later fixtures count the shared objects, so leave the store as it was.
            if (File.Exists(shared)) File.Delete(shared);
            foreach (string folder in new[] { hash[..2], "zz", "yy", "xx" })
            {
                string path = Path.Combine(SharedGameFiles.Root, "assets", "objects", folder);
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            DownloadCache.ForgetVerification(SharedGameFiles.Root);
        }
        Console.WriteLine("DIRECT_DOWNLOAD_OK");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
