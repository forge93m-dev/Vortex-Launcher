using System;
using System.Collections.Generic;
using VortexLauncher.Core.Network;

namespace VortexLauncher.Core.Launch;

public static class ServerLaunchArguments
{
    public static IReadOnlyList<string> Build(string version, string address)
    {
        address = DiscordServerInvite.NormalizeAddress(address.Trim());
        if (Version.TryParse(version, out var parsed) && (parsed.Major >= 26 || parsed.Major == 1 && parsed.Minor >= 20))
            return new[] { "--quickPlayMultiplayer", address };
        string host = address, port = "25565";
        if (address.StartsWith('['))
        {
            int close = address.IndexOf(']'); host = address[..(close+1)];
            if (close+1 < address.Length) port = address[(close+2)..];
        }
        else if (address.LastIndexOf(':') is int separator && separator >= 0)
        { host = address[..separator]; port = address[(separator+1)..]; }
        return new[] { "--server", host, "--port", port };
    }
}
