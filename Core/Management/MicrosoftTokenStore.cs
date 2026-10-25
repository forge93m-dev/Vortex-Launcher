using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using VortexLauncher.Core.Launch;

namespace VortexLauncher.Core.Management;

/// <summary>Encrypts a secret so only this Windows user can read it back. Other systems have no protector, so nothing is kept on disk.</summary>
public interface ISecretProtector
{
    byte[]? Protect(byte[] data);
    byte[]? Unprotect(byte[] data);
}

/// <summary>Windows DPAPI (current user). Used directly through crypt32 so no extra package is needed.</summary>
public sealed class WindowsSecretProtector : ISecretProtector
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr handle);
    private const int UiForbidden = 0x1;

    public static bool IsSupported => OperatingSystem.IsWindows();
    public byte[]? Protect(byte[] data) => Run(data, protect: true);
    public byte[]? Unprotect(byte[] data) => Run(data, protect: false);

    private static byte[]? Run(byte[] data, bool protect)
    {
        if (!IsSupported) return null;
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            Blob output;
            bool ok = protect
                ? CryptProtectData(ref input, "Vortex Launcher", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok) return null;
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            LocalFree(output.Data);
            return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}

/// <summary>
/// The long-lived Microsoft refresh tokens, one per player, encrypted for the current user and kept in config/.
/// Access tokens are never written to disk. Without a protector (or if it fails) nothing is stored and the player signs in again next time.
/// </summary>
public sealed class MicrosoftTokenStore
{
    private readonly string _path;
    private readonly ISecretProtector? _protector;

    public MicrosoftTokenStore(string? configDirectory = null, ISecretProtector? protector = null)
    {
        _path = Path.Combine(configDirectory ?? Path.Combine(PlatformSupport.DataRoot, "config"), "microsoft-tokens.json");
        _protector = protector ?? (WindowsSecretProtector.IsSupported ? new WindowsSecretProtector() : null);
    }

    /// <summary>False when this system cannot protect the token, so it is kept for this session only.</summary>
    public bool CanPersist => _protector != null;

    private Dictionary<string, string> Load() =>
        SafeJsonStore.Read<Dictionary<string, string>>(_path, map => map.All(pair => pair.Key.Length == 36 && pair.Value.Length < 16384)).Value ?? new();

    public bool Save(string uuid, string refreshToken)
    {
        if (_protector == null || string.IsNullOrWhiteSpace(refreshToken) || !Guid.TryParse(uuid, out _)) return false;
        byte[]? sealedBytes = _protector.Protect(Encoding.UTF8.GetBytes(refreshToken));
        if (sealedBytes == null) return false;
        var map = Load();
        map[uuid.ToLowerInvariant()] = Convert.ToBase64String(sealedBytes);
        SafeJsonStore.Write(_path, map);
        return true;
    }

    public string? Get(string uuid)
    {
        if (_protector == null || !Load().TryGetValue(uuid.ToLowerInvariant(), out var text)) return null;
        try
        {
            byte[]? plain = _protector.Unprotect(Convert.FromBase64String(text));
            return plain == null ? null : Encoding.UTF8.GetString(plain);
        }
        catch (FormatException) { return null; }
    }

    public void Remove(string uuid)
    {
        var map = Load();
        if (map.Remove(uuid.ToLowerInvariant())) SafeJsonStore.Write(_path, map);
    }
}
