using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Launch;

// PolyMC/MultiMC store launcher metadata beside a separate Minecraft directory.
// Import game content only; Java paths, commands and runtime caches are machine-specific.
internal sealed class PolyMcArchive
{
    public InstanceModel Instance { get; }
    private readonly List<(ZipArchiveEntry Entry, string Path)> _files;
    public IReadOnlyList<string> ContentPaths => _files.Select(f => f.Path).ToArray();

    private PolyMcArchive(InstanceModel instance, List<(ZipArchiveEntry, string)> files)
    {
        Instance = instance;
        _files = files;
    }

    public static PolyMcArchive? Read(ZipArchive archive, string fallbackName)
    {
        var manifests = archive.Entries.Where(e =>
            Normalize(e.FullName).Split('/').Last() == "mmc-pack.json").ToArray();
        if (manifests.Length == 0) return null;
        if (manifests.Length != 1)
            throw new InvalidDataException("This archive contains multiple instances. Export one instance at a time.");

        // Validate every entry before creating an instance directory, including ignored entries.
        foreach (var entry in archive.Entries)
        {
            ValidatePath(Normalize(entry.FullName).TrimEnd('/'));
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Symbolic links are not supported in instance archives.");
        }
        string manifestPath = Normalize(manifests[0].FullName);
        string prefix = manifestPath[..^"mmc-pack.json".Length];
        using var stream = manifests[0].Open();
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The PolyMC component list is missing.");

        var instance = new InstanceModel { Name = fallbackName };
        foreach (var component in components.EnumerateArray())
        {
            string uid = component.GetProperty("uid").GetString() ?? "";
            string version = component.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            if (uid == "net.minecraft") instance.Version = version;
            string? loader = uid switch
            {
                "net.fabricmc.fabric-loader" => "Fabric",
                "net.minecraftforge" => "Forge",
                "org.quiltmc.quilt-loader" => "Quilt",
                _ => null
            };
            if (loader != null)
            {
                if (instance.Loader != "Vanilla")
                    throw new InvalidDataException("This instance contains conflicting loaders.");
                if (string.IsNullOrWhiteSpace(version))
                    throw new InvalidDataException($"The {loader} version is missing.");
                instance.Loader = loader;
                instance.LoaderVersion = version;
            }
            else if (uid != "net.minecraft" && uid != "net.fabricmc.intermediary" &&
                     uid != "org.lwjgl" && uid != "org.lwjgl3")
                throw new InvalidDataException($"The PolyMC component '{uid}' is not supported by this launcher.");
        }
        if (string.IsNullOrWhiteSpace(instance.Version))
            throw new InvalidDataException("The Minecraft version is missing from mmc-pack.json.");

        var cfg = archive.Entries.FirstOrDefault(e => Normalize(e.FullName) == prefix + "instance.cfg");
        if (cfg != null)
        {
            using var reader = new StreamReader(cfg.Open());
            while (reader.ReadLine() is { } line)
            {
                int separator = line.IndexOf('=');
                if (separator < 0) continue;
                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (key == "name" && !string.IsNullOrWhiteSpace(value)) instance.Name = value;
                if (key == "totalTimePlayed" && long.TryParse(value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long seconds))
                    instance.TotalPlayTimeSeconds = Math.Max(0, seconds);
            }
        }

        string[] roots = { prefix + ".minecraft/", prefix + "minecraft/" };
        var presentRoots = roots.Where(root => archive.Entries.Any(e =>
            !Normalize(e.FullName).EndsWith('/') &&
            Normalize(e.FullName).StartsWith(root, StringComparison.Ordinal))).ToArray();
        // A newly created PolyMC instance can be exported before its first run.
        // Its components are sufficient to install the game; there is no game
        // directory yet, so an empty content list is valid, not a broken archive.
        if (presentRoots.Length == 0)
        {
            var unexpected = archive.Entries.FirstOrDefault(e =>
            {
                string path = Normalize(e.FullName);
                if (path.EndsWith('/')) return false;
                string relative = path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
                return relative is not ("mmc-pack.json" or "instance.cfg" or ".packignore" or "icon.png" or "icon.jpg");
            });
            if (unexpected != null)
                throw new InvalidDataException("The archive has files outside the Minecraft content directory. Export it again with its minecraft or .minecraft folder.");
            return new PolyMcArchive(instance, new List<(ZipArchiveEntry, string)>());
        }
        if (presentRoots.Length > 1)
            throw new InvalidDataException("The archive contains files in both minecraft and .minecraft. Export only the active Minecraft content directory.");
        string gameRoot = presentRoots[0];
        var files = new List<(ZipArchiveEntry, string)>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            string path = Normalize(entry.FullName);
            if (!path.StartsWith(gameRoot, StringComparison.Ordinal) || path.EndsWith('/')) continue;
            string relative = path[gameRoot.Length..];
            string first = relative.Split('/')[0];
            if (first is ".fabric" or ".quilt" or ".cache" or "natives" or "libraries" or "assets" or "downloads") continue;
            if (first is "instance.json" or ".vortex-importing" or ".vortex-cloning" or ".vortex-installation-complete")
                throw new InvalidDataException("The archive contains reserved launcher metadata inside Minecraft content.");
            if (!destinations.Add(relative))
                throw new InvalidDataException($"The archive contains duplicate file paths: {relative}");
            files.Add((entry, relative));
        }
        return new PolyMcArchive(instance, files);
    }

    public void Extract(string directory, string? operationId = null, System.Threading.CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        foreach (var (entry, relative) in _files)
        {
            string target = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException("An archive path escapes the instance directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            VortexLauncher.Core.Management.VerifiedCopy.FileAsync(source, target, operationId ?? Guid.NewGuid().ToString("N"), cancellationToken, directory).GetAwaiter().GetResult();
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            path.Split('/').Any(part => part is ".." or "." or "" ||
                part.IndexOfAny(new[] { ':', '\0', '<', '>', '"', '|', '?', '*' }) >= 0 ||
                part.EndsWith(' ') || part.EndsWith('.')))
            throw new InvalidDataException($"The archive contains an unsafe path: {path}");
    }
}
