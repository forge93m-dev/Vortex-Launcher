using System;
using System.IO;

namespace VortexLauncher.Core.Launch;

/// <summary>Installation-local data, outside signed Mac bundles. Never reconnect another installation implicitly.</summary>
internal static class LauncherDataLocation
{
    private static string Validate(string root, string temporary)
    {
        if (!Path.IsPathFullyQualified(root)) throw new IOException("The launcher installation path must be absolute.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporary));
        if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase) || root.Equals(temp, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Extract or install Vortex in a permanent, writable folder before launching it. Do not run it from a temporary ZIP folder or a drive root. Your library stays beside the installed application.");
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked library folders are not supported.");
        return root;
    }
    internal static string Resolve(string executableDirectory, string temporary)
    {
        string root = Validate(executableDirectory, temporary);
        if (!Directory.Exists(root)) throw new IOException("The launcher installation folder is unavailable. No replacement library was created.");
        try
        {
            // Fail clearly rather than silently putting data in AppData when the
            // installation is read-only. The probe is removed when its handle closes.
            using var probe = new FileStream(Path.Combine(root, ".vortex-write-test-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Vortex needs write access to its installation folder. Move or install it in a folder you own, rather than a protected system folder. No alternate library was created.", error);
        }
        return root;
    }
    internal static string ResolveMac(string executableDirectory, string temporary)
    {
        // Keep user data outside the signed bundle and next to the installed app.
        // Gatekeeper's translocated copy is not a permanent installation location.
        if (executableDirectory.Contains("/AppTranslocation/", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Move Vortex Launcher.app to a permanent, writable folder before launching it. The library will be stored beside the application, not in a temporary Gatekeeper location.");
        for (var directory = new DirectoryInfo(Path.GetFullPath(executableDirectory)); directory != null; directory = directory.Parent)
        {
            if (!directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) continue;
            string parent = directory.Parent?.FullName ?? throw new IOException("The application needs a permanent installation folder.");
            Resolve(parent, temporary);
            string data = Path.Combine(parent, "Vortex Launcher Data");
            // Validate an existing location before any writes, including symlink rejection.
            Validate(data, temporary);
            Directory.CreateDirectory(data);
            return Resolve(data, temporary);
        }
        // Unbundled development builds also remain installation-local.
        return Resolve(executableDirectory, temporary);
    }
}
