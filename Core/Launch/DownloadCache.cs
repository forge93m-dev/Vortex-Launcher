using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VortexLauncher.Core.Launch;

internal static class DownloadCache
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 64,
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    }) { Timeout = TimeSpan.FromMinutes(5), DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private sealed record VerifiedFile(long Length, long Written, long Created, string Sha1);
    private static readonly ConcurrentDictionary<string, VerifiedFile> Verified = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VortexLauncher", "cache", "downloads");

    public static async Task EnsureAsync(string url, string destination,
        CancellationToken token = default, string? sha1 = null, long? size = null,
        Action<long>? received = null, HttpClient? httpClient = null, string? cacheRoot = null, bool validateArchive = false)
    {
        token.ThrowIfCancellationRequested();
        if (await IsValidAsync(destination, sha1, size, token, validateArchive)) return;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            // Only recheck a destination inside the lock if another caller created it.
            if (File.Exists(destination) && await IsValidAsync(destination, sha1, size, token, validateArchive)) return;
            string root = cacheRoot ?? Root;
            Directory.CreateDirectory(root);
            string cached = Path.Combine(root, key);
            await using var processLease = await AcquireProcessLockAsync(cached + ".download-lock", token);
            if (!await IsValidAsync(cached, sha1, size, token, validateArchive))
            {
                if (size.HasValue && !File.Exists(cached + ".partial")) DiskSpace.EnsureAvailable(cached, size.Value);
                string actualHash = await ResumableTransfer.DownloadAsync(httpClient ?? Client, url, cached, sha1, size, validateArchive, received, token);
                Remember(cached, actualHash);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            token.ThrowIfCancellationRequested();
            DiskSpace.EnsureAvailable(destination, new FileInfo(cached).Length);
            string localTemporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await CopyVerifiedFileAsync(cached, localTemporary, token);
                token.ThrowIfCancellationRequested();
                File.Move(localTemporary, destination, true);
                if (Verified.TryGetValue(cached, out var checkedFile)) Remember(destination, checkedFile.Sha1);
            }
            finally
            {
                if (File.Exists(localTemporary)) File.Delete(localTemporary);
            }
        }
        catch (IOException ex) when (DiskSpace.IsFull(ex) && ex is not DiskSpaceException)
        {
            throw new DiskSpaceException(destination, inner: ex);
        }
        finally { gate.Release(); }
    }

    // CopyToAsync rents its working buffer. Disabling FileStream's additional
    // buffers avoids allocating two 256 KB arrays for every tiny asset.
    internal static async Task CopyVerifiedFileAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, true);
        await input.CopyToAsync(output, input.Length >= 1024 * 1024 ? 256 * 1024 : 64 * 1024, token);
    }

    internal static async Task<bool> IsValidAsync(string path, string? sha1, long? size, CancellationToken token, bool validateArchive = false)
    {
        if (!File.Exists(path)) return false;
        long length = new FileInfo(path).Length;
        if (length == 0 || (size.HasValue && length != size.Value)) return false;
        if (validateArchive)
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(path);
                if (archive.Entries.Count == 0) return false;
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    using var entryStream = entry.Open();
                    await entryStream.CopyToAsync(Stream.Null, token);
                }
            }
            catch (System.IO.InvalidDataException) { return false; }
        }
        if (string.IsNullOrEmpty(sha1)) return true;
        var info = new FileInfo(path);
        if (Verified.TryGetValue(path, out var verified) && verified.Length == length &&
            verified.Written == info.LastWriteTimeUtc.Ticks && verified.Created == info.CreationTimeUtc.Ticks &&
            verified.Sha1.Equals(sha1, StringComparison.OrdinalIgnoreCase)) return true;
        await using var stream = File.OpenRead(path);
        string hash = Convert.ToHexString(await SHA1.HashDataAsync(stream, token));
        bool valid = hash.Equals(sha1, StringComparison.OrdinalIgnoreCase);
        if (valid) Remember(path, hash);
        return valid;
    }

    private static async Task<FileStream> AcquireProcessLockAsync(string path, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Download lock is a link.");
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 3000) { await Task.Delay(100, token); }
        }
    }

    private static void Remember(string path, string hash)
    {
        var info = new FileInfo(path);
        Verified[path] = new VerifiedFile(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks, hash);
    }

    internal static void ForgetVerification(string directory)
    {
        string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string path in Verified.Keys)
            if (path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                Verified.TryRemove(path, out _);
    }
}
