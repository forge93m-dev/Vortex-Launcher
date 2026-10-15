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
        CancellationToken cancellationToken = default, TransferProgress? transfer = null)
    {
        string instancePath = Path.Combine(PlatformSupport.DataRoot, "instances", instanceName);
        Directory.CreateDirectory(instancePath);

        string clientJarPath = Path.Combine(instancePath, $"{version}.jar");

        progressCallback?.Invoke("Fetching version metadata...", 5);
        string versionJson = await HttpClient.GetStringAsync(versionJsonUrl, cancellationToken);

        using var doc = JsonDocument.Parse(versionJson);
        var root = doc.RootElement;
        var gameArchitecture = PlatformSupport.GetGameArchitecture(root);

        // Forge's official installer expects the vanilla profile under the
        // standard versions/<id> directory so it can run its client processors.
        string versionsDir = Path.Combine(instancePath, "versions", version);
        Directory.CreateDirectory(versionsDir);
        await File.WriteAllTextAsync(Path.Combine(versionsDir, $"{version}.json"), versionJson);

        var targets = new List<(string url, string path, string? hash, long? size)>();
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        if (root.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("client", out var clientProp) &&
            clientProp.TryGetProperty("url", out var clientUrlProp) &&
            !string.IsNullOrEmpty(clientUrlProp.GetString()))
            targets.Add((clientUrlProp.GetString()!, clientJarPath,
                clientProp.TryGetProperty("sha1", out var clientHash) ? clientHash.GetString() : null,
                clientProp.TryGetProperty("size", out var clientSize) ? clientSize.GetInt64() : null));

        string assetsDir = Path.Combine(instancePath, "assets");
        string assetsIndexesDir = Path.Combine(assetsDir, "indexes");
        string assetsObjectsDir = Path.Combine(assetsDir, "objects");
        Directory.CreateDirectory(assetsIndexesDir);
        Directory.CreateDirectory(assetsObjectsDir);

        if (root.TryGetProperty("assetIndex", out var assetIndexProp))
        {
            if (assetIndexProp.TryGetProperty("url", out var indexUrlProp) &&
                assetIndexProp.TryGetProperty("id", out var indexIdProp))
            {
                string indexUrl = indexUrlProp.GetString() ?? "";
                string indexId = indexIdProp.GetString() ?? version;
                string indexPath = Path.Combine(assetsIndexesDir, $"{indexId}.json");

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
                                if (hash.Length >= 2)
                                {
                                    string prefix = hash.Substring(0, 2);
                                    string objFolder = Path.Combine(assetsObjectsDir, prefix);
                                    string objFile = Path.Combine(objFolder, hash);

                                    long? objectSize = obj.Value.TryGetProperty("size", out var sizeProp)
                                        ? sizeProp.GetInt64() : null;
                                    if (seenHashes.Add(hash) && (!File.Exists(objFile) ||
                                        (objectSize.HasValue && new FileInfo(objFile).Length != objectSize.Value)))
                                    {
                                        Directory.CreateDirectory(objFolder);
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

        // Remove stale native binaries before extracting the current platform.
        // Otherwise a later x86/ARM download can overwrite the Windows x64 DLL
        // with the same filename (for example lwjgl.dll).
        foreach (string nativeFile in Directory.GetFiles(nativesDir))
        {
            try { File.Delete(nativeFile); } catch { }
        }

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
                        // Сохраняем иерархию папок для библиотек
                        string destPath = Path.Combine(librariesDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        long? expectedSize = artifact.TryGetProperty("size", out var libSize) ? libSize.GetInt64() : null;
                        if (!File.Exists(destPath) || (expectedSize.HasValue && new FileInfo(destPath).Length != expectedSize.Value))
                        {
                            libTargets.Add((url, destPath,
                                artifact.TryGetProperty("sha1", out var libHash) ? libHash.GetString() : null, expectedSize));
                        }
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
                            string nativeDestPath = Path.Combine(
                                librariesDir,
                                nativeRelativePath.Replace('/', Path.DirectorySeparatorChar));
                            long? expectedNativeSize = classifier.Value.TryGetProperty("size", out var nativeSize)
                                ? nativeSize.GetInt64() : null;
                            if (!File.Exists(nativeDestPath) || (expectedNativeSize.HasValue && new FileInfo(nativeDestPath).Length != expectedNativeSize.Value))
                            {
                                libTargets.Add((nativeUrl, nativeDestPath,
                                    classifier.Value.TryGetProperty("sha1", out var nativeHash) ? nativeHash.GetString() : null, expectedNativeSize));
                            }
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
        transfer?.Begin("Minecraft files", pending.Length, pending.Sum(item => item.size ?? 0));
        progressCallback?.Invoke("Preparing Minecraft files...", 15);
        await Parallel.ForEachAsync(pending,
            new ParallelOptions { MaxDegreeOfParallelism = 24, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                long received = 0;
                await DownloadCache.EnsureAsync(item.url, item.path, token, item.hash, item.size, bytes =>
                {
                    received += bytes;
                    Interlocked.Add(ref networkBytes, bytes);
                    transfer?.Received(bytes);
                }, httpClient: _cacheRoot == null ? null : HttpClient, cacheRoot: _cacheRoot);
                transfer?.Complete(new FileInfo(item.path).Length, received);
                int done = Interlocked.Increment(ref ready);
                if (done % 20 == 0 || done == pending.Length)
                    progressCallback?.Invoke($"Preparing Minecraft files ({done}/{pending.Length})...",
                        15 + (double)done / Math.Max(1, pending.Length) * 80);
            });
        transferTimer.Stop();

        string vanillaVersionJar = Path.Combine(versionsDir, $"{version}.jar");
        var clientTarget = targets.FirstOrDefault(item => item.path == clientJarPath);
        if (File.Exists(clientJarPath) && !await DownloadCache.IsValidAsync(vanillaVersionJar, clientTarget.hash, clientTarget.size, cancellationToken))
        {
            string temporary = vanillaVersionJar + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var input = new FileStream(clientJarPath, FileMode.Open, FileAccess.Read, FileShare.Read, 262144, true))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 262144, true))
                    await input.CopyToAsync(output, 262144, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, vanillaVersionJar, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var extractionTimer = System.Diagnostics.Stopwatch.StartNew();

        // Extract after downloading so two native archives cannot overwrite a
        // DLL concurrently. Also handles libraries restored from the cache.
        foreach (string nativeJar in Directory.GetFiles(librariesDir, "*.jar", SearchOption.AllDirectories)
                     .Where(path => PlatformSupport.MatchesNativePath(path, architecture: gameArchitecture)).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtractNatives(nativeJar, nativesDir);
        }

        extractionTimer.Stop();
        string timing = $"Version: {version}\nPlanning/index: {planningSeconds:0.000}s\n" +
            $"Download/cache/check/copy: {transferTimer.Elapsed.TotalSeconds:0.000}s\n" +
            $"Native extraction: {extractionTimer.Elapsed.TotalSeconds:0.000}s\n" +
            $"Total file preparation: {totalTimer.Elapsed.TotalSeconds:0.000}s\n" +
            $"Files: {pending.Length}; downloaded bytes: {networkBytes}\n";
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

    private static void ExtractNatives(string zipPath, string extractTo)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (PlatformSupport.IsNativeBinary(entry.Name))
                {
                    string destinationPath = Path.Combine(extractTo, entry.Name);
                    entry.ExtractToFile(destinationPath, true);
                }
            }
        }
        catch { }
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
            string destinationPath = Path.Combine(librariesDir, fileName);

            targets.Add((fileUrl, destinationPath));
        }

        var uniqueTargets = targets.DistinctBy(item => item.path).ToArray();
        total = uniqueTargets.Length;
        transfer?.Begin("Loader libraries", total, 0);
        await Parallel.ForEachAsync(uniqueTargets, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (item, token) =>
        {
            long received = 0;
            await DownloadCache.EnsureAsync(item.url, item.path, token, received: bytes => { received += bytes; transfer?.Received(bytes); }, validateArchive: verifyArchives);
            transfer?.Complete(new FileInfo(item.path).Length, received);
            int done = Interlocked.Increment(ref current);
            progressCallback?.Invoke($"Preparing loader libraries ({done}/{total})...", (double)done / Math.Max(1, total) * 100);
        });
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
