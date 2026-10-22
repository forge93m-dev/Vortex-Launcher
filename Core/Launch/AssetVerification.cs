using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using VortexLauncher.Core.Management;

namespace VortexLauncher.Core.Launch;

// A restart should not reread hundreds of MB of unchanged sounds/textures.
// This optimization is deliberately limited to content-addressed assets, never
// executable JARs, Java runtimes, mods or update packages. Repair clears it.
internal sealed class AssetVerification
{
    internal sealed record Stamp(long Size, long Written, long Created);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, long> Generations = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _objects;
    private readonly string _ledger;
    private readonly long _generation;
    private readonly ConcurrentDictionary<string, Stamp> _records = new(StringComparer.OrdinalIgnoreCase);
    private int _dirty;
    internal long Hits;

    internal AssetVerification()
    {
        _objects = Path.Combine(SharedGameFiles.Assets, "objects");
        _ledger = SharedGameFiles.Checked(Path.Combine(SharedGameFiles.Root, ".verified-assets.json"));
        lock (Gate)
        {
            _generation = Generations.GetValueOrDefault(_ledger);
            try
            {
                // Invalid/missing metadata is a cache miss, not an installation failure.
                // Do not recover a backup: it may predate an explicit Repair reset.
                if (File.Exists(_ledger) && new FileInfo(_ledger).Length <= 8 * 1024 * 1024)
                {
                    var records = JsonSerializer.Deserialize<Dictionary<string, Stamp>>(File.ReadAllText(_ledger));
                    if (records != null && records.Count <= 32768)
                        foreach (var pair in records)
                            if (ValidHash(pair.Key) && pair.Value != null) _records[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }

    private static bool ValidHash(string hash) => hash.Length == 40 && hash.All(Uri.IsHexDigit);
    private bool IsAsset(string path, string? hash) => hash != null && ValidHash(hash) &&
        string.Equals(Path.GetFullPath(path), Path.Combine(_objects, hash[..2], hash),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal bool IsUnchanged(string path, string? hash, long? size)
    {
        if (!IsAsset(path, hash)) return false;
        lock (Gate)
            if (Generations.GetValueOrDefault(_ledger) != _generation) return false;
        if (!_records.TryGetValue(hash!, out var stamp)) return false;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0 || (size.HasValue && info.Length != size.Value) ||
            info.Length != stamp.Size || info.LastWriteTimeUtc.Ticks != stamp.Written || info.CreationTimeUtc.Ticks != stamp.Created)
            return false;
        Interlocked.Increment(ref Hits);
        return true;
    }

    internal void RememberVerified(string path, string? hash)
    {
        if (!IsAsset(path, hash)) return;
        var info = new FileInfo(path);
        if (!info.Exists) return;
        _records[hash!] = new Stamp(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks);
        Interlocked.Exchange(ref _dirty, 1);
    }

    internal void Save()
    {
        if (Volatile.Read(ref _dirty) == 0 || _records.Count > 32768) return;
        lock (Gate)
        {
            if (Generations.GetValueOrDefault(_ledger) != _generation) return;
            try { SafeJsonStore.Write(SharedGameFiles.Checked(_ledger), new Dictionary<string, Stamp>(_records)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static void Invalidate(string directory)
    {
        string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string ledger = Path.GetFullPath(Path.Combine(SharedGameFiles.Root, ".verified-assets.json"));
        string objects = Path.GetFullPath(Path.Combine(SharedGameFiles.Root, "assets", "objects")) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!ledger.StartsWith(prefix, comparison) && !objects.StartsWith(prefix, comparison) && !prefix.StartsWith(objects, comparison)) return;
        lock (Gate)
        {
            Generations[ledger] = Generations.GetValueOrDefault(ledger) + 1;
            // Reset current metadata; backups are never trusted by this cache.
            // No assets or user files are removed.
            if (File.Exists(ledger) || File.Exists(ledger + ".bak"))
            {
                SharedGameFiles.Checked(ledger); SharedGameFiles.Checked(ledger + ".bak");
                SafeJsonStore.Write(ledger, new Dictionary<string, Stamp>());
            }
        }
    }
}
