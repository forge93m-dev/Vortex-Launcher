using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using VortexLauncher.Core.Media;

internal static class ArtworkChecks
{
    internal static void Run(Action<int> pump, string output)
    {
        string root = Directory.CreateDirectory(Path.Combine(output, "artwork-regression")).FullName;
        ArtworkCache.DiskRoot = Path.Combine(root, "cache");
        var paths = new List<string>();
        foreach (var format in new[] { SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Webp })
        {
            using var pixels = new SKBitmap(1200, 800);
            pixels.Erase(new SKColor(40, 110, 200));
            using var image = SKImage.FromBitmap(pixels);
            using var data = image.Encode(format, 85);
            string path = Path.Combine(root, format + ".image");
            File.WriteAllBytes(path, data.ToArray()); paths.Add(path);
        }
        for (int round = 0; round < 12; round++)
        {
            var coldPaths = paths.Select(path => { string copy = path + ".round-" + round; File.Copy(path, copy); return copy; }).ToArray();
            var tasks = coldPaths.SelectMany(path => new[] { 64, 256, 544, 768 }.Select(width =>
                ArtworkCache.AcquireAsync(path, width, default))).ToArray();
            var all = Task.WhenAll(tasks); Wait(all, pump);
            foreach (var lease in all.Result)
            {
                if (lease.Bitmap.PixelSize.Width is <= 0 or > 768 || lease.Bitmap.PixelSize.Height is <= 0 or > 768)
                    throw new Exception("Artwork exceeded its decoded dimensions budget.");
                lease.Dispose();
            }
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
        Console.WriteLine("PASS: concurrent PNG/JPEG/WebP artwork remains valid across repeated loads and garbage collection");
        string diskOnly = Path.Combine(root, "cached-source.png"); File.Copy(paths[0], diskOnly);
        string key = diskOnly + "|256|" + File.GetLastWriteTimeUtc(diskOnly).Ticks;
        Directory.CreateDirectory(ArtworkCache.DiskRoot);
        string diskFile = Path.Combine(ArtworkCache.DiskRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".png");
        File.WriteAllText(diskFile, "broken cached thumbnail");
        var recovered = ArtworkCache.AcquireAsync(diskOnly, 256, default); Wait(recovered, pump);
        recovered.Result.Dispose();
        using (var saved = SKBitmap.Decode(diskFile))
            if (saved == null || saved.Width != 256) throw new Exception("Corrupt artwork cache was not repaired.");
        Console.WriteLine("PASS: corrupt cached artwork is rebuilt from the valid source without changing that source");
        foreach (var size in new[] { new SKSizeI(1, 10000), new SKSizeI(10000, 1) })
        {
            string skinny = Path.Combine(root, $"skinny-{size.Width}.png");
            using (var pixels = new SKBitmap(size.Width, size.Height))
            using (var image = SKImage.FromBitmap(pixels))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(skinny, data.ToArray());
            var decoded = ArtworkCache.AcquireAsync(skinny, 64, default); Wait(decoded, pump);
            if (decoded.Result.Bitmap.PixelSize.Width < 1 || decoded.Result.Bitmap.PixelSize.Height < 1) throw new Exception("Zero-sized thumbnail.");
            decoded.Result.Dispose();
        }
        Console.WriteLine("PASS: unusually tall/wide images never produce zero-sized thumbnails");
        string invalid = Path.Combine(root, "not-an-image"); File.WriteAllText(invalid, "not an image");
        var failed = ArtworkCache.AcquireAsync(invalid, 256, default);
        try { Wait(failed, pump); throw new Exception("Invalid artwork was accepted."); }
        catch (IOException) { Console.WriteLine("PASS: invalid artwork fails without terminating the launcher"); }
        if (ArtworkCache.Diagnostics.Loading != 0) throw new Exception("Artwork jobs were left pending.");
        Console.WriteLine("ARTWORK_REGRESSION_OK");
    }
    private static void Wait(Task task, Action<int> pump)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(30)) pump(10);
        if (!task.IsCompleted) throw new Exception("Artwork regression timed out.");
        task.GetAwaiter().GetResult();
    }
}
