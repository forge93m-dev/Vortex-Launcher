using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Management;

internal static class VerifiedCopy
{
    internal static async Task FileAsync(Stream source, string target, string operationId, CancellationToken token, string? root = null)
    {
        string boundary = Path.GetFullPath(root ?? Path.GetDirectoryName(target)!);
        if (!Path.GetFullPath(target).StartsWith(boundary.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Copy target escapes its instance.");
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(target)!); directory != null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery will not follow directory links.");
            if (string.Equals(directory.FullName, boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
        }
        if (File.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery will not follow file links.");
            await using var existing = System.IO.File.OpenRead(target);
            var expected = await SHA256.HashDataAsync(source, token);
            var actual = await SHA256.HashDataAsync(existing, token);
            if (!expected.AsSpan().SequenceEqual(actual)) throw new IOException("An existing file differs from the source; it was NOT overwritten: " + Path.GetFileName(target));
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".vortex-" + operationId + ".part";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true))
            { await source.CopyToAsync(output, token); await output.FlushAsync(token); }
            token.ThrowIfCancellationRequested();
            System.IO.File.Move(temporary, target, false);
        }
        finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
    }
}
