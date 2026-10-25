using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Launch;

if (args.Length >= 1 && args[0] == "--benchmark-network")
{
    await NetworkBenchmark.RunAsync(args.Length > 1 ? args[1] : "1.21.11");
    return;
}

if (args.Length >= 1 && args[0] == "--benchmark-install")
{
    await RealInstallBenchmark.RunAsync(args.Length > 1 ? args[1] : null, args.Contains("forge"));
    return;
}

if (args.Length == 2 && args[0] == "--profile-assets")
{
    await AssetVerificationChecks.ProfileAsync(Path.GetFullPath(args[1]));
    return;
}

SessionWindowChecks.Run();
if (args.Contains("--session-windows")) return;

string testRoot = Directory.CreateDirectory(Path.Combine(FixturePaths.TempRoot(), "vortex-download-smoke-" + Guid.NewGuid().ToString("N"))).FullName;
PlatformSupport.DataRootOverride = Directory.CreateDirectory(Path.Combine(testRoot, "data")).FullName;
DataLocationChecks.Run(testRoot);
await DirectDownloadChecks.RunAsync(testRoot);
await InstallationSpeedChecks.RunAsync(testRoot);
if (args.Contains("--installation-speed")) return;
await ModPreflightChecks.RunAsync(testRoot);
if (args.Contains("--mod-check")) return;
string cache = Path.Combine(testRoot, "cache");
var handler = new FixtureHandler();
using var client = new HttpClient(handler);
byte[] payload = Encoding.UTF8.GetBytes(new string('a', 1024 * 1024));
string hash = Sha(payload);
string url = "https://fixture.test/client.jar";
handler.Files[url] = payload;
string first = Path.Combine(testRoot, "first.jar");
long received = 0;
await DownloadCache.EnsureAsync(url, first, sha1: hash, size: payload.Length, received: n => received += n, httpClient: client, cacheRoot: cache);
Check(received == payload.Length && File.ReadAllBytes(first).SequenceEqual(payload), "cold download preserves bytes and verifies streaming SHA-1");
int requests = handler.Requests;
await DownloadCache.EnsureAsync(url, first, sha1: hash, size: payload.Length, httpClient: client, cacheRoot: cache);
string second = Path.Combine(testRoot, "second.jar");
await DownloadCache.EnsureAsync(url, second, sha1: hash, size: payload.Length, httpClient: client, cacheRoot: cache);
Check(handler.Requests == requests && File.ReadAllBytes(second).SequenceEqual(payload), "warm install reuses verified cache without network");
File.WriteAllBytes(second, Encoding.UTF8.GetBytes(new string('b', payload.Length)));
File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddMinutes(1));
await DownloadCache.EnsureAsync(url, second, sha1: hash, size: payload.Length, httpClient: client, cacheRoot: cache);
Check(File.ReadAllBytes(second).SequenceEqual(payload), "same-length modified destination is repaired, not trusted");
string cachedFile = Path.Combine(cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))));
File.WriteAllBytes(cachedFile, Encoding.UTF8.GetBytes(new string('c', payload.Length)));
File.SetLastWriteTimeUtc(cachedFile, DateTime.UtcNow.AddMinutes(2));
await DownloadCache.EnsureAsync(url, Path.Combine(testRoot, "third.jar"), sha1: hash, size: payload.Length, httpClient: client, cacheRoot: cache);
Check(handler.Requests == requests + 1, "modified cache is revalidated and fetched again");

string badUrl = "https://fixture.test/bad.jar";
handler.Files[badUrl] = payload;
bool rejected = false;
string badDestination = Path.Combine(testRoot, "bad.jar");
try { await DownloadCache.EnsureAsync(badUrl, badDestination, sha1: new string('0', 40), size: payload.Length, httpClient: client, cacheRoot: cache); }
catch (IOException) { rejected = true; }
Check(rejected && !File.Exists(badDestination) && !Directory.EnumerateFiles(cache, "*.part").Any(), "bad checksum never publishes a partial destination");

