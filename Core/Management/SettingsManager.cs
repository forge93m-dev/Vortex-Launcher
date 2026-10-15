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
    public string AccentColor { get; set; } = "#7AA2F7";
    public string ThemeName { get; set; } = "Tokyo Night";
    public bool HasLaunchedBefore { get; set; }
    public string UpdateManifestUrl { get; set; } = DefaultUpdateManifestUrl;
    public bool MinimizeOnGameLaunch { get; set; } = true;
}

public class SettingsManager
{
    private readonly string _configPath;

    public SettingsManager()
    {
        string dir = Path.Combine(PlatformSupport.DataRoot, "config");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "settings.json");
    }

    public LauncherSettings LoadSettings()
    {
        if (!File.Exists(_configPath))
        {
            var defaultSettings = new LauncherSettings();
            SaveSettings(defaultSettings);
            return defaultSettings;
        }

        try
        {
            string json = File.ReadAllText(_configPath);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            if (string.IsNullOrWhiteSpace(settings.UpdateManifestUrl))
            {
                settings.UpdateManifestUrl = LauncherSettings.DefaultUpdateManifestUrl;
            }

            return settings;
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    public void SaveSettings(LauncherSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }
}
