using System.IO.Compression;
using VortexLauncher.Core.Diagnostics;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Network;

internal static class ModAndReportChecks
{
    internal static async Task RunAsync(string parent)
    {
        using (var f = new ModFixture(parent))
        {
            var a = f.Add("alpha"); var b = f.Add("bravo", disabled: true);
            f.Api.Projects["alpha"].Add(f.Version("alpha", "wrong-loader", 3, "forge"));
            f.Api.Projects["alpha"].Add(f.Version("alpha", "wrong-mc", 4, minecraft: "other"));
            f.Api.Projects["alpha"].Add(f.Version("alpha", "beta", 5, channel: "beta"));
            File.WriteAllBytes(Path.Combine(f.Root, "mods", "unknown.jar"), ModFixture.Jar("{}"));
            File.WriteAllBytes(Path.Combine(f.Root, "mods", "vortex.jar"), ModFixture.Jar("{\"id\":\"vortex_client\"}"));
            var preview = await f.Service.CheckAsync(f.Root, "26.3", "Fabric", false, null, default);
            Check(preview.Updates.Count == 2 && preview.Unrecognized == 2 && f.Handler.Requests == 0, "preview identifies exact hashes, skips client/unknown files and never downloads updates");
            Check(preview.Updates.All(u => u.Latest.VersionType == "release") && preview.Updates.Single(u => u.Current.ProjectId == "alpha").Latest.Id == a.New.Id, "preview rejects wrong loader/game version and defaults to stable releases");
            var beta = await f.Service.CheckAsync(f.Root, "26.3", "Fabric", true, null, default);
            Check(beta.Updates.Single(u => u.Current.ProjectId == "alpha").Latest.VersionType == "beta", "prereleases require explicit opt-in");
            string backup = await f.Service.ApplyAsync(preview, preview.Updates.Where(u => u.Current.ProjectId == "bravo").ToArray(), () => true, null, default);
            Check(File.Exists(Path.Combine(f.Root, "mods", "bravo-new.jar.disabled")) && !File.Exists(Path.Combine(f.Root, "mods", "bravo-new.jar")), "selected update preserves disabled state");
            Check(File.Exists(Path.Combine(f.Root, "mods", "alpha-old.jar")) && File.Exists(Path.Combine(backup, "previous-0", "bravo-old.jar.disabled")), "unselected mods remain unchanged and replaced originals remain recoverable");
        }
        using (var f = new ModFixture(parent))
        {
            var a = f.Add("library"); var b = f.Add("dependent");
            b.Old.Dependencies.Add(new() { ProjectId = "library", VersionId = a.Old.Id, Type = "required" });
            b.New.Dependencies.Add(new() { ProjectId = "library", VersionId = a.New.Id, Type = "required" });
            var preview = await f.Service.CheckAsync(f.Root, "26.3", "Fabric", false, null, default);
            await Reject(() => f.Service.ApplyAsync(preview, preview.Updates.Where(u => u.Current.ProjectId == "library").ToArray(), () => true, null, default), "selection cannot break an unchanged mod's exact dependency");
            Check(f.Handler.Requests == 0 && File.Exists(Path.Combine(f.Root, "mods", "library-old.jar")), "dependency failure happens before any file downloads or replacement");
            await f.Service.ApplyAsync(preview, preview.Updates, () => true, null, default);
            Check(File.Exists(Path.Combine(f.Root, "mods", "library-new.jar")) && File.Exists(Path.Combine(f.Root, "mods", "dependent-new.jar")), "compatible dependency updates can be applied together");
        }
        using (var f = new ModFixture(parent))
        {
            f.Add("changed"); var preview = await f.Service.CheckAsync(f.Root, "26.3", "Fabric", false, null, default);
            File.WriteAllText(Path.Combine(f.Root, "mods", "changed-old.jar"), "user change");
            await Reject(() => f.Service.ApplyAsync(preview, preview.Updates, () => true, null, default), "files modified after preview are never overwritten");
        }
        using (var f = new ModFixture(parent))
        {
            var a = f.Add("bad-hash"); a.New.Files[0].Hashes["sha512"] = new string('0', 128);
            var preview = await f.Service.CheckAsync(f.Root, "26.3", "Fabric", false, null, default);
            await Reject(() => f.Service.ApplyAsync(preview, preview.Updates, () => true, null, default), "bad SHA-512 prevents publishing even when SHA-1 matches");
            Check(File.Exists(Path.Combine(f.Root, "mods", "bad-hash-old.jar")), "failed download verification preserves original mod");
        }
        using (var f = new ModFixture(parent))
        {
            f.Add("rollback-a"); f.Add("rollback-b");
            var preview = await f.Service.CheckAsync(f.Root, "26.3", "Fabric", false, null, default);
            int guards = 0;
            await Reject(() => f.Service.ApplyAsync(preview, preview.Updates, () => ++guards < 3, null, default), "instance becoming busy during commit stops the update");
            Check(preview.Installed.All(i => File.Exists(i.Path)) && !File.Exists(Path.Combine(f.Root, "mods", "rollback-a-new.jar")), "partial batch rollback restores original filenames and content");
            using var stop = new CancellationTokenSource(); stop.Cancel();
            bool cancelled = false;
            try { await f.Service.ApplyAsync(preview, preview.Updates, () => true, null, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && preview.Installed.All(i => File.Exists(i.Path)), "cancelled update does not change installed mods");
            string enabled = preview.Installed[0].Path, disabled = enabled + ".disabled";
            File.WriteAllText(disabled, "do not overwrite");
            bool collision = false; try { ModFiles.SetEnabled(f.Root, enabled, false); } catch (IOException) { collision = true; }
            Check(collision && File.ReadAllText(disabled) == "do not overwrite" && File.Exists(enabled), "toggle refuses destination collision rather than overwriting another mod");
        }
        using (var f = new ModFixture(parent))
        {
            f.Add("network"); f.Api.Fail = true;
            var preview = await f.Service.CheckAsync(f.Root, "26.3", "Fabric", false, null, default);
            Check(preview.Errors.Count == 1 && preview.Updates.Count == 0, "network failures are reported, not called up-to-date");
        }
        string root = Directory.CreateDirectory(Path.Combine(parent, "private-report-instance")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "logs")); Directory.CreateDirectory(Path.Combine(root, "crash-reports")); Directory.CreateDirectory(Path.Combine(root, "mods"));
        string secret = "--accessToken SUPER-PRIVATE-TOKEN\n{\"refresh_token\":\"REFRESH-PRIVATE\",\"password\":\"SECRET-PASSWORD\"}\nAuthorization: Bearer VERY-PRIVATE\nEmail: testperson@example.org\nC:\\Users\\SecretPerson\\Documents\\game\n/home/SecretPerson/game\n[CHAT] SecretPerson: private chat\nConnecting to private.example.org, 25565\nUUID: 123e4567-e89b-12d3-a456-426614174000\nSetting user: SecretPlayer\nIP 192.168.10.42\njava.lang.OutOfMemoryError: Java heap space\n";
        File.WriteAllText(Path.Combine(root, "logs", "latest.log"), secret);
        File.WriteAllText(Path.Combine(root, "crash-reports", "crash.txt"), secret);
        File.WriteAllText(Path.Combine(root, "accounts.json"), "ACCOUNT-DATABASE-PRIVATE");
        File.WriteAllText(Path.Combine(root, "settings.json"), "SETTINGS-PRIVATE");
        File.WriteAllText(Path.Combine(root, "instance.json"), "{\"Name\":\"Private pack\",\"Version\":\"26.3\",\"Loader\":\"Fabric\",\"AccessToken\":\"METADATA-SECRET\"}");
        File.WriteAllText(Path.Combine(root, "mods", "example.jar"), "not included");
        string report = Path.Combine(parent, "diagnostic-fixture.zip");
        await DiagnosticReport.CreateAsync(report, root, new[] { secret });
        using (var zip = ZipFile.OpenRead(report))
        {
            Check(zip.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "README.txt", "console.log", "crash-report.txt", "latest.log", "mods.txt", "summary.txt" }.Order()), "report ZIP contains only allowlisted text, no accounts/settings/worlds or raw archives");
            string text = string.Join('\n', zip.Entries.Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
            Check(new[] { "SUPER-PRIVATE", "REFRESH-PRIVATE", "SECRET-PASSWORD", "VERY-PRIVATE", "testperson@example.org", "SecretPerson", "SecretPlayer", "192.168.10.42", "private.example.org", "123e4567", "ACCOUNT-DATABASE", "METADATA-SECRET", "Private pack" }.All(s => !text.Contains(s)), "diagnostic report redacts credentials, IDs, emails, usernames, paths, chat and server addresses");
            Check(text.Contains("OutOfMemoryError") && text.Contains("26.3") && text.Contains("Fabric"), "sanitization retains useful error and game/loader information");
        }
        await Reject(() => DiagnosticReport.CreateAsync(report, root), "report export never silently overwrites an existing file");
        Console.WriteLine("MOD_REPORT_CHECKS_OK");
    }
    private static async Task Reject(Func<Task> operation, string label)
    { bool rejected = false; try { await operation(); } catch (IOException) { rejected = true; } Check(rejected, label); }
    private static void Check(bool passed, string label) { if (!passed) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
}
