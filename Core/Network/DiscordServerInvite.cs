using System;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace VortexLauncher.Core.Network;

/// <summary>Untrusted routing hint, never authentication. Compatible with Java ServerInvite.</summary>
public sealed record DiscordServerInvite(string Version, string Address, long Expires, string Nonce)
{
    public bool Matches(string version, string loader) => version == Version && string.Equals(loader, "Fabric", StringComparison.OrdinalIgnoreCase);
    public string Encode()
    {
        if (!Regex.IsMatch(Version, "^[0-9][A-Za-z0-9_.+-]{0,23}$") || !Regex.IsMatch(Nonce, "^[a-f0-9]{16}$")) throw new FormatException("Invalid invitation");
        string payload = Version + "|" + NormalizeAddress(Address) + "|" + Base36(Expires) + "|" + Nonce;
        string result = "vxi1." + Convert.ToBase64String(Encoding.ASCII.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (result.Length > 128) throw new FormatException("Invitation too long");
        return result;
    }
    public static bool TryDecode(string? token, long now, out DiscordServerInvite? invite)
    {
        invite = null;
        try
        {
            if (token == null || token.Length > 128 || !Regex.IsMatch(token, "^vxi1\\.[A-Za-z0-9_-]+$")) return false;
            string data = token[5..].Replace('-', '+').Replace('_', '/');
            data = data.PadRight((data.Length + 3)/4*4, '=');
            var parts = Encoding.ASCII.GetString(Convert.FromBase64String(data)).Split('|');
            if (parts.Length != 4 || !Regex.IsMatch(parts[2], "^[0-9a-z]{1,10}$")) return false;
            long expires = 0;
            foreach (char c in parts[2]) expires = checked(expires*36 + (c <= '9' ? c-'0' : c-'a'+10));
            var candidate = new DiscordServerInvite(parts[0], NormalizeAddress(parts[1]), expires, parts[3]);
            if (expires <= now || expires-now > 1860 || candidate.Encode() != token) return false;
            invite = candidate; return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException) { return false; }
    }
    public static string NormalizeAddress(string address)
    {
        if (address.Length > 253 || address != address.Trim()) throw new FormatException("Invalid server address");
        string host, port = "";
        if (address.StartsWith('['))
        {
            int end = address.IndexOf(']'); if (end < 0) throw new FormatException("Invalid IPv6 address");
            host = address[..(end+1)];
            if (!Regex.IsMatch(host, "^\\[[0-9a-fA-F:]+\\]$") || !IPAddress.TryParse(host[1..^1], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) throw new FormatException("Invalid IPv6 address");
            if (end+1 < address.Length) { if (address[end+1] != ':') throw new FormatException("Invalid port"); port = address[(end+2)..]; if (port.Length == 0) throw new FormatException("Invalid port"); }
        }
        else
        {
            int colon = address.IndexOf(':'); host = colon < 0 ? address : address[..colon];
            if (colon >= 0) { port = address[(colon+1)..]; if (port.Length == 0) throw new FormatException("Invalid port"); }
            foreach (string label in host.Split('.')) if (!Regex.IsMatch(label, "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$")) throw new FormatException("Invalid hostname");
        }
        if (port.Length > 0)
        {
            if (!Regex.IsMatch(port, "^[0-9]{1,5}$") || !int.TryParse(port, out int number) || number < 1 || number > 65535) throw new FormatException("Invalid port");
            port = number.ToString(CultureInfo.InvariantCulture);
        }
        return host.ToLowerInvariant() + (port.Length == 0 ? "" : ":" + port);
    }
    private static string Base36(long value)
    {
        if (value <= 0) throw new FormatException("Invalid expiry");
        string result = "";
        while (value > 0) { result = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)(value%36)] + result; value /= 36; }
        return result;
    }
}