string concurrentUrl = "https://fixture.test/concurrent.jar";
handler.Files[concurrentUrl] = payload;
requests = handler.Requests;
await Task.WhenAll(Enumerable.Range(0, 8).Select(i => DownloadCache.EnsureAsync(concurrentUrl,
    Path.Combine(testRoot, "concurrent-" + i + ".jar"), sha1: hash, size: payload.Length, httpClient: client, cacheRoot: cache)));
Check(handler.Requests == requests + 1, "concurrent callers download a cache object only once");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    bool stopped = false;
    try { await DownloadCache.EnsureAsync(url, Path.Combine(testRoot, "cancelled.jar"), cancelled.Token, hash, payload.Length, httpClient: client, cacheRoot: cache); }
    catch (OperationCanceledException) { stopped = true; }
    Check(stopped && !File.Exists(Path.Combine(testRoot, "cancelled.jar")), "cancelled installation does not publish a destination");
}
using (var interrupted = new CancellationTokenSource())
{
    string cancelUrl = "https://fixture.test/interrupted.jar";
    handler.Files[cancelUrl] = payload;
    string destination = Path.Combine(testRoot, "interrupted.jar");
    bool stopped = false;
    try
    {
        await DownloadCache.EnsureAsync(cancelUrl, destination, interrupted.Token, hash, payload.Length,
            received: _ => interrupted.Cancel(), httpClient: client, cacheRoot: cache);
    }
    catch (OperationCanceledException) { stopped = true; }
    Check(stopped && !File.Exists(destination) && !Directory.EnumerateFiles(cache, "*.part").Any(),
        "mid-transfer cancellation cleans partial files");
    await DownloadCache.EnsureAsync(cancelUrl, destination, sha1: hash, size: payload.Length, httpClient: client, cacheRoot: cache);
    Check(File.ReadAllBytes(destination).SequenceEqual(payload), "retry after cancellation installs valid content");
}

// Synthetic Minecraft manifest: no Mojang requests, game processes or real instances.
var objects = new Dictionary<string, object>();
for (int i = 0; i < 32; i++)
{
    byte[] bytes = Encoding.UTF8.GetBytes("asset-" + i + new string('x', 2048));
    string sha = Sha(bytes);
    objects["asset" + i] = new { hash = sha, size = bytes.Length };
    handler.Files[$"https://resources.download.minecraft.net/{sha[..2]}/{sha}"] = bytes;
}
byte[] index = JsonSerializer.SerializeToUtf8Bytes(new { objects });
string indexUrl = "https://fixture.test/index.json";
handler.Files[indexUrl] = index;
var libraries = new List<object>();
for (int i = 0; i < 8; i++)
{
    string libUrl = $"https://fixture.test/lib-{i}.jar";
    byte[] bytes = Encoding.UTF8.GetBytes("library-" + i + new string('z', 4096));
    handler.Files[libUrl] = bytes;
    libraries.Add(new { name = $"fixture:lib{i}:1", downloads = new { artifact = new { url = libUrl, path = $"fixture/lib{i}/1/lib{i}-1.jar", sha1 = Sha(bytes), size = bytes.Length } } });
}
string fixtureNativeName = OperatingSystem.IsWindows() ? "fixture.dll" : OperatingSystem.IsMacOS() ? "libfixture.dylib" : "libfixture.so";
string fixtureClassifier = PlatformSupport.MinecraftOs == "osx" ? "macos" : PlatformSupport.MinecraftOs;
if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64) fixtureClassifier += "-arm64";
using (var archiveStream = new MemoryStream())
{
    using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Create, true))
    using (var writer = new StreamWriter(zip.CreateEntry(fixtureNativeName).Open())) writer.Write("native fixture");
    byte[] bytes = archiveStream.ToArray();
    string nativeUrl = "https://fixture.test/native.jar";
    handler.Files[nativeUrl] = bytes;
    libraries.Add(new { name = "org.lwjgl:lwjgl:3", downloads = new { artifact = new { url = nativeUrl,
        path = $"org/lwjgl/lwjgl/3/lwjgl-3-natives-{fixtureClassifier}.jar", sha1 = Sha(bytes), size = bytes.Length } } });
}
string manifestUrl = "https://fixture.test/version.json";
handler.Files[manifestUrl] = JsonSerializer.SerializeToUtf8Bytes(new {
    id = "fixture", downloads = new { client = new { url, sha1 = hash, size = payload.Length } },
    assetIndex = new { id = "fixture", url = indexUrl, sha1 = Sha(index), size = index.Length }, libraries });
