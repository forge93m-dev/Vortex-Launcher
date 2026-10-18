using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Network;

namespace VortexLauncher.Core.Diagnostics;

public sealed record CompatibilityReport(IReadOnlyList<string> Warnings, int Checked, int Unknown)
{
    public string Summary => Warnings.Count > 0
        ? string.Join(Environment.NewLine, Warnings) + $"\n\nChecked: {Checked}. Unverified: {Unknown}."
        : $"No known incompatibilities found. Checked: {Checked}. Unverified: {Unknown}.";
}

public sealed class ModCompatibilityService
{
    private readonly ModrinthService _modrinth = new();
    private static readonly ConcurrentDictionary<string, ModrinthVersionItem> Versions = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> Unresolved = new();

    public async Task<CompatibilityReport> CheckAsync(string instancePath, string gameVersion,
        string loader, CancellationToken token = default)
    {
        string directory = Path.Combine(instancePath, "mods");
        if (!Directory.Exists(directory)) return new CompatibilityReport(Array.Empty<string>(), 0, 0);
        string[] files = Directory.GetFiles(directory, "*.jar");
        var warnings = new ConcurrentBag<string>();
        int known = 0, unknown = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (path, ct) =>
        {
            string name = Path.GetFileName(path);
            try
            {
                string hash;
                await using (var stream = File.OpenRead(path))
                    hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, ct)).ToLowerInvariant();
                if (!Versions.TryGetValue(hash, out var version))
                {
                    if (!Unresolved.TryGetValue(hash, out var retryAfter) || DateTimeOffset.UtcNow >= retryAfter)
                    {
                        version = await _modrinth.GetVersionFromHashAsync(hash, ct);
                        if (version == null) Unresolved[hash] = DateTimeOffset.UtcNow.AddMinutes(5);
                    }
                    if (version != null) Versions[hash] = version;
                }
                if (version != null)
                {
                    Interlocked.Increment(ref known);
                    if (version.GameVersions.Count > 0 && !version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
                        warnings.Add($"{name}: Minecraft {gameVersion} is not supported. Declared: {string.Join(", ", version.GameVersions.Take(8))}.");
                    if (version.Loaders.Count > 0 && !version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
                        warnings.Add($"{name}: {loader} support is not declared. Loaders: {string.Join(", ", version.Loaders)}.");
                    return;
                }

                Interlocked.Increment(ref unknown);
                // Offline fallback detects loader metadata, but cannot guarantee
                // Minecraft compatibility without the author's version metadata.
                using var archive = ZipFile.OpenRead(path);
                var loaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (archive.GetEntry("fabric.mod.json") != null) loaders.Add("Fabric");
                if (archive.GetEntry("quilt.mod.json") != null) loaders.Add("Quilt");
                if (archive.GetEntry("META-INF/mods.toml") != null || archive.GetEntry("mcmod.info") != null) loaders.Add("Forge");
                if (archive.GetEntry("META-INF/neoforge.mods.toml") != null) loaders.Add("NeoForge");
                if (loaders.Count > 0 && !loaders.Contains(loader))
                    warnings.Add($"{name}: file declares {string.Join(", ", loaders)}, instance uses {loader}. Compatibility is unverified.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                warnings.Add($"{name}: cannot read mod archive ({ex.Message}).");
            }
        });
        return new CompatibilityReport(warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray(), known, unknown);
    }
}
