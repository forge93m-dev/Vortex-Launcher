using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Network;

// The helper is the new self-contained launcher, started before the old process exits.
// It replaces only one executable/app bundle. Instances/config/runtime are never targets.
public static class LauncherUpdateInstaller
{
    public static string StagingRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VortexLauncher", "updates");
    private const string AppName = "Vortex Launcher.app";
    private const string ExeName = "Vortex Launcher.exe";

    internal static string ExtractPackage(string archivePath, string directory, string rid, string version, bool verifyBinary = true, CancellationToken token = default)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        bool mac = rid.StartsWith("osx-", StringComparison.Ordinal);
        if (archive.Entries.Count == 0 || archive.Entries.Count > 5000 || archive.Entries.Sum(e => e.Length) > 2147483648L)
            throw new InvalidDataException("Invalid launcher package size.");
        var paths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            string path = entry.FullName.TrimEnd('/');
            ArchiveImportPlan.ValidatePath(path);
            if (!paths.Add(path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("Duplicate or linked update package entry.");
            bool appleMetadata = mac && (path == "__MACOSX/" + AppName || path.StartsWith("__MACOSX/" + AppName + "/", StringComparison.Ordinal));
            if (mac ? !(path == AppName || path.StartsWith(AppName + "/", StringComparison.Ordinal) || appleMetadata || path == "__MACOSX") : path != ExeName)
                throw new InvalidDataException("The update contains files outside the launcher application.");
        }
        // All paths are validated before extracting anything, and the staging folder is new.
        if (Directory.Exists(directory)) throw new IOException("The update staging folder already exists.");
        Directory.CreateDirectory(directory);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal)) continue;
            string target = Path.Combine(directory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = entry.Open())
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] buffer = new byte[81920]; long written = 0; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested(); written += count;
                    if (written > entry.Length) throw new InvalidDataException("An update file exceeded its declared size.");
                    output.Write(buffer, 0, count);
                }
                if (written != entry.Length) throw new InvalidDataException("An update file is truncated.");
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1FF));
        }
        string staged = mac ? Path.Combine(directory, AppName) : Path.Combine(directory, ExeName);
        string binary = mac ? Path.Combine(staged, "Contents", "MacOS", "Vortex Launcher") : staged;
        if (mac) ValidateMacBundle(staged, version);
        if (!File.Exists(binary)) throw new InvalidDataException("The launcher executable is missing from the update.");
        if (verifyBinary) ValidateBinary(binary, rid, version);
        return staged;
    }

    internal static void ValidateMacBundle(string path, string? version = null)
    {
        string plist = Path.Combine(path, "Contents", "Info.plist");
        if (!File.Exists(plist) || new FileInfo(plist).Length > 1048576) throw new InvalidDataException("The macOS app metadata is missing or too large.");
        using var reader = XmlReader.Create(plist, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var fields = XDocument.Load(reader).Root?.Element("dict")?.Elements().ToArray() ?? Array.Empty<XElement>();
        string? Get(string key) => fields.Select((f, i) => (f, i)).Where(p => p.f.Name == "key" && p.f.Value == key && p.i + 1 < fields.Length)
            .Select(p => fields[p.i + 1].Value).SingleOrDefault();
        if (Get("CFBundleIdentifier") != "com.vortexlauncher.desktop" || Get("CFBundleExecutable") != "Vortex Launcher" ||
            (version != null && Get("CFBundleShortVersionString") != version))
            throw new InvalidDataException("The macOS application identity/version does not match the update.");
    }

    private static void ValidateBinary(string binary, string rid, string version)
    {
        using var stream = File.OpenRead(binary);
        if (rid.StartsWith("win-", StringComparison.Ordinal))
        {
            using var pe = new PEReader(stream);
            var expected = rid.EndsWith("arm64", StringComparison.Ordinal) ? Machine.Arm64 : Machine.Amd64;
            if (pe.PEHeaders.CoffHeader.Machine != expected) throw new InvalidDataException("Wrong Windows update CPU architecture.");
            string? product = FileVersionInfo.GetVersionInfo(binary).ProductVersion?.Split('+')[0];
            if (product != version) throw new InvalidDataException("The Windows launcher version does not match the manifest.");
        }
        else
        {
            Span<byte> header = stackalloc byte[8]; stream.ReadExactly(header);
            uint cpu = BitConverter.ToUInt32(header[4..]);
            uint expected = rid == "osx-arm64" ? 0x0100000Cu : 0x01000007u;
            if (BitConverter.ToUInt32(header[..4]) != 0xFEEDFACF || cpu != expected)
                throw new InvalidDataException("Wrong macOS update CPU architecture.");
        }
    }

    internal static async Task VerifyMacSignatureAsync(string app, CancellationToken token)
    {
        await RunToolAsync("/usr/bin/codesign", new[] { "--verify", "--deep", "--strict", app }, token);
    }

    internal static async Task<int> RunToolAsync(string executable, string[] arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not start " + executable);
        var error = process.StandardError.ReadToEndAsync(token);
        var output = process.StandardOutput.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { try { process.Kill(true); } catch { } throw; }
        string details = await error; await output;
        if (process.ExitCode != 0) throw new IOException(Path.GetFileName(executable) + " failed: " + details.Trim());
        return process.ExitCode;
    }

    internal static string? CurrentTarget(string executable)
    {
        if (OperatingSystem.IsWindows()) return Path.GetFileName(executable).Equals(ExeName, StringComparison.OrdinalIgnoreCase) ? executable : null;
        if (!OperatingSystem.IsMacOS()) return null;
        var macos = Directory.GetParent(executable);
        var bundle = macos?.Parent?.Parent;
        return macos?.Name == "MacOS" && macos.Parent?.Name == "Contents" && bundle?.Name == AppName ? bundle.FullName : null;
    }

    internal static async Task<bool> StartAsync(string staged, string directory, string version, string kind, CancellationToken token)
    {
        string? target = Environment.ProcessPath is { } executable ? CurrentTarget(executable) : null;
        if (target == null || (OperatingSystem.IsWindows() && File.Exists(Path.Combine(Path.GetDirectoryName(target)!, "Vortex Launcher.dll"))))
        {
            PlatformSupport.OpenPath(Path.GetDirectoryName(staged)!);
            return false; // Developer/non-single-file builds use a visible manual replacement, never self-delete.
        }
        RejectLinks(target);
        if (OperatingSystem.IsMacOS()) ValidateMacBundle(target);
        string currentBinary = OperatingSystem.IsMacOS() ? Path.Combine(target, "Contents/MacOS/Vortex Launcher") : target;
        var plan = new UpdatePlan { Target = Path.GetFullPath(target), Source = Path.GetFullPath(staged), Version = version, Kind = kind,
            ParentPid = Environment.ProcessId, ParentStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            OriginalSha256 = await HashAsync(currentBinary, token),
            PayloadSha256 = await HashAsync(OperatingSystem.IsMacOS() ? Path.Combine(staged, "Contents/MacOS/Vortex Launcher") : staged, token) };
        string planPath = Path.Combine(directory, "apply.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), token);
        token.ThrowIfCancellationRequested();
        string helper = OperatingSystem.IsMacOS() ? Path.Combine(staged, "Contents/MacOS/Vortex Launcher") : staged;
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        start.ArgumentList.Add("--apply-launcher-update"); start.ArgumentList.Add(planPath);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the update helper. The installed launcher was not changed.");
        return true;
    }

    public static bool TryRunHelper(string[] args)
    {
        if (args.Length != 2 || args[0] != "--apply-launcher-update") return false;
        Environment.ExitCode = RunHelperAsync(args[1]).GetAwaiter().GetResult();
        return true;
    }

    private static async Task<int> RunHelperAsync(string planPath)
    {
        string? result = null;
        UpdatePlan? trustedPlan = null;
        try
        {
            string path = Path.GetFullPath(planPath);
            string directory = Path.GetDirectoryName(path)!;
            if (!directory.StartsWith(Path.GetFullPath(StagingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || Path.GetFileName(path) != "apply.json")
                throw new IOException("The update plan is outside the updater staging area.");
            RejectLinks(path);
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Update plan is too large.");
            var plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Invalid update plan.");
            if (plan.Kind is not ("mac-app-zip" or "windows-singlefile-zip")) throw new InvalidDataException("Unsupported updater plan.");
            result = Path.Combine(directory, "result.json");
            string expectedSource = Path.Combine(directory, "staged", plan.Kind == "mac-app-zip" ? AppName : ExeName);
            if (Path.GetFullPath(plan.Source) != Path.GetFullPath(expectedSource)) throw new IOException("Invalid staged application path.");
            string ownExecutable = plan.Kind == "mac-app-zip" ? Path.Combine(plan.Source, "Contents/MacOS/Vortex Launcher") : plan.Source;
            if (Path.GetFullPath(Environment.ProcessPath ?? "") != Path.GetFullPath(ownExecutable)) throw new IOException("The helper is not running from the staged update.");
            if (plan.ParentPid == Environment.ProcessId) throw new IOException("The update helper cannot replace itself.");
            ValidateReplacement(plan, plan.Kind == "mac-app-zip");
            trustedPlan = plan;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                using var parent = Process.GetProcessById(plan.ParentPid);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStarted) await parent.WaitForExitAsync(timeout.Token);
            }
            catch (ArgumentException) { }
            string backup = plan.Kind == "mac-app-zip" ? await ReplaceMacAsync(plan, timeout.Token) : await ReplaceWindowsAsync(plan, timeout.Token);
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { success = true, version = plan.Version, backup }));
            await RestartAsync(plan, result, timeout.Token);
            return 0;
        }
        catch (Exception ex)
        {
            if (result != null) try { await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { success = false, error = ex.Message })); } catch { }
            if (trustedPlan != null && result != null)
            {
                bool parentAlive = false;
                try { using var parent = Process.GetProcessById(trustedPlan.ParentPid); parentAlive = !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == trustedPlan.ParentStarted; } catch { }
                if (!parentAlive) try { await RestartAsync(trustedPlan, result, CancellationToken.None); } catch { }
            }
            return 1;
        }
    }

    private static async Task RestartAsync(UpdatePlan plan, string result, CancellationToken token)
    {
        if (plan.Kind == "mac-app-zip") await RunToolAsync("/usr/bin/open", new[] { "-n", plan.Target, "--args", "--launcher-update-result", result }, token);
        else
        {
            var start = new ProcessStartInfo(plan.Target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(plan.Target)! };
            start.ArgumentList.Add("--launcher-update-result"); start.ArgumentList.Add(result);
            Process.Start(start);
        }
    }

    public static string ReadResultMessage(string[] args)
    {
        int index = Array.IndexOf(args, "--launcher-update-result");
        if (index < 0 || index + 1 >= args.Length) return "";
        try
        {
            string path = Path.GetFullPath(args[index + 1]);
            if (!path.StartsWith(Path.GetFullPath(StagingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || Path.GetFileName(path) != "result.json") return "";
            RejectLinks(path);
            if (new FileInfo(path).Length > 16384) return "";
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.GetProperty("success").GetBoolean() ? "Updated successfully. Backup: " + root.GetProperty("backup").GetString() :
                "Previous update failed: " + root.GetProperty("error").GetString();
        }
        catch { return "The previous updater result could not be read. Check the updater staging folder."; }
    }

    internal static async Task<string> ReplaceWindowsAsync(UpdatePlan plan, CancellationToken token = default)
    {
        ValidateReplacement(plan, false);
        if (await HashAsync(plan.Target, token) != plan.OriginalSha256) throw new IOException("The installed launcher changed during the update. Nothing was replaced.");
        if (await HashAsync(plan.Source, token) != plan.PayloadSha256) throw new IOException("The staged launcher changed after verification. Nothing was replaced.");
        string temporary = plan.Target + ".update-" + Guid.NewGuid().ToString("N");
        string backup = plan.Target + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(plan.Source, temporary, false);
            token.ThrowIfCancellationRequested();
            File.Replace(temporary, plan.Target, backup);
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> ReplaceMacAsync(UpdatePlan plan, CancellationToken token)
    {
        ValidateReplacement(plan, true); ValidateMacBundle(plan.Target); ValidateMacBundle(plan.Source, plan.Version);
        if (await HashAsync(Path.Combine(plan.Source, "Contents/MacOS/Vortex Launcher"), token) != plan.PayloadSha256)
            throw new IOException("The staged app changed after verification. Nothing was replaced.");
        if (await HashAsync(Path.Combine(plan.Target, "Contents/MacOS/Vortex Launcher"), token) != plan.OriginalSha256)
            throw new IOException("The installed app changed during the update. Nothing was replaced.");
        string parent = Path.GetDirectoryName(plan.Target)!;
        string temporary = Path.Combine(parent, ".Vortex Launcher.update-" + Guid.NewGuid().ToString("N") + ".app");
        string backup = Path.Combine(parent, ".Vortex Launcher.previous-" + Guid.NewGuid().ToString("N") + ".app");
        // ditto preserves the publisher signature, notarization ticket and executable permissions.
        await RunToolAsync("/usr/bin/ditto", new[] { plan.Source, temporary }, token);
        await VerifyMacSignatureAsync(temporary, token);
        token.ThrowIfCancellationRequested();
        Directory.Move(plan.Target, backup);
        try { Directory.Move(temporary, plan.Target); }
        catch { Directory.Move(backup, plan.Target); throw; }
        return backup;
    }

    private static void ValidateReplacement(UpdatePlan plan, bool mac)
    {
        string target = Path.GetFullPath(plan.Target), source = Path.GetFullPath(plan.Source);
        if (Path.GetFileName(target) != (mac ? AppName : ExeName) || target == source ||
            source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Path.GetDirectoryName(target)))
            throw new IOException("The update target must be the existing Vortex Launcher executable/app, not a data directory.");
        RejectLinks(target); RejectLinks(source);
    }

    private static void RejectLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Launcher updates cannot follow linked application paths.");
        for (var directory = Directory.GetParent(path); directory != null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Launcher updates cannot follow linked parent directories.");
    }

    internal static async Task<string> HashAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    internal sealed class UpdatePlan
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
        public string Version { get; set; } = "";
        public string Kind { get; set; } = "";
        public int ParentPid { get; set; }
        public long ParentStarted { get; set; }
        public string OriginalSha256 { get; set; } = "";
        public string PayloadSha256 { get; set; } = "";
    }
}
