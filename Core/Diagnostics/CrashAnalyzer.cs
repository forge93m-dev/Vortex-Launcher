using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VortexLauncher.Core.Diagnostics;

public sealed record LaunchIssue(string Title, string Explanation, string Action, bool CanRepair = false)
{
    public override string ToString() => $"{Title}\n\n{Explanation}\n\nWhat to do: {Action}";
}

public static class CrashAnalyzer
{
    public static string Analyze(string[] lines, string instancePath)
    {
        return Diagnose(lines).ToString();
    }

    public static LaunchIssue Diagnose(string[] lines)
    {
        // A JAR in the classpath is not evidence of a faulty mod.
        string text = string.Join('\n', lines);
        bool Has(string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);
        if (Has("OutOfMemoryError") || Has("Could not reserve enough space") || Has("insufficient memory"))
            return new("Not enough memory", "Minecraft could not allocate the memory it needs.", "Close other applications and check the RAM limit in Launcher settings. Do not allocate all system memory.");
        if (Has("UnsupportedClassVersionError") || Has("requires Java") || Has("Unsupported Java"))
        {
            var version = Regex.Match(text, @"class file version (\d+)");
            string required = version.Success && int.TryParse(version.Groups[1].Value, out int major) ? $" Java {major - 44}" : " a compatible Java version";
            return new("Wrong Java version", $"A game component needs{required}.", "Clear the custom Java path in Launcher settings to use automatic detection, or select the required Java executable.");
        }
        if (Has("Incompatible mods found") || Has("Mod resolution encountered") || Has("requires version") || Has("depends on"))
            return new("Missing or incompatible mods", "The loader could not resolve the mod dependencies.", "Open Manage → Mods. Check the loader/version and install the missing dependency shown in the technical details, or disable the incompatible mod.");
        if (Has("Mixin apply failed") || Has("MixinTransformerError") || Has("InvalidMixinException"))
            return new("Mod transformation failed", "A mod's mixin could not be applied. This does not mean the Mixin library itself is broken.", "Check the final Caused by lines in the log. Update or disable the named mod and verify its Minecraft/loader compatibility.");
        if (Has("UnsatisfiedLinkError") || Has("wrong architecture") || Has("incompatible architecture"))
            return new("Native library could not load", "Java and the game libraries may use different CPU architectures, or a native file is missing.", "Use automatic Java detection and Repair instance. On Apple Silicon, older Intel-only versions may require Rosetta 2.", true);
        if (Has("ZipException") || Has("invalid LOC header") || Has("Could not find or load main class") || Has("ClassNotFoundException") || Has("FileNotFoundException"))
            return new("Missing or damaged game component", "A required class or file could not be read. A user mod can also cause this.", "Try Repair instance first. If the error names a mod, reinstall that mod separately; repair leaves user mods untouched.", true);
        if (Has("HttpRequestException") || Has("NameResolutionError") || Has("Could not download") || Has("timed out"))
            return new("Download or connection failed", "The launcher could not retrieve a required file.", "Check your connection, proxy and firewall, then retry. Verified downloaded files will be reused.", true);
        if (Has("UnauthorizedAccessException") || Has("being used by another process") || Has("Access to the path"))
            return new("File access blocked", "A required file is locked or the launcher cannot write to its folder.", "Close other launchers/game processes using this instance and check folder permissions. Do not delete worlds or mods.");
        var cause = lines.LastOrDefault(line => line.Contains("Caused by:", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("SignedJWT", StringComparison.OrdinalIgnoreCase) && !line.Contains("InvalidCredentials", StringComparison.OrdinalIgnoreCase) &&
            !Regex.IsMatch(line, @"status\s*[:=]?\s*401", RegexOptions.IgnoreCase));
        return new("Minecraft could not start or closed unexpectedly",
            cause == null ? "The captured output does not establish a specific cause. Realms/401 warnings alone do not explain a crash." : cause.Trim(),
            "Review the technical details and the newest crash report in the instance folder. Share the full log and exit code if the cause remains unclear.");
    }
}
