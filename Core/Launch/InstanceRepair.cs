using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Launch;

public static class InstanceRepair
{
    public static void ValidateTarget(InstanceModel instance)
    {
        string root = Path.GetFullPath(Path.Combine(PlatformSupport.DataRoot, "instances"));
        string path = Path.GetFullPath(instance.InstancePath).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Repair is restricted to an instance inside this launcher's instances folder.");
        ArchiveImportPlan.ValidatePath(instance.Version);
        if (instance.Version.Contains('/') || instance.Version.Contains('\\')) throw new IOException("Invalid Minecraft version.");
        // Reject junctions rather than following them outside the scoped game directories.
        for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Repair cannot follow linked instance folders.");
        foreach (string name in new[] { "libraries", "assets", "versions", "natives" })
        {
            var dir = new DirectoryInfo(Path.Combine(path, name));
            if (!dir.Exists) continue;
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Repair cannot follow linked game directories.");
            foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false }))
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Repair cannot follow linked game files.");
        }
        foreach (string name in new[] { instance.Version + ".jar", "version.json", "instance.json", ".vortex-installation-pending", ".vortex-installation-complete" })
            if (File.Exists(Path.Combine(path, name)) && (File.GetAttributes(Path.Combine(path, name)) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Repair cannot replace linked metadata or game files.");
    }

    public static string ResolveInstalledLoaderVersion(InstanceModel instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.LoaderVersion) && instance.LoaderVersion != "latest") return instance.LoaderVersion;
        string libraries = Path.Combine(instance.InstancePath, "libraries");
        if (!Directory.Exists(libraries) || instance.Loader == "Vanilla") return "";
        string prefix = instance.Loader == "Fabric" ? "fabric-loader-" : "quilt-loader-";
        string[] versions;
        if (instance.Loader == "Forge")
        {
            string forge = Path.Combine(libraries, "net", "minecraftforge", "forge");
            versions = Directory.Exists(forge) ? Directory.GetDirectories(forge).Select(Path.GetFileName)
                .OfType<string>().Where(v => v.StartsWith(instance.Version + "-", StringComparison.Ordinal)).ToArray() : Array.Empty<string>();
        }
        else versions = Directory.EnumerateFiles(libraries, prefix + "*.jar", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().Select(n => n[prefix.Length..]).Where(v => Regex.IsMatch(v, @"^\d+(\.\d+)+([-.][\w.]+)?$")).Distinct().ToArray();
        if (versions.Length > 1) throw new InvalidOperationException("More than one loader version is installed. Select the intended loader version before repairing.");
        return versions.SingleOrDefault() ?? "";
    }
}
