using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using VortexLauncher.Core.Network;
using VortexLauncher.Models;
using VortexLauncher.Core.Management;
using System.Threading;
using System.Linq;

namespace VortexLauncher.Core.Launch;

public class InstanceManager
{
    private readonly string _instancesDirectory;

    public InstanceManager(string? instancesDirectory = null)
    {
        _instancesDirectory = instancesDirectory ?? Path.Combine(PlatformSupport.DataRoot, "instances");
    }

    public List<string> LoadNotices { get; } = new();
    /// <summary>The user's own CurseForge API key, read when a CurseForge pack is imported.</summary>
    public Func<string?>? CurseForgeApiKey { get; set; }
    /// <summary>What the last import could not do for the user (files to download by hand); empty when everything arrived.</summary>
    public List<string> ImportNotes { get; } = new();
    internal HttpClient? CurseForgeClient { get; set; }
    internal HttpClient? DownloadClient { get; set; }
    internal string ValidateRecoveryTarget(string target) => OperationJournal.ValidateTarget(_instancesDirectory, target);
    internal static bool ValidMetadata(InstanceModel item) => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Version) && !string.IsNullOrWhiteSpace(item.Loader);
    public List<InstanceModel> LoadInstances(CancellationToken cancellationToken = default)
    {
        LoadNotices.Clear();
        var list = new List<InstanceModel>();
        if (!Directory.Exists(_instancesDirectory))
            return list;

        foreach (var dir in Directory.GetDirectories(_instancesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            string jsonPath = Path.Combine(dir, "instance.json");
            var stored = SafeJsonStore.Read<InstanceModel>(jsonPath, ValidMetadata);
            if (stored.Notice.Length > 0) LoadNotices.Add(Path.GetFileName(dir) + ": " + stored.Notice);
            if (stored.Value is { } inst)
            {
                inst.InstancePath = dir;
                VortexLauncher.Core.Launch.LaunchSettings.Sanitize(inst);
                inst.RefreshScreenshotCover();
                OperationJournal.Discover(inst);
                list.Add(inst);
                continue;
            }

            // Запасной вариант, если instance.json еще не создан
            string folderName = Path.GetFileName(dir);
            list.Add(new InstanceModel
            {
                Name = folderName,
                Version = "Unknown",
                Loader = "Unknown",
                InstancePath = dir,
                MetadataProblem = "Instance metadata is missing or damaged. Open its folder to recover it; no version/loader was guessed."
            });
        }

        return list;
    }

    public InstanceModel CreateInstance(string name, string version, string loader)
    {
        string uniquePath = CreateUniqueInstanceDirectory(name);
        Directory.CreateDirectory(uniquePath);

        var instance = new InstanceModel
        {
            Name = name,
            Version = version,
            Loader = loader,
            InstancePath = uniquePath
        };

        string jsonPath = Path.Combine(uniquePath, "instance.json");
        string json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
        SafeJsonStore.Write(jsonPath, instance, ValidMetadata);

        return instance;
    }

    public void SaveInstance(InstanceModel instance)
    {
        if (instance == null || string.IsNullOrWhiteSpace(instance.InstancePath)) return;

        Directory.CreateDirectory(instance.InstancePath);
        string jsonPath = Path.Combine(instance.InstancePath, "instance.json");
        string json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
        if (instance.MetadataProblem.Length > 0) throw new InvalidDataException(instance.MetadataProblem);
        SafeJsonStore.Write(jsonPath, instance, ValidMetadata);
    }

    public string CreateUniqueInstanceDirectory(string cleanName)
    {
        cleanName = string.Concat(cleanName.Split(Path.GetInvalidFileNameChars())).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "New Instance";
        string targetDir = Path.Combine(_instancesDirectory, cleanName);
        int counter = 1;
        while (Directory.Exists(targetDir))
        {
            targetDir = Path.Combine(_instancesDirectory, $"{cleanName}_({counter})");
            counter++;
        }
        return targetDir;
    }

    public async Task<InstanceModel> CloneAsync(InstanceModel source, string? resumeTarget = null, CancellationToken cancellationToken = default, TransferProgress? transfer = null)
    {
        OperationJournal.ValidateTarget(_instancesDirectory, source.InstancePath);
        if (!source.CanChangeFiles || source.HasInterruptedOperation) throw new IOException("Finish the source instance operation before copying it.");
        string target = resumeTarget == null ? CreateUniqueInstanceDirectory(source.Name + " Copy") : OperationJournal.ValidateTarget(_instancesDirectory, resumeTarget);
        Directory.CreateDirectory(target);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var paths = Directory.EnumerateFiles(source.InstancePath, "*", options).Where(path =>
            !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(path).StartsWith("instance.json", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(path).StartsWith(OperationJournal.FileName, StringComparison.Ordinal)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string Fingerprint() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", paths.Select(path =>
            Path.GetRelativePath(source.InstancePath, path) + "|" + new FileInfo(path).Length + "|" + File.GetLastWriteTimeUtc(path).Ticks)))));
        string fingerprint = Fingerprint();
        transfer?.Begin("Copying instance", paths.Length, paths.Sum(path => new FileInfo(path).Length));
        var journal = OperationJournal.Begin(target, "Clone", source.InstancePath, fingerprint, resumeTarget != null);
        var clone = new InstanceModel { Name = source.Name + " Copy", Version = source.Version, Loader = source.Loader,
            LoaderVersion = source.LoaderVersion, VortexClientEnabled = source.VortexClientEnabled, InstancePath = target };
        if (resumeTarget == null) SaveInstance(clone);
        // Only publish the clone after the entire independent copy succeeds.
        await File.WriteAllTextAsync(Path.Combine(target, ".vortex-cloning"), "Copying");
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
            string destination = Path.Combine(target, Path.GetRelativePath(source.InstancePath, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = File.OpenRead(path);
            await VerifiedCopy.FileAsync(input, destination, journal.Id, cancellationToken, target);
            transfer?.Complete(new FileInfo(path).Length, 0);
        }
        if (fingerprint != Fingerprint()) throw new IOException("The copy source changed. Partial files were kept for inspection.");
        clone.IsInstalled = source.IsInstalled;
        clone.CoverImagePath = CopyImage(source.CoverImagePath, "cover"); clone.IconPath = CopyImage(source.IconPath, "icon");
        SaveInstance(clone);
        OperationJournal.Complete(target);
        return clone;

        string CopyImage(string path, string name)
        {
            if (!File.Exists(path)) return string.Empty;
            string destination = Path.Combine(target, "artwork", name + Path.GetExtension(path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = File.OpenRead(path);
            VerifiedCopy.FileAsync(input, destination, journal.Id, cancellationToken, target).GetAwaiter().GetResult();
            return destination;
        }
    }

    public InstanceModel ImportArchive(string archivePath) =>
        ImportArchiveAsync(archivePath).GetAwaiter().GetResult();

    public async Task<InstanceModel> ImportArchiveAsync(string archivePath,
        string? expectedFingerprint = null, System.Threading.CancellationToken cancellationToken = default,
        TransferProgress? transfer = null, string? resumeTarget = null)
    {
        // Hold the inspected file open without write sharing until import finishes.
        using var input = File.OpenRead(archivePath);
        string fingerprint = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, cancellationToken));
        if (expectedFingerprint != null && fingerprint != expectedFingerprint)
            throw new InvalidDataException("The archive changed after preview. Select it again.");
        input.Position = 0;
        using var archive = new ZipArchive(input);
        var plan = ArchiveImportPlan.Read(archive, Path.GetFileNameWithoutExtension(archivePath));
        cancellationToken.ThrowIfCancellationRequested();
        ImportNotes.Clear();
        // Resolved before anything is created, so a missing key or a refused request leaves no half-made instance.
        var manual = plan.CurseForge != null ? await ResolveCurseForgeAsync(plan, cancellationToken) : new List<string>();
        string target = resumeTarget == null ? CreateUniqueInstanceDirectory(plan.Instance.Name) : OperationJournal.ValidateTarget(_instancesDirectory, resumeTarget);
        Directory.CreateDirectory(target);
        var journal = OperationJournal.Begin(target, "Import", Path.GetFullPath(archivePath), fingerprint, resumeTarget != null);
        plan.Instance.InstancePath = target;
        if (resumeTarget == null) SaveInstance(plan.Instance);
        string marker = Path.Combine(target, ".vortex-importing");
        await File.WriteAllTextAsync(marker, "Importing", cancellationToken);
        transfer?.Begin("Importing archive", plan.Entries.Count + plan.Downloads.Count, 0);
        if (plan.PolyMc != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.PolyMc.Extract(target, journal.Id, cancellationToken);
        }
        else
        {
            foreach (var (entry, path) in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(target, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await VerifiedCopy.FileAsync(source, destination, journal.Id, cancellationToken, target);
                transfer?.Complete(entry.Length, 0);
            }
            await DownloadBatch.RunAsync(plan.Downloads, async (file, downloadToken) =>
            {
                string destination = Path.Combine(target, file.Path.Replace('/', Path.DirectorySeparatorChar));
                // Explicit overrides take precedence over downloaded pack files.
                if (!File.Exists(destination))
                {
                    long received = 0;
                    await DownloadCache.EnsureAsync(file.Url, destination, downloadToken, file.Sha1, file.Size, bytes => { received += bytes; transfer?.Received(bytes); }, DownloadClient);
                    transfer?.Complete(file.Size, received);
                }
                else transfer?.Complete(file.Size, 0);
            }, cancellationToken);
        }
        if (manual.Count > 0)
        {
            await File.WriteAllTextAsync(Path.Combine(target, "CurseForge manual downloads.txt"),
                "These files of the CurseForge pack could not be downloaded by the launcher.\r\n" +
                "Download them from CurseForge and put them in this instance's mods folder (or the folder that fits their type).\r\n\r\n" +
                string.Join("\r\n", manual) + "\r\n", cancellationToken);
            ImportNotes.Add($"{manual.Count} file(s) must be downloaded by hand: see \"CurseForge manual downloads.txt\" in the instance folder.");
        }
        plan.Instance.InstancePath = target;
        SaveInstance(plan.Instance);
        OperationJournal.Complete(target);
        return plan.Instance;
    }

    /// <summary>
    /// Turns a CurseForge pack's file ids into verified downloads. Files whose authors block other launchers, or
    /// that cannot be verified, are never fetched; they come back as lines for a "download by hand" list.
    /// </summary>
    private async Task<List<string>> ResolveCurseForgeAsync(ArchiveImportPlan plan, CancellationToken cancellationToken)
    {
        string key = CurseForgeApiKey?.Invoke()?.Trim() ?? "";
        if (key.Length == 0)
            throw new InvalidOperationException("CurseForge packs need your own free CurseForge API key. Get one at console.curseforge.com and enter it in Settings → Java → CurseForge.");
        var manual = new List<string>();
        var resolved = await new CurseForgeService(key, CurseForgeClient).ResolveAsync(plan.CurseForge!.Files, cancellationToken);
        foreach (var file in resolved)
        {
            string label = file.ProjectName.Length > 0 ? file.ProjectName : $"Project {file.ProjectId}";
            string? folder = file.ClassId switch
            {
                6 => "mods", 12 => "resourcepacks", 6552 => "shaderpacks",
                0 => file.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ? "mods" : "resourcepacks",
                _ => null
            };
            string? problem = null;
            if (file.FileName.Length == 0) problem = "was not found on CurseForge";
            else if (folder == null) problem = "is content the launcher does not install by itself (for example a world)";
            else if (file.DownloadUrl == null) problem = "does not allow downloads outside CurseForge";
            else if (!CurseForgeService.IsTrustedDownload(file.DownloadUrl)) problem = "has an untrusted download address";
            else if (!Regex.IsMatch(file.Sha1, "^[a-fA-F0-9]{40}$") || file.Size <= 0 || file.Size > 2L * 1024 * 1024 * 1024) problem = "could not be verified";
            else
            {
                try { ArchiveImportPlan.ValidateContentPath(folder + "/" + file.FileName); }
                catch (InvalidDataException) { problem = "has an unsafe file name"; }
            }
            if (problem != null)
            {
                manual.Add($"{label} (file {file.FileId}) {problem}. {file.Website ?? $"https://www.curseforge.com/projects/{file.ProjectId}"}");
                continue;
            }
            string path = folder + "/" + file.FileName;
            // Files the pack itself ships in its overrides win over a downloaded copy.
            if (plan.Entries.Any(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) || plan.Downloads.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            plan.Downloads.Add(new(path, file.DownloadUrl!, file.Sha1, file.Size));
        }
        return manual;
    }

    public void ExportArchive(InstanceModel instance, string archivePath)
    {
        if (instance.HasInterruptedOperation || instance.MetadataProblem.Length > 0) throw new InvalidOperationException("Recover this instance before exporting it.");
        if (string.IsNullOrWhiteSpace(instance.Version) || string.IsNullOrWhiteSpace(instance.Loader))
            throw new InvalidDataException("The instance version and loader are required for export.");
        string destination = Path.GetFullPath(archivePath);
        string instanceRoot = Path.GetFullPath(instance.InstancePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destination.StartsWith(instanceRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Save the exported ZIP outside the instance folder.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // The in-memory instance is authoritative. A stale/missing disk manifest
        // must never turn a Fabric/Forge/Quilt export into a Vanilla instance.
        // Java path, memory and arguments belong to this computer: they are not written into the archive.
        var exported = JsonSerializer.Deserialize<InstanceModel>(JsonSerializer.Serialize(instance))!;
        LaunchSettings.Reset(exported);
        string metadata = JsonSerializer.Serialize(exported, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            ZipFile.CreateFromDirectory(instance.InstancePath, temporary, CompressionLevel.Fastest, false);
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                archive.GetEntry("instance.json")?.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("instance.json").Open());
                writer.Write(metadata);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
