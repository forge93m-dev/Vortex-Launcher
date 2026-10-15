using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VortexLauncher.Core.Launch;

public static class PlatformSupport
{
    // Keep the existing Windows portable layout. macOS bundles may be read-only
    // and must never contain worlds, accounts or downloaded runtimes.
    public static string DataRoot => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "VortexLauncher")
        : AppDomain.CurrentDomain.BaseDirectory;

    public static string MinecraftOs => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
    public static string JavaExecutable => OperatingSystem.IsWindows() ? "javaw.exe" : "java";
    public static string RuntimeId => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") +
        (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "-arm64" : "-x64");

    public static Architecture GetGameArchitecture(JsonElement metadata, string? os = null, Architecture? host = null)
    {
        os ??= MinecraftOs;
        var architecture = host ?? RuntimeInformation.ProcessArchitecture;
        if (os == "osx" && architecture == Architecture.Arm64 && metadata.TryGetProperty("libraries", out var libraries))
        {
            bool armNatives = libraries.EnumerateArray().Any(lib =>
                lib.TryGetProperty("downloads", out var downloads) &&
                ((downloads.TryGetProperty("artifact", out var artifact) && artifact.TryGetProperty("path", out var path) &&
                  MatchesNativePath(path.GetString() ?? "", os, Architecture.Arm64)) ||
                 (downloads.TryGetProperty("classifiers", out var classifiers) &&
                  classifiers.EnumerateObject().Any(c => MatchesNativePath(c.Name, os, Architecture.Arm64)))));
            return armNatives ? Architecture.Arm64 : Architecture.X64;
        }
        return architecture;
    }

    public static Architecture GetGameArchitecture(string instancePath, string version)
    {
        string metadata = Path.Combine(instancePath, "versions", version, $"{version}.json");
        if (File.Exists(metadata))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metadata));
            return GetGameArchitecture(doc.RootElement);
        }
        return RuntimeInformation.ProcessArchitecture;
    }

    public static bool RulesAllow(JsonElement item, string? osName = null, Architecture? architecture = null)
    {
        if (!item.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array) return true;
        osName ??= MinecraftOs;
        var arch = architecture ?? RuntimeInformation.ProcessArchitecture;
        string javaArch = arch == Architecture.Arm64 ? "aarch64" : arch == Architecture.X86 ? "x86" : "amd64";
        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            bool matches = true;
            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name)) matches &= name.GetString() == osName;
                if (os.TryGetProperty("arch", out var osArch)) matches &= Regex.IsMatch(javaArch, osArch.GetString() ?? "", RegexOptions.None, TimeSpan.FromSeconds(1));
                if (os.TryGetProperty("version", out var osVersion)) matches &= Regex.IsMatch(Environment.OSVersion.Version.ToString(), osVersion.GetString() ?? "", RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            // The launcher uses a normal, non-demo launch with default resolution.
            if (rule.TryGetProperty("features", out var features))
                matches &= features.EnumerateObject().All(f => f.Value.ValueKind == JsonValueKind.False);
            if (matches) allowed = rule.TryGetProperty("action", out var action) && action.GetString() == "allow";
        }
        return allowed;
    }

    public static bool MatchesNativePath(string path, string? osName = null, Architecture? architecture = null)
    {
        osName ??= MinecraftOs;
        var arch = architecture ?? RuntimeInformation.ProcessArchitecture;
        string name = Path.GetFileName(path.Replace('\\', '/')).ToLowerInvariant();
        string prefix = osName == "osx" ? "natives-macos" : "natives-" + osName;
        int index = name.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0 && osName == "osx") { prefix = "natives-osx"; index = name.IndexOf(prefix, StringComparison.Ordinal); }
        if (index < 0) return false;
        string suffix = name[(index + prefix.Length)..];
        bool arm = suffix.Contains("arm64") || suffix.Contains("aarch64");
        if (!arm && suffix.Contains("arm")) return false;
        bool x86 = (suffix.Contains("x86") && !suffix.Contains("x86_64")) || suffix.StartsWith("-32", StringComparison.Ordinal);
        return arch == Architecture.Arm64 ? arm : arch == Architecture.X86 ? !arm && x86 : !arm && !x86;
    }

    public static bool IsNativeBinary(string name) => MinecraftOs switch
    {
        "windows" => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
        "osx" => name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jnilib", StringComparison.OrdinalIgnoreCase),
        _ => name.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
    };

    public static void OpenPath(string path)
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows()) start = new ProcessStartInfo(path) { UseShellExecute = true };
        else
        {
            start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(path);
        }
        Process.Start(start);
    }

    public static void ValidateJavaArchitecture(string javaPath, Architecture required)
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists(javaPath)) return;
        var actual = JavaResolver.GetArchitecture(javaPath);
        if (actual.HasValue && actual.Value != required)
            throw new InvalidOperationException($"This Minecraft version requires {required} Java, but your custom Java is {actual}. Clear the custom Java path to select a compatible runtime automatically.");
    }
}
