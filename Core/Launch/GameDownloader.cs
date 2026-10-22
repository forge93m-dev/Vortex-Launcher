using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Network;

namespace VortexLauncher.Core.Launch;

public class GameDownloader
{
    internal static int TransferConcurrency(int files) => files >= 128 ? 64 : 24;
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient HttpClient;
    private readonly string? _cacheRoot;
    public GameDownloader() : this(DefaultHttpClient, null) { }
    internal GameDownloader(HttpClient httpClient, string? cacheRoot)
    {
        HttpClient = httpClient;
        _cacheRoot = cacheRoot;
    }

    public async Task DownloadGameAsync(string instanceName, string version, string versionJsonUrl, Action<string, double> progressCallback,
        CancellationToken cancellationToken = default, TransferProgress? transfer = null, bool shareLibraries = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string instancePath = Path.Combine(PlatformSupport.DataRoot, "instances", instanceName);
        Directory.CreateDirectory(instancePath);

        string clientJarPath = Path.Combine(instancePath, $"{version}.jar");

        progressCallback?.Invoke("Fetching version metadata...", 5);
        string versionJson = await HttpClient.GetStringAsync(versionJsonUrl, cancellationToken);

        using var doc = JsonDocument.Parse(versionJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("downloads", out var requiredDownloads) ||
            !requiredDownloads.TryGetProperty("client", out var requiredClient) ||
            !requiredClient.TryGetProperty("url", out var requiredClientUrl) ||
            requiredClientUrl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(requiredClientUrl.GetString()))
            throw new InvalidDataException("Minecraft version metadata is incomplete: the client download URL is missing. Retry installation.");
        var gameArchitecture = PlatformSupport.GetGameArchitecture(root);

        // Forge's official installer expects the vanilla profile under the
        // standard versions/<id> directory so it can run its client processors.
        string versionsDir = Path.Combine(instancePath, "versions", version);
        Directory.CreateDirectory(versionsDir);
        await File.WriteAllTextAsync(Path.Combine(versionsDir, $"{version}.json"), versionJson, cancellationToken);

        var targets = new List<(string url, string path, string? hash, long? size)>();
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        if (root.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("client", out var clientProp) &&
            clientProp.TryGetProperty("url", out var clientUrlProp) &&
            !string.IsNullOrEmpty(clientUrlProp.GetString()))
            targets.Add((clientUrlProp.GetString()!, clientJarPath,
                clientProp.TryGetProperty("sha1", out var clientHash) ? clientHash.GetString() : null,
                clientProp.TryGetProperty("size", out var clientSize) ? clientSize.GetInt64() : null));

        var previousLayout = SharedGameFiles.Read(instancePath, allowDamaged: true);
        var layout = new SharedGameFiles.Layout { Version = version, SharedLibraries = shareLibraries };
        if (previousLayout?.Version == version && previousLayout.SharedLibraries == shareLibraries)
            layout.LoaderLibraries = previousLayout.LoaderLibraries;
        string assetsDir = SharedGameFiles.Assets;
        string assetsIndexesDir = Path.Combine(assetsDir, "indexes");
        string assetsObjectsDir = Path.Combine(assetsDir, "objects");
        Directory.CreateDirectory(assetsIndexesDir);
        Directory.CreateDirectory(assetsObjectsDir);
        var assetPaths = new SharedGameFiles.AssetPaths();

        if (root.TryGetProperty("assetIndex", out var assetIndexProp))
        {
            if (assetIndexProp.TryGetProperty("url", out var indexUrlProp) &&
                assetIndexProp.TryGetProperty("id", out var indexIdProp))
            {
                string indexUrl = indexUrlProp.GetString() ?? "";
                string indexId = SharedGameFiles.Key(indexUrl + "|" + (assetIndexProp.TryGetProperty("sha1", out var indexIdentity) ? indexIdentity.GetString() : version));
                layout.AssetIndex = indexId;
                string indexPath = SharedGameFiles.Checked(Path.Combine(assetsIndexesDir, $"{indexId}.json"));

                string indexJsonContent = string.Empty;
                if (!string.IsNullOrEmpty(indexUrl))
                    await DownloadCache.EnsureAsync(indexUrl, indexPath, cancellationToken,
                        assetIndexProp.TryGetProperty("sha1", out var indexHash) ? indexHash.GetString() : null,
                        assetIndexProp.TryGetProperty("size", out var indexSize) ? indexSize.GetInt64() : null,
                        httpClient: _cacheRoot == null ? null : HttpClient, cacheRoot: _cacheRoot);
                if (File.Exists(indexPath))
                    indexJsonContent = await File.ReadAllTextAsync(indexPath, cancellationToken);

                if (!string.IsNullOrEmpty(indexJsonContent))
                {
                    using var indexDoc = JsonDocument.Parse(indexJsonContent);
                    if (indexDoc.RootElement.TryGetProperty("objects", out var objectsProp))
                    {
                        var assetTargets = new List<(string url, string path, string? hash, long? size)>();
                        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var obj in objectsProp.EnumerateObject())
                        {
                            if (obj.Value.TryGetProperty("hash", out var hashProp))
                            {
                                string hash = hashProp.GetString() ?? "";
                                if (hash.Length == 40 && hash.All(Uri.IsHexDigit))
                                {
                                    string prefix = hash.Substring(0, 2);

                                    long? objectSize = obj.Value.TryGetProperty("size", out var sizeProp)
                                        ? sizeProp.GetInt64() : null;
                                    if (seenHashes.Add(hash))
                                    {
                                        string objFile = assetPaths.Resolve(hash);
                                        assetTargets.Add(($"https://resources.download.minecraft.net/{prefix}/{hash}", objFile, hash, objectSize));
                                    }
                                }
                            }
                        }

                        targets.AddRange(assetTargets);
                    }
                }
            }
        }

