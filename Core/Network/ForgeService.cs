using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Network;

public class ForgeService
{
    private static readonly HttpClient HttpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return client;
    }

    private const string OfficialMaven = "https://maven.minecraftforge.net/";
    private const string MirrorMaven = "https://bmclapi2.bangbang93.com/maven/";
    private const long MaxInstallerBytes = 64L * 1024 * 1024;

    private static async Task<string?> TryGetOfficialSha1Async(string officialUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            string text = (await HttpClient.GetStringAsync(officialUrl + ".sha1", timeout.Token)).Trim();
            string hash = text.Split(' ', '\t', '\r', '\n')[0];
            return hash.Length == 40 && hash.All(Uri.IsHexDigit) ? hash : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>Returns a path under <paramref name="root"/> or null when the relative path escapes it.</summary>
    internal static string? ResolveInside(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':')) return null;
        string fullRoot = Path.GetFullPath(root);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar)) fullRoot += Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        return full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? full : null;
    }

    public ForgeService()
    {
    }

    /// <summary>Forge and NeoForge share the ModLauncher start-up, installer format and instance layout.</summary>
    public static bool IsForgeFamily(string? loader) =>
        string.Equals(loader, "Forge", StringComparison.OrdinalIgnoreCase) || string.Equals(loader, "NeoForge", StringComparison.OrdinalIgnoreCase);

    public async Task<string?> GetPromotedForgeVersionAsync(string mcVersion)
    {
        try
        {
            string url = $"https://bmclapi2.bangbang93.com/forge/minecraft/{mcVersion}";
            string json = await HttpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                string? latest = doc.RootElement.EnumerateArray()
                    .Select(item => item.TryGetProperty("version", out var versionProperty)
                        ? versionProperty.GetString()
                        : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .OrderByDescending(value =>
                        Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0))
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(latest))
                {
                    return $"{mcVersion}-{latest}";
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<bool> InstallForgeProfileAsync(string forgeVersion, string minecraftVersion, string instancePath, string javaPath, Action<string, double> progressCallback,
        CancellationToken cancellationToken = default, TransferProgress? transfer = null, bool verifyArchives = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string librariesDir = Path.Combine(instancePath, "libraries");
        Directory.CreateDirectory(librariesDir);

        // Forge 1.7.10 is stored in Maven with an additional Minecraft
        // classifier (for example: 1.7.10-10.13.4.1614-1.7.10).
        string legacyArtifact = string.Equals(minecraftVersion, "1.7.10", StringComparison.OrdinalIgnoreCase)
            ? $"{forgeVersion}-1.7.10"
            : forgeVersion;

        // Prefer the official Forge Maven. A mirror is used only when its bytes
        // match the SHA-1 published by the official Maven, because the installer
        // JAR is executed (Forge processors) after download.
        var installerCandidates = new[] { legacyArtifact, forgeVersion }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(artifact => $"net/minecraftforge/forge/{artifact}/forge-{artifact}-installer.jar")
            .ToArray();

        byte[]? installerBytes = null;
        foreach (string mavenPath in installerCandidates)
        {
            string officialUrl = OfficialMaven + mavenPath;
            string? expectedSha1 = await TryGetOfficialSha1Async(officialUrl, cancellationToken);
            foreach (string url in new[] { officialUrl, MirrorMaven + mavenPath })
            {
                bool official = url.StartsWith(OfficialMaven, StringComparison.Ordinal);
                if (!official && expectedSha1 == null) continue; // never trust an unverifiable mirror copy
                try
                {
                    progressCallback?.Invoke("Downloading Forge installer...", 10);
                    using var resp = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!resp.IsSuccessStatusCode) continue;
                    if (resp.Content.Headers.ContentLength > MaxInstallerBytes) continue;
                    transfer?.Begin("Forge installer", 1, resp.Content.Headers.ContentLength ?? 0);
                    using var output = new MemoryStream();
                    await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    byte[] buffer = new byte[81920];
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        if (output.Length + count > MaxInstallerBytes) throw new InvalidDataException("Forge installer is too large.");
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        transfer?.Received(count);
                    }
                    var data = output.ToArray();
                    transfer?.Complete(data.Length, data.Length);
                    if (data.Length <= 100000) continue;
                    if (expectedSha1 != null && !Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(data))
                            .Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                        continue;
                    installerBytes = data;
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }
            }
            if (installerBytes != null) break;
        }

        if (installerBytes == null)
        {
            throw new Exception("Could not download Forge installer JAR from any mirror.");
        }

        string tempInstaller = Path.Combine(Path.GetTempPath(), $"vortex-forge-{forgeVersion}-{Guid.NewGuid():N}-installer.jar");
        await File.WriteAllBytesAsync(tempInstaller, installerBytes);

        string? versionJsonContent = null;

        {
            using (var archive = ZipFile.OpenRead(tempInstaller))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string entryPath = entry.FullName.Replace('\\', '/');

                    if (entryPath.Equals("version.json", StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        versionJsonContent = await reader.ReadToEndAsync();
                    }

                    // Forge 1.7.x and other legacy installers use
                    // install_profile.json and put the launcher profile under
                    // its versionInfo property instead of version.json.
                    if (entryPath.Equals("install_profile.json", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(versionJsonContent))
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        string installProfile = await reader.ReadToEndAsync();
                        using var installDocument = JsonDocument.Parse(installProfile);
                        if (installDocument.RootElement.TryGetProperty("versionInfo", out var versionInfo))
                        {
                            versionJsonContent = versionInfo.GetRawText();
                        }
                    }

                    if (entryPath.StartsWith("maven/", StringComparison.OrdinalIgnoreCase) && entryPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        string relPath = entryPath.Substring(6);
                        string? destPath = ResolveInside(librariesDir, relPath);
                        if (destPath == null) throw new InvalidDataException($"Forge installer contains an unsafe path: {entryPath}");
                        string? dir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        entry.ExtractToFile(destPath, true);
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(versionJsonContent))
        {
            throw new Exception("version.json not found inside Forge installer.");
        }

        try
        {
            using var versionDoc = JsonDocument.Parse(versionJsonContent);
            if (versionDoc.RootElement.TryGetProperty("id", out var idProp))
            {
                string profileId = idProp.GetString() ?? $"{minecraftVersion}-forge-{forgeVersion.Split('-').Last()}";
                string profileDir = Path.Combine(instancePath, "versions", profileId);
                Directory.CreateDirectory(profileDir);
                await File.WriteAllTextAsync(Path.Combine(profileDir, $"{profileId}.json"), versionJsonContent);
            }
        }
        catch { }

        using var doc = JsonDocument.Parse(versionJsonContent);
        if (doc.RootElement.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
        {
            var gameArchitecture = PlatformSupport.GetGameArchitecture(instancePath, minecraftVersion);
            var downloadTasks = new List<(string url, string path, string? hash, long? size)>();

            foreach (var lib in libs.EnumerateArray())
            {
                if (!PlatformSupport.RulesAllow(lib, architecture: gameArchitecture)) continue;
                string path = "";
                string url = "";
                string? hash = null;
                long? size = null;

                if (lib.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact))
                {
                    if (artifact.TryGetProperty("path", out var p)) path = p.GetString() ?? "";
                    if (artifact.TryGetProperty("url", out var u)) url = u.GetString() ?? "";
                    if (artifact.TryGetProperty("sha1", out var h)) hash = h.GetString();
                    if (artifact.TryGetProperty("size", out var s)) size = s.GetInt64();
                }

                if (string.IsNullOrEmpty(path) && lib.TryGetProperty("name", out var nameProp))
                {
                    path = NameToPath(nameProp.GetString() ?? "");
                }

                if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                {
                    url = $"https://bmclapi2.bangbang93.com/maven/{path}";
                }

                if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(url))
                {
                    string dest = Path.Combine(librariesDir, path.Replace('/', Path.DirectorySeparatorChar));
                    if (verifyArchives || !File.Exists(dest))
                    {
                        downloadTasks.Add((url, dest, hash, size));
                    }
                }
            }

            int total = downloadTasks.Count;
            int current = 0;
            transfer?.Begin("Forge libraries", total, 0);

            await Parallel.ForEachAsync(downloadTasks, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (item, token) =>
            {
                try
                {
                    string? dir = Path.GetDirectoryName(item.path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    long received = 0;
                    await DownloadCache.EnsureAsync(item.url, item.path, token, item.hash, item.size, received: bytes =>
                    {
                        received += bytes;
                        transfer?.Received(bytes);
                    }, validateArchive: verifyArchives);
                    transfer?.Complete(new FileInfo(item.path).Length, received);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch when (!verifyArchives) { }

                int done = System.Threading.Interlocked.Increment(ref current);
                progressCallback?.Invoke($"Downloading Forge libraries ({done}/{total})...", 20 + ((double)done / Math.Max(1, total) * 75));
            });
        }

        string processedClient = Path.Combine(
            librariesDir,
            "net", "minecraftforge", "forge", legacyArtifact,
            $"forge-{legacyArtifact}-client.jar");
        string universalForge = Path.Combine(
            librariesDir,
            "net", "minecraftforge", "forge", legacyArtifact,
            $"forge-{legacyArtifact}-universal.jar");
        string legacyForgeArtifact = Path.Combine(
            librariesDir,
            "net", "minecraftforge", "forge", legacyArtifact,
            $"forge-{legacyArtifact}.jar");
        bool universalForgeReady = File.Exists(universalForge) || File.Exists(legacyForgeArtifact);

        bool requiresPatchedClient = versionJsonContent.Contains(
            "net.minecraftforge.bootstrap.ForgeBootstrap",
            StringComparison.OrdinalIgnoreCase);
        string? mcpVersion = GetProfileArgument(versionJsonContent, "--fml.mcpVersion");
        string? legacyExtra = string.IsNullOrWhiteSpace(mcpVersion)
            ? null
            : Path.Combine(
                librariesDir, "net", "minecraft", "client",
                $"{minecraftVersion}-{mcpVersion}",
                $"client-{minecraftVersion}-{mcpVersion}-extra.jar");
        bool legacyExtraReady = legacyExtra == null || File.Exists(legacyExtra);
        bool processedClientReady = File.Exists(processedClient) &&
                                    (!requiresPatchedClient || ContainsArchiveEntry(
                                        processedClient, ".forge_patched_minecraft"));

        // Forge 26+ can leave the raw Maven client jar beside the installer
        // artifacts. It is not launchable until the installer processors add
        // the .forge_patched_minecraft marker/resource.
        if ((!processedClientReady && (requiresPatchedClient || !universalForgeReady)) ||
            !legacyExtraReady)
        {
            progressCallback?.Invoke("Preparing Forge client...", 95);

            if (requiresPatchedClient && File.Exists(processedClient))
            {
                // This is the raw client artifact extracted from the installer;
                // remove it so the Forge processor can recreate the patched jar.
                File.Delete(processedClient);
            }

            string versionsDir = Path.Combine(instancePath, "versions", minecraftVersion);
            Directory.CreateDirectory(versionsDir);

            string vanillaProfile = Path.Combine(versionsDir, $"{minecraftVersion}.json");
            string vanillaJar = Path.Combine(versionsDir, $"{minecraftVersion}.jar");
            string rootClientJar = Path.Combine(instancePath, $"{minecraftVersion}.jar");
            if (!File.Exists(vanillaJar) && File.Exists(rootClientJar))
            {
                File.Copy(rootClientJar, vanillaJar, true);
            }

            if (!File.Exists(vanillaProfile))
            {
                try
                {
                    string manifest = await HttpClient.GetStringAsync(
                        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", cancellationToken);
                    using var manifestDoc = JsonDocument.Parse(manifest);
                    if (manifestDoc.RootElement.TryGetProperty("versions", out var versions))
                    {
                        foreach (var item in versions.EnumerateArray())
                        {
                            if (!item.TryGetProperty("id", out var id) ||
                                !string.Equals(id.GetString(), minecraftVersion, StringComparison.OrdinalIgnoreCase) ||
                                !item.TryGetProperty("url", out var profileUrl))
                            {
                                continue;
                            }

                            string profile = await HttpClient.GetStringAsync(profileUrl.GetString() ?? "", cancellationToken);
                            await File.WriteAllTextAsync(vanillaProfile, profile);
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }

                if (!File.Exists(vanillaProfile))
                {
                    throw new InvalidOperationException(
                        $"Minecraft profile not found: {vanillaProfile}. Download the Minecraft version before installing Forge.");
                }
            }

            string profilesPath = Path.Combine(instancePath, "launcher_profiles.json");
            if (!File.Exists(profilesPath))
            {
                await File.WriteAllTextAsync(profilesPath,
                    "{\"profiles\":{\"Vortex\":{\"name\":\"Vortex\",\"lastVersionId\":\"" +
                    minecraftVersion + "\"}},\"selectedProfile\":\"Vortex\"}");
            }

            var installer = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = instancePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            installer.ArgumentList.Add("-jar");
            installer.ArgumentList.Add(tempInstaller);
            installer.ArgumentList.Add("--installClient");
            installer.ArgumentList.Add(instancePath);

            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = installer };
            var outputLines = new StringBuilder();
            var errorLines = new StringBuilder();
            string? lastStage = null;
            // The installer runs for a while and prints tens of thousands of lines;
            // surface only its stage changes so the progress text keeps moving.
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (outputLines) outputLines.AppendLine(e.Data);
                string? stage = InstallerStage(e.Data);
                if (stage != null && stage != lastStage)
                {
                    lastStage = stage;
                    progressCallback?.Invoke(stage, 95);
                }
            };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errorLines) errorLines.AppendLine(e.Data); };
            if (!process.Start()) throw new InvalidOperationException("Could not start the Forge installer.");
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Wait for the child to exit before allowing Resume to write files.
            await process.WaitForExitAsync();
            string output, error;
            lock (outputLines) output = outputLines.ToString();
            lock (errorLines) error = errorLines.ToString();
            cancellationToken.ThrowIfCancellationRequested();

            string profileId = $"{minecraftVersion}-forge-{forgeVersion.Split('-').Last()}";
            string installedProfile = Path.Combine(
                instancePath, "versions", profileId, $"{profileId}.json");
            bool forgeArtifactsReady = requiresPatchedClient
                ? processedClientReady || (File.Exists(processedClient) &&
                    ContainsArchiveEntry(processedClient, ".forge_patched_minecraft"))
                : File.Exists(processedClient) ||
                  universalForgeReady ||
                  File.Exists(installedProfile);
            forgeArtifactsReady &= legacyExtra == null || File.Exists(legacyExtra);

            if (process.ExitCode != 0 || !forgeArtifactsReady)
            {
                throw new InvalidOperationException(
                    $"Forge client preparation failed (exit code {process.ExitCode}).\n{output}\n{error}");
            }
        }

        string processorJopt = Path.Combine(
            librariesDir, "net", "sf", "jopt-simple", "jopt-simple", "6.0-alpha-3",
            "jopt-simple-6.0-alpha-3.jar");
        if (File.Exists(processorJopt))
        {
            File.Delete(processorJopt);
        }

        string intermediateClient = Path.Combine(
            librariesDir, "net", "minecraft", "client", minecraftVersion,
            $"client-{minecraftVersion}-official.jar");
        if (File.Exists(intermediateClient))
        {
            File.Delete(intermediateClient);
        }

        if (File.Exists(tempInstaller))
        {
            File.Delete(tempInstaller);
        }

        progressCallback?.Invoke("Forge setup ready!", 100);
        return true;
    }

    private const string NeoForgeMaven = "https://maven.neoforged.net/releases/";
    internal const string NeoForgeVersionsUrl = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";

    /// <summary>
    /// The version prefix NeoForge uses for a Minecraft version: 1.21.1 is 21.1.x, 1.21 is 21.0.x, 26.1 is 26.1.0.x.
    /// Null for versions NeoForge does not publish (1.20.1 and older, snapshots).
    /// </summary>
    internal static string? NeoForgePrefix(string minecraftVersion)
    {
        var parts = minecraftVersion.Trim().Split('.');
        if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || p.Length > 4 || !p.All(char.IsAsciiDigit))) return null;
        var numbers = parts.Select(int.Parse).ToList();
        if (numbers[0] == 1)
        {
            numbers.RemoveAt(0);
            if (numbers[0] < 20 || (numbers[0] == 20 && (numbers.Count < 2 || numbers[1] < 2))) return null;
            while (numbers.Count < 2) numbers.Add(0);
        }
        else if (numbers[0] >= 26) { while (numbers.Count < 3) numbers.Add(0); }
        else return null;
        return string.Join('.', numbers) + ".";
    }

    /// <summary>NeoForge versions that belong to a Minecraft version, best first (stable before beta, then newest). Alpha/snapshot builds are never offered.</summary>
    internal static List<string> NeoForgeVersionsFor(string minecraftVersion, IEnumerable<string> all)
    {
        string? prefix = NeoForgePrefix(minecraftVersion);
        if (prefix == null) return new();
        static long[] Key(string version)
        {
            var numbers = version.Split('-')[0].Split('.').Select(p => long.TryParse(p, out long n) ? n : 0).ToList();
            while (numbers.Count < 6) numbers.Add(0);
            return numbers.ToArray();
        }
        var comparer = Comparer<long[]>.Create((a, b) => { for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]); return 0; });
        return all.Where(v => v.StartsWith(prefix, StringComparison.Ordinal) && !v.Contains("alpha", StringComparison.OrdinalIgnoreCase) &&
                              !v.Contains('+') && IsSafeNeoForgeVersion(v))
            .OrderByDescending(v => v.EndsWith("-beta", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(Key, comparer)
            .ToList();
    }

    internal static bool IsSafeNeoForgeVersion(string? version) => !string.IsNullOrEmpty(version) && version.Length <= 40 &&
        version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') && char.IsAsciiDigit(version[0]);

    /// <summary>The newest NeoForge for a Minecraft version (stable builds first), or null when there is none or the list is unreachable.</summary>
    public async Task<string?> GetLatestNeoForgeVersionAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        if (NeoForgePrefix(minecraftVersion) == null) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var document = JsonDocument.Parse(await HttpClient.GetStringAsync(NeoForgeVersionsUrl, timeout.Token));
            var all = document.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString() ?? "").ToList();
            return NeoForgeVersionsFor(minecraftVersion, all).FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// Installs NeoForge for an instance with the official installer from NeoForged's Maven. Unlike Forge there is no
    /// mirror: the installer is executed, so it is fetched only from the official host and checked against its published SHA-1.
    /// </summary>
    public async Task<bool> InstallNeoForgeProfileAsync(string neoForgeVersion, string minecraftVersion, string instancePath, string javaPath,
        Action<string, double> progressCallback, CancellationToken cancellationToken = default, TransferProgress? transfer = null, bool verifyArchives = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSafeNeoForgeVersion(neoForgeVersion)) throw new InvalidOperationException($"\"{neoForgeVersion}\" is not a valid NeoForge version.");
        string librariesDir = Path.Combine(instancePath, "libraries");
        Directory.CreateDirectory(librariesDir);
        string profileId = $"neoforge-{neoForgeVersion}";
        string profilePath = Path.Combine(instancePath, "versions", profileId, $"{profileId}.json");
        string processedClient = Path.Combine(librariesDir, "net", "neoforged", "neoforge", neoForgeVersion, $"neoforge-{neoForgeVersion}-client.jar");
        // The patched client only ever comes out of the installer's processors, so its presence next to the profile means a finished install.
        if (!verifyArchives && File.Exists(profilePath) && File.Exists(processedClient) && NeoForgeLibrariesPresent(profilePath, librariesDir))
        {
            progressCallback?.Invoke("NeoForge setup ready!", 100);
            return true;
        }

        string mavenPath = $"net/neoforged/neoforge/{neoForgeVersion}/neoforge-{neoForgeVersion}-installer.jar";
        string installerUrl = NeoForgeMaven + mavenPath;
        string? expectedSha1 = await TryGetOfficialSha1Async(installerUrl, cancellationToken);
        progressCallback?.Invoke("Downloading NeoForge installer...", 10);
        byte[] installerBytes;
        using (var response = await HttpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"NeoForge {neoForgeVersion} was not found on NeoForged's server (HTTP {(int)response.StatusCode}).");
            if (response.Content.Headers.ContentLength > MaxInstallerBytes) throw new InvalidDataException("NeoForge installer is too large.");
            transfer?.Begin("NeoForge installer", 1, response.Content.Headers.ContentLength ?? 0);
            using var output = new MemoryStream();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + count > MaxInstallerBytes) throw new InvalidDataException("NeoForge installer is too large.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                transfer?.Received(count);
            }
            installerBytes = output.ToArray();
            transfer?.Complete(installerBytes.Length, installerBytes.Length);
        }
        if (installerBytes.Length <= 100000) throw new InvalidDataException("The NeoForge installer download is incomplete.");
        if (expectedSha1 != null && !Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(installerBytes)).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The NeoForge installer does not match the checksum published by NeoForged.");

        string tempInstaller = Path.Combine(Path.GetTempPath(), $"vortex-neoforge-{neoForgeVersion}-{Guid.NewGuid():N}-installer.jar");
        try
        {
            await File.WriteAllBytesAsync(tempInstaller, installerBytes, cancellationToken);
            string? versionJson = ExtractNeoForgeInstaller(tempInstaller, librariesDir, cancellationToken);
            if (string.IsNullOrEmpty(versionJson)) throw new InvalidDataException("version.json not found inside NeoForge installer.");
            using (var versionDoc = JsonDocument.Parse(versionJson))
            {
                string id = versionDoc.RootElement.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? profileId : profileId;
                if (!id.Equals(profileId, StringComparison.Ordinal)) throw new InvalidDataException($"The NeoForge installer describes \"{id}\", expected \"{profileId}\".");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            await File.WriteAllTextAsync(profilePath, versionJson, cancellationToken);

            await DownloadProfileLibrariesAsync(versionJson, instancePath, minecraftVersion, librariesDir, "NeoForge", progressCallback, transfer, verifyArchives, cancellationToken);

            progressCallback?.Invoke("Preparing NeoForge client...", 95);
            await EnsureVanillaForInstallerAsync(instancePath, minecraftVersion, cancellationToken);
            if (File.Exists(processedClient)) File.Delete(processedClient); // only the installer's own output counts

            var installer = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = instancePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            installer.ArgumentList.Add("-jar");
            installer.ArgumentList.Add(tempInstaller);
            installer.ArgumentList.Add("--installClient");
            installer.ArgumentList.Add(instancePath);

            using var process = new Process { StartInfo = installer };
            var outputLines = new StringBuilder();
            var errorLines = new StringBuilder();
            string? lastStage = null;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (outputLines) outputLines.AppendLine(e.Data);
                string? stage = InstallerStage(e.Data)?.Replace("Forge", "NeoForge", StringComparison.Ordinal);
                if (stage != null && stage != lastStage)
                {
                    lastStage = stage;
                    progressCallback?.Invoke(stage, 95);
                }
            };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errorLines) errorLines.AppendLine(e.Data); };
            if (!process.Start()) throw new InvalidOperationException("Could not start the NeoForge installer.");
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            string output, error;
            lock (outputLines) output = outputLines.ToString();
            lock (errorLines) error = errorLines.ToString();
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || !File.Exists(processedClient))
            {
                try { if (File.Exists(processedClient)) File.Delete(processedClient); } catch { }
                throw new InvalidOperationException($"NeoForge client preparation failed (exit code {process.ExitCode}).\n{output}\n{error}");
            }
        }
        finally
        {
            try { if (File.Exists(tempInstaller)) File.Delete(tempInstaller); } catch { }
        }

        progressCallback?.Invoke("NeoForge setup ready!", 100);
        return true;
    }

    /// <summary>Copies the installer's bundled maven/ jars into the libraries folder (never outside it) and returns its version.json.</summary>
    internal static string? ExtractNeoForgeInstaller(string installerPath, string librariesDir, CancellationToken cancellationToken)
    {
        string? versionJson = null;
        using var archive = ZipFile.OpenRead(installerPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string entryPath = entry.FullName.Replace('\\', '/');
            if (entryPath.Equals("version.json", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("version.json inside the NeoForge installer is too large.");
                using var reader = new StreamReader(entry.Open());
                versionJson = reader.ReadToEnd();
            }
            else if (entryPath.StartsWith("maven/", StringComparison.OrdinalIgnoreCase) && entryPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                string? destination = ResolveInside(librariesDir, entryPath.Substring(6));
                if (destination == null) throw new InvalidDataException($"NeoForge installer contains an unsafe path: {entryPath}");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
        return versionJson;
    }

    private static bool NeoForgeLibrariesPresent(string profilePath, string librariesDir)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
            if (!document.RootElement.TryGetProperty("libraries", out var libraries)) return false;
            foreach (var library in libraries.EnumerateArray())
            {
                if (!PlatformSupport.RulesAllow(library)) continue;
                string path = library.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact) &&
                              artifact.TryGetProperty("path", out var p) ? p.GetString() ?? "" : library.TryGetProperty("name", out var n) ? NameToPath(n.GetString() ?? "") : "";
                if (path.Length == 0) continue;
                string? full = ResolveInside(librariesDir, path);
                if (full == null || !File.Exists(full)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Downloads the libraries a loader profile lists (verified by SHA-1 and size when the profile gives them).</summary>
    private static async Task DownloadProfileLibrariesAsync(string profileJson, string instancePath, string minecraftVersion, string librariesDir, string label,
        Action<string, double>? progressCallback, TransferProgress? transfer, bool verifyArchives, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(profileJson);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array) return;
        var architecture = PlatformSupport.GetGameArchitecture(instancePath, minecraftVersion);
        var tasks = new List<(string url, string path, string? hash, long? size)>();
        foreach (var library in libraries.EnumerateArray())
        {
            if (!PlatformSupport.RulesAllow(library, architecture: architecture)) continue;
            string path = "", url = ""; string? hash = null; long? size = null;
            if (library.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact))
            {
                if (artifact.TryGetProperty("path", out var p)) path = p.GetString() ?? "";
                if (artifact.TryGetProperty("url", out var u)) url = u.GetString() ?? "";
                if (artifact.TryGetProperty("sha1", out var h)) hash = h.GetString();
                if (artifact.TryGetProperty("size", out var s) && s.TryGetInt64(out long length)) size = length;
            }
            if (path.Length == 0 && library.TryGetProperty("name", out var name)) path = NameToPath(name.GetString() ?? "");
            // A library without a download address ships inside the installer (already extracted) or is built by it.
            if (path.Length == 0 || url.Length == 0) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException($"{label} lists a library with an unsafe address: {url}");
            string? destination = ResolveInside(librariesDir, path);
            if (destination == null) throw new InvalidDataException($"{label} lists a library outside the libraries folder: {path}");
            if (verifyArchives || !File.Exists(destination)) tasks.Add((url, destination, hash, size));
        }
        int total = tasks.Count, current = 0;
        transfer?.Begin($"{label} libraries", total, 0);
        await Parallel.ForEachAsync(tasks, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (item, token) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.path)!);
            long received = 0;
            await DownloadCache.EnsureAsync(item.url, item.path, token, item.hash, item.size, received: bytes =>
            {
                received += bytes;
                transfer?.Received(bytes);
            }, validateArchive: verifyArchives);
            transfer?.Complete(new FileInfo(item.path).Length, received);
            int done = Interlocked.Increment(ref current);
            progressCallback?.Invoke($"Downloading {label} libraries ({done}/{total})...", 20 + ((double)done / Math.Max(1, total) * 70));
        });
    }

    /// <summary>The installer patches the vanilla jar in place, so versions/&lt;mc&gt;/&lt;mc&gt;.json and .jar must exist and launcher_profiles.json must be there.</summary>
    private static async Task EnsureVanillaForInstallerAsync(string instancePath, string minecraftVersion, CancellationToken cancellationToken)
    {
        string versionsDir = Path.Combine(instancePath, "versions", minecraftVersion);
        Directory.CreateDirectory(versionsDir);
        string vanillaProfile = Path.Combine(versionsDir, $"{minecraftVersion}.json");
        string vanillaJar = Path.Combine(versionsDir, $"{minecraftVersion}.jar");
        string rootClientJar = Path.Combine(instancePath, $"{minecraftVersion}.jar");
        if (!File.Exists(vanillaJar) && File.Exists(rootClientJar)) File.Copy(rootClientJar, vanillaJar, true);
        if (!File.Exists(vanillaProfile))
        {
            string manifest = await HttpClient.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", cancellationToken);
            using var manifestDoc = JsonDocument.Parse(manifest);
            foreach (var item in manifestDoc.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (!string.Equals(item.GetProperty("id").GetString(), minecraftVersion, StringComparison.OrdinalIgnoreCase)) continue;
                await File.WriteAllTextAsync(vanillaProfile, await HttpClient.GetStringAsync(item.GetProperty("url").GetString() ?? "", cancellationToken), cancellationToken);
                break;
            }
            if (!File.Exists(vanillaProfile))
                throw new InvalidOperationException($"Minecraft profile not found: {vanillaProfile}. Download the Minecraft version before installing NeoForge.");
        }
        string profilesPath = Path.Combine(instancePath, "launcher_profiles.json");
        if (!File.Exists(profilesPath))
            await File.WriteAllTextAsync(profilesPath, "{\"profiles\":{\"Vortex\":{\"name\":\"Vortex\",\"lastVersionId\":\"" + minecraftVersion + "\"}},\"selectedProfile\":\"Vortex\"}", cancellationToken);
    }

    // Maps the official installer's processor announcements to readable stages.
    internal static string? InstallerStage(string line)
    {
        string text = line.Trim();
        if (text.StartsWith("Downloading libraries", StringComparison.Ordinal)) return "Forge: checking libraries...";
        if (!text.StartsWith("MainClass:", StringComparison.Ordinal))
            return text.StartsWith("Injecting profile", StringComparison.Ordinal) ? "Forge: finishing setup..." : null;
        if (text.Contains("installertools", StringComparison.Ordinal)) return "Forge 1/4: preparing mappings...";
        if (text.Contains("jarsplitter", StringComparison.Ordinal)) return "Forge 2/4: splitting the game files...";
        if (text.Contains(".fart.", StringComparison.Ordinal) || text.Contains("SpecialSource", StringComparison.Ordinal))
            return "Forge 3/4: remapping Minecraft (the longest step)...";
        if (text.Contains("binarypatcher", StringComparison.Ordinal)) return "Forge 4/4: applying Forge patches...";
        return "Forge: running installer step...";
    }

    private static bool ContainsArchiveEntry(string archivePath, string entryName)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            return archive.Entries.Any(entry =>
                string.Equals(entry.FullName, entryName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string? GetProfileArgument(string profileJson, string argumentName)
    {
        try
        {
            using var document = JsonDocument.Parse(profileJson);
            if (!document.RootElement.TryGetProperty("arguments", out var arguments) ||
                !arguments.TryGetProperty("game", out var gameArguments) ||
                gameArguments.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = gameArguments.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .ToList();

            for (int i = 0; i + 1 < values.Count; i++)
            {
                if (string.Equals(values[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return values[i + 1];
                }
            }
        }
        catch
        {
            // A malformed optional profile argument must not prevent Forge
            // from using the normal installation path.
        }

        return null;
    }

    private static string NameToPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return "";
        string group = parts[0].Replace('.', '/');
        string artifact = parts[1];
        string version = parts[2];
        string classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }
}
