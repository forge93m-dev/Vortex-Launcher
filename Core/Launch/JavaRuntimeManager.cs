using System;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Launch;

public sealed class JavaRuntimeManager
{
    private static readonly HttpClient HttpClient = new();

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
        string url = GetDownloadUrl(majorVersion, PlatformSupport.MinecraftOs, arch);

        try
        {
            progress?.Invoke($"Downloading Java {majorVersion}...", 5);
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(archivePath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

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

    internal static string GetDownloadUrl(int version, string os, Architecture architecture) =>
        $"https://api.adoptium.net/v3/binary/latest/{version}/ga/{(os == "osx" ? "mac" : os)}/{(architecture == Architecture.Arm64 ? "aarch64" : "x64")}/jdk/hotspot/normal/eclipse?project=jdk";
}
