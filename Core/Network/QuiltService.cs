using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Network;

/// <summary>
/// Quilt exposes the same launcher-profile shape as Fabric, but uses its own
/// metadata service. Keeping it separate prevents Quilt instances from being
/// accidentally installed through the Forge code path.
/// </summary>
public sealed class QuiltService
{
    private static readonly HttpClient HttpClient = new();
    private const string MetaUrl = "https://meta.quiltmc.org/v3/versions/loader";

    public async Task<string?> GetLatestLoaderVersionAsync(string gameVersion)
    {
        try
        {
            string url = $"{MetaUrl}/{Uri.EscapeDataString(gameVersion)}";
            string json = await HttpClient.GetStringAsync(url);
            using JsonDocument document = JsonDocument.Parse(json);

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("loader", out JsonElement loader) &&
                    loader.TryGetProperty("version", out JsonElement version))
                {
                    string? value = version.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
        }
        catch
        {
            // An unavailable metadata endpoint simply means Quilt is not
            // available for this Minecraft version.
        }

        return null;
    }

    public async Task<FabricProfile?> GetQuiltProfileAsync(string gameVersion, string loaderVersion = "latest")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(loaderVersion) ||
                loaderVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                loaderVersion = await GetLatestLoaderVersionAsync(gameVersion) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(loaderVersion)) return null;
            }

            string url = $"{MetaUrl}/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
            string json = await HttpClient.GetStringAsync(url);
            return JsonSerializer.Deserialize<FabricProfile>(json);
        }
        catch
        {
            return null;
        }
    }
}