        string librariesDir = Path.Combine(instancePath, "libraries");
        string nativesDir = Path.Combine(instancePath, "natives");
        Directory.CreateDirectory(librariesDir);
        Directory.CreateDirectory(nativesDir);

        var nativeArchives = new HashSet<string>(StringComparer.Ordinal);

        if (root.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array)
        {
            var libTargets = new List<(string url, string path, string? hash, long? size)>();

            foreach (var lib in libraries.EnumerateArray())
            {
                if (!PlatformSupport.RulesAllow(lib, architecture: gameArchitecture)) continue;
                if (lib.TryGetProperty("downloads", out var libDownloads) &&
                    libDownloads.TryGetProperty("artifact", out var artifact) &&
                    artifact.TryGetProperty("url", out var artifactUrl) &&
                    artifact.TryGetProperty("path", out var artifactPath))
                {
                    string url = artifactUrl.GetString() ?? string.Empty;
                    string relativePath = artifactPath.GetString() ?? string.Empty;

                    if (relativePath.Contains("-natives-", StringComparison.OrdinalIgnoreCase) &&
                        !PlatformSupport.MatchesNativePath(relativePath, architecture: gameArchitecture)) continue;

                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(relativePath))
                    {
                        // Forge needs its Maven hierarchy; other loaders use immutable shared inputs.
                        string? expectedHash = artifact.TryGetProperty("sha1", out var artifactHash) ? artifactHash.GetString() : null;
                        string destPath = shareLibraries ? SharedGameFiles.Library(url, relativePath, expectedHash)
                            : Path.Combine(librariesDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        if (shareLibraries) layout.Libraries.Add(SharedGameFiles.Relative(destPath));
                        long? expectedSize = artifact.TryGetProperty("size", out var libSize) ? libSize.GetInt64() : null;
                        libTargets.Add((url, destPath,
                            artifact.TryGetProperty("sha1", out var libHash) ? libHash.GetString() : null, expectedSize));
                        if (PlatformSupport.MatchesNativePath(relativePath, architecture: gameArchitecture))
                            nativeArchives.Add(destPath);
                    }
                }

                // Older Minecraft metadata stores platform natives in
                // downloads.classifiers instead of downloads.artifact.
                // Select only the current OS/architecture, including LWJGL 2 on macOS.
                if (lib.TryGetProperty("downloads", out libDownloads) &&
                    libDownloads.TryGetProperty("classifiers", out var classifiers) &&
                    classifiers.ValueKind == JsonValueKind.Object)
                {
                    foreach (var classifier in classifiers.EnumerateObject())
                    {
                        if (!PlatformSupport.MatchesNativePath(classifier.Name, architecture: gameArchitecture) ||
                            classifier.Value.ValueKind != JsonValueKind.Object ||
                            !classifier.Value.TryGetProperty("url", out var nativeUrlProp) ||
                            !classifier.Value.TryGetProperty("path", out var nativePathProp))
                        {
                            continue;
                        }

                        string nativeUrl = nativeUrlProp.GetString() ?? string.Empty;
                        string nativeRelativePath = nativePathProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(nativeUrl) &&
                            !string.IsNullOrEmpty(nativeRelativePath))
                        {
                            string? expectedNativeHash = classifier.Value.TryGetProperty("sha1", out var classifierHash) ? classifierHash.GetString() : null;
                            string nativeDestPath = shareLibraries ? SharedGameFiles.Library(nativeUrl, nativeRelativePath, expectedNativeHash) : Path.Combine(
                                librariesDir,
                                nativeRelativePath.Replace('/', Path.DirectorySeparatorChar));
                            if (shareLibraries) layout.Libraries.Add(SharedGameFiles.Relative(nativeDestPath));
                            long? expectedNativeSize = classifier.Value.TryGetProperty("size", out var nativeSize)
                                ? nativeSize.GetInt64() : null;
                            libTargets.Add((nativeUrl, nativeDestPath,
                                classifier.Value.TryGetProperty("sha1", out var nativeHash) ? nativeHash.GetString() : null, expectedNativeSize));
                            nativeArchives.Add(nativeDestPath);
                        }
                    }
                }
            }

