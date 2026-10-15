using System;
using System.IO;
using System.Linq;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Management;

public sealed class InterruptedOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "";
    public string Source { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string State { get; set; } = "Active";
    [System.Text.Json.Serialization.JsonIgnore] public string Target { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public bool MetadataAvailable { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore] public bool CanResume => MetadataAvailable && Kind is "Install" or "Repair" or "Import" or "Clone";
    [System.Text.Json.Serialization.JsonIgnore] public bool CanCancel => CanResume && State != "Cancelled";
    [System.Text.Json.Serialization.JsonIgnore] public string Description => $"{Name} · {Kind} · {(State == "Cancelled" ? "Cancelled — files kept" : "Interrupted — choose Resume or Cancel")}";
}

internal static class OperationJournal
{
    internal const string FileName = ".vortex-operation.json";
    internal static readonly string[] Markers = { ".vortex-cloning", ".vortex-importing", ".vortex-installation-pending" };
    internal static bool HasMarker(string target) => Markers.Any(name => File.Exists(Path.Combine(target, name)));
    internal static InterruptedOperation? Read(string target) => SafeJsonStore.Read<InterruptedOperation>(Path.Combine(target, FileName),
        item => Guid.TryParseExact(item.Id, "N", out _) && item.Kind is "Install" or "Repair" or "Import" or "Clone" && item.State is "Active" or "Cancelled").Value;
    internal static InterruptedOperation Begin(string target, string kind, string source = "", string fingerprint = "", bool resume = false)
    {
        var old = Read(target);
        if (resume && (old == null || old.Kind != kind || old.Source != source || old.Fingerprint != fingerprint))
            throw new InvalidDataException("The saved operation or its source changed. Nothing was overwritten; start a separate import/copy instead.");
        var operation = resume ? old! : new InterruptedOperation { Kind = kind, Source = source, Fingerprint = fingerprint };
        operation.State = "Active";
        SafeJsonStore.Write(Path.Combine(target, FileName), operation);
        return operation;
    }
    internal static void Complete(string target)
    {
        foreach (string name in Markers.Concat(new[] { FileName, FileName + ".bak" })) File.Delete(Path.Combine(target, name));
    }
    internal static void Cancel(InterruptedOperation item)
    {
        var current = Read(item.Target);
        if (current == null || current.Id != item.Id) throw new IOException("This operation changed. Reload the library.");
        current.State = "Cancelled";
        SafeJsonStore.Write(Path.Combine(item.Target, FileName), current);
    }
    internal static string ValidateTarget(string root, string target)
    {
        string resolved = Path.GetFullPath(target);
        string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(resolved), parent, comparison) || !Directory.Exists(resolved)
            || (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery target is not an owned instance directory.");
        return resolved;
    }
    internal static InterruptedOperation? Discover(InstanceModel instance)
    {
        var stored = Read(instance.InstancePath);
        instance.HasInterruptedOperation = false;
        if (stored == null && !HasMarker(instance.InstancePath) && !File.Exists(Path.Combine(instance.InstancePath, FileName))
            && !File.Exists(Path.Combine(instance.InstancePath, FileName + ".bak"))) return null;
        stored ??= new InterruptedOperation { Kind = "Legacy task (source not recorded)" };
        stored.Target = instance.InstancePath; stored.Name = instance.Name;
        stored.MetadataAvailable = instance.MetadataProblem.Length == 0 || stored.Kind is "Import" or "Clone";
        instance.HasInterruptedOperation = true;
        return stored;
    }
}
