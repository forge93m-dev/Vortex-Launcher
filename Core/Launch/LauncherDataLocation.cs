using System;
using System.IO;
using VortexLauncher.Core.Management;

namespace VortexLauncher.Core.Launch;

/// <summary>One remembered library per OS user. Existing portable data is connected, never moved.</summary>
internal static class LauncherDataLocation
{
    internal sealed record Location(string Root);
    internal static string RegistryDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VortexLauncher");
    internal static bool HasLibrary(string root) => Directory.Exists(Path.Combine(root, "instances")) || File.Exists(Path.Combine(root, "config", "settings.json"));
    private static string Validate(string root, string temporary)
    {
        if (!Path.IsPathFullyQualified(root)) throw new IOException("The saved library path must be absolute.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporary));
        if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase) || root.Equals(temp, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a permanent Vortex folder, not a temporary folder or drive root.");
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked library folders are not supported.");
        return root;
    }
    internal static string Resolve(string executableDirectory, string registryDirectory, string temporary)
    {
        string registry = Path.Combine(registryDirectory, "data-location.json");
        var stored = SafeJsonStore.Read<Location>(registry, value => !string.IsNullOrWhiteSpace(value.Root) && Path.IsPathFullyQualified(value.Root));
        if (stored.Damaged) throw new IOException("The Vortex library location could not be read. Original files were preserved: " + registry);
        if (stored.Value is { } location) {
            string saved = Validate(location.Root, temporary);
            if (!Directory.Exists(saved)) throw new IOException("The saved Vortex library is unavailable. Reconnect its drive or folder: " + saved);
            return saved;
        }
        string root = Path.Combine(registryDirectory, "data");
        if (HasLibrary(executableDirectory)) {
            // ZIP launchers can have short-lived extraction directories. Never remember those.
            try { root = Validate(executableDirectory, temporary); }
            catch (IOException) { }
        }
        root = Validate(root, temporary);
        Directory.CreateDirectory(root);
        SafeJsonStore.Write(registry, new Location(root));
        return root;
    }
    internal static void Connect(string root, string registryDirectory, string temporary)
    {
        root = Validate(root, temporary);
        if (!HasLibrary(root)) throw new IOException("Select the old Vortex folder containing instances or config/settings.json. Nothing was changed.");
        SafeJsonStore.Write(Path.Combine(registryDirectory, "data-location.json"), new Location(root));
    }
}
