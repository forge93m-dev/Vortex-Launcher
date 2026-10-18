using System.IO.Compression;
using System.Text.Json;
using VortexLauncher.Core.Management;
using VortexLauncher.Core.Launch;
using VortexLauncher.Models;

internal static class QolChecks
{
    internal static void Run(string parent)
    {
        string root = Directory.CreateDirectory(Path.Combine(parent, "qol-" + Guid.NewGuid().ToString("N"))).FullName;
        string Write(string relative, string text = "fixture") { string p = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); return p; }
        var a = Write("mods/a.jar", "alpha"); var b = Write("mods/b.jar.disabled", "bravo");
        var backups = new[] { new RemovedContent("Mods", InstanceContentRemoval.MoveToBackup(root, "Mods", a)), new RemovedContent("Mods", InstanceContentRemoval.MoveToBackup(root, "Mods", b)) };
        RemovalUndoHistory.Remember(root, backups);
        Check(RemovalUndoHistory.HasUndo(root) && !File.Exists(a), "removed batch exposes session Undo");
        Reject(() => RemovalUndoHistory.UndoLast(root, () => false), "Undo refuses busy instances");
        Write("mods/a.jar", "keep new file");
        Reject(() => RemovalUndoHistory.UndoLast(root, () => true), "Undo preflights whole batch before moving files");
        Check(!File.Exists(b) && File.ReadAllText(a) == "keep new file" && backups.All(p => File.Exists(p.BackupPath)), "collision never overwrites or partially restores a batch");
        File.Delete(a); Write("mods/a.jar.disabled", "disabled copy");
        Reject(() => RemovalUndoHistory.UndoLast(root, () => true), "Undo detects enabled/disabled counterpart collisions");
        File.Delete(Path.Combine(root, "mods/a.jar.disabled"));
        Check(RemovalUndoHistory.UndoLast(root, () => true) == 2 && !RemovalUndoHistory.HasUndo(root) && File.ReadAllText(a) == "alpha" && File.ReadAllText(b) == "bravo", "bulk Undo restores exact bytes and disabled state");
        foreach (var (section, folder) in new[] { ("Resource packs", "resourcepacks"), ("Shader packs", "shaderpacks") }) {
            string path = Write(folder + "/sample.zip");
            RemovalUndoHistory.Remember(root, new[] { new RemovedContent(section, InstanceContentRemoval.MoveToBackup(root, section, path)) });
            Check(RemovalUndoHistory.UndoLast(root, () => true) == 1 && File.Exists(path), "Undo restores " + section);
        }
        Reject(() => InstanceContentRemoval.RestoreFromBackup(root, "Mods", Path.Combine(parent, "outside.jar")), "restore refuses foreign backup paths");
        var partial = new[] { new RemovedContent("Mods", InstanceContentRemoval.MoveToBackup(root, "Mods", a)), new RemovedContent("Mods", InstanceContentRemoval.MoveToBackup(root, "Mods", b)) };
        RemovalUndoHistory.Remember(root, partial); int guards = 0;
        Reject(() => RemovalUndoHistory.UndoLast(root, () => ++guards < 3), "instance becoming busy stops a batch Undo");
        Check(RemovalUndoHistory.HasUndo(root) && File.Exists(b) && !File.Exists(a) && RemovalUndoHistory.UndoLast(root, () => true) == 1 && File.Exists(a), "remaining Undo batch stays recoverable after a partial interruption");
        var item = new InstanceModel { InstancePath = root, Name = "Shared fixture", Version = "26.3", Loader = "Fabric", LoaderVersion = "0.19.5", VortexClientEnabled = true,
            CoverImagePath = "PRIVATE_COVER", IconPath = "PRIVATE_ICON", TotalPlayTimeSeconds = 123456 };
        Write("accounts.json", "PRIVATE_ACCOUNT"); Write("servers.dat", "PRIVATE_SERVER"); Write("logs/latest.log", "PRIVATE_LOG"); Write(".vortex-trash/secret", "PRIVATE_BACKUP");
        Write("config/example.json", "OPTIONAL_CONFIG"); Write("config/tokens.json", "PRIVATE_TOKEN"); Write("config/nested/logs/debug.txt");
        Write("saves/World/level.dat", "OPTIONAL_WORLD"); Write("saves/World/session.lock"); Write("screenshots/shot.png", "OPTIONAL_SCREENSHOT"); Write("mods/a.jar.part");
        var preview = SharePackService.Preview(item, new());
        Check(preview.Files.Count == 4 && preview.Files.All(f => f.RelativePath.StartsWith("mods/") || f.RelativePath.StartsWith("resourcepacks/") || f.RelativePath.StartsWith("shaderpacks/")), "share defaults include only mods, resource packs and shaders");
        string target = Path.Combine(parent, "shared-" + Guid.NewGuid().ToString("N") + ".zip");
        SharePackService.Export(item, preview, target);
        using (var zip = ZipFile.OpenRead(target)) {
            using var reader = new StreamReader(zip.GetEntry("instance.json")!.Open()); string metadata = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(metadata);
            Check(zip.Entries.Count == 5 && doc.RootElement.GetProperty("LoaderVersion").GetString() == "0.19.5" && !metadata.Contains("PRIVATE") && !metadata.Contains("InstancePath") && !metadata.Contains("TotalPlayTime"), "share metadata contains only portable installation fields, not local paths/accounts/history");
        }
        var imported = ArchivePreview.Inspect(target);
        Check(imported.Name == item.Name && imported.Version == item.Version && imported.Mods == 2 && imported.Downloads == 0, "shared ZIP opens in existing import preview with game version and content");
        Reject(() => SharePackService.Export(item, preview, target), "sharing never overwrites an existing ZIP");
        Reject(() => SharePackService.Export(item, preview, Path.Combine(root, "self.zip")), "sharing cannot recursively include its own output");
        var all = SharePackService.Preview(item, new(Config: true, Worlds: true, Screenshots: true));
        Check(all.Files.Count == 7 && !all.Files.Any(f => f.RelativePath.Contains("tokens") || f.RelativePath.Contains("session.lock") || f.RelativePath.Contains("logs/")), "private categories require opt-in and known secrets/locks/logs stay excluded");
        string failed = Path.Combine(parent, "failed-" + Guid.NewGuid().ToString("N") + ".zip");
        Write("mods/a.jar", "ALPHA");
        Reject(() => SharePackService.Export(item, preview, failed), "same-size modifications after preview invalidate export");
        Check(!File.Exists(failed) && !Directory.EnumerateFiles(parent, Path.GetFileName(failed) + "*.part").Any(), "failed export cleans its own partial ZIP without publishing");
        preview = SharePackService.Preview(item, new()); Write("mods/added.jar");
        Reject(() => SharePackService.Export(item, preview, failed), "new files require a fresh preview");
        preview = SharePackService.Preview(item, new()); item.IsRunning = true;
        Reject(() => SharePackService.Export(item, preview, failed), "sharing refuses running instances"); item.IsRunning = false;
        using var stop = new CancellationTokenSource(); stop.Cancel();
        bool cancelled = false; try { SharePackService.Export(item, preview, failed, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && !File.Exists(failed), "cancelled sharing publishes no ZIP");
        using var interrupted = new CancellationTokenSource();
        bool stopped = false;
        try { SharePackService.Export(item, preview, failed, interrupted.Token, new InlineProgress(_ => interrupted.Cancel())); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped && !File.Exists(failed) && !Directory.EnumerateFiles(parent, Path.GetFileName(failed) + "*.part").Any(), "mid-export cancellation removes the partial archive");
        bool emptyCancelled = false; try { SharePackService.Preview(item, new(false, false, false), stop.Token); } catch (OperationCanceledException) { emptyCancelled = true; }
        Check(emptyCancelled, "even an empty preview honors cancellation");
    }
    private static void Reject(Action action, string text) { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Check(rejected, text); }
    private static void Check(bool condition, string text) { if (!condition) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
}