var downloader = new GameDownloader(client, Path.Combine(testRoot, "game-cache"));
string instanceName = "download-smoke-" + Guid.NewGuid().ToString("N");
handler.DelayMs = 35;
handler.Peak = 0;
// Hold real batch requests until 17 overlap, rather than relying on CI disk/CPU timing.
handler.MinimumBatchOverlap = 17;
var progress = new ConcurrentQueue<TransferSnapshot>();
var timer = Stopwatch.StartNew();
await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { }, transfer: new TransferProgress(progress.Enqueue));
timer.Stop();
handler.MinimumBatchOverlap = 0;
string instance = Path.Combine(PlatformSupport.DataRoot, "instances", instanceName);
Check(handler.Peak > 16 && handler.Peak <= 24, "one bounded queue overlaps independent file transfers");
Check(Directory.GetFiles(Path.Combine(SharedGameFiles.Assets, "objects"), "*", SearchOption.AllDirectories).Length == 32 &&
    !Directory.Exists(Path.Combine(instance, "assets")), "assets are shared beside the launcher, not copied into each instance");
Check(File.Exists(Path.Combine(instance, "natives", fixtureNativeName)) && File.Exists(Path.Combine(instance, "versions", "fixture", "fixture.jar")),
    "native extraction and Forge-compatible vanilla copy still work");
Check(progress.Last().Percent == 100 && !progress.Any(p => p.Percent < 0 || p.Percent > 100), "aggregate progress finishes at 100 percent");
requests = handler.Requests;
// An obsolete native archive must not overwrite the current manifest's DLL.
string staleNative = Path.Combine(instance, "libraries", $"stale-natives-{fixtureClassifier}.jar");
using (var staleZip = ZipFile.Open(staleNative, ZipArchiveMode.Create))
using (var writer = new StreamWriter(staleZip.CreateEntry(fixtureNativeName).Open())) writer.Write("obsolete native");
await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { });
Check(handler.Requests == requests + 1, "repeat preparation fetches only version metadata, not game files");
Check(File.ReadAllText(Path.Combine(instance, "natives", fixtureNativeName)) == "native fixture", "obsolete native archives are not extracted over current binaries");
string corruptLibrary = SharedGameFiles.Library("https://fixture.test/lib-0.jar", "lib0-1.jar", Sha(handler.Files["https://fixture.test/lib-0.jar"]));
string corruptAsset = Directory.GetFiles(Path.Combine(SharedGameFiles.Assets, "objects"), "*", SearchOption.AllDirectories)[0];
byte[] originalAsset = File.ReadAllBytes(corruptAsset);
File.WriteAllBytes(corruptLibrary, new byte[new FileInfo(corruptLibrary).Length]);
File.WriteAllBytes(corruptAsset, new byte[originalAsset.Length]);
DownloadCache.ForgetVerification(instance);
DownloadCache.ForgetVerification(SharedGameFiles.Root);
requests = handler.Requests;
await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { });
Check(File.ReadAllBytes(corruptLibrary).SequenceEqual(handler.Files["https://fixture.test/lib-0.jar"]) &&
    File.ReadAllBytes(corruptAsset).SequenceEqual(originalAsset) && handler.Requests == requests + 1,
    "installer repairs same-size corrupted assets and libraries from cache without redownloading");
