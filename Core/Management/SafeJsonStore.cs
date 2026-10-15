using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace VortexLauncher.Core.Management;

// The last valid document is backed up; a failed write never truncates the live file.
internal static class SafeJsonStore
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    internal sealed record Result<T>(T? Value, bool Recovered, bool Damaged, string Notice) where T : class;

    public static Result<T> Read<T>(string path, Func<T, bool>? validate = null) where T : class
    {
        lock (Gates.GetOrAdd(Path.GetFullPath(path), _ => new object()))
        {
            if (TryRead(path, validate, out T? value)) return new(value, false, false, "");
            if (TryRead(path + ".bak", validate, out value))
            {
                try { Write(path, value!, validate); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { return new(value, true, true, "Using backup; could not restore " + Path.GetFileName(path) + ": " + ex.Message); }
                return new(value, true, false, "Restored " + Path.GetFileName(path) + " from backup.");
            }
            bool damaged = File.Exists(path) || File.Exists(path + ".bak");
            return new(null, false, damaged, damaged ? Path.GetFileName(path) + " is unreadable. Original files were kept." : "");
        }
    }

    public static void Write<T>(string path, T value, Func<T, bool>? validate = null) where T : class
    {
        if (validate != null && !validate(value)) throw new InvalidDataException("Refusing to save invalid metadata.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Metadata document is too large.");
        lock (Gates.GetOrAdd(Path.GetFullPath(path), _ => new object()))
        {
            CheckFile(path); CheckFile(path + ".bak");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
                { stream.Write(bytes); stream.Flush(true); }
                if (!File.Exists(path)) File.Move(temporary, path);
                else if (TryRead(path, validate, out T? _)) File.Replace(temporary, path, path + ".bak");
                else
                {
                    // Never rotate a damaged primary over the last good backup.
                    File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"), false);
                    File.Move(temporary, path, true);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static bool TryRead<T>(string path, Func<T, bool>? validate, out T? value) where T : class
    {
        value = null;
        try
        {
            CheckFile(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024) return false;
            using var stream = File.OpenRead(path);
            value = JsonSerializer.Deserialize<T>(stream, Options);
            return value != null && (validate == null || validate(value));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }

    private static void CheckFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Metadata symlinks are not supported: " + Path.GetFileName(path));
    }
}
