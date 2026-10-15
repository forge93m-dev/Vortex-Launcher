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
        MaxConnectionsPerServer = 24,
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
            if (!await IsValidAsync(cached, sha1, size, token, validateArchive))
            {
                for (int attempt = 0; ; attempt++)
                {
                    string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".part";
                    long attemptBytes = 0;
                    try
                    {
                        using var response = await (httpClient ?? Client).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                        response.EnsureSuccessStatusCode();
                        long? expectedLength = size ?? response.Content.Headers.ContentLength;
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                        await using (var input = await response.Content.ReadAsStreamAsync(token))
                        await using (var output = new FileStream(temporary, FileMode.CreateNew,
                            FileAccess.Write, FileShare.None, 81920, true))
                        {
                            byte[] buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
                            try
                            {
                                int count;
                                while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
                                {
                                    hash.AppendData(buffer, 0, count);
                                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                                    attemptBytes += count;
                                    received?.Invoke(count);
                                }
                            }
                            finally { ArrayPool<byte>.Shared.Return(buffer); }
                        }
                        string actualHash = Convert.ToHexString(hash.GetHashAndReset());
                        if (attemptBytes == 0 || (expectedLength.HasValue && attemptBytes != expectedLength.Value) ||
                            (!string.IsNullOrEmpty(sha1) && !actualHash.Equals(sha1, StringComparison.OrdinalIgnoreCase)))
                            throw new IOException("Downloaded file failed verification: " + url);
                        token.ThrowIfCancellationRequested();
                        if (validateArchive && !await IsValidAsync(temporary, null, null, token, true))
                            throw new IOException("Downloaded library is not a readable JAR archive.");
                        File.Move(temporary, cached, true);
                        Remember(cached, actualHash);
                        break;
                    }
                    catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested &&
                        (ex is HttpRequestException || ex is IOException || ex is TaskCanceledException))
                    {
                        received?.Invoke(-attemptBytes);
                        await Task.Delay(300 * (attempt + 1), token);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            token.ThrowIfCancellationRequested();
            string localTemporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var input = new FileStream(cached, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, true))
                await using (var output = new FileStream(localTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024, true))
                    await input.CopyToAsync(output, 256 * 1024, token);
                token.ThrowIfCancellationRequested();
                File.Move(localTemporary, destination, true);
                if (Verified.TryGetValue(cached, out var checkedFile)) Remember(destination, checkedFile.Sha1);
            }
            finally
            {
                if (File.Exists(localTemporary)) File.Delete(localTemporary);
            }
        }
        finally { gate.Release(); }
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
