using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Platform;
using SkiaSharp;

namespace VortexLauncher.Core.Media;

internal static class ArtworkCache
{
    private const long Budget = 16 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly object DecodeGate = new();
    private static readonly Dictionary<string, Entry> Entries = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task>> Pending = new();
    private static readonly SemaphoreSlim Workers = new(4);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    internal static string DiskRoot { get; set; } = Path.Combine(VortexLauncher.Core.Launch.PlatformSupport.DataRoot, "cache", "artwork");
    private static int _diskWrites;
    private sealed class Entry(Bitmap bitmap) { public Bitmap Bitmap = bitmap; public long Bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4; public int Pins; public long Used; }
    internal sealed class Lease : IDisposable
    {
        private readonly Entry _entry;
        private bool _disposed;
        public Bitmap Bitmap => _entry.Bitmap;
        private Lease(Entry entry) { _entry = entry; }
        public void Dispose() { lock (Gate) { if (_disposed) return; _disposed = true; _entry.Pins--; Trim(); } }
        internal static Lease Pin(object entry) { var item = (Entry)entry; item.Pins++; item.Used = Environment.TickCount64; return new(item); }
    }
    internal static (int Count, long Bytes, int Loading) Diagnostics { get { lock (Gate) return (Entries.Count, Entries.Values.Sum(e => e.Bytes), Pending.Count); } }
    public static async Task<Lease> AcquireAsync(string path, int width, CancellationToken token)
    {
        width = Math.Clamp(width, 32, 768);
        string key = await Task.Run(() => path + "|" + width + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0), token);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (Gate) if (Entries.TryGetValue(key, out var hit)) return Lease.Pin(hit);
            await Pending.GetOrAdd(key, _ => new Lazy<Task>(() => LoadAsync(key, path, width))).Value.WaitAsync(token);
        }
    }
    private static async Task LoadAsync(string key, string path, int width)
    {
        await Workers.WaitAsync();
        try
        {
            lock (Gate) if (Entries.ContainsKey(key)) return;
            Bitmap bitmap = await Task.Run(async () =>
            {
                string disk = Path.Combine(DiskRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".png");
                if (File.Exists(disk))
                {
                    try { using var cached = File.OpenRead(disk); return Decode(cached, width); }
                    catch (Exception ex) when (ex is IOException or ArgumentException) { }
                }
                using var input = new MemoryStream();
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new IOException("Image is too large.");
                    await using var file = File.OpenRead(path); await file.CopyToAsync(input);
                }
                else
                {
                    if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new IOException("Unsupported artwork URL.");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    var buffer = new byte[81920]; int read;
                    while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
                    { if (input.Length + read > 8 * 1024 * 1024) throw new IOException("Artwork download is too large."); input.Write(buffer, 0, read); }
                }
                input.Position = 0;
                var decoded = Decode(input, width);
                try
                {
                    Directory.CreateDirectory(DiskRoot);
                    string temporary = disk + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { decoded.Save(temporary); File.Move(temporary, disk, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    if (Interlocked.Increment(ref _diskWrites) % 32 == 0) TrimDisk(disk);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                return decoded;
            });
            lock (Gate) { Entries[key] = new(bitmap) { Used = Environment.TickCount64 }; Trim(key); }
        }
        finally { Workers.Release(); Pending.TryRemove(key, out _); }
    }
    private static void Trim(string? keep = null)
    {
        long bytes = Entries.Values.Sum(e => e.Bytes);
        foreach (var pair in Entries.Where(p => p.Value.Pins == 0 && p.Key != keep).OrderBy(p => p.Value.Used).ToArray())
        {
            if (bytes <= Budget && Entries.Count <= 64) break;
            Entries.Remove(pair.Key); bytes -= pair.Value.Bytes; pair.Value.Bitmap.Dispose();
        }
    }
    private static Bitmap Decode(Stream stream, int width)
    {
        // Avoid Avalonia 11.0's scaled-codec path (GetScaledDimensions), which
        // crashes natively for concurrent mixed-format artwork. Own one codec
        // over immutable data and copy decoded pixels into Avalonia instead.
        // Serialize decoding and cap source pixels to bound transient memory.
        lock (DecodeGate)
        {
            if (!stream.CanSeek || stream.Length > 20 * 1024 * 1024) throw new IOException("Artwork image is too large.");
            stream.Position = 0;
            using var managed = new SKManagedStream(stream, false);
            using var data = SKData.Create(managed) ?? throw new IOException("Unsupported artwork image.");
            using var codec = SKCodec.Create(data) ?? throw new IOException("Unsupported artwork image.");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || info.Width > 65535 || info.Height > 65535 ||
                (long)info.Width * info.Height > 16 * 1024 * 1024)
                throw new IOException("Unsupported artwork dimensions (maximum 16 megapixels).");
            var pixelsInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var pixels = new SKBitmap(pixelsInfo);
            if (pixels.GetPixels() == IntPtr.Zero || codec.GetPixels(pixelsInfo, pixels.GetPixels()) != SKCodecResult.Success)
                throw new IOException("Artwork image could not be decoded.");
            double scale = Math.Min(1d, Math.Min((double)width / info.Width, 768d / info.Height));
            int targetWidth = Math.Max(1, (int)Math.Round(info.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(info.Height * scale));
            using var thumbnail = pixels.Resize(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.Medium)
                ?? throw new IOException("Artwork image could not be resized.");
            // This constructor copies pixels; no native codec/stream outlives this method.
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, thumbnail.GetPixels(),
                new PixelSize(targetWidth, targetHeight), new Vector(96, 96), thumbnail.RowBytes);
        }
    }
    private static void TrimDisk(string keep)
    {
        var files = new DirectoryInfo(DiskRoot).EnumerateFiles("*.png").Where(f => f.Name.Length == 68 && f.Name[..64].All(Uri.IsHexDigit)
            && (f.Attributes & FileAttributes.ReparsePoint) == 0).OrderBy(f => f.LastWriteTimeUtc).ToArray();
        long total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= 48 * 1024 * 1024) break;
            if (file.FullName == keep) continue;
            try { long length = file.Length; file.Delete(); total -= length; } catch (IOException) { }
        }
    }
}
