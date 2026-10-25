using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VortexLauncher.Core.Network;

public class VersionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("assetIndex")]
    public AssetIndexInfo? AssetIndex { get; set; }

    [JsonPropertyName("downloads")]
    public Dictionary<string, DownloadItem>? Downloads { get; set; }

    [JsonPropertyName("libraries")]
    public List<LibraryItem>? Libraries { get; set; }
}

public class AssetIndexInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public class AssetIndexContent
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject>? Objects { get; set; }
}

public class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class DownloadItem
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class LibraryItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }
}

public class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public DownloadItem? Artifact { get; set; }
}
