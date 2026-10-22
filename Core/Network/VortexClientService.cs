using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Network;

public sealed class VortexClientService
{
    public const string ClientVersion = "0.2.2";
    private sealed record ClientBuild(string Minecraft)
    {
        public string FileName => $"vortex-client-{Minecraft}-fabric-{ClientVersion}.jar";
        public string ResourceName => "VortexLauncher.Client." + FileName;
    }
    public static System.Collections.Generic.IReadOnlyList<string> SupportedMinecraftVersions { get; } =
        Array.AsReadOnly(new[] { "26.3", "26.2", "26.1.2", "26.1.1", "26.1", "1.21.11" });
    public static string SupportedVersionsDescription => "Minecraft " + string.Join(", ", SupportedMinecraftVersions) + " with Fabric";
    public static bool IsExperimentalTarget(string minecraftVersion) => minecraftVersion != "26.3" && SupportedMinecraftVersions.Contains(minecraftVersion);
    private static ClientBuild BuildFor(string minecraft, string loader) => IsCompatible(minecraft, loader)
        ? new ClientBuild(minecraft) : throw new InvalidOperationException("Vortex Client is available for " + SupportedVersionsDescription + ".");

    public static bool IsCompatible(string minecraftVersion, string loader) =>
        SupportedMinecraftVersions.Contains(minecraftVersion) && loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase);

    public static string InstalledVersion(InstanceModel instance)
    {
        var file = FindClient(instance);
        if (file == null) return "Not installed";
        using var jar = ZipFile.OpenRead(file);
        using var metadata = jar.GetEntry("fabric.mod.json")!.Open();
        using var document = JsonDocument.Parse(metadata);
        return (document.RootElement.GetProperty("version").GetString() ?? "Unknown") +
            (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? " (disabled)" : "");
    }

    private static string? FindClient(InstanceModel instance)
    {
        string mods = Path.Combine(instance.InstancePath, "mods");
        if (!Directory.Exists(mods)) return null;
        var files = Directory.EnumerateFiles(mods).Where(file =>
            (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) && HasClientId(file)).ToArray();
        if (files.Length > 1) throw new InvalidOperationException("Multiple Vortex Client JARs found. Keep one copy in Mods before updating.");
        return files.SingleOrDefault();
    }

    public async Task<string?> UpdateAsync(InstanceModel instance, CancellationToken cancellationToken = default)
    {
        if (instance.IsRunning || !IsCompatible(instance.Version, instance.Loader))
            throw new InvalidOperationException("Close Minecraft and select a supported Fabric instance to update Vortex Client. Supported: " + SupportedVersionsDescription + ".");
        var build = BuildFor(instance.Version, instance.Loader);
        if (!string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
            (!Version.TryParse(instance.LoaderVersion, out var loader) || loader < new Version(0, 19, 5)))
            throw new InvalidOperationException("Vortex Client requires Fabric Loader 0.19.5 or newer.");
        string? existing = FindClient(instance);
        if (existing != null && Version.TryParse(InstalledVersion(instance).Split(' ')[0], out var installed) && installed > Version.Parse(ClientVersion))
            throw new InvalidOperationException("A newer Vortex Client is already installed. Update the launcher; it will not downgrade your client.");
        using var bundled = Assembly.GetExecutingAssembly().GetManifestResourceStream(build.ResourceName)
            ?? throw new InvalidOperationException("This launcher build does not include Vortex Client.");
        using var content = new MemoryStream();
        await bundled.CopyToAsync(content, cancellationToken);
        byte[] bytes = content.ToArray();
        ValidateArtifact(bytes, build);
        if (existing != null)
        {
            await using var input = File.OpenRead(existing);
            if (CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(input, cancellationToken), SHA256.HashData(bytes))) return null;
        }
        string mods = Path.Combine(instance.InstancePath, "mods");
        for (var directory = new DirectoryInfo(instance.InstancePath); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Client updates cannot follow linked instance folders.");
        if ((File.GetAttributes(instance.InstancePath) & FileAttributes.ReparsePoint) != 0 ||
            (Directory.Exists(mods) && (File.GetAttributes(mods) & FileAttributes.ReparsePoint) != 0) ||
            (existing != null && (File.GetAttributes(existing) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Linked instance/mod folders are not supported for client updates.");
        Directory.CreateDirectory(mods);
        string target = Path.Combine(mods, build.FileName + (existing?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true ? ".disabled" : ""));
        if (existing != target && File.Exists(target)) throw new IOException("The target filename is already used by another file. Nothing was overwritten.");
        string temporary = Path.Combine(mods, ".vortex-client-" + Guid.NewGuid().ToString("N") + ".part");
        string? backup = null;
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            if (existing != null)
            {
                string backups = Path.Combine(instance.InstancePath, ".vortex-client-backups");
                if (Directory.Exists(backups) && (File.GetAttributes(backups) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The client backup folder must not be a symbolic link.");
                string folder = Path.Combine(backups, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                backup = Path.Combine(folder, Path.GetFileName(existing));
                File.Copy(existing, backup, false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (existing == target) File.Move(temporary, target, true);
            else
            {
                string? rollback = existing == null ? null : existing + "." + Guid.NewGuid().ToString("N") + ".part";
                if (existing != null) File.Move(existing, rollback!);
                try { File.Move(temporary, target, false); }
                catch { if (rollback != null) File.Move(rollback, existing!); throw; }
                if (rollback != null) File.Delete(rollback);
            }
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task InstallAsync(InstanceModel instance, CancellationToken cancellationToken = default)
    {
        if (!instance.VortexClientEnabled) return;
        if (!IsCompatible(instance.Version, instance.Loader))
            throw new InvalidOperationException("Vortex Client is available for " + SupportedVersionsDescription + ".");
        var build = BuildFor(instance.Version, instance.Loader);
        if (instance.IsRunning)
            throw new InvalidOperationException("Close Minecraft before installing Vortex Client.");
        if (!string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
            (!Version.TryParse(instance.LoaderVersion, out var pinned) || pinned < new Version(0, 19, 5)))
            throw new InvalidOperationException("Vortex Client requires Fabric Loader 0.19.5 or newer.");

        using var bundled = Assembly.GetExecutingAssembly().GetManifestResourceStream(build.ResourceName)
            ?? throw new InvalidOperationException("This launcher build does not include Vortex Client.");
        using var content = new MemoryStream();
        await bundled.CopyToAsync(content, cancellationToken);
        byte[] bytes = content.ToArray();
        ValidateArtifact(bytes, build);
        string mods = Path.Combine(instance.InstancePath, "mods");
        Directory.CreateDirectory(mods);
        string target = Path.Combine(mods, build.FileName);
        byte[] expectedHash = SHA256.HashData(bytes);
        int installedCopies = 0;
        foreach (string file in Directory.EnumerateFiles(mods))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) continue;
            bool sameName = Path.GetFileName(file).Equals(build.FileName, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).Equals(build.FileName + ".disabled", StringComparison.OrdinalIgnoreCase);
            if (!sameName && !HasClientId(file)) continue;
            if (++installedCopies > 1)
                throw new InvalidOperationException("Multiple Vortex Client JARs are installed. Keep only one enabled copy in Mods.");
            if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Vortex Client is disabled in Mods. Enable it there or turn off Vortex Client for this instance.");
            using var existing = File.OpenRead(file);
            if (CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(existing, cancellationToken), expectedHash))
                continue;
            throw new InvalidOperationException("A different Vortex Client JAR is already installed. Remove it in Mods before installing the bundled version; it will not be overwritten.");
        }
        // An identical JAR under another name already satisfies the installation.
        if (installedCopies == 1) return;
        string temporary = Path.Combine(mods, ".vortex-client-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static bool HasClientId(string path)
    {
        try
        {
            using var jar = ZipFile.OpenRead(path);
            var metadata = jar.GetEntry("fabric.mod.json");
            if (metadata == null) return false;
            using var stream = metadata.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("id", out var id) && id.GetString() == "vortex_client";
        }
        catch (InvalidDataException) { return false; }
        catch (JsonException) { return false; }
    }

    private static void ValidateArtifact(byte[] content, ClientBuild build)
    {
        using var stream = new MemoryStream(content);
        using var jar = new ZipArchive(stream);
        using var metadata = (jar.GetEntry("fabric.mod.json") ?? throw new InvalidDataException("Vortex Client metadata is missing.")).Open();
        using var document = JsonDocument.Parse(metadata);
        var root = document.RootElement;
        if (root.GetProperty("id").GetString() != "vortex_client" ||
            root.GetProperty("version").GetString() != ClientVersion ||
            root.GetProperty("environment").GetString() != "client" ||
            root.GetProperty("depends").GetProperty("minecraft").GetString() != build.Minecraft ||
            jar.GetEntry("dev/vortex/client/VortexClient.class") == null)
            throw new InvalidDataException("The bundled Vortex Client is not compatible with this release.");
    }
}
