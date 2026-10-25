using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VortexLauncher.Core.Management;

public sealed record WorldInfo(string Name, DateTime LastPlayed);

/// <summary>
/// A quick, read-only look at what an instance holds, for the workspace overview and its section counts.
/// Links and junctions are never followed, and a folder that cannot be read counts as empty.
/// </summary>
public sealed record InstanceContentSummary(int Mods, int DisabledMods, int ResourcePacks, int ShaderPacks, int Worlds,
    IReadOnlyList<string> ModNames, IReadOnlyList<WorldInfo> RecentWorlds)
{
    public static InstanceContentSummary Empty { get; } = new(0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<WorldInfo>());

    public static InstanceContentSummary Read(string instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(instancePath)) return Empty;
        var mods = Files(Path.Combine(instancePath, "mods"), name => name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
        var enabled = mods.Where(name => !name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)).ToList();
        var names = enabled.Select(DisplayName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        var worlds = new List<WorldInfo>();
        string saves = Path.Combine(instancePath, "saves");
        foreach (var directory in Entries(saves))
        {
            string level = Path.Combine(directory, "level.dat");
            if (!File.Exists(level)) continue;
            try { worlds.Add(new WorldInfo(Path.GetFileName(directory), File.GetLastWriteTime(level))); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new InstanceContentSummary(enabled.Count, mods.Count - enabled.Count,
            Packs(Path.Combine(instancePath, "resourcepacks")), Packs(Path.Combine(instancePath, "shaderpacks")), worlds.Count,
            names, worlds.OrderByDescending(world => world.LastPlayed).ThenBy(world => world.Name, StringComparer.OrdinalIgnoreCase).Take(3).ToList());
    }

    /// <summary>"sodium-fabric-0.6.1+mc1.21.jar" becomes "sodium-fabric-0.6.1+mc1.21".</summary>
    internal static string DisplayName(string fileName)
    {
        string name = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? fileName[..^9] : fileName;
        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static List<string> Files(string folder, Func<string, bool> accept)
    {
        var result = new List<string>();
        try
        {
            if (!Directory.Exists(folder) || IsLink(folder)) return result;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && accept(file.Name)) result.Add(file.Name);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    /// <summary>Resource and shader packs are zip files or unpacked folders.</summary>
    private static int Packs(string folder)
    {
        int count = Files(folder, name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Count;
        return count + Entries(folder).Count();
    }

    private static IEnumerable<string> Entries(string folder)
    {
        var result = new List<string>();
        try
        {
            if (!Directory.Exists(folder) || IsLink(folder)) return result;
            foreach (var directory in new DirectoryInfo(folder).EnumerateDirectories())
                if ((directory.Attributes & FileAttributes.ReparsePoint) == 0) result.Add(directory.FullName);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
