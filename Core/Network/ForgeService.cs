using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Network;

public class ForgeService
{
    private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = true
    });

    public ForgeService()
    {
        if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }
    }

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

        string[] installerUrls = new[]
        {
            $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{legacyArtifact}/forge-{legacyArtifact}-installer.jar",
            $"https://maven.minecraftforge.net/net/minecraftforge/forge/{legacyArtifact}/forge-{legacyArtifact}-installer.jar",
            $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar",
            $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar"
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        byte[]? installerBytes = null;
        foreach (var url in installerUrls)
        {
            try
            {
                progressCallback?.Invoke("Downloading Forge installer...", 10);
                using var resp = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    transfer?.Begin("Forge installer", 1, resp.Content.Headers.ContentLength ?? 0);
                    using var output = new MemoryStream();
                    await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    byte[] buffer = new byte[81920];
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        transfer?.Received(count);
                    }
                    var data = output.ToArray();
                    transfer?.Complete(data.Length, data.Length);
                    if (data.Length > 100000)
                    {
                        installerBytes = data;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
        }

        if (installerBytes == null)
        {
            throw new Exception("Could not download Forge installer JAR from any mirror.");
        }

        string tempInstaller = Path.Combine(Path.GetTempPath(), $"forge-{forgeVersion}-installer.jar");
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
                        string destPath = Path.Combine(librariesDir, relPath.Replace('/', Path.DirectorySeparatorChar));
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
            using var process = Process.Start(installer)
                ?? throw new InvalidOperationException("Could not start the Forge installer.");
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            // Wait for the child to exit before allowing Resume to write files.
            await process.WaitForExitAsync();
            string output = await outputTask;
            string error = await errorTask;
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
