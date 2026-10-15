using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

public sealed record ModInstallEntry(string ProjectId, string Title, ModrinthVersionItem Version,
    bool IsDependency = false, string RequiredBy = "");

public sealed class ModDependencyResolver
{
    private readonly ModrinthService _service;
    public ModDependencyResolver(ModrinthService service) => _service = service;

    public async Task<IReadOnlyList<ModInstallEntry>> ResolveAsync(IReadOnlyList<ModInstallEntry> roots,
        string modsFolder, string minecraft, string loader)
    {
        bool Compatible(ModrinthVersionItem v) => v.GameVersions.Contains(minecraft, StringComparer.OrdinalIgnoreCase) &&
            v.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase) && v.Files.Any(f => !string.IsNullOrWhiteSpace(f.Url));
        var installed = new Dictionary<string, (ModrinthVersionItem Version, bool Disabled)>();
        if (roots.Any(r => r.Version.Dependencies.Any(d => d.Type == "required")) && Directory.Exists(modsFolder))
        {
            foreach (string path in Directory.GetFiles(modsFolder).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
            {
                await using var stream = File.OpenRead(path);
                string hash = Convert.ToHexString(await SHA512.HashDataAsync(stream)).ToLowerInvariant();
                var version = await _service.IdentifyInstalledVersionAsync(hash);
                if (version == null || string.IsNullOrEmpty(version.ProjectId)) continue;
                bool disabled = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (installed.TryGetValue(version.ProjectId, out var previous) && previous.Version.Id != version.Id)
                    throw new InvalidOperationException($"Multiple installed versions of {version.ProjectId}. Remove the duplicate before installing mods.");
                if (!installed.ContainsKey(version.ProjectId) || !disabled) installed[version.ProjectId] = (version, disabled);
            }
        }

        var chosen = roots.ToDictionary(r => r.ProjectId);
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        var plan = new List<ModInstallEntry>();
        async Task Visit(ModInstallEntry entry)
        {
            if (visited.Contains(entry.ProjectId) || visiting.Contains(entry.ProjectId)) return;
            if (chosen.Count > 256) throw new InvalidOperationException("Too many dependencies in this installation.");
            if (!Compatible(entry.Version)) throw new InvalidOperationException($"{entry.Title} has no compatible version for Minecraft {minecraft} / {loader}.");
            visiting.Add(entry.ProjectId);
            foreach (var dependency in entry.Version.Dependencies.Where(d => d.Type == "required"))
            {
                ModrinthVersionItem? exact = null;
                if (!string.IsNullOrEmpty(dependency.VersionId)) exact = await _service.GetVersionAsync(dependency.VersionId);
                string? project = exact?.ProjectId ?? dependency.ProjectId;
                if (string.IsNullOrEmpty(project))
                    throw new InvalidOperationException($"{entry.Title} requires {dependency.FileName ?? "an external dependency"}. Install it manually; no Modrinth download is available.");
                if (exact != null && !string.IsNullOrEmpty(dependency.ProjectId) && project != dependency.ProjectId)
                    throw new InvalidOperationException("Dependency project/version metadata does not match.");
                if (chosen.TryGetValue(project, out var existing))
                {
                    if (exact != null && existing.Version.Id != exact.Id)
                        throw new InvalidOperationException($"{entry.Title} requires a different version of {existing.Title}. Change the selected version first.");
                    await Visit(existing);
                    continue;
                }
                if (installed.TryGetValue(project, out var local))
                {
                    if (local.Disabled) throw new InvalidOperationException($"{entry.Title} requires an installed but disabled dependency ({project}). Enable it first.");
                    if (!Compatible(local.Version) || (exact != null && local.Version.Id != exact.Id))
                        throw new InvalidOperationException($"{entry.Title} requires a different version of installed dependency {project}. Update it manually first.");
                    var localEntry = new ModInstallEntry(project, await _service.GetProjectTitleAsync(project), local.Version, true, entry.Title);
                    chosen[project] = localEntry;
                    await Visit(localEntry);
                    continue;
                }
                var version = exact ?? (await _service.GetProjectVersionsAsync(project)).Where(Compatible)
                    .OrderByDescending(v => v.DatePublished).FirstOrDefault();
                if (version == null) throw new InvalidOperationException($"No compatible dependency {project} for {entry.Title}.");
                var required = new ModInstallEntry(project, await _service.GetProjectTitleAsync(project), version, true, entry.Title);
                chosen[project] = required;
                await Visit(required);
            }
            visiting.Remove(entry.ProjectId);
            visited.Add(entry.ProjectId);
            if (installed.TryGetValue(entry.ProjectId, out var installedEntry))
            {
                if (installedEntry.Disabled || installedEntry.Version.Id != entry.Version.Id)
                    throw new InvalidOperationException($"{entry.Title} is already installed with a different version or disabled. Resolve it first.");
            }
            else plan.Add(entry);
        }
        foreach (var root in roots) await Visit(root);
        return plan;
    }
}
