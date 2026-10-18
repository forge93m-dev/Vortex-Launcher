using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace VortexLauncher.Core.Accounts;

/// <summary>Windows per-user DPAPI. Never writes a plaintext token, password or instance-relative credential.</summary>
public sealed class VortexSessionStore
{
    private readonly string _path;
    public bool CanRemember => OperatingSystem.IsWindows();
    internal static string DefaultPath => OperatingSystem.IsWindows()
        ? Path.Combine(VortexLauncher.Core.Launch.PlatformSupport.DataRoot, "config", "launcher-session.dpapi")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vortex", "launcher-session.dpapi");
    public VortexSessionStore() : this(DefaultPath) { }
    internal VortexSessionStore(string path) => _path = Path.GetFullPath(path);
    private void CheckPath()
    {
        for (string? p = _path; p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked session storage is not allowed.");
    }
    public VortexSession? Load()
    {
        if (!CanRemember) return null;
        CheckPath(); if (!File.Exists(_path)) return null;
        if (new FileInfo(_path).Length > 16384) throw new IOException("Saved session is unreadable. The original file was kept.");
        byte[] clear = Protect(File.ReadAllBytes(_path), false);
        try
        {
            var session = JsonSerializer.Deserialize<VortexSession>(clear);
            if (session == null || !VortexAccountClient.ValidToken(session.Token) || session.Profile == null || !Guid.TryParse(session.Profile.Id, out var id) || id==Guid.Empty
                || !VortexAccountClient.ValidLogin(session.Profile.Login) || !VortexAccountClient.ValidNickname(session.Profile.Nickname)) throw new IOException("Saved session is invalid. The original file was kept.");
            return session;
        }
        catch (JsonException) { throw new IOException("Saved session is unreadable. The original file was kept."); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(VortexSession session)
    {
        if (!CanRemember) throw new PlatformNotSupportedException("Remembered Vortex sign-in is currently available on Windows only.");
        CheckPath(); Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(session), encrypted;
        try { encrypted = Protect(clear, true); } finally { CryptographicOperations.ZeroMemory(clear); }
        string temporary = Path.Combine(Path.GetDirectoryName(_path)!, ".session-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllBytes(temporary, encrypted); CheckPath(); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Clear() { CheckPath(); if (File.Exists(_path)) File.Delete(_path); }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet=CharSet.Unicode, SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll", CharSet=CharSet.Unicode, SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Protect(byte[] data,bool encrypt)
    {
        var input = new Blob { Length=data.Length, Data=Marshal.AllocHGlobal(data.Length) };Blob output=default;
        try
        {
            Marshal.Copy(data,0,input.Data,data.Length);
            bool ok=encrypt?CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok || output.Length<1 || output.Length>16384)throw new IOException("Could not access protected session storage. The original file was kept.");
            byte[] result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally
        {
            for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);
            if(output.Data!=IntPtr.Zero){for(int i=0;i<Math.Min(output.Length,16384);i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}
        }
    }
}
