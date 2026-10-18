using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VortexLauncher.Core.Management;

public sealed record RemovedContent(string Section, string BackupPath);

// Session-only undo history. Backups remain recoverable on disk after exit.
public static class RemovalUndoHistory
{
    private static readonly Dictionary<string, List<List<RemovedContent>>> History = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public static event Action<string>? Changed;
    public static bool HasUndo(string root) => !string.IsNullOrWhiteSpace(root) && History.TryGetValue(Path.GetFullPath(root), out var batches) && batches.Count > 0;
    public static void Remember(string root, IEnumerable<RemovedContent> items)
    {
        var batch = items.ToList(); if (batch.Count == 0) return;
        root = Path.GetFullPath(root);
        if (!History.TryGetValue(root, out var batches)) History[root] = batches = new();
        batches.Add(batch);
        if (batches.Count > 20) batches.RemoveAt(0);
        Changed?.Invoke(root);
    }
    public static bool Matches(string root, string changed) => !string.IsNullOrWhiteSpace(root) && string.Equals(Path.GetFullPath(root), changed, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public static int UndoLast(string root, Func<bool> canChange)
    {
        root = Path.GetFullPath(root);
        if (!canChange()) throw new IOException("Stop the game and other instance operations before restoring files.");
        if (!History.TryGetValue(root, out var batches) || batches.Count == 0) return 0;
        var batch = batches[^1];
        foreach (var item in batch) InstanceContentRemoval.ValidateRestore(root, item.Section, item.BackupPath);
        int restored = 0;
        try
        {
            while (batch.Count > 0)
            {
                if (!canChange()) throw new IOException("The instance became busy. Remaining files are still recoverable with Undo.");
                var item = batch[^1];
                InstanceContentRemoval.RestoreFromBackup(root, item.Section, item.BackupPath);
                batch.RemoveAt(batch.Count - 1); restored++;
            }
            batches.RemoveAt(batches.Count - 1);
            return restored;
        }
        finally { Changed?.Invoke(root); }
    }
}
