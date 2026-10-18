using VortexLauncher.Core.Launch;

internal static class DataLocationChecks
{
    public static void Run(string testRoot)
    {
        string root = Directory.CreateDirectory(Path.Combine(testRoot, "locations")).FullName;
        string temporary = Directory.CreateDirectory(Path.Combine(root, "temporary")).FullName;
        string first = Directory.CreateDirectory(Path.Combine(root, "install-a")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(root, "install-b")).FullName;
        string oldRegistry = Directory.CreateDirectory(Path.Combine(root, "old-user-data")).FullName;
        File.WriteAllText(Path.Combine(oldRegistry, "data-location.json"), "{broken}");
        Check(LauncherDataLocation.Resolve(first, temporary) == first, "fresh installation owns its library beside the executable");
        Directory.CreateDirectory(Path.Combine(first, "instances", "Existing"));
        string marker = Path.Combine(first, "instances", "Existing", "world-marker");
        File.WriteAllText(marker, "keep");
        Check(LauncherDataLocation.Resolve(first, temporary) == first && File.ReadAllText(marker) == "keep", "restarting/updating in the same folder preserves the existing library");
        Check(LauncherDataLocation.Resolve(second, temporary) == second && !Directory.Exists(Path.Combine(second, "instances")), "another installation starts clean instead of reconnecting the old library");
        Check(File.ReadAllText(marker) == "keep" && File.ReadAllText(Path.Combine(oldRegistry, "data-location.json")) == "{broken}", "old libraries and legacy global pointers are neither read nor rewritten");
        Check(!Directory.EnumerateFiles(first, ".vortex-write-test-*").Any() && !Directory.EnumerateFiles(second).Any(), "write-access validation leaves no probe files or global pointers");
        string zip = Directory.CreateDirectory(Path.Combine(temporary, "zip-a")).FullName;
        Reject(() => LauncherDataLocation.Resolve(zip, temporary), "temporary ZIP launch folders fail clearly instead of silently using AppData");
        Reject(() => LauncherDataLocation.Resolve(Path.GetPathRoot(first)!, temporary), "drive-root installations are rejected");
        Reject(() => LauncherDataLocation.Resolve(Path.Combine(root, "missing"), temporary), "missing installation folders are not replaced elsewhere");
        string macInstall = Directory.CreateDirectory(Path.Combine(root, "mac-install")).FullName;
        string macBinary = Directory.CreateDirectory(Path.Combine(macInstall, "Vortex Launcher.app", "Contents", "MacOS")).FullName;
        string macData = LauncherDataLocation.ResolveMac(macBinary, temporary);
        Check(macData == Path.Combine(macInstall, "Vortex Launcher Data") && !Directory.Exists(Path.Combine(macBinary, "instances")), "macOS data stays beside the application and never changes its signed bundle");
        Directory.CreateDirectory(Path.Combine(macData, "instances"));
        File.WriteAllText(Path.Combine(macData, "instances", "keep"), "keep");
        Check(LauncherDataLocation.ResolveMac(macBinary, temporary) == macData && File.ReadAllText(Path.Combine(macData, "instances", "keep")) == "keep", "macOS application replacement reuses its adjacent library");
        string anotherMac = Directory.CreateDirectory(Path.Combine(root, "another-mac", "Vortex Launcher.app", "Contents", "MacOS")).FullName;
        Check(LauncherDataLocation.ResolveMac(anotherMac, temporary) != macData, "a separate Mac installation does not connect another application's library");
        Reject(() => LauncherDataLocation.ResolveMac("/private/var/folders/example/AppTranslocation/id/d/Vortex Launcher.app/Contents/MacOS", temporary), "Gatekeeper translocation cannot become the library location");
        if (OperatingSystem.IsWindows())
        {
            Check(DownloadCache.Root == Path.Combine(PlatformSupport.DataRoot, "cache", "downloads"), "download cache belongs to the current installation");
            Check(VortexLauncher.Core.Media.ArtworkCache.DiskRoot == Path.Combine(PlatformSupport.DataRoot, "cache", "artwork"), "artwork cache belongs to the current installation");
            Check(VortexLauncher.Core.Accounts.VortexSessionStore.DefaultPath == Path.Combine(PlatformSupport.DataRoot, "config", "launcher-session.dpapi"), "remembered sign-in is installation-local and does not reuse another installation's session");
        }
        Console.WriteLine("DATA_LOCATION_OK");
    }
    private static void Check(bool success, string message) { if (!success) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static void Reject(Action action, string message) { try { action(); } catch (IOException) { Check(true, message); return; } throw new Exception(message); }
}
