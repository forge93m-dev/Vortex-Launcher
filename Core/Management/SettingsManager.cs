using System;
using System.IO;
using System.Text.Json;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Management;

public class LauncherSettings
{
    public const string DefaultUpdateManifestUrl =
        "https://raw.githubusercontent.com/forge93m-dev/Vortex-Launcher/main/latest.json";

    public int RamAllocationGb { get; set; } = 4;
    public string CustomJavaPath { get; set; } = string.Empty;
    public string JvmArguments { get; set; } = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions";
    /// <summary>The user's own CurseForge API key (needed to import CurseForge packs); stored only in this settings file.</summary>
    public string CurseForgeApiKey { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#A78BFA";
    public string ThemeName { get; set; } = "Midnight";
    public bool HasLaunchedBefore { get; set; }
    public string UpdateManifestUrl { get; set; } = DefaultUpdateManifestUrl;
    public bool MinimizeOnGameLaunch { get; set; } = true;
    public bool InterfaceAnimationsEnabled { get; set; } = true;
    public bool DiscordPresenceEnabled { get; set; }
    public bool ReceiveDiscordInvites { get; set; }
}

public class SettingsManager
{
    private readonly string _configPath;

    public string LoadNotice { get; private set; } = "";
    public bool HasLoadError { get; private set; }
    public SettingsManager(string? configDirectory = null)
    {
        string dir = configDirectory ?? Path.Combine(PlatformSupport.DataRoot, "config");
        _configPath = Path.Combine(dir, "settings.json");
    }

    public LauncherSettings LoadSettings()
    {
        var stored = SafeJsonStore.Read<LauncherSettings>(_configPath);
        LoadNotice = stored.Notice; HasLoadError = stored.Damaged;
        if (stored.Value is { } recovered)
        {
            if (string.IsNullOrWhiteSpace(recovered.UpdateManifestUrl)) recovered.UpdateManifestUrl = LauncherSettings.DefaultUpdateManifestUrl;
            return recovered;
        }
        if (stored.Damaged) return new LauncherSettings();
        if (!File.Exists(_configPath))
        {
            var defaultSettings = new LauncherSettings();
            SaveSettings(defaultSettings);
            return defaultSettings;
        }

        return new LauncherSettings();
    }

    public void SaveSettings(LauncherSettings settings)
    {
        SafeJsonStore.Write(_configPath, settings);
    }
}
