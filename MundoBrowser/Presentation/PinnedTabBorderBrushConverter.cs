using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace MundoBrowser;

public class PinnedTabBorderBrushConverter : IMultiValueConverter
{
    private const int MaxCacheEntries = 256;
    private static readonly ConcurrentDictionary<string, Brush> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, List<PositionedColor>> ExtractedColorsCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ActiveExtractions = new(StringComparer.Ordinal);
    private static readonly SolidColorBrush DefaultBrush;

    static PinnedTabBorderBrushConverter()
    {
        DefaultBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204));
        DefaultBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string faviconUrl = values.ElementAtOrDefault(0) is string fav && fav != DependencyProperty.UnsetValue.ToString() ? fav : string.Empty;
        string pageUrl = values.ElementAtOrDefault(1) is string page && page != DependencyProperty.UnsetValue.ToString() ? page : string.Empty;

        if (string.IsNullOrEmpty(faviconUrl) && string.IsNullOrEmpty(pageUrl))
            return DefaultBrush;

        string cacheKey = faviconUrl + "|" + pageUrl;
        if (Cache.TryGetValue(cacheKey, out var cachedBrush))
            return cachedBrush;

        if (Cache.Count > MaxCacheEntries)
            Cache.Clear();

        if (!string.IsNullOrEmpty(faviconUrl) && ExtractedColorsCache.TryGetValue(faviconUrl, out var preExtracted))
        {
            var brush = CreateBrush(preExtracted);
            if (brush.CanFreeze) brush.Freeze();
            Cache[cacheKey] = brush;
            return brush;
        }

        var fallbackColors = GetFallbackColors(pageUrl);
        var liveBrush = CreateBrush(fallbackColors);

        if (IsLocalFile(faviconUrl))
        {
            Cache[cacheKey] = liveBrush;

            if (ActiveExtractions.TryAdd(faviconUrl, 0))
            {
                Task.Run(() =>
                {
                    try
                    {
                        var colors = ExtractPositionedColors(faviconUrl);
                        if (colors.Count > 0)
                        {
                            ExtractedColorsCache[faviconUrl] = colors;
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                try
                                {
                                    if (!liveBrush.IsFrozen)
                                    {
                                        ApplyColorsToBrush(liveBrush, colors);
                                        if (liveBrush.CanFreeze) liveBrush.Freeze();
                                    }
                                }
                                catch
                                {
                                }
                            });
                        }
                    }
                    finally
                    {
                        ActiveExtractions.TryRemove(faviconUrl, out _);
                    }
                });
            }

            return liveBrush;
        }

        if (liveBrush.CanFreeze) liveBrush.Freeze();
        Cache[cacheKey] = liveBrush;
        return liveBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }

    private static bool IsLocalFile(string faviconUrl)
    {
        return !string.IsNullOrWhiteSpace(faviconUrl)
            && Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri)
            && uri.IsFile
            && File.Exists(uri.LocalPath);
    }

    private static List<PositionedColor> ExtractPositionedColors(string faviconUrl)
    {
        if (!Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri)
            || !uri.IsFile
            || !File.Exists(uri.LocalPath))
            return [];

        try
        {
            using var stream = File.Open(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = stream;
            bitmapImage.DecodePixelWidth = 16;
            bitmapImage.DecodePixelHeight = 16;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            var bitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Bgra32, null, 0);
            bitmap.Freeze();
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            if (width <= 0 || height <= 0)
                return [];

            int stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            var buckets = new Dictionary<int, ColorBucket>();

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                double normY = y / (double)Math.Max(1, height - 1);
                for (int x = 0; x < width; x++)
                {
                    int offset = rowOffset + x * 4;
                    byte b = pixels[offset];
                    byte g = pixels[offset + 1];
                    byte r = pixels[offset + 2];
                    byte a = pixels[offset + 3];
                    byte max = Math.Max(r, Math.Max(g, b));
                    byte min = Math.Min(r, Math.Min(g, b));

                    if (a < 96 || max < 45 || (min > 225 && max - min < 18))
                        continue;

                    int key = (r / 32 << 10) | (g / 32 << 5) | (b / 32);
                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new ColorBucket();
                        buckets[key] = bucket;
                    }

                    bucket.Add(r, g, b, x / (double)Math.Max(1, width - 1), normY);
                }
            }

            var candidates = buckets.Values
                .Select(bucket => bucket.ToCandidate())
                .OrderByDescending(candidate => candidate.Score)
                .ToList();

            var result = new List<ColorCandidate>();
            foreach (var candidate in candidates)
            {
                if (result.All(existing => ColorDistance(existing.Color, candidate.Color) >= 75))
                    result.Add(candidate with { Color = EnsureVisible(candidate.Color) });

                if (result.Count == 4)
                    break;
            }

            return NormalizePositions(result);
        }
        catch
        {
            return [];
        }
    }

    private static List<PositionedColor> GetFallbackColors(string pageUrl)
    {
        string host = string.Empty;
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
            host = uri.Host.ToLowerInvariant();

        if (host.Contains("google."))
        {
            return
            [
                new PositionedColor(Color.FromRgb(66, 133, 244), 0.14, 0.40),
                new PositionedColor(Color.FromRgb(234, 67, 53), 0.66, 0.12),
                new PositionedColor(Color.FromRgb(251, 188, 4), 0.86, 0.58),
                new PositionedColor(Color.FromRgb(52, 168, 83), 0.42, 0.88)
            ];
        }

        if (host.Contains("youtube."))
            return [new PositionedColor(Color.FromRgb(255, 0, 0), 0.5, 0.5)];

        int hash = 17;
        foreach (char character in host)
            hash = hash * 31 + character;

        return [new PositionedColor(FromHsv(Math.Abs(hash % 360), 0.72, 0.9), 0.5, 0.5)];
    }

    private static List<PositionedColor> NormalizePositions(IReadOnlyList<ColorCandidate> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates.Count == 0
                ? []
                : [new PositionedColor(candidates[0].Color, 0.5, 0.5)];
        }

        double minX = candidates.Min(candidate => candidate.X);
        double maxX = candidates.Max(candidate => candidate.X);
        double minY = candidates.Min(candidate => candidate.Y);
        double maxY = candidates.Max(candidate => candidate.Y);
        double rangeX = maxX - minX;
        double rangeY = maxY - minY;

        return candidates
            .Select(candidate => new PositionedColor(
                candidate.Color,
                rangeX < 0.04 ? 0.5 : 0.12 + (candidate.X - minX) / rangeX * 0.76,
                rangeY < 0.04 ? 0.5 : 0.12 + (candidate.Y - minY) / rangeY * 0.76))
            .ToList();
    }

    private static LinearGradientBrush CreateBrush(IReadOnlyList<PositionedColor> positionedColors)
    {
        var brush = new LinearGradientBrush();
        ApplyColorsToBrush(brush, positionedColors);
        return brush;
    }

    private static void ApplyColorsToBrush(LinearGradientBrush brush, IReadOnlyList<PositionedColor> positionedColors)
    {
        brush.GradientStops.Clear();

        if (positionedColors.Count == 0)
        {
            var fallback = Color.FromRgb(0, 122, 204);
            brush.GradientStops.Add(new GradientStop(fallback, 0.0));
            brush.GradientStops.Add(new GradientStop(fallback, 1.0));
            return;
        }

        if (positionedColors.Count == 1)
        {
            var c = positionedColors[0].Color;
            brush.GradientStops.Add(new GradientStop(c, 0.0));
            brush.GradientStops.Add(new GradientStop(c, 1.0));
            return;
        }

        if (positionedColors.Count == 2)
        {
            var c1 = positionedColors[0];
            var c2 = positionedColors[1];
            brush.StartPoint = new Point(c1.X, c1.Y);
            brush.EndPoint = new Point(c2.X, c2.Y);
            brush.GradientStops.Add(new GradientStop(c1.Color, 0.0));
            brush.GradientStops.Add(new GradientStop(c2.Color, 1.0));
            return;
        }

        brush.StartPoint = new Point(0, 0);
        brush.EndPoint = new Point(1, 1);
        for (int i = 0; i < positionedColors.Count; i++)
        {
            double offset = (double)i / (positionedColors.Count - 1);
            brush.GradientStops.Add(new GradientStop(positionedColors[i].Color, offset));
        }
    }

    private static Color EnsureVisible(Color color)
    {
        byte max = Math.Max(color.R, Math.Max(color.G, color.B));
        if (max >= 125) return color;

        double scale = 155d / Math.Max(max, (byte)1);
        return Color.FromRgb(
            (byte)Math.Min(255, color.R * scale),
            (byte)Math.Min(255, color.G * scale),
            (byte)Math.Min(255, color.B * scale));
    }

    private static double ColorDistance(Color first, Color second)
    {
        int red = first.R - second.R;
        int green = first.G - second.G;
        int blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double match = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromRgb(
            (byte)((r + match) * 255),
            (byte)((g + match) * 255),
            (byte)((b + match) * 255));
    }

    private sealed class ColorBucket
    {
        private long _red;
        private long _green;
        private long _blue;
        private double _x;
        private double _y;
        private int _count;

        public void Add(byte red, byte green, byte blue, double x, double y)
        {
            _red += red;
            _green += green;
            _blue += blue;
            _x += x;
            _y += y;
            _count++;
        }

        public ColorCandidate ToCandidate()
        {
            var color = Color.FromRgb(
                (byte)(_red / _count),
                (byte)(_green / _count),
                (byte)(_blue / _count));
            byte max = Math.Max(color.R, Math.Max(color.G, color.B));
            byte min = Math.Min(color.R, Math.Min(color.G, color.B));
            double saturation = max == 0 ? 0 : (max - min) / (double)max;
            return new ColorCandidate(
                color,
                _count * (0.4 + saturation * 1.8),
                _x / _count,
                _y / _count);
        }
    }

    private sealed record PositionedColor(Color Color, double X, double Y);
    private sealed record ColorCandidate(Color Color, double Score, double X, double Y);
}