await SharedGameFilesChecks.RunAsync(downloader, handler, instance, manifestUrl);
await AssetVerificationChecks.RunAsync(downloader, instanceName, manifestUrl);
await InstallationPolishChecks.RunAsync(testRoot, handler, client, downloader, fixtureClassifier, fixtureNativeName);
string vanillaJar = Path.Combine(instance, "versions", "fixture", "fixture.jar");
File.WriteAllText(vanillaJar, "corrupt vanilla copy");
await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { });
Check(File.ReadAllBytes(vanillaJar).SequenceEqual(payload), "repair restores a damaged Forge-compatible vanilla JAR copy");
Directory.CreateDirectory(Path.Combine(instance, "saves", "World"));
Directory.CreateDirectory(Path.Combine(instance, "mods"));
File.WriteAllText(Path.Combine(instance, "saves", "World", "level.dat"), "world");
File.WriteAllText(Path.Combine(instance, "mods", "user.jar"), "user mod");
InstanceRepair.ValidateTarget(new VortexLauncher.Models.InstanceModel { InstancePath = instance, Version = "fixture" });
await downloader.DownloadGameAsync(instanceName, "fixture", manifestUrl, (_, _) => { });
Check(File.ReadAllText(Path.Combine(instance, "saves", "World", "level.dat")) == "world" && File.ReadAllText(Path.Combine(instance, "mods", "user.jar")) == "user mod", "managed game repair preserves worlds and user mods");
await MaintenanceChecks.RunAsync(testRoot, handler, client);
await UpdateChecks.RunAsync(testRoot, handler, client);
await StabilityChecks.RunAsync(testRoot);
await ModAndReportChecks.RunAsync(testRoot);
QolChecks.Run(testRoot);
await HardeningChecks.RunAsync(testRoot);
if (args.Length == 2 && args[0] == "--verify-release") await UpdateChecks.VerifyPublishedZipAsync(Path.GetFullPath(args[1]), testRoot);
if (args.Length == 3 && args[0] == "--verify-release")
{
    await UpdateChecks.VerifyPublishedZipAsync(Path.GetFullPath(args[1]), testRoot);
    UpdateChecks.VerifyMacCrossBuild(Path.GetFullPath(args[2]), testRoot);
}
Console.WriteLine($"Fixture cold install: {timer.Elapsed.TotalMilliseconds:0} ms; peak parallel requests: {handler.Peak}");
Console.WriteLine("Timing report: " + Path.Combine(instance, ".vortex-installation-timing.txt"));
Console.WriteLine("DOWNLOAD_SMOKE_OK; fixtures: " + testRoot);

static string Sha(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
static void Check(bool condition, string text) { if (!condition) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }

sealed class FixtureHandler : HttpMessageHandler
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public int Requests, Peak, DelayMs;
    public int MinimumBatchOverlap;
    public bool ManifestRequestedFresh;
    private int _batchArrivals;
    private readonly TaskCompletionSource<bool> _batchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Interlocked.Increment(ref Requests);
        int active = Interlocked.Increment(ref _active);
        int previous;
        do { previous = Peak; if (active <= previous) break; } while (Interlocked.CompareExchange(ref Peak, active, previous) != previous);
        try
        {
            string url = request.RequestUri!.ToString();
            if (request.RequestUri.AbsolutePath.EndsWith("/latest.json", StringComparison.Ordinal))
                ManifestRequestedFresh = request.Headers.CacheControl?.NoCache == true && request.Headers.CacheControl?.NoStore == true;
            int minimum = Volatile.Read(ref MinimumBatchOverlap);
            if (minimum > 0 && (request.RequestUri.Host == "resources.download.minecraft.net" ||
                url.StartsWith("https://fixture.test/lib-", StringComparison.Ordinal) || url == "https://fixture.test/native.jar"))
            {
                if (Interlocked.Increment(ref _batchArrivals) >= minimum) _batchReady.TrySetResult(true);
                await _batchReady.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            }
            if (DelayMs > 0) await Task.Delay(DelayMs, token);
            return Files.TryGetValue(url, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        finally { Interlocked.Decrement(ref _active); }
    }
}
