using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Launch;
using VortexLauncher.Core.Management;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Network;

public sealed record InstalledModSnapshot(string Path, string Hash, ModrinthVersionItem? Version);
public sealed record ModUpdatePreview(string Root, string Minecraft, string Loader,
    IReadOnlyList<InstalledModSnapshot> Installed, IReadOnlyList<ModUpdateModel> Updates,
    int Unrecognized, IReadOnlyList<string> Errors);

public sealed class ModUpdateService
{
    private readonly ModrinthService _api;
    private readonly HttpClient? _http;
    private readonly string? _cache;
    public ModUpdateService(ModrinthService? api = null) => _api = api ?? new();
    internal ModUpdateService(ModrinthService api, HttpClient http, string cache) { _api = api; _http = http; _cache = cache; }
    public async Task<string?> GetProjectUrlAsync(string root, string path, CancellationToken token)
    {
        ModFiles.Validate(root, path);
        if (VortexClientService.HasClientId(path)) return null;
        var version = await _api.IdentifyForUpdateAsync(await HashAsync(path, token), token);
        return string.IsNullOrWhiteSpace(version?.ProjectId) ? null : "https://modrinth.com/mod/" + Uri.EscapeDataString(version.ProjectId);
    }
    internal static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        return Convert.ToHexString(await SHA512.HashDataAsync(input, token)).ToLowerInvariant();
    }
    private static bool Compatible(ModrinthVersionItem v, string minecraft, string loader) =>
        v.GameVersions.Contains(minecraft, StringComparer.OrdinalIgnoreCase) && v.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase);
    private static bool SafeFile(ModrinthFileItem file) => file.Filename.Length is > 4 and < 200 &&
        file.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
        file.Filename.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\r', '\n' }) < 0 &&
        !file.Filename.StartsWith('.') && file.Size is > 0 and <= 536870912 &&
        file.Hashes.TryGetValue("sha512", out string? hash) && hash.Length == 128 && hash.All(Uri.IsHexDigit) &&
        Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 &&
        uri.Host.Equals("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase);

    public async Task<ModUpdatePreview> CheckAsync(string root, string minecraft, string loader,
        bool includePrereleases, IProgress<string>? progress, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        string mods = Path.Combine(root, "mods");
        ModFiles.RejectLinks(mods);
        var installed = new List<InstalledModSnapshot>();
        var updates = new List<ModUpdateModel>();
        var errors = new List<string>();
        int unknown = 0;
        foreach (string path in Directory.Exists(mods) ? Directory.GetFiles(mods).Where(ModFiles.IsMod).OrderBy(p => p, StringComparer.Ordinal).ToArray() : Array.Empty<string>())
        {
            token.ThrowIfCancellationRequested();
            ModFiles.Validate(root, path);
            progress?.Report("Checking " + Path.GetFileName(path));
            string hash = await HashAsync(path, token);
            ModrinthVersionItem? current = null;
            try
            {
                // The embedded client has its own verified launcher update path.
                if (!VortexClientService.HasClientId(path)) current = await _api.IdentifyForUpdateAsync(hash, token);
                installed.Add(new(path, hash, current));
                if (current == null || string.IsNullOrWhiteSpace(current.ProjectId)) { unknown++; continue; }
                var versions = await _api.GetUpdateVersionsAsync(current.ProjectId, token);
                var latest = versions.Where(v => v.ProjectId == current.ProjectId && Compatible(v, minecraft, loader) &&
                    (v.VersionType == "release" || includePrereleases && v.VersionType is "beta" or "alpha") && v.Files.Any(SafeFile))
                    .OrderByDescending(v => v.DatePublished).FirstOrDefault();
                if (latest == null || latest.Id == current.Id || latest.DatePublished <= current.DatePublished) continue;
                var file = latest.Files.Where(SafeFile).OrderByDescending(f => f.Primary).First();
                if (string.Equals(file.Hashes["sha512"], hash, StringComparison.OrdinalIgnoreCase)) continue;
                updates.Add(new() { SourcePath = path, OriginalHash = hash, Current = current, Latest = latest, File = file });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
            {
                if (!installed.Any(i => i.Path == path)) installed.Add(new(path, hash, current));
                errors.Add(Path.GetFileName(path) + ": " + ex.Message);
            }
        }
        foreach (var duplicate in installed.Where(i => i.Version != null).GroupBy(i => i.Version!.ProjectId).Where(g => g.Count() > 1))
        {
            errors.Add("Duplicate project " + duplicate.Key + ": remove duplicates before updating.");
            updates.RemoveAll(u => u.Current.ProjectId == duplicate.Key);
        }
        return new(root, minecraft, loader, installed, updates, unknown, errors);
    }

    private async Task ValidateDependenciesAsync(ModUpdatePreview preview, ModUpdateModel[] selected, CancellationToken token)
    {
        var effective = preview.Installed.Where(i => i.Version != null && !ModFiles.IsDisabled(i.Path))
            .GroupBy(i => i.Version!.ProjectId).ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First().Version! :
                throw new IOException("Duplicate installed project " + g.Key + ". Resolve it before updating."));
        foreach (var item in selected.Where(i => !ModFiles.IsDisabled(i.SourcePath))) effective[item.Current.ProjectId] = item.Latest;
        var exactVersions = new Dictionary<string, ModrinthVersionItem>();
        foreach (var version in effective.Values)
        foreach (var dependency in version.Dependencies.Where(d => d.Type is "required" or "incompatible"))
        {
            token.ThrowIfCancellationRequested();
            string? project = dependency.ProjectId;
            if (!string.IsNullOrEmpty(dependency.VersionId))
            {
                if (!exactVersions.TryGetValue(dependency.VersionId, out var exact))
                    exactVersions[dependency.VersionId] = exact = await _api.GetVersionAsync(dependency.VersionId).WaitAsync(token);
                if (project != null && project != exact.ProjectId) throw new IOException("Dependency project/version metadata mismatch.");
                project = exact.ProjectId;
            }
            bool present = project != null && effective.TryGetValue(project, out _);
            if (dependency.Type == "required" && (!present || dependency.VersionId != null && effective[project!].Id != dependency.VersionId))
                throw new IOException($"{version.DisplayName} requires {project ?? dependency.FileName ?? "an unknown dependency"}" +
                    (dependency.VersionId != null ? " version " + dependency.VersionId : "") + ". Select its compatible update or install/enable the dependency first. Nothing changed.");
            if (dependency.Type == "incompatible" && present && (dependency.VersionId == null || effective[project!].Id == dependency.VersionId))
                throw new IOException($"{version.DisplayName} conflicts with {project}. Nothing changed.");
        }
    }

    public async Task<string> ApplyAsync(ModUpdatePreview preview, IReadOnlyList<ModUpdateModel> selection,
        Func<bool> canChange, IProgress<string>? progress, CancellationToken token)
    {
        var selected = selection.ToArray();
        if (selected.Length == 0) throw new IOException("Select updates first.");
        if (selected.Distinct().Count() != selected.Length || selected.Any(s => !preview.Updates.Contains(s) || !SafeFile(s.File)))
            throw new IOException("The update selection is not part of this preview.");
        if (!canChange()) throw new IOException("Stop the game and other instance operations first.");
        await ValidateInventoryAsync(preview, token);
        await ValidateDependenciesAsync(preview, selected, token);
        string batch = Path.Combine(preview.Root, ".vortex-mod-updates", Guid.NewGuid().ToString("N"));
        ModFiles.RejectLinks(batch);
        Directory.CreateDirectory(batch);
        var changes = selected.Select((s, i) => new Change(s,
            Path.Combine(batch, "download-" + i + ".jar"), Path.Combine(batch, "previous-" + i, Path.GetFileName(s.SourcePath)),
            Path.Combine(preview.Root, "mods", s.File.Filename + (ModFiles.IsDisabled(s.SourcePath) ? ".disabled" : "")))).ToArray();
        var destinations = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (!destinations.Add(change.Destination)) throw new IOException("Multiple updates use the same filename.");
            ModFiles.Validate(preview.Root, change.Destination);
            if (!SamePath(change.Destination, change.Item.SourcePath) && File.Exists(change.Destination))
                throw new IOException("Update destination already exists: " + Path.GetFileName(change.Destination));
            if (File.Exists(Path.Combine(preview.Root, "mods", change.Item.File.Filename)) && ModFiles.IsDisabled(change.Destination))
                throw new IOException("An enabled version already uses this filename.");
            if (File.Exists(Path.Combine(preview.Root, "mods", change.Item.File.Filename + ".disabled")) && !ModFiles.IsDisabled(change.Destination))
                throw new IOException("A disabled version already uses this filename.");
        }
        await DownloadBatch.RunAsync(changes, async (change, downloadToken) =>
        {
            progress?.Report("Downloading " + change.Item.File.Filename);
            await DownloadCache.EnsureAsync(change.Item.File.Url, change.Staged, downloadToken,
                change.Item.File.Hashes.GetValueOrDefault("sha1"), change.Item.File.Size, httpClient: _http, cacheRoot: _cache, validateArchive: true);
            if (!string.Equals(await HashAsync(change.Staged, downloadToken), change.Item.File.Hashes["sha512"], StringComparison.OrdinalIgnoreCase))
                throw new IOException("SHA-512 mismatch. Original mods were not changed.");
            using var archive = System.IO.Compression.ZipFile.OpenRead(change.Staged);
            if (archive.Entries.Count == 0 || VortexClientService.HasClientId(change.Staged)) throw new IOException("Invalid mod update archive.");
        }, token);
        await ValidateInventoryAsync(preview, token);
        // Keep explicit old/new paths and hashes beside the backups if the process
        // is terminated between filesystem renames. Never auto-delete old mods.
        SafeJsonStore.Write(Path.Combine(batch, "changes.json"), changes.Select(c => new
        { oldFile = Path.GetFileName(c.Item.SourcePath), newFile = Path.GetFileName(c.Destination), backup = Path.GetRelativePath(batch, c.Backup), oldHash = c.Item.OriginalHash, newHash = c.Item.File.Hashes["sha512"] }).ToArray());
        var moved = new List<Change>();
        try
        {
            foreach (var change in changes)
            {
                token.ThrowIfCancellationRequested();
                if (!canChange()) throw new IOException("Instance became busy. Updates stopped.");
                ModFiles.Validate(preview.Root, change.Item.SourcePath); ModFiles.Validate(preview.Root, change.Destination);
                if (await HashAsync(change.Item.SourcePath, token) != change.Item.OriginalHash) throw new IOException("Mod changed after preview; check updates again.");
                Directory.CreateDirectory(Path.GetDirectoryName(change.Backup)!);
                File.Move(change.Item.SourcePath, change.Backup, false);
                moved.Add(change);
                File.Move(change.Staged, change.Destination, false);
            }
            SafeJsonStore.Write(Path.Combine(batch, "result.json"), new { state = "Completed", count = changes.Length });
            return batch;
        }
        catch (Exception original)
        {
            var rollbackErrors = new List<string>();
            foreach (var change in moved.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(change.Destination) && !File.Exists(change.Staged))
                    {
                        ModFiles.Validate(preview.Root, change.Destination);
                        if (!string.Equals(await HashAsync(change.Destination, CancellationToken.None), change.Item.File.Hashes["sha512"], StringComparison.OrdinalIgnoreCase))
                            throw new IOException("New file was externally modified; left untouched.");
                        File.Move(change.Destination, change.Staged, false);
                    }
                    ModFiles.Validate(preview.Root, change.Item.SourcePath);
                    File.Move(change.Backup, change.Item.SourcePath, false);
                }
                catch (Exception ex) { rollbackErrors.Add(ex.Message); }
            }
            if (rollbackErrors.Count > 0) throw new IOException("Could not fully roll back. Original files are in " + batch + ". " + string.Join("; ", rollbackErrors), original);
            throw;
        }
    }
    private sealed record Change(ModUpdateModel Item, string Staged, string Backup, string Destination);
    private static bool SamePath(string a, string b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static async Task ValidateInventoryAsync(ModUpdatePreview preview, CancellationToken token)
    {
        string mods = Path.Combine(preview.Root, "mods"); ModFiles.RejectLinks(mods);
        string[] paths = Directory.Exists(mods) ? Directory.GetFiles(mods).Where(ModFiles.IsMod).OrderBy(p => p, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
        if (!paths.SequenceEqual(preview.Installed.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal)))
            throw new IOException("The mod list changed. Check updates again.");
        foreach (var local in preview.Installed)
        {
            ModFiles.Validate(preview.Root, local.Path);
            if (await HashAsync(local.Path, token) != local.Hash) throw new IOException("A mod changed after preview. Check updates again.");
        }
    }
}
