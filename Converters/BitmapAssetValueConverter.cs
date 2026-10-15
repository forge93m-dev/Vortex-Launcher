using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace VortexLauncher.Converters;

public class BitmapAssetValueConverter : IValueConverter
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        if (Cache.TryGetValue(url, out var cachedBitmap))
            return cachedBitmap;

        // Avalonia does not reliably interpret a raw Windows path as an Image source.
        // Load local cover/icon files explicitly so paths such as C:\\...\\icon.png work.
        if (File.Exists(url))
        {
            try
            {
                using var stream = File.OpenRead(url);
                var bitmap = new Bitmap(stream);
                Cache[url] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        // Запускаем асинхронную загрузку в фоновом потоке
        _ = LoadImageAsync(url);
        return null;
    }

    private async Task LoadImageAsync(string url)
    {
        try
        {
            byte[] data = await HttpClient.GetByteArrayAsync(url);
            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);
            Cache[url] = bitmap;
        }
        catch
        {
            // Не удалось загрузить иконку
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
