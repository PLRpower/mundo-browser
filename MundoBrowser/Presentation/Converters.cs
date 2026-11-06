using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Binding = System.Windows.Data.Binding;

namespace MundoBrowser;

public static class Converters
{
    public static readonly IValueConverter NullToBool = new NullToBoolConverter();
    public static readonly IValueConverter FaviconImage = new FaviconImageSourceConverter();
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public sealed class FaviconImageSourceConverter : IValueConverter
{
    private const int MaxCacheEntries = 512;
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        if (Cache.TryGetValue(url, out var cached))
            return cached;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (Cache.Count > MaxCacheEntries)
        {
            foreach (var key in Cache.Keys.Take(MaxCacheEntries / 4))
                Cache.TryRemove(key, out _);
        }

        try
        {
            if (uri.IsFile)
            {
                if (!File.Exists(uri.LocalPath))
                    return null;

                using var stream = File.Open(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 32;
                bmp.DecodePixelHeight = 32;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                Cache[url] = bmp;
                return bmp;
            }

            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 32;
                bmp.DecodePixelHeight = 32;
                bmp.UriSource = uri;
                bmp.EndInit();
                Cache[url] = bmp;
                return bmp;
            }
        }
        catch
        {
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
