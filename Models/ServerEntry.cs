using System.Text.Json.Serialization;

namespace VortexLauncher.Models;

public partial class ServerEntry
{
    public string Name { get; set; } = "Minecraft Server";
    public string Address { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayAddress => string.IsNullOrWhiteSpace(Address) ? "No address" : Address;
}