            targets.AddRange(libTargets);
        }

        // Start large files first, alongside assets, in one bounded queue.
        var pending = targets.DistinctBy(item => item.path).OrderByDescending(item => item.size ?? 0).ToArray();
        double planningSeconds = totalTimer.Elapsed.TotalSeconds;
        var transferTimer = System.Diagnostics.Stopwatch.StartNew();
        int ready = 0;
        long networkBytes = 0;
        var assetVerification = new AssetVerification();
        transfer?.Begin("Minecraft files", pending.Length, pending.Sum(item => item.size ?? 0));
        progressCallback?.Invoke("Preparing Minecraft files...", 15);
        await Parallel.ForEachAsync(pending,
            new ParallelOptions { MaxDegreeOfParallelism = TransferConcurrency(pending.Length), CancellationToken = cancellationToken },
            async (item, token) =>
            {
                long received = 0;
                if (!assetVerification.IsUnchanged(item.path, item.hash, item.size))
                {
                    await DownloadCache.EnsureAsync(item.url, item.path, token, item.hash, item.size, bytes =>
                    {
                        received += bytes;
                        Interlocked.Add(ref networkBytes, bytes);
                        transfer?.Received(bytes);
                    }, httpClient: _cacheRoot == null ? null : HttpClient, cacheRoot: _cacheRoot);
                    assetVerification.RememberVerified(item.path, item.hash);
                }
                transfer?.Complete(new FileInfo(item.path).Length, received);
                int done = Interlocked.Increment(ref ready);
                if (done % 20 == 0 || done == pending.Length)
                    progressCallback?.Invoke($"Preparing Minecraft files ({done}/{pending.Length})...",
                        15 + (double)done / Math.Max(1, pending.Length) * 80);
            });
        transferTimer.Stop();
        assetVerification.Save();

        string vanillaVersionJar = Path.Combine(versionsDir, $"{version}.jar");
        var clientTarget = targets.FirstOrDefault(item => item.path == clientJarPath);
        if (File.Exists(clientJarPath) && !await DownloadCache.IsValidAsync(vanillaVersionJar, clientTarget.hash, clientTarget.size, cancellationToken))
        {
            DiskSpace.EnsureAvailable(vanillaVersionJar, new FileInfo(clientJarPath).Length);
            string temporary = vanillaVersionJar + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await DownloadCache.CopyVerifiedFileAsync(clientJarPath, temporary, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, vanillaVersionJar, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var extractionTimer = System.Diagnostics.Stopwatch.StartNew();

        // Extract after downloading so two native archives cannot overwrite a
        // DLL concurrently. Also handles libraries restored from the cache.
        // Do not touch working natives until all downloads have succeeded.
        // Only archives from the current manifest may supply native binaries.
        cancellationToken.ThrowIfCancellationRequested();
        foreach (string nativeFile in Directory.GetFiles(nativesDir).Where(PlatformSupport.IsNativeBinary))
            File.Delete(nativeFile);
        foreach (string nativeJar in nativeArchives.OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtractNatives(nativeJar, nativesDir, cancellationToken);
        }

        extractionTimer.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        SharedGameFiles.Save(instancePath, layout);
        string timing = $"Version: {version}\nPlanning/index: {planningSeconds:0.000}s\n" +
            $"Download/cache/check/copy: {transferTimer.Elapsed.TotalSeconds:0.000}s\n" +
            $"Native extraction: {extractionTimer.Elapsed.TotalSeconds:0.000}s\n" +
            $"Total file preparation: {totalTimer.Elapsed.TotalSeconds:0.000}s\n" +
            $"Files: {pending.Length}; downloaded bytes: {networkBytes}\n" +
            $"Unchanged assets reused without rereading content: {assetVerification.Hits}\n";
        System.Diagnostics.Debug.WriteLine(timing);
        try { await File.WriteAllTextAsync(Path.Combine(instancePath, ".vortex-installation-timing.txt"), timing, cancellationToken); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        progressCallback?.Invoke("Game files ready!", 100);
    }

    private static async Task SafeWriteAllBytesAsync(string path, byte[] bytes)
    {
        string tempPath = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static void ExtractNatives(string zipPath, string extractTo, CancellationToken token)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (PlatformSupport.IsNativeBinary(entry.Name))
                {
                    string destinationPath = Path.Combine(extractTo, entry.Name);
                    DiskSpace.EnsureAvailable(destinationPath, entry.Length);
                    string temporary = destinationPath + "." + Guid.NewGuid().ToString("N") + ".part";
                    try
                    {
                        entry.ExtractToFile(temporary, false);
                        token.ThrowIfCancellationRequested();
                        File.Move(temporary, destinationPath, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"Damaged native archive: {zipPath}. Retry or repair the instance.", ex);
        }
    }

    public async Task DownloadFabricLibrariesAsync(FabricProfile profile, string instancePath, Action<string, double> progressCallback,
        CancellationToken cancellationToken = default, TransferProgress? transfer = null)
        => await DownloadLoaderLibrariesAsync(profile, instancePath, progressCallback, "https://maven.fabricmc.net/", cancellationToken, transfer);

    public async Task DownloadLoaderLibrariesAsync(
        FabricProfile profile,
        string instancePath,
        Action<string, double> progressCallback,
        string fallbackRepository, CancellationToken cancellationToken = default, TransferProgress? transfer = null, bool verifyArchives = false)
    {
        string librariesDir = Path.Combine(instancePath, "libraries");
        Directory.CreateDirectory(librariesDir);

        var libs = profile.Libraries;
        if (libs == null) return;

        var layout = SharedGameFiles.Read(instancePath);
        int total = libs.Count;
        int current = 0;

        var targets = new List<(string url, string path)>();
        foreach (var lib in libs)
        {
            string? fileUrl = lib.Downloads?.Artifact?.Url;

            if (string.IsNullOrEmpty(fileUrl) && !string.IsNullOrEmpty(lib.Name))
            {
                var parts = lib.Name.Split(':');
                if (parts.Length >= 3)
                {
                    string group = parts[0].Replace('.', '/');
                    string name = parts[1];
                    string ver = parts[2];
                    string repository = string.IsNullOrWhiteSpace(lib.Url)
                        ? fallbackRepository
                        : lib.Url;
                    fileUrl = $"{repository.TrimEnd('/')}/{group}/{name}/{ver}/{name}-{ver}.jar";
                }
            }

            if (string.IsNullOrEmpty(fileUrl))
                throw new InvalidOperationException("No download URL for loader library: " + lib.Name);

            string fileName = Path.GetFileName(fileUrl);
            string destinationPath = layout?.SharedLibraries == true ? SharedGameFiles.Library(fileUrl, fileName) : Path.Combine(librariesDir, fileName);

            targets.Add((fileUrl, destinationPath));
        }

        var uniqueTargets = targets.DistinctBy(item => item.path).ToArray();
        total = uniqueTargets.Length;
        transfer?.Begin("Loader libraries", total, 0);
        await Parallel.ForEachAsync(uniqueTargets, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (item, token) =>
        {
            long received = 0;
            await DownloadCache.EnsureAsync(item.url, item.path, token, received: bytes => { received += bytes; transfer?.Received(bytes); },
                httpClient: _cacheRoot == null ? null : HttpClient, cacheRoot: _cacheRoot, validateArchive: verifyArchives);
            transfer?.Complete(new FileInfo(item.path).Length, received);
            int done = Interlocked.Increment(ref current);
            progressCallback?.Invoke($"Preparing loader libraries ({done}/{total})...", (double)done / Math.Max(1, total) * 100);
        });
        if (layout?.SharedLibraries == true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            layout.LoaderLibraries = uniqueTargets.Select(item => SharedGameFiles.Relative(item.path)).ToList();
            SharedGameFiles.Save(instancePath, layout);
        }
    }

    public async Task InstallForgeOrNeoForgeAsync(string installerUrl, string instancePath, string javaPath, Action<string, double> progressCallback)
    {
        string librariesDir = Path.Combine(instancePath, "libraries");
        Directory.CreateDirectory(librariesDir);

        progressCallback?.Invoke("Downloading loader installer...", 10);
        string tempInstallerPath = Path.Combine(Path.GetTempPath(), "forge_installer.jar");

        try
        {
            var response = await HttpClient.GetAsync(installerUrl);
            if (!response.IsSuccessStatusCode)
            {
                progressCallback?.Invoke($"Forge installer not found (Status: {response.StatusCode}). Skipping...", 100);
                return;
            }

            byte[] installerBytes = await response.Content.ReadAsByteArrayAsync();
            await SafeWriteAllBytesAsync(tempInstallerPath, installerBytes);

            progressCallback?.Invoke("Extracting Forge libraries...", 40);
            using (var archive = ZipFile.OpenRead(tempInstallerPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".jar") && !entry.FullName.Contains("clientdata"))
                    {
                        string fileName = Path.GetFileName(entry.FullName);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            string destPath = Path.Combine(librariesDir, fileName);
                            entry.ExtractToFile(destPath, true);
                        }
                    }
                }
            }

            if (File.Exists(tempInstallerPath)) File.Delete(tempInstallerPath);
            progressCallback?.Invoke("Forge dependencies installed!", 100);
        }
        catch (Exception ex)
        {
            progressCallback?.Invoke($"Failed to install Forge: {ex.Message}", 100);
        }
    }
}
