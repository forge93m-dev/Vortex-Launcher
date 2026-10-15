using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Launch;

public class InstanceManager
{
    private readonly string _instancesDirectory;

    public InstanceManager(string? instancesDirectory = null)
    {
        _instancesDirectory = instancesDirectory ?? Path.Combine(PlatformSupport.DataRoot, "instances");
        Directory.CreateDirectory(_instancesDirectory);
    }

    public List<InstanceModel> LoadInstances()
    {
        var list = new List<InstanceModel>();
        if (!Directory.Exists(_instancesDirectory))
            return list;

        foreach (var dir in Directory.GetDirectories(_instancesDirectory))
        {
            if (File.Exists(Path.Combine(dir, ".vortex-cloning")) ||
                File.Exists(Path.Combine(dir, ".vortex-importing"))) continue;
            string jsonPath = Path.Combine(dir, "instance.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    var inst = JsonSerializer.Deserialize<InstanceModel>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (inst != null)
                    {
                        inst.InstancePath = dir;
                        list.Add(inst);
                        continue;
                    }
                }
                catch { }
            }

            // Запасной вариант, если instance.json еще не создан
            string folderName = Path.GetFileName(dir);
            list.Add(new InstanceModel
            {
                Name = folderName,
                Version = "1.20.1",
                Loader = "Vanilla",
                InstancePath = dir
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
        File.WriteAllText(jsonPath, json);

        return instance;
    }

    public void SaveInstance(InstanceModel instance)
    {
        if (instance == null || string.IsNullOrWhiteSpace(instance.InstancePath)) return;

        Directory.CreateDirectory(instance.InstancePath);
        string jsonPath = Path.Combine(instance.InstancePath, "instance.json");
        string json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jsonPath, json);
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

    public async Task<InstanceModel> CloneAsync(InstanceModel source)
    {
        string target = CreateUniqueInstanceDirectory(source.Name + " Copy");
        Directory.CreateDirectory(target);
        // Only publish the clone after the entire independent copy succeeds.
        await File.WriteAllTextAsync(Path.Combine(target, ".vortex-cloning"), "Copying");
        var options = new EnumerationOptions { RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (string path in Directory.EnumerateFiles(source.InstancePath, "*", options))
        {
            if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
            string destination = Path.Combine(target, Path.GetRelativePath(source.InstancePath, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = File.OpenRead(path);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output);
        }
        var clone = new InstanceModel
        {
            Name = source.Name + " Copy", Version = source.Version, Loader = source.Loader,
            LoaderVersion = source.LoaderVersion,
            VortexClientEnabled = source.VortexClientEnabled,
            InstancePath = target, IsInstalled = source.IsInstalled,
            CoverImagePath = CopyImage(source.CoverImagePath, "cover"),
            IconPath = CopyImage(source.IconPath, "icon")
        };
        SaveInstance(clone);
        File.Delete(Path.Combine(target, ".vortex-cloning"));
        return clone;

        string CopyImage(string path, string name)
        {
            if (!File.Exists(path)) return string.Empty;
            string destination = Path.Combine(target, "artwork", name + Path.GetExtension(path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination, true);
            return destination;
        }
    }

    public InstanceModel ImportArchive(string archivePath) =>
        ImportArchiveAsync(archivePath).GetAwaiter().GetResult();

    public async Task<InstanceModel> ImportArchiveAsync(string archivePath,
        string? expectedFingerprint = null, System.Threading.CancellationToken cancellationToken = default,
        TransferProgress? transfer = null)
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
        string target = CreateUniqueInstanceDirectory(plan.Instance.Name);
        Directory.CreateDirectory(target);
        string marker = Path.Combine(target, ".vortex-importing");
        await File.WriteAllTextAsync(marker, "Importing", cancellationToken);
        transfer?.Begin("Importing archive", plan.Entries.Count + plan.Downloads.Count, 0);
        if (plan.PolyMc != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.PolyMc.Extract(target);
        }
        else
        {
            foreach (var (entry, path) in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(target, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(output, cancellationToken);
                transfer?.Complete(entry.Length, 0);
            }
            foreach (var file in plan.Downloads)
            {
                string destination = Path.Combine(target, file.Path.Replace('/', Path.DirectorySeparatorChar));
                // Explicit overrides take precedence over downloaded pack files.
                if (!File.Exists(destination))
                    await DownloadCache.EnsureAsync(file.Url, destination, cancellationToken, file.Sha1, file.Size, bytes => transfer?.Received(bytes));
                transfer?.Complete(file.Size, 0);
            }
        }
        plan.Instance.InstancePath = target;
        SaveInstance(plan.Instance);
        File.Delete(marker);
        return plan.Instance;
    }

    public void ExportArchive(InstanceModel instance, string archivePath)
    {
        if (string.IsNullOrWhiteSpace(instance.Version) || string.IsNullOrWhiteSpace(instance.Loader))
            throw new InvalidDataException("The instance version and loader are required for export.");
        string destination = Path.GetFullPath(archivePath);
        string instanceRoot = Path.GetFullPath(instance.InstancePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destination.StartsWith(instanceRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Save the exported ZIP outside the instance folder.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // The in-memory instance is authoritative. A stale/missing disk manifest
        // must never turn a Fabric/Forge/Quilt export into a Vanilla instance.
        string metadata = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
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
