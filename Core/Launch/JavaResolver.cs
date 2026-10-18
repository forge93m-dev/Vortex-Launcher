using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VortexLauncher.Core.Launch;

public static class JavaResolver
{
    public static string GetJavaForVersion(string version, Architecture? architecture = null)
    {
        int requiredVersion = GetRequiredJavaVersion(version);
        var arch = architecture ?? RuntimeInformation.ProcessArchitecture;
        var candidates = new List<string>();
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome)) candidates.Add(Path.Combine(javaHome, "bin", PlatformSupport.JavaExecutable));
        if (OperatingSystem.IsMacOS())
        {
            string? home = FindMacJavaHome(requiredVersion, arch);
            if (home != null) candidates.Add(Path.Combine(home, "bin", "java"));
            foreach (string prefix in new[] { "/opt/homebrew/opt", "/usr/local/opt" })
                foreach (string name in new[] { "openjdk", $"openjdk@{requiredVersion}" })
                    candidates.Add(Path.Combine(prefix, name, "libexec", "openjdk.jdk", "Contents", "Home", "bin", "java"));
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(folder, PlatformSupport.JavaExecutable));
        string[] searchPaths = OperatingSystem.IsWindows() ? new[]
        {
            @"C:\Program Files\Java", @"C:\Program Files (x86)\Java",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
            Path.Combine(PlatformSupport.DataRoot, "runtime")
        } : new[]
        {
            "/Library/Java/JavaVirtualMachines",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Java", "JavaVirtualMachines"),
            Path.Combine(PlatformSupport.DataRoot, "runtime")
        };

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            try
            {
                candidates.AddRange(Directory.EnumerateFiles(basePath, PlatformSupport.JavaExecutable,
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }));
            }
            catch (IOException) { }
        }

        return candidates.Distinct().FirstOrDefault(file => File.Exists(file) && IsRuntimeComplete(file) && GetMajorVersion(file) == requiredVersion &&
            (!OperatingSystem.IsMacOS() || GetArchitecture(file) == arch)) ?? PlatformSupport.JavaExecutable;
    }

    private static string? FindMacJavaHome(int version, Architecture architecture)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var info = new ProcessStartInfo("/usr/libexec/java_home") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add("-v"); info.ArgumentList.Add(version == 8 ? "1.8" : version.ToString());
            info.ArgumentList.Add("-a"); info.ArgumentList.Add(architecture == Architecture.Arm64 ? "arm64" : "x86_64");
            using var process = Process.Start(info);
            if (process == null) return null;
            if (!process.WaitForExit(2000)) { process.Kill(); return null; }
            string home = process.StandardOutput.ReadToEnd().Trim();
            return process.ExitCode == 0 && Directory.Exists(home) ? home : null;
        }
        catch { }
        return null;
    }

    private static string? ReadReleaseProperty(string javaPath, string property)
    {
        string? bin = Path.GetDirectoryName(javaPath);
        string? home = bin == null ? null : Path.GetDirectoryName(bin);
        if (home == null) return null;
        string release = Path.Combine(home, "release");
        if (!File.Exists(release)) return null;
        return File.ReadLines(release).FirstOrDefault(line => line.StartsWith(property + "=", StringComparison.Ordinal))?
            .Split('=', 2)[1].Trim().Trim('"');
    }

    private static bool IsRuntimeComplete(string javaPath)
    {
        var parent = new DirectoryInfo(Path.GetDirectoryName(javaPath)!);
        for (int depth = 0; parent != null && depth < 8; depth++, parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, ".vortex-runtime-pending"))) return false;
        return true;
    }

    public static Architecture? GetArchitecture(string javaPath) =>
        ReadReleaseProperty(javaPath, "OS_ARCH")?.ToLowerInvariant() switch
        {
            "aarch64" or "arm64" => Architecture.Arm64,
            "amd64" or "x86_64" or "x64" => Architecture.X64,
            "x86" or "i386" or "i686" => Architecture.X86,
            _ => null
        };

    public static int GetMajorVersion(string javaPath)
    {
        try
        {
            string? value = ReadReleaseProperty(javaPath, "JAVA_VERSION");
            if (value != null)
            {
                var match = Regex.Match(value, @"^(?:1\.)?(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int major)) return major;
            }
            if (OperatingSystem.IsWindows())
            {
                var info = FileVersionInfo.GetVersionInfo(javaPath);
                if (info.ProductMajorPart > 1) return info.ProductMajorPart;
                if (info.FileMajorPart > 1) return info.FileMajorPart;
                if (info.ProductMajorPart == 1 && info.ProductMinorPart == 8) return 8;
            }
            var pathMatch = Regex.Match(javaPath, @"(?:jdk|jre)[-]?(?:1\.)?(\d+)", RegexOptions.IgnoreCase);
            if (pathMatch.Success && int.TryParse(pathMatch.Groups[1].Value, out int pathMajor)) return pathMajor;
        }
        catch (IOException) { }
        return 0;
    }

    public static int GetRequiredJavaVersion(string version)
    {
        // Minecraft 26.x uses Java 25. Minecraft 1.20.5+ and 1.21.x use
        // Java 21, while 1.17-1.20.4 use Java 17.
        if (version.StartsWith("26.", StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }

        if (version.StartsWith("1.20.5", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.20.6", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.21", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.22", StringComparison.OrdinalIgnoreCase))
        {
            return 21;
        }

        if (version.StartsWith("1.17", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.18", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.19", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.20", StringComparison.OrdinalIgnoreCase))
        {
            return 17;
        }

        return 8;
    }
}
