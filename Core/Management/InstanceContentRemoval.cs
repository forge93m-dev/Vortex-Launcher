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

    public static string ValidateRestore(string instanceRoot, string section, string backupPath)
    {
        string folder = section switch { "Mods" => "mods", "Resource packs" => "resourcepacks", "Shader packs" => "shaderpacks", _ => throw new IOException("This section cannot be restored.") };
        string root = Path.GetFullPath(instanceRoot), backup = Path.GetFullPath(backupPath);
        string? batch = Path.GetDirectoryName(backup);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (batch == null || !Guid.TryParseExact(Path.GetFileName(batch), "N", out _) ||
            !string.Equals(Path.GetDirectoryName(batch), Path.Combine(root, ".vortex-trash", folder), comparison))
            throw new IOException("The backup is outside this instance's removal history.");
        RejectLinks(root); RejectLinks(backup);
        string destination = Path.Combine(root, folder, Path.GetFileName(backup));
        RejectLinks(destination);
        if (!File.Exists(backup)) throw new IOException("The removed file is no longer in its backup folder.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("A file with this name already exists. Nothing was overwritten.");
        if (section == "Mods")
        {
            string alternative = destination.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? destination[..^9] : destination + ".disabled";
            if (File.Exists(alternative) || Directory.Exists(alternative)) throw new IOException("An enabled/disabled copy already exists. Nothing was overwritten.");
        }
        return destination;
    }
    public static void RestoreFromBackup(string root, string section, string backup)
    {
        string destination = ValidateRestore(root, section, backup);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(backup, destination, overwrite: false);
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
