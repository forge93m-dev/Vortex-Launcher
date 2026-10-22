using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VortexLauncher.Core.Management;

namespace VortexLauncher.Core.Launch;

// Only immutable downloaded inputs are shared. Worlds, mods, natives and Forge
// processor outputs remain private to the instance. Paths on disk are portable.
internal static class SharedGameFiles
{
    internal sealed class Layout
    {
        public int Format { get; set; } = 1;
        public string Version { get; set; } = "";
        public string AssetIndex { get; set; } = "";
        public bool SharedLibraries { get; set; }
        public List<string> Libraries { get; set; } = new();
        public List<string> LoaderLibraries { get; set; } = new();
    }

    internal static string Root => Path.Combine(PlatformSupport.DataRoot, "game-files");
    internal static string Assets => Checked(Path.Combine(Root, "assets"));
    private static string Manifest(string instance) => Path.Combine(instance, ".vortex-shared-game.json");
    internal static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal sealed class AssetPaths
    {
        private readonly string _objects = Path.Combine(Assets, "objects");
        private readonly HashSet<string> _checkedFolders = new(StringComparer.Ordinal);
        internal string Resolve(string hash, bool createDirectory = true)
        {
            if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid asset hash.");
            string directory = Path.Combine(_objects, hash[..2]);
            // There are at most 256 hash-prefix folders. Validate each once
            // per preparation, rather than restatting every ancestor 5,000 times.
            if (_checkedFolders.Add(directory))
            {
                Checked(directory);
                if (createDirectory) Directory.CreateDirectory(directory);
            }
            string path = Path.Combine(directory, hash);
            var info = new FileInfo(path);
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Shared assets cannot use linked files.");
            return path;
        }
    }
    internal static string Library(string url, string name, string? sha1 = null)
    {
        string filename = Path.GetFileName(name.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(filename) || filename is "." or ".." || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Invalid shared library filename.");
        return Checked(Path.Combine(Root, "libraries", Key(url + "|" + sha1?.ToLowerInvariant()), filename));
    }

    internal static string Checked(string path)
    {
        string root = Path.GetFullPath(PlatformSupport.DataRoot);
        string full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Shared game path is outside the launcher folder.");
        for (string? cursor = full; cursor != null && !string.Equals(cursor, root, comparison); cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Shared game files cannot use linked folders. Repair the instance.");
        return full;
    }

    private static bool Valid(Layout value) => value.Format == 1 && !string.IsNullOrWhiteSpace(value.Version) &&
        value.AssetIndex != null && value.AssetIndex.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') &&
        value.AssetIndex != "." && value.AssetIndex != ".." && value.Libraries != null && value.LoaderLibraries != null &&
        value.Libraries.Count + value.LoaderLibraries.Count <= 4096 &&
        value.Libraries.Concat(value.LoaderLibraries).All(p => !string.IsNullOrWhiteSpace(p) && !Path.IsPathRooted(p) &&
            p.Replace('\\', '/').Split('/').All(part => part.Length > 0 && part != "." && part != ".." && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) &&
            p.Replace('\\', '/').StartsWith("libraries/", StringComparison.Ordinal));

    internal static Layout? Read(string instance, bool allowDamaged = false)
    {
        string path = Manifest(instance);
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
        Checked(path); Checked(path + ".bak");
        foreach (string candidate in new[] { path, path + ".bak" })
            if (File.Exists(candidate) && new FileInfo(candidate).Length > 1024 * 1024)
                throw new InvalidDataException("Shared game metadata is too large. Repair the instance.");
        var value = SafeJsonStore.Read<Layout>(path, Valid).Value;
        if (value == null && !allowDamaged) throw new InvalidDataException("Shared game metadata is damaged. Repair the instance.");
        return value;
    }

    internal static void Save(string instance, Layout layout)
    {
        Checked(Manifest(instance)); Checked(Manifest(instance) + ".bak");
        SafeJsonStore.Write(Manifest(instance), layout, Valid);
    }
    internal static string Relative(string path) => Path.GetRelativePath(Root, Checked(path)).Replace('\\', '/');
    internal static string AssetsFor(string instance) => Read(instance) == null ? Path.Combine(instance, "assets") : Assets;
    internal static string IndexFor(string instance, string fallback) => Read(instance) is { AssetIndex.Length: > 0 } layout ? layout.AssetIndex : fallback;
    internal static string[]? LibrariesFor(string instance, string version)
    {
        var layout = Read(instance);
        if (layout == null || !layout.SharedLibraries) return null;
        if (layout.Version != version) throw new InvalidDataException("Shared game metadata belongs to a different version. Repair the instance.");
        return layout.Libraries.Concat(layout.LoaderLibraries).Distinct().Select(p => Checked(Path.Combine(Root, p.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
    }
}
