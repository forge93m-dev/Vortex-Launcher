using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VortexLauncher.Core.Management;

namespace VortexLauncher.Core.Launch;

internal static class ResumableTransfer
{
    internal sealed class State
    {
        public string Url { get; set; } = "";
        public string Hash { get; set; } = "";
        public string ETag { get; set; } = "";
        public long? Length { get; set; }
        public string PrefixHash { get; set; } = "";
        public long PrefixLength { get; set; }
    }
    internal static async Task<string> DownloadAsync(HttpClient client, string url, string cached, string? sha1,
        long? size, bool validateArchive, Action<long>? received, CancellationToken token)
    {
        string partial = cached + ".partial", metadata = cached + ".resume.json";
        if (File.Exists(partial) && (File.GetAttributes(partial) & FileAttributes.ReparsePoint) != 0) throw new IOException("Resume data must not be a link.");
        var state = SafeJsonStore.Read<State>(metadata).Value;
        bool Matches(State? value) => value != null && value.Url == url && string.Equals(value.Hash, sha1 ?? "", StringComparison.OrdinalIgnoreCase)
            && (!size.HasValue || value.Length == size) && (sha1?.Length == 40 || (StrongETag(value.ETag) && value.PrefixHash?.Length == 64 && value.PrefixLength > 0));
        void Reset() { File.Delete(partial); File.Delete(metadata); File.Delete(metadata + ".bak"); state = null; }
        if (!Matches(state) || !File.Exists(partial) || (state!.Length.HasValue && new FileInfo(partial).Length > state.Length)) Reset();
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            long reported = 0;
            try
            {
                long offset = Matches(state) && File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (offset > 0 && sha1?.Length != 40)
                {
                    await using var prefix = File.OpenRead(partial);
                    if (state!.PrefixLength != offset || !Convert.ToHexString(await SHA256.HashDataAsync(prefix, token)).Equals(state.PrefixHash, StringComparison.OrdinalIgnoreCase))
                    { prefix.Close(); Reset(); offset = 0; }
                }
                if (offset > 0 && state!.Length == offset && sha1?.Length == 40 && await DownloadCache.IsValidAsync(partial, sha1, size, token, validateArchive))
                { File.Move(partial, cached, true); File.Delete(metadata); File.Delete(metadata + ".bak"); return sha1; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (offset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                    if (StrongETag(state!.ETag)) request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(state.ETag));
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { Reset(); throw new IOException("Server rejected the saved range; restarting safely."); }
                response.EnsureSuccessStatusCode();
                long? total = size ?? response.Content.Headers.ContentLength;
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (offset == 0 || range == null || range.Unit != "bytes" || range.From != offset || !range.Length.HasValue
                        || range.To != range.Length - 1 || (size.HasValue && range.Length != size)
                        || (state!.Length.HasValue && range.Length != state.Length)
                        || (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != range.To - range.From + 1)
                        || (StrongETag(state.ETag) && response.Headers.ETag?.ToString() != state.ETag))
                    { Reset(); throw new IOException("Invalid or changed range response; restarting safely."); }
                    total = range.Length;
                }
                else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                else { Reset(); throw new IOException("Unsupported download response."); }
                string etag = response.Headers.ETag?.ToString() ?? "";
                bool resumable = total >= 1024 * 1024 && (sha1?.Length == 40 || StrongETag(etag));
                state = new() { Url = url, Hash = sha1 ?? "", ETag = etag, Length = total };
                if (resumable) SafeJsonStore.Write(metadata, state);
                else { File.Delete(metadata); File.Delete(metadata + ".bak"); }
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(total >= 1024 * 1024 ? 256 * 1024 : 64 * 1024);
                try
                {
                    if (offset > 0)
                    {
                        await using var prefix = File.OpenRead(partial);
                        int read;
                        while ((read = await prefix.ReadAsync(buffer, timeout.Token)) > 0) hash.AppendData(buffer, 0, read);
                    }
                    await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                    await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1, true))
                    {
                        int count;
                        while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                        {
                            if (total.HasValue && output.Length + count > total) throw new InvalidDataException("Download exceeds its declared size.");
                            await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                            hash.AppendData(buffer, 0, count); reported += count; received?.Invoke(count);
                        }
                        await output.FlushAsync(timeout.Token);
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
                long actual = new FileInfo(partial).Length;
                if (actual == 0 || (total.HasValue && actual != total)) throw new IOException("Download ended before its declared size.");
                string actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if ((!string.IsNullOrEmpty(sha1) && !actualHash.Equals(sha1, StringComparison.OrdinalIgnoreCase)) ||
                    (validateArchive && !await DownloadCache.IsValidAsync(partial, null, total, token, true)))
                { Reset(); throw new IOException("Download checksum or archive validation failed."); }
                token.ThrowIfCancellationRequested();
                File.Move(partial, cached, true); File.Delete(metadata); File.Delete(metadata + ".bak");
                return actualHash;
            }
            catch (IOException ex) when (DiskSpace.IsFull(ex))
            {
                // Retrying or writing resume metadata on a full disk only hides
                // the original error. Keep already downloaded bytes for retry.
                received?.Invoke(-reported);
                throw new DiskSpaceException(partial, inner: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                received?.Invoke(-reported);
                if (sha1?.Length != 40 && ex is not InvalidDataException && state != null && StrongETag(state.ETag) && File.Exists(metadata) && File.Exists(partial))
                {
                    await using var saved = File.OpenRead(partial);
                    state.PrefixLength = saved.Length;
                    state.PrefixHash = Convert.ToHexString(await SHA256.HashDataAsync(saved));
                    SafeJsonStore.Write(metadata, state);
                }
                if (!Matches(state) || !File.Exists(metadata) || ex is InvalidDataException) Reset();
                if (token.IsCancellationRequested) throw;
                if (attempt >= 2) throw;
                await Task.Delay(300 * (attempt + 1), token);
            }
        }
    }
    private static bool StrongETag(string etag) => EntityTagHeaderValue.TryParse(etag, out var parsed) && !parsed.IsWeak && parsed.Tag != "*";
}
