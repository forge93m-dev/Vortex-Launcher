using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

public class FabricService
{
    private static readonly HttpClient HttpClient = new();
    private const string FabricMetaUrl = "https://meta.fabricmc.net/v2/versions/loader";

    public async Task<string?> GetLatestLoaderVersionAsync(string gameVersion)
    {
        try
        {
            string url = $"{FabricMetaUrl}/{gameVersion}";
            var json = await HttpClient.GetStringAsync(url);
            var loaders = JsonSerializer.Deserialize<List<FabricLoaderItem>>(json);
            return loaders?
                .Select(item => item.Loader)
                .Where(loader => loader != null)
                .OrderByDescending(loader => loader!.Stable)
                .ThenByDescending(loader => loader!.Version, StringComparer.Ordinal)
                .FirstOrDefault()?.Version;
        }
        catch
        {
            return null;
        }
    }

    public async Task<FabricProfile?> GetFabricProfileAsync(string gameVersion, string loaderVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(loaderVersion) ||
                loaderVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                loaderVersion = await GetLatestLoaderVersionAsync(gameVersion) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(loaderVersion))
                {
                    return null;
                }
            }

            string url = $"{FabricMetaUrl}/{gameVersion}/{loaderVersion}/profile/json";
            var json = await HttpClient.GetStringAsync(url);
            return JsonSerializer.Deserialize<FabricProfile>(json);
        }
        catch
        {
            return null;
        }
    }
}

public class FabricLoaderItem
{
    [JsonPropertyName("loader")]
    public FabricMetaVersion? Loader { get; set; }
}
