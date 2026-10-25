using System;
using System.IO;

namespace VortexLauncher.Core.Launch;

internal sealed class DiskSpaceException : IOException
{
    internal DiskSpaceException(string path, long? required = null, long? available = null, Exception? inner = null)
        : base($"Not enough disk space to write to {path}." +
               (required.HasValue && available.HasValue
                   ? $" Required for this file: {required.Value:N0} bytes; available: {available.Value:N0} bytes." : ""), inner) { }
}

internal static class DiskSpace
{
    internal static bool IsFull(IOException exception)
    {
        int code = exception.HResult & 0xffff;
        return exception is DiskSpaceException ||
            (OperatingSystem.IsWindows() ? code is 112 or 39 : code == 28) ||
            exception.Message.Contains("There is not enough space on the disk", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase);
    }

    // This is an early per-file check, not a reservation: other downloads/processes
    // can still consume space. Actual write failures must also be handled.
    internal static void EnsureAvailable(string path, long required)
    {
        if (required <= 0 || !OperatingSystem.IsWindows()) return;
        string root = Path.GetPathRoot(Path.GetFullPath(path))!;
        // Do not guess availability on network shares or mounted folder volumes.
        if (root.StartsWith(@"\\", StringComparison.Ordinal)) return;
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
             parent != null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) return;
        var drive = new DriveInfo(root);
        long available;
        try { available = drive.AvailableFreeSpace; }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        ValidateAvailable(path, required, available);
    }

    /// <summary>Free bytes on the drive holding <paramref name="path"/>, or null when that cannot be told (network share, no such drive).</summary>
    internal static long? FreeBytes(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            if (root.Length == 0 || root.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    internal static void ValidateAvailable(string path, long required, long available)
    {
        if (required > available) throw new DiskSpaceException(path, required, available);
    }
}
