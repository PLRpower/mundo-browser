using System.IO;
using System.Net.Http;
using MundoBrowser.Helpers;

namespace MundoBrowser.Services;

public partial class FaviconService
{
    private void PreloadCache()
    {
        if (!Directory.Exists(_faviconsPath)) return;
        foreach (var file in Directory.EnumerateFiles(_faviconsPath, "*.*"))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".png" or ".ico" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".svg")) continue;

            var fileName = Path.GetFileNameWithoutExtension(file);
            int quality = QualityStandard;

            if (fileName.Contains(".q"))
            {
                var parts = fileName.Split(".q");
                if (parts.Length > 1 && int.TryParse(parts[1], out var parsedQuality))
                {
                    quality = parsedQuality;
                    fileName = parts[0];
                }
            }

            var domain = Uri.UnescapeDataString(fileName).Replace('_', '.');
            var relativePath = $"Favicons/{Path.GetFileName(file)}";

            if (!_domainQuality.TryGetValue(domain, out var existingQuality) || quality > existingQuality)
            {
                if (!_domainQuality.ContainsKey(domain) && _domainQuality.Count >= MaxCachedDomains)
                    continue;

                _domainToRelativePath[domain] = relativePath;
                _domainToAbsoluteUrl[domain] = new Uri(file).AbsoluteUri;
                _domainQuality[domain] = quality;
            }
        }
    }

    public string? GetAbsoluteFaviconPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath;
        var fullPath = Path.Combine(AppRuntime.LocalDataDirectory, relativePath);
        return File.Exists(fullPath) ? new Uri(fullPath).AbsoluteUri : null;
    }

    public string? GetFaviconUrlForPage(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            return null;

        return GetCachedFaviconUrlForPage(pageUrl)
               ?? $"https://www.google.com/s2/favicons?sz=64&domain_url={Uri.EscapeDataString(uri.GetLeftPart(UriPartial.Authority))}";
    }

    public string? GetCachedFaviconUrlForPage(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            return null;

        lock (_cacheLock)
        {
            if (_domainToAbsoluteUrl.TryGetValue(uri.Host, out var cachedUrl))
                return cachedUrl;

            if (_domainToRelativePath.TryGetValue(uri.Host, out var cachedRelativePath))
            {
                cachedUrl = GetAbsoluteFaviconPath(cachedRelativePath);
                if (cachedUrl != null)
                {
                    _domainToAbsoluteUrl[uri.Host] = cachedUrl;
                    return cachedUrl;
                }
            }
        }

        return null;
    }

    private async Task<string?> SaveFaviconAsync(Stream stream, string domain, string extension, int quality)
    {
        try
        {
            lock (_cacheLock)
            {
                if (_domainQuality.TryGetValue(domain, out var currentQuality) && quality < currentQuality)
                    return null;
            }

            if (stream.CanSeek)
                stream.Position = 0;

            var bytes = await ReadLimitedBytesAsync(stream);
            if (bytes == null)
                return null;

            var saved = await Task.Run(() => SaveFaviconToDisk(bytes, domain, extension, quality));
            if (saved == null)
                return null;

            lock (_cacheLock)
            {
                EnsureCacheCapacity(domain);
                _domainToRelativePath[domain] = saved.Value.RelativePath;
                _domainToAbsoluteUrl[domain] = new Uri(saved.Value.FullPath).AbsoluteUri;
                _domainQuality[domain] = saved.Value.Quality;
            }

            return new Uri(saved.Value.FullPath).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private (string FullPath, string RelativePath, int Quality)? SaveFaviconToDisk(
        byte[] bytes,
        string domain,
        string extension,
        int quality)
    {
        string? temporaryPath = null;
        try
        {
            if (extension == "svg")
            {
                using var svgStream = new MemoryStream(bytes);
                var svgDoc = Svg.SvgDocument.Open<Svg.SvgDocument>(svgStream);
                if (svgDoc == null)
                    return null;

                using var bitmap = svgDoc.Draw(128, 128);
                using var pngStream = new MemoryStream();
                bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
                bytes = pngStream.ToArray();
                extension = "png";
                quality = QualityHighRes;
            }

            var safeDomain = domain.Replace('.', '_');
            var fileName = $"{safeDomain}.q{quality}.{extension}";
            var fullPath = Path.Combine(_faviconsPath, fileName);

            temporaryPath = fullPath + ".tmp";
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, fullPath, overwrite: true);
            temporaryPath = null;

            if (Directory.Exists(_faviconsPath))
            {
                foreach (var oldFile in Directory.EnumerateFiles(_faviconsPath, $"{safeDomain}*"))
                {
                    if (string.Equals(oldFile, fullPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var oldFileName = Path.GetFileName(oldFile);
                    if (oldFileName.StartsWith(safeDomain + ".q") || oldFileName.StartsWith(safeDomain + "."))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }
                }
            }

            return (fullPath, $"Favicons/{fileName}", quality);
        }
        catch
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
            return null;
        }
    }

    private static string GetExtensionFromContentType(string contentType) => contentType switch
    {
        "image/svg+xml" => "svg",
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/webp" => "webp",
        "image/x-icon" or "image/vnd.microsoft.icon" or "image/ico" => "ico",
        "image/bmp" => "bmp",
        "image/gif" => "gif",
        _ => "png"
    };

    private static string DetectExtension(byte[] bytes, string fallback)
    {
        if (bytes.Length < 4) return fallback;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "png";
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return "jpg";
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return "webp";
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "gif";
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return "bmp";
        if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) return "ico";
        return fallback;
    }

    private static async Task<byte[]?> ReadImageBytesAsync(HttpContent content)
    {
        if (content.Headers.ContentLength is > MaxFaviconDownloadBytes)
            return null;

        await using var input = await content.ReadAsStreamAsync();
        return await ReadLimitedBytesAsync(input, content.Headers.ContentLength);
    }

    private static async Task<byte[]?> ReadLimitedBytesAsync(Stream input, long? expectedLength = null)
    {
        if (input.CanSeek && input.Length - input.Position > MaxFaviconDownloadBytes)
            return null;

        using var output = new MemoryStream(
            expectedLength is > 0
                ? (int)Math.Min(expectedLength.Value, MaxFaviconDownloadBytes)
                : 0);
        var buffer = new byte[81920];
        int total = 0;

        while (true)
        {
            int read = await input.ReadAsync(buffer);
            if (read == 0)
                break;

            total += read;
            if (total > MaxFaviconDownloadBytes)
                return null;

            await output.WriteAsync(buffer.AsMemory(0, read));
        }

        return total == 0 ? null : output.ToArray();
    }

    private void EnsureCacheCapacity(string domain)
    {
        if (_domainQuality.ContainsKey(domain))
            return;

        while (_domainQuality.Count >= MaxCachedDomains)
        {
            var domainToRemove = _domainQuality.Keys.First();
            _domainQuality.Remove(domainToRemove);
            _domainToRelativePath.Remove(domainToRemove);
            _domainToAbsoluteUrl.Remove(domainToRemove);
        }
    }
}
