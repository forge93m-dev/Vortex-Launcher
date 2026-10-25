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
    // Mojang's asset CDN throttles each HTTP/2 connection; parallel HTTP/1.1 connections
    // measured 73-109 MB/s against 43-67 MB/s over HTTP/2 (Tests/DownloadSmoke --benchmark-network).
    }) { Timeout = TimeSpan.FromMinutes(5), DefaultRequestVersion = HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private sealed record VerifiedFile(long Length, long Written, long Created, string Sha1, bool ArchiveValidated);
    internal static long ArchiveValidationPasses;
    private static readonly ConcurrentDictionary<string, VerifiedFile> Verified = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    internal static string Root => Path.Combine(PlatformSupport.DataRoot, "cache", "downloads");

    public static async Task EnsureAsync(string url, string destination,
        CancellationToken token = default, string? sha1 = null, long? size = null,
        Action<long>? received = null, HttpClient? httpClient = null, string? cacheRoot = null, bool validateArchive = false)
    {
        token.ThrowIfCancellationRequested();
        if (await IsValidAsync(destination, sha1, size, token, validateArchive)) return;
        if (cacheRoot == null && httpClient == null && IsSmallSharedFile(destination, sha1, size, validateArchive))
        {
            await DownloadSmallSharedAsync(url, destination, sha1!, size!.Value, received, token);
            return;
        }
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
                Remember(cached, actualHash, validateArchive);
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
                if (Verified.TryGetValue(cached, out var checkedFile)) Remember(destination, checkedFile.Sha1, checkedFile.ArchiveValidated);
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

    internal const int SmallFileLimit = 16 * 1024 * 1024;
    internal static long DirectSmallDownloads;
    // Tests serve the direct path from a fixture handler instead of the network.
    internal static HttpClient? DirectClientOverride { get; set; }

    // Checksummed files up to 16 MB in the shared, content-addressed store (assets and shared
    // libraries) skip the download cache: a second cached copy, a lock file and a copy per file
    // cost more than the transfer itself, and the shared store already deduplicates them.
    // A failed transfer restarts the file; larger files keep the resumable cache path.
    internal static bool IsSmallSharedFile(string destination, string? sha1, long? size, bool validateArchive)
    {
        if (sha1?.Length != 40 || size is not (> 0 and <= SmallFileLimit) || validateArchive) return false;
        string root = Path.GetFullPath(SharedGameFiles.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(destination).StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static async Task DownloadSmallSharedAsync(string url, string destination, string sha1, long size, Action<long>? received, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            long reported = 0;
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                using var response = await (DirectClientOverride ?? Client).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                DiskSpace.EnsureAvailable(destination, size);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(size >= 256 * 1024 ? 256 * 1024 : 64 * 1024);
                try
                {
                    long written = 0;
                    await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, true))
                    {
                        int count;
                        while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                        {
                            if (written + count > size) throw new InvalidDataException("Download exceeds its declared size.");
                            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                            hash.AppendData(buffer, 0, count);
                            written += count; reported += count; received?.Invoke(count);
                        }
                    }
                    if (written != size) throw new IOException("Download ended before its declared size.");
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Download checksum failed.");
                // Verified before it is published: the file appears whole under its final name or not at all.
                File.Move(temporary, destination, true);
                Remember(destination, sha1);
                Interlocked.Increment(ref DirectSmallDownloads);
                return;
            }
            catch (IOException ex) when (DiskSpace.IsFull(ex))
            {
                received?.Invoke(-reported);
                throw new DiskSpaceException(destination, inner: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                received?.Invoke(-reported);
                if (token.IsCancellationRequested || attempt >= 2) throw;
                await Task.Delay(300 * (attempt + 1), token);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
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
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        long length = info.Length;
        if (length == 0 || (size.HasValue && length != size.Value)) return false;
        if (Verified.TryGetValue(path, out var verified) && verified.Length == length &&
            verified.Written == info.LastWriteTimeUtc.Ticks && verified.Created == info.CreationTimeUtc.Ticks &&
            (!validateArchive || verified.ArchiveValidated) &&
            (string.IsNullOrEmpty(sha1) || verified.Sha1.Equals(sha1, StringComparison.OrdinalIgnoreCase))) return true;
        if (validateArchive)
        {
            try
            {
                Interlocked.Increment(ref ArchiveValidationPasses);
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
        if (string.IsNullOrEmpty(sha1)) { if (validateArchive) Remember(path, "", true); return true; }
        await using var stream = File.OpenRead(path);
        string hash = Convert.ToHexString(await SHA1.HashDataAsync(stream, token));
        bool valid = hash.Equals(sha1, StringComparison.OrdinalIgnoreCase);
        if (valid) Remember(path, hash, validateArchive);
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

    private static void Remember(string path, string hash, bool archiveValidated = false)
    {
        var info = new FileInfo(path);
        Verified[path] = new VerifiedFile(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks, hash, archiveValidated);
    }

    internal static void ForgetVerification(string directory)
    {
        AssetVerification.Invalidate(directory);
        string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string path in Verified.Keys)
            if (path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                Verified.TryRemove(path, out _);
    }
}
