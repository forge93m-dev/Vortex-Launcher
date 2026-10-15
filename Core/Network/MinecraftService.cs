using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

public class MinecraftService
{
    private static readonly HttpClient HttpClient = new();
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public async Task<List<string>> GetReleaseVersionsAsync()
    {
        try
        {
            var json = await HttpClient.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(json);

            if (manifest?.Versions == null)
                return GetFallbackVersions();

            return manifest.Versions
                .Where(v => v.Type == "release")
                .Select(v => v.Id)
                .ToList();
        }
        catch
        {
            return GetFallbackVersions();
        }
    }

    public async Task<string?> GetVersionJsonUrlAsync(string versionId)
    {
        try
        {
            var json = await HttpClient.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
            return manifest?.Versions?.FirstOrDefault(v => v.Id == versionId)?.Url;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetFallbackVersions()
    {
        return new List<string> { "1.20.4", "1.20.2", "1.20.1", "1.19.4", "1.16.5", "1.12.2" };
    }
}