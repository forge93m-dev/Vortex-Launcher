using System;
using System.IO;
using System.Linq;
using VortexLauncher.Models;

namespace VortexLauncher.Core.Launch;

/// <summary>
/// Per-instance Java, memory and JVM argument overrides. An empty or zero value means "follow the launcher
/// settings", so existing instances keep their behaviour and a global change still reaches them.
/// </summary>
public static class LaunchSettings
{
    public const int MinRamGb = 1, MaxRamGb = 64, MaxTextLength = 4096;

    public static int ClampRam(int gigabytes) => Math.Clamp(gigabytes, MinRamGb, MaxRamGb);

    public static string JavaPath(InstanceModel instance, string launcherDefault) =>
        !string.IsNullOrWhiteSpace(instance.JavaPathOverride) ? instance.JavaPathOverride.Trim() : launcherDefault;

    public static int RamGb(InstanceModel instance, int launcherDefault) =>
        instance.RamGbOverride > 0 ? ClampRam(instance.RamGbOverride) : launcherDefault;

    /// <summary>Null means "use the launcher's arguments"; an empty string is a real choice (no extra arguments).</summary>
    public static string JvmArgs(InstanceModel instance, string launcherDefault) => instance.JvmArgsOverride ?? launcherDefault;

    public static bool HasOverrides(InstanceModel instance) =>
        instance.RamGbOverride > 0 || !string.IsNullOrWhiteSpace(instance.JavaPathOverride) || instance.JvmArgsOverride != null;

    public static void Reset(InstanceModel instance)
    {
        instance.RamGbOverride = 0; instance.JavaPathOverride = ""; instance.JvmArgsOverride = null;
    }

    /// <summary>Keeps values read from instance.json inside safe bounds (a damaged or hand-edited file cannot break launching).</summary>
    public static void Sanitize(InstanceModel instance)
    {
        instance.RamGbOverride = instance.RamGbOverride <= 0 ? 0 : ClampRam(instance.RamGbOverride);
        instance.JavaPathOverride = Clean(instance.JavaPathOverride ?? "");
        instance.JvmArgsOverride = instance.JvmArgsOverride == null ? null : Clean(instance.JvmArgsOverride, keepLineBreaks: true);
    }

    private static string Clean(string value, bool keepLineBreaks = false)
    {
        string cleaned = new(value.Where(c => !char.IsControl(c) || keepLineBreaks && c is '\n' or '\r' or '\t').ToArray());
        return cleaned.Length > MaxTextLength ? cleaned[..MaxTextLength] : cleaned;
    }

    /// <summary>Settings that arrive with a shared pack or an imported archive are never trusted: they would run someone else's Java.</summary>
    public static void RejectImported(InstanceModel instance) => Reset(instance);

    /// <summary>Null when the path is empty (use the default) or points at a Java executable; otherwise a short reason.</summary>
    public static string? CheckJavaPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim();
        if (path.Length > MaxTextLength || path.Any(char.IsControl)) return "That path is not valid.";
        string name = Path.GetFileName(path);
        if (!(name.Equals("java", StringComparison.OrdinalIgnoreCase) || name.Equals("java.exe", StringComparison.OrdinalIgnoreCase) ||
              name.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)))
            return "Choose the java (or javaw.exe) file inside a Java installation's bin folder.";
        return File.Exists(path) ? null : "That file does not exist. The launcher will pick Java automatically instead.";
    }

    public static int SystemRamGb()
    {
        long bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return bytes <= 0 ? 0 : (int)Math.Min(1024, bytes / (1024L * 1024 * 1024));
    }

    /// <summary>A short warning when the game would take most of this computer's memory; empty otherwise.</summary>
    public static string RamWarning(int requestedGb, int systemGb)
    {
        if (systemGb <= 0 || requestedGb <= 0) return "";
        if (requestedGb >= systemGb) return $"This computer has about {systemGb} GB, so {requestedGb} GB cannot be used. The game may fail to start.";
        return requestedGb * 4 > systemGb * 3 ? $"This computer has about {systemGb} GB. Leave some for Windows and other programs." : "";
    }
}
