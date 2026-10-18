using System;
using System.IO;

namespace VortexLauncher.Core.Management;

internal static class ModFiles
{
    internal static bool IsMod(string path) => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    internal static bool IsDisabled(string path) => path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    internal static string EnabledName(string path)
    {
        string name = Path.GetFileName(path);
        if (IsDisabled(name)) name = name[..^9];
        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ? name : name + ".jar";
    }
    internal static string Validate(string root, string path)
    {
        string full = Path.GetFullPath(path);
        string mods = Path.Combine(Path.GetFullPath(root), "mods");
        if (!string.Equals(Path.GetDirectoryName(full), mods, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !IsMod(full))
            throw new IOException("Not a mod in this instance.");
        RejectLinks(full);
        return full;
    }
    internal static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files/folders cannot be changed here.");
    }
    internal static void SetEnabled(string root, string path, bool enabled)
    {
        string source = Validate(root, path);
        if (enabled == !IsDisabled(source)) return;
        string destination = Path.Combine(Path.GetDirectoryName(source)!, EnabledName(source) + (enabled ? "" : ".disabled"));
        RejectLinks(destination);
        File.Move(source, destination, overwrite: false);
    }
}
