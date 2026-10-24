using System;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Launch;

public sealed class JavaRuntimeManager
{
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromMinutes(15) };
    private const long MaxRuntimeBytes = 512L * 1024 * 1024;
    private readonly HttpClient _client;

    public JavaRuntimeManager() : this(DefaultClient) { }
    internal JavaRuntimeManager(HttpClient client) => _client = client;

    internal sealed record RuntimePackage(string Url, string Sha256, long Size, string ImageType);

    public async Task<string> EnsureJavaAsync(int majorVersion, Action<string, double>? progress = null,
        CancellationToken cancellationToken = default, Architecture? architecture = null)
    {
        var arch = architecture ?? RuntimeInformation.ProcessArchitecture;
        string runtimeName = $"java-{majorVersion}" + (OperatingSystem.IsWindows() ? "" : $"-{(arch == Architecture.Arm64 ? "arm64" : "x64")}");
        string runtimeRoot = Path.Combine(PlatformSupport.DataRoot, "runtime", runtimeName);
        string pending = Path.Combine(runtimeRoot, ".vortex-runtime-pending");
        string? existing = FindJavaExecutable(runtimeRoot);
        if (existing != null && !File.Exists(pending)) return existing;

        Directory.CreateDirectory(runtimeRoot);
        string archivePath = Path.Combine(Path.GetTempPath(), $"vortex-temurin-{majorVersion}-{Guid.NewGuid():N}" + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz"));

        try
        {
            progress?.Invoke($"Downloading Java {majorVersion}...", 5);
            var package = await ResolvePackageAsync(majorVersion, PlatformSupport.MinecraftOs, arch, cancellationToken);
            await DownloadVerifiedAsync(_client, package, archivePath, cancellationToken);

            progress?.Invoke($"Installing Java {majorVersion}...", 75);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(pending, "Installing", cancellationToken);
            if (OperatingSystem.IsWindows()) ZipFile.ExtractToDirectory(archivePath, runtimeRoot, true);
            else
            {
                await using var archive = File.OpenRead(archivePath);
                await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gzip, runtimeRoot, overwriteFiles: true, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            string? java = FindJavaExecutable(runtimeRoot);
            if (java == null)
            {
                throw new InvalidOperationException($"Java {majorVersion} executable was not found after extraction.");
            }

            if (!OperatingSystem.IsWindows())
            {
                foreach (string executable in Directory.EnumerateFiles(Path.GetDirectoryName(java)!))
                    File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            File.Delete(pending);
            progress?.Invoke($"Java {majorVersion} is ready.", 100);
            return java;
        }
        finally
        {
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
        }
    }

    private static string? FindJavaExecutable(string root)
    {
        if (!Directory.Exists(root)) return null;
        return Directory.GetFiles(root, PlatformSupport.JavaExecutable, SearchOption.AllDirectories)
            .FirstOrDefault(path => path.EndsWith(Path.Combine("bin", PlatformSupport.JavaExecutable), StringComparison.OrdinalIgnoreCase));
    }

    internal static string GetAssetsUrl(int version, string os, Architecture architecture, string imageType) =>
        $"https://api.adoptium.net/v3/assets/latest/{version}/hotspot?architecture={(architecture == Architecture.Arm64 ? "aarch64" : "x64")}" +
        $"&image_type={imageType}&os={(os == "osx" ? "mac" : os)}&vendor=eclipse";

    /// <summary>
    /// Asks Adoptium for the latest Temurin build. A JRE is preferred (about half the size of a JDK);
    /// the JDK is used only when no JRE is published for this platform. The API supplies the
    /// SHA-256 and size that the download must match.
    /// </summary>
    private async Task<RuntimePackage> ResolvePackageAsync(int version, string os, Architecture architecture, CancellationToken token)
    {
        foreach (string imageType in new[] { "jre", "jdk" })
        {
            using var response = await _client.GetAsync(GetAssetsUrl(version, os, architecture, imageType), token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            var package = ParsePackage(await response.Content.ReadAsStringAsync(token), imageType);
            if (package != null) return package;
        }
        throw new InvalidOperationException($"Adoptium does not publish Java {version} for {os}/{architecture}.");
    }

    internal static RuntimePackage? ParsePackage(string json, string imageType)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in document.RootElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("binary", out var binary) || !binary.TryGetProperty("package", out var package)) continue;
            string link = package.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
            string checksum = package.TryGetProperty("checksum", out var c) ? c.GetString() ?? "" : "";
            long size = package.TryGetProperty("size", out var z) && z.TryGetInt64(out long parsed) ? parsed : 0;
            string extension = link.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" :
                link.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : "";
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || extension == "") continue;
            if (checksum.Length != 64 || !checksum.All(Uri.IsHexDigit) || size <= 0 || size > MaxRuntimeBytes) continue;
            return new RuntimePackage(link, checksum, size, imageType);
        }
        return null;
    }

    internal static async Task DownloadVerifiedAsync(HttpClient client, RuntimePackage package, string destination, CancellationToken token)
    {
        using var response = await client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != package.Size)
            throw new IOException("The Java download size does not match Adoptium's metadata.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            byte[] buffer = new byte[262144];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                received += count;
                if (received > package.Size) throw new IOException("The Java download exceeded its declared size.");
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
        }
        if (received != package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Java checksum verification failed. The download was discarded.");
    }
}
