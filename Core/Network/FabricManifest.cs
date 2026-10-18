using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VortexLauncher.Core.Network;

public class FabricMetaVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

public class FabricProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("inheritsFrom")]
    public string InheritsFrom { get; set; } = string.Empty;

    [JsonPropertyName("mainClass")]
    public string MainClass { get; set; } = string.Empty;

    [JsonPropertyName("libraries")]
    public List<LibraryItem>? Libraries { get; set; }
}