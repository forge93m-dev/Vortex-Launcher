using System;
using System.IO;

namespace VortexLauncher.Core.Management;

public static class InstanceContentRemoval
{
    // Keep removed content recoverable, but outside every folder scanned by Minecraft.
    public static string MoveToBackup(string instanceRoot, string section, string source)
    {
        string folder = section switch
        {
            "Mods" => "mods", "Resource packs" => "resourcepacks", "Shader packs" => "shaderpacks",
            _ => throw new InvalidOperationException("This section does not support removal.")
        };
        if (string.IsNullOrWhiteSpace(instanceRoot)) throw new IOException("Instance folder is missing.");
        string root = Path.GetFullPath(instanceRoot);
        string content = Path.Combine(root, folder);
        string path = Path.GetFullPath(source);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(path), content, comparison))
            throw new IOException("The file is outside this content folder.");
        if (!File.Exists(path)) throw new IOException("The file no longer exists.");
        RejectLinks(root);
        RejectLinks(content);
        RejectLinks(path);
        string backup = Path.Combine(root, ".vortex-trash", folder);
        RejectLinks(Path.Combine(root, ".vortex-trash"));
        RejectLinks(backup);
        string destination = Path.Combine(backup, Guid.NewGuid().ToString("N"), Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(path, destination, overwrite: false);
        return destination;
    }

    private static void RejectLinks(string path)
    {
        // Check parents too: lexical containment alone is not safe with junctions/symlinks.
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked folders or files cannot be removed here.");
    }
}
