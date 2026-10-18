using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VortexLauncher.Core.Network;

static class UpdateChecks
{
    public static async Task RunAsync(string root, FixtureHandler handler, HttpClient http)
    {
        string work = Directory.CreateDirectory(Path.Combine(root, "updater")).FullName;
        byte[] payload = Encoding.UTF8.GetBytes("verified update fixture");
        string url = "https://fixture.test/VortexLauncher-0.1.3-win-x64.zip";
        var update = new LauncherUpdate { Version = "0.1.3", Notes = "Fixture release", Packages = new()
        {
            ["win-x64"] = new() { Url = url, Kind = "windows-singlefile-zip", Size = payload.Length, Sha256 = Convert.ToHexString(SHA256.HashData(payload)) },
            ["osx-arm64"] = new() { Url = "https://fixture.test/VortexLauncher-0.1.3-osx-arm64.zip", Kind = "mac-app-zip", Size = payload.Length, Sha256 = Convert.ToHexString(SHA256.HashData(payload)) }
        } };
        string manifest = "https://fixture.test/latest.json";
        handler.Files[manifest] = JsonSerializer.SerializeToUtf8Bytes(update);
        handler.Files[url] = payload;
        var windows = new UpdateService(http, "0.1.2", "win-x64");
        var mac = new UpdateService(http, "0.1.2", "osx-arm64");
        Check((await windows.CheckForUpdateAsync(manifest)).Update?.Version == "0.1.3", "new version is detected for Windows");
        Check((await mac.CheckForUpdateAsync(manifest)).Update?.Url.EndsWith("osx-arm64.zip") == true, "Apple Silicon selects its own ZIP, never the Windows EXE");
        var intel = new UpdateService(http, "0.1.2", "osx-x64");
        Check((await intel.CheckForUpdateAsync(manifest)).Message.Contains("no package"), "missing CPU package is explained, not silently treated as up to date");
        Check((await new UpdateService(http, "0.1.3", "win-x64").CheckForUpdateAsync(manifest)).Update == null, "same version does not produce an update loop");
        Check((await new UpdateService(http, "0.1.4", "win-x64").CheckForUpdateAsync(manifest)).Update == null, "older remote build cannot downgrade the launcher");
        Check((await windows.CheckForUpdateAsync("https://fixture.test/missing.json")).Message.Contains("Could not check"), "network failure is not reported as up to date");
        Check((await windows.CheckForUpdateAsync("http://fixture.test/latest.json")).Message.Contains("HTTPS"), "insecure manifest URL is rejected");
        var legacy = new LauncherUpdate { Version = "0.1.3", Url = "https://fixture.test/setup.exe" };
        Check(legacy.GetDownloadUrl("osx-arm64") == null, "legacy Windows-only manifest cannot install on macOS");
        Reject(() => legacy.RequireVerifiedPackage("win-x64"), "manifest without checksums cannot install an unverified program");
        string download = await windows.DownloadAsync(update, Path.Combine(work, "verified"), default, null);
        Check(File.ReadAllBytes(download).SequenceEqual(payload), "launcher update is streamed and verified with SHA-256");
        handler.Files[url] = Encoding.UTF8.GetBytes(new string('x', payload.Length));
        await RejectAsync(() => windows.DownloadAsync(update, Path.Combine(work, "bad-hash"), default, null), "same-size tampered download fails verification");
        Check(!File.Exists(Path.Combine(work, "bad-hash", "package.zip")) && !File.Exists(Path.Combine(work, "bad-hash", "package.zip.part")), "failed checksum publishes no executable and removes only its own partial file");
        handler.Files[url] = payload;
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        await RejectAsync(() => windows.DownloadAsync(update, Path.Combine(work, "cancelled"), stopped.Token, null), "cancelled update does not install a program");

        string safeZip = Path.Combine(work, "safe.zip"); Zip(safeZip, ("Vortex Launcher.exe", "new launcher"));
        string staged = LauncherUpdateInstaller.ExtractPackage(safeZip, Path.Combine(work, "stage"), "win-x64", "0.1.3", verifyBinary: false);
        Check(File.ReadAllText(staged) == "new launcher", "portable update extracts only the declared application executable");
        foreach (string entry in new[] { "../outside.exe", "config/settings.json", "instances/World/level.dat", "another.exe" })
        {
            string bad = Path.Combine(work, "bad-" + Guid.NewGuid().ToString("N") + ".zip");
            Zip(bad, ("Vortex Launcher.exe", "program"), (entry, "unexpected"));
            string destination = Path.Combine(work, "reject-" + Guid.NewGuid().ToString("N"));
            Reject(() => LauncherUpdateInstaller.ExtractPackage(bad, destination, "win-x64", "0.1.3", false), "update ZIP rejects non-application path: " + entry);
            Check(!Directory.Exists(destination), "unsafe package creates no staging directory");
        }
        string app = Path.Combine(work, "mac.zip");
        string plist = "<?xml version=\"1.0\"?><!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\"><plist><dict><key>CFBundleIdentifier</key><string>com.vortexlauncher.desktop</string><key>CFBundleExecutable</key><string>Vortex Launcher</string><key>CFBundleShortVersionString</key><string>0.1.3</string></dict></plist>";
        Zip(app, ("Vortex Launcher.app/Contents/Info.plist", plist), ("Vortex Launcher.app/Contents/MacOS/Vortex Launcher", "fixture"));
        string bundle = LauncherUpdateInstaller.ExtractPackage(app, Path.Combine(work, "mac-stage"), "osx-arm64", "0.1.3", false);
        Check(Directory.Exists(bundle), "Mac bundle identity and version are validated without fetching external XML DTDs");
        Reject(() => LauncherUpdateInstaller.ValidateMacBundle(bundle, "0.1.4"), "Mac package version must match its manifest");

        string installation = Directory.CreateDirectory(Path.Combine(work, "installation")).FullName;
        string target = Path.Combine(installation, "Vortex Launcher.exe"); File.WriteAllText(target, "old launcher");
        Directory.CreateDirectory(Path.Combine(installation, "instances", "World")); Directory.CreateDirectory(Path.Combine(installation, "config"));
        File.WriteAllText(Path.Combine(installation, "instances", "World", "level.dat"), "world");
        File.WriteAllText(Path.Combine(installation, "config", "settings.json"), "settings");
        var plan = new LauncherUpdateInstaller.UpdatePlan { Source = staged, Target = target,
            OriginalSha256 = await LauncherUpdateInstaller.HashAsync(target), PayloadSha256 = await LauncherUpdateInstaller.HashAsync(staged) };
        string backup = await LauncherUpdateInstaller.ReplaceWindowsAsync(plan);
        Check(File.ReadAllText(target) == "new launcher" && File.ReadAllText(backup) == "old launcher", "update replacement is atomic and keeps the exact previous executable");
        Check(File.ReadAllText(Path.Combine(installation, "instances", "World", "level.dat")) == "world" && File.ReadAllText(Path.Combine(installation, "config", "settings.json")) == "settings", "launcher replacement preserves worlds and settings byte-for-byte");
        await RejectAsync(() => LauncherUpdateInstaller.ReplaceWindowsAsync(plan), "changed target blocks a stale update plan");
        plan.OriginalSha256 = await LauncherUpdateInstaller.HashAsync(target);
        plan.PayloadSha256 = new string('0', 64);
        await RejectAsync(() => LauncherUpdateInstaller.ReplaceWindowsAsync(plan), "changed staged payload cannot replace the installed program");
        plan.Target = Path.Combine(installation, "config", "settings.json");
        await RejectAsync(() => LauncherUpdateInstaller.ReplaceWindowsAsync(plan), "helper cannot target a data/settings file");
        Check(typeof(VortexLauncher.ViewModels.MainWindowViewModel).GetProperty("InstallLauncherUpdateCommand") != null,
            "the exact launcher-update command bound by the UI exists");
        Console.WriteLine("LAUNCHER_UPDATE_CHECKS_OK");
    }
    public static async Task VerifyPublishedZipAsync(string archive, string root)
    {
        string version = new UpdateService().CurrentVersion;
        string exe = LauncherUpdateInstaller.ExtractPackage(archive, Path.Combine(root, "native-release"), "win-x64", version);
        var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--launcher-version");
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch { } throw; }
        string text = await output; string details = await error;
        Check(process.ExitCode == 0 && text.Contains(version + " win-x64"), "real self-contained Windows ZIP validates and reports its version without opening a game/app window: " + details);
    }
    public static void VerifyMacCrossBuild(string archive, string root)
    {
        string staged = LauncherUpdateInstaller.ExtractPackage(archive, Path.Combine(root, "mac-cross-build"), "osx-arm64", new UpdateService().CurrentVersion);
        Check(File.Exists(Path.Combine(staged, "Contents/MacOS/Vortex Launcher")), "real Apple Silicon ZIP has matching Mach-O CPU and app version (signature and actual Mac launch are NOT verified on Windows)");
    }
    private static void Zip(string path, params (string Name, string Contents)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries) { using var output = new StreamWriter(archive.CreateEntry(name).Open()); output.Write(content); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static void Reject(Action action, string message)
    { bool failed = false; try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException) { failed = true; } Check(failed, message); }
    private static async Task RejectAsync(Func<Task> action, string message)
    { bool failed = false; try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException) { failed = true; } Check(failed, message); }
}
