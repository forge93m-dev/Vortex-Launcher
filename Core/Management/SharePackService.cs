using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using VortexLauncher.Core.Launch;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Management;

public sealed record SharePackOptions(bool Mods = true, bool ResourcePacks = true, bool Shaders = true, bool Config = false, bool Worlds = false, bool Screenshots = false);
public sealed record SharePackFile(string RelativePath, long Length, string Sha256);
public sealed record SharePackPreview(string Root, string Metadata, SharePackOptions Options, IReadOnlyList<SharePackFile> Files)
{
    public long Bytes => Files.Sum(f => f.Length);
}

public static class SharePackService
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { "logs", "crash-reports", ".vortex-trash", ".vortex-mod-updates", ".git", ".cache", "cache", "accounts.json", "launcher_accounts.json", "servers.dat", "servers.dat_old", "session.lock", ".env", "credentials.json", "tokens.json", "auth.json" };
    private static string Metadata(InstanceModel item) => JsonSerializer.Serialize(new
    { item.Name, item.Version, item.Loader, item.LoaderVersion, item.VortexClientEnabled, ShareFormatVersion = 1 }, new JsonSerializerOptions { WriteIndented = true });
    private static void CheckInstance(InstanceModel instance)
    {
        if (!instance.CanChangeFiles) throw new IOException("Stop the game and finish instance operations before sharing.");
        if (string.IsNullOrWhiteSpace(instance.Version) || string.IsNullOrWhiteSpace(instance.Loader) || !Directory.Exists(instance.InstancePath)) throw new IOException("The instance folder, game version and loader are required.");
        RejectLinks(instance.InstancePath);
    }
    public static SharePackPreview Preview(InstanceModel instance, SharePackOptions options, CancellationToken token = default)
    {
        CheckInstance(instance); token.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(instance.InstancePath);
        var files = new List<SharePackFile>();
        var portableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "instance.json" };
        foreach (string file in Enumerate(root, options, token))
        {
            token.ThrowIfCancellationRequested(); CheckInstance(instance); RejectLinks(file);
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            ArchiveImportPlan.ValidatePath(relative);
            if (!portableNames.Add(relative)) throw new IOException("The selected files have conflicting portable names.");
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            files.Add(new(relative, length, CopyAndHash(stream, null, token)));
            if (files.Count > 250000) throw new IOException("Too many files. Share fewer categories at a time.");
        }
        return new(root, Metadata(instance), options, files.ToArray());
    }
    public static void Export(InstanceModel instance, SharePackPreview preview, string destination, CancellationToken token = default, IProgress<string>? progress = null)
    {
        CheckInstance(instance); token.ThrowIfCancellationRequested();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string root = Path.GetFullPath(instance.InstancePath), target = Path.GetFullPath(destination);
        if (!string.Equals(root, preview.Root, comparison) || Metadata(instance) != preview.Metadata) throw new IOException("Instance metadata changed. Build a fresh preview.");
        if (target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)) throw new IOException("Save the shared ZIP outside the instance folder.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Choose a new ZIP name; existing files are not overwritten.");
        RejectLinks(Path.GetDirectoryName(target)!);
        var current = Enumerate(root, preview.Options, token).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (!current.SequenceEqual(preview.Files.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal))) throw new IOException("The selected file list changed. Build a fresh preview.");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var file in preview.Files)
                {
                    token.ThrowIfCancellationRequested(); CheckInstance(instance);
                    ArchiveImportPlan.ValidatePath(file.RelativePath);
                    string source = Path.GetFullPath(Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!source.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)) throw new IOException("A file escaped the instance folder.");
                    RejectLinks(source);
                    progress?.Report("Packing " + file.RelativePath);
                    using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length != file.Length) throw new IOException("A previewed file changed: " + file.RelativePath);
                    using var entry = zip.CreateEntry(file.RelativePath, CompressionLevel.Fastest).Open();
                    if (CopyAndHash(input, entry, token) != file.Sha256) throw new IOException("A previewed file changed: " + file.RelativePath);
                }
                using var writer = new StreamWriter(zip.CreateEntry("instance.json").Open());
                writer.Write(preview.Metadata);
            }
            token.ThrowIfCancellationRequested(); CheckInstance(instance);
            File.Move(temporary, target, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static IEnumerable<string> Enumerate(string root, SharePackOptions options, CancellationToken token)
    {
        foreach (var (folder, enabled) in new[] { ("mods", options.Mods), ("resourcepacks", options.ResourcePacks), ("shaderpacks", options.Shaders), ("config", options.Config), ("saves", options.Worlds), ("screenshots", options.Screenshots) })
        {
            token.ThrowIfCancellationRequested();
            string directory = Path.Combine(root, folder);
            if (!enabled || !Directory.Exists(directory)) continue;
            foreach (string file in Walk(directory, token, 0))
            {
                if (folder == "mods" && !(file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))) continue;
                if (folder == "screenshots" && !new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                yield return file;
            }
        }
    }
    private static IEnumerable<string> Walk(string directory, CancellationToken token, int depth)
    {
        token.ThrowIfCancellationRequested(); RejectLinks(directory);
        if (depth > 128) throw new IOException("A selected folder is too deeply nested to share safely.");
        foreach (string file in Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(file);
            if (Excluded.Contains(name) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            RejectLinks(file); yield return file;
        }
        foreach (string child in Directory.EnumerateDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
            if (!Excluded.Contains(Path.GetFileName(child))) foreach (string file in Walk(child, token, depth + 1)) yield return file;
    }
    private static string CopyAndHash(Stream source, Stream? destination, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024]; int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); destination?.Write(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders or files cannot be shared.");
    }
}
