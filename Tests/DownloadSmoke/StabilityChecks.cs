using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Models;

static class StabilityChecks
{
    public static async Task RunAsync(string root)
    {
        string config = Directory.CreateDirectory(Path.Combine(root, "safe-json")).FullName;
        var settings = new SettingsManager(config);
        settings.SaveSettings(new() { RamAllocationGb = 6 });
        settings.SaveSettings(new() { RamAllocationGb = 8 });
        string path = Path.Combine(config, "settings.json");
        File.WriteAllText(path, "{broken");
        Check(settings.LoadSettings().RamAllocationGb == 6 && settings.LoadNotice.Contains("Restored"), "damaged settings restore the last valid backup");
        Check(Directory.GetFiles(config, "settings.json.corrupt-*").Length == 1 && File.ReadAllText(path + ".bak").Contains("6"), "recovery retains the damaged original and never rotates it over the good backup");
        File.WriteAllText(path, "broken primary"); File.WriteAllText(path + ".bak", "broken backup");
        settings.LoadSettings();
        Check(settings.HasLoadError && File.ReadAllText(path) == "broken primary", "unrecoverable settings are reported without auto-overwriting originals");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => SafeJsonStore.Write(Path.Combine(config, "parallel.json"), new LauncherSettings { RamAllocationGb = i + 1 }))));
        Check(SafeJsonStore.Read<LauncherSettings>(Path.Combine(config, "parallel.json")).Value != null && !Directory.GetFiles(config, "*.tmp").Any(), "concurrent saves leave a valid JSON document and no temporary files");
        var accounts = new AccountManager(config);
        accounts.SaveAccounts(new() { new("Alice") }); accounts.SaveAccounts(new() { new("Bob") });
        File.WriteAllText(Path.Combine(config, "accounts.json"), "bad");
        Check(accounts.LoadAccounts()[0].Username == "Alice", "profile recovery uses the last valid account file");

        string instancesRoot = Directory.CreateDirectory(Path.Combine(root, "recovery-instances")).FullName;
        var manager = new InstanceManager(instancesRoot);
        var instance = manager.CreateInstance("Fabric pack", "26.3", "Fabric");
        instance.LoaderVersion = "0.19.5"; manager.SaveInstance(instance);
        File.WriteAllText(Path.Combine(instance.InstancePath, "instance.json"), "bad");
        Check(manager.LoadInstances().Single().Loader == "Fabric", "instance metadata recovery preserves Fabric instead of guessing Vanilla");
        string unknown = Directory.CreateDirectory(Path.Combine(instancesRoot, "Unknown pack")).FullName;
        File.WriteAllText(Path.Combine(unknown, "instance.json"), "bad");
        var invalid = manager.LoadInstances().Single(i => i.Name == "Unknown pack");
        Check(invalid.Version == "Unknown" && !invalid.CanChangeFiles && !invalid.CanLaunch, "unknown/corrupt metadata cannot be launched or silently assigned Minecraft 1.20.1");

        string zipPath = Path.Combine(root, "recovery-source.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            Write(zip, "instance.json", "{\"Name\":\"Recovered pack\",\"Version\":\"26.3\",\"Loader\":\"Fabric\"}");
            Write(zip, "mods/a.jar", "first mod"); Write(zip, "config/options.txt", "pack config");
        }
        string fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zipPath)));
        var target = manager.CreateInstance("Recovered pack", "26.3", "Fabric");
        OperationJournal.Begin(target.InstancePath, "Import", zipPath, fingerprint);
        File.WriteAllText(Path.Combine(target.InstancePath, ".vortex-importing"), "Importing");
        Directory.CreateDirectory(Path.Combine(target.InstancePath, "mods")); File.WriteAllText(Path.Combine(target.InstancePath, "mods/a.jar"), "first mod");
        File.WriteAllText(Path.Combine(target.InstancePath, "player-notes.txt"), "keep me");
        var pending = manager.LoadInstances().Single(i => i.InstancePath == target.InstancePath);
        Check(pending.HasInterruptedOperation && !pending.CanLaunch, "interrupted import remains visible after a fresh manager load and cannot launch");
        var journal = OperationJournal.Discover(pending)!;
        OperationJournal.Cancel(journal);
        Check(OperationJournal.Read(target.InstancePath)!.State == "Cancelled" && File.ReadAllText(Path.Combine(target.InstancePath, "player-notes.txt")) == "keep me", "cancelling a persisted operation never deletes game/user files");
        await manager.ImportArchiveAsync(zipPath, fingerprint, resumeTarget: target.InstancePath);
        Check(!OperationJournal.HasMarker(target.InstancePath) && OperationJournal.Read(target.InstancePath) == null && File.ReadAllText(Path.Combine(target.InstancePath, "config/options.txt")) == "pack config", "resume completes the same import directory and skips verified existing files");
        Check(File.ReadAllText(Path.Combine(target.InstancePath, "player-notes.txt")) == "keep me", "resumed import preserves unrelated user content");
        OperationJournal.Begin(target.InstancePath, "Import", zipPath, fingerprint);
        File.WriteAllText(Path.Combine(target.InstancePath, "mods/a.jar"), "edited by player");
        await Reject(() => manager.ImportArchiveAsync(zipPath, fingerprint, resumeTarget: target.InstancePath));
        Check(File.ReadAllText(Path.Combine(target.InstancePath, "mods/a.jar")) == "edited by player", "resume refuses to overwrite a file changed by the player");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update)) Write(zip, "new-file.txt", "changed archive");
        await Reject(() => manager.ImportArchiveAsync(zipPath, fingerprint, resumeTarget: target.InstancePath));
        Check(!File.Exists(Path.Combine(target.InstancePath, "new-file.txt")), "changed recovery archive is rejected before extraction");
        var source = manager.CreateInstance("Copy source", "26.3", "Vanilla");
        File.WriteAllText(Path.Combine(source.InstancePath, "world.txt"), "world data");
        using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); await Reject(() => manager.CloneAsync(source, cancellationToken: cancelled.Token)); }
        var partial = manager.LoadInstances().Single(i => i.Name == "Copy source Copy");
        Check(partial.HasInterruptedOperation, "a stopped copy has recoverable metadata and a journal");
        await manager.CloneAsync(source, partial.InstancePath);
        Check(File.ReadAllText(Path.Combine(partial.InstancePath, "world.txt")) == "world data" && OperationJournal.Read(partial.InstancePath) == null, "resuming a stopped copy completes it without deleting source data");

        foreach (string mode in new[] { "range", "ignored", "wrong-range", "changed-etag", "bad-prefix", "no-validator", "etag-only", "etag-bad-prefix" })
            await ResumeCase(root, mode);
        await CancelledResume(root);
        Console.WriteLine("STABILITY_CHECKS_OK");
    }
    private static async Task ResumeCase(string root, string mode)
    {
        byte[] payload = new byte[2 * 1024 * 1024]; new Random(123).NextBytes(payload);
        string sha = Convert.ToHexString(SHA1.HashData(payload)); string url = "https://fixture.test/" + mode;
        string cache = Directory.CreateDirectory(Path.Combine(root, "resume-" + mode)).FullName;
        string key = Path.Combine(cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))));
        byte[] prefix = payload[..(512 * 1024)]; string prefixHash = Convert.ToHexString(SHA256.HashData(prefix));
        if (mode is "bad-prefix" or "etag-bad-prefix") prefix[0] ^= 1;
        File.WriteAllBytes(key + ".partial", prefix);
        bool hashless = mode is "no-validator" or "etag-only" or "etag-bad-prefix";
        SafeJsonStore.Write(key + ".resume.json", new ResumableTransfer.State { Url = url, Hash = hashless ? "" : sha,
            ETag = mode == "no-validator" ? "W/\"v1\"" : "\"v1\"", Length = payload.Length, PrefixHash = prefixHash, PrefixLength = prefix.Length });
        using var handler = new RangeHandler(payload, mode); using var client = new HttpClient(handler);
        string destination = Path.Combine(cache, "finished.jar");
        await DownloadCache.EnsureAsync(url, destination, sha1: hashless ? null : sha, size: payload.Length, httpClient: client, cacheRoot: cache);
        Check(File.ReadAllBytes(destination).SequenceEqual(payload), "resumed bytes validate correctly: " + mode);
        Check(mode is "no-validator" or "etag-bad-prefix" ? handler.Offsets[0] == null : handler.Offsets[0] == prefix.Length, "Range is used only for a validated saved prefix: " + mode);
        if (mode is "wrong-range" or "changed-etag" or "bad-prefix") Check(handler.Offsets.Count >= 2 && handler.Offsets[1] == null, "invalid range/ETag/hash restarts from zero: " + mode);
        Check(!File.Exists(key + ".partial") && !File.Exists(key + ".resume.json"), "successful download clears only its own resume state: " + mode);
    }
    private static async Task CancelledResume(string root)
    {
        byte[] data = new byte[2 * 1024 * 1024]; new Random(5).NextBytes(data);
        string sha = Convert.ToHexString(SHA1.HashData(data)), url = "https://fixture.test/cancel-resume";
        string cache = Directory.CreateDirectory(Path.Combine(root, "cancel-resume")).FullName;
        using var handler = new RangeHandler(data, "range"); using var http = new HttpClient(handler); using var stop = new CancellationTokenSource();
        string target = Path.Combine(cache, "game.jar");
        await Reject(() => DownloadCache.EnsureAsync(url, target, stop.Token, sha, data.Length, n => { if (n > 0) stop.Cancel(); }, http, cache));
        Check(!File.Exists(target) && Directory.GetFiles(cache, "*.partial").Length == 1, "cancellation keeps a validated resumable prefix but publishes no game file");
        await DownloadCache.EnsureAsync(url, target, sha1: sha, size: data.Length, httpClient: http, cacheRoot: cache);
        Check(handler.Offsets.Last() > 0 && File.ReadAllBytes(target).SequenceEqual(data), "a later caller resumes a cancelled download with final checksum verification");
    }
    private static void Write(ZipArchive zip, string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    private static async Task Reject(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException) { return; } throw new Exception("Expected a safe rejection."); }
    private sealed class RangeHandler(byte[] data, string mode) : HttpMessageHandler
    {
        public List<long?> Offsets { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            long? offset = request.Headers.Range?.Ranges.Single().From; Offsets.Add(offset);
            bool range = offset.HasValue && mode != "ignored";
            var response = new HttpResponseMessage(range ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            { Content = new ByteArrayContent(range ? data[(int)offset!.Value..] : data) };
            response.Headers.ETag = new EntityTagHeaderValue(mode == "changed-etag" ? "\"v2\"" : "\"v1\"");
            if (range) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset!.Value + (mode == "wrong-range" ? 1 : 0), data.Length - 1, data.Length);
            return Task.FromResult(response);
        }
    }
}
