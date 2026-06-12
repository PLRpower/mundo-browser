using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.ViewModels;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public class FaviconService : IFaviconService
{
    private readonly string _faviconsPath;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, string> _domainToRelativePath = [];
    private readonly Dictionary<string, int> _domainQuality = [];

    private const int QualityFallback = 0;
    private const int QualityStandard = 1;
    private const int QualityHighRes = 2;

    public FaviconService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "MundoBrowser");
        _faviconsPath = Path.Combine(appFolder, "Favicons");
        Directory.CreateDirectory(_faviconsPath);

        _httpClient = new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = 4 });
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        PreloadCache();
    }

    private void PreloadCache()
    {
        if (!Directory.Exists(_faviconsPath)) return;
        foreach (var file in Directory.GetFiles(_faviconsPath, "*.*"))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".png" or ".ico" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".svg")) continue;
            
            var fileName = Path.GetFileNameWithoutExtension(file);
            int quality = QualityStandard; // Default for old files

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
                _domainToRelativePath[domain] = relativePath;
                _domainQuality[domain] = quality;
            }
        }
    }

    public string? GetAbsoluteFaviconPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fullPath = Path.Combine(appData, "MundoBrowser", relativePath);
        return File.Exists(fullPath) ? new Uri(fullPath).AbsoluteUri : null;
    }

    private readonly Dictionary<string, Task<string?>> _activeResolutions = [];
    private readonly HashSet<string> _failedDomains = [];
    private readonly Dictionary<string, DateTime> _lastForcedResolutions = [];

    public async Task ResolveFaviconAsync(WebView2 wv, TabViewModel tab, bool forceReload = false)
    {
        if (wv.CoreWebView2 == null) return;
        var source = wv.CoreWebView2.Source;
        if (string.IsNullOrEmpty(source)) return;

        string domain;
        try { domain = new Uri(source).Host; }
        catch { return; }

        if (forceReload)
        {
            lock (_activeResolutions)
            {
                var now = DateTime.UtcNow;
                if (_lastForcedResolutions.TryGetValue(domain, out var lastResolution)
                    && now - lastResolution < TimeSpan.FromSeconds(15))
                {
                    forceReload = false;
                }
                else
                {
                    _lastForcedResolutions[domain] = now;
                }
            }
        }

        if (!forceReload)
        {
            // Check cache
            if (_domainToRelativePath.TryGetValue(domain, out var cachedRelative))
            {
                var absolute = GetAbsoluteFaviconPath(cachedRelative);
                if (absolute != null) { tab.FaviconUrl = absolute; return; }
            }

            // Negative caching: don't retry failed domains too often
            if (_failedDomains.Contains(domain)) return;
        }

        // Avoid concurrent identical resolutions
        Task<string?>? resolutionTask;
        lock (_activeResolutions)
        {
            if (_activeResolutions.TryGetValue(domain, out resolutionTask)) { }
            else
            {
                resolutionTask = PerformResolveFaviconAsync(wv, domain);
                _activeResolutions[domain] = resolutionTask;
            }
        }

        var result = await resolutionTask;
        if (result != null) tab.FaviconUrl = result;
        
        lock (_activeResolutions) { _activeResolutions.Remove(domain); }
    }

    private async Task<string?> PerformResolveFaviconAsync(WebView2 wv, string domain)
    {
        string? bestLocalPath = null;
        
        // 1. Try to get it from WebView2 directly (fastest)
        try
        {
            using var stream = await wv.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream != null)
            {
                var saved = await SaveFaviconAsync(stream, domain, "png", QualityStandard);
                if (saved != null) bestLocalPath = saved;
            }
        }
        catch { }

        // Only inspect the page DOM when WebView2 did not provide a native favicon.
        if (bestLocalPath == null)
        {
            try
            {
                var highResPath = await FetchHighResIconAsync(wv, domain);
                if (highResPath != null) bestLocalPath = highResPath;
            }
            catch { }
        }

        if (bestLocalPath != null) return bestLocalPath;

        // If we have any cached version, prefer it over the fallback
        if (_domainToRelativePath.TryGetValue(domain, out var cachedRelative))
        {
            var absolute = GetAbsoluteFaviconPath(cachedRelative);
            if (absolute != null) return absolute;
        }

        // 3. Fallback to Google Favicon Service
        try
        {
            var fallbackUrl = $"https://www.google.com/s2/favicons?sz=128&domain_url={domain}";
            var fallbackBytes = await _httpClient.GetByteArrayAsync(fallbackUrl);
            using var ms = new MemoryStream(fallbackBytes);
            var saved = await SaveFaviconAsync(ms, domain, DetectExtension(fallbackBytes, "png"), QualityFallback);
            if (saved != null) return saved;
        }
        catch { }

        _failedDomains.Add(domain);
        return $"https://www.google.com/s2/favicons?sz=128&domain_url={domain}";
    }

    private async Task<string?> FetchHighResIconAsync(WebView2 wv, string domain)
    {
        string script = @"
            (function() {
                try {
                    let links = Array.from(document.querySelectorAll('link[rel~=""icon""], link[rel~=""apple-touch-icon""], link[rel=""shortcut icon""]'));
                    let best = null;
                    let maxScore = -1;
                    
                    links.forEach(l => {
                        let score = 0;
                        let href = l.href || l.getAttribute('href');
                        if (!href) return;
                        
                        let type = l.type || '';
                        let isSvg = type === 'image/svg+xml' || href.split('?')[0].toLowerCase().endsWith('.svg');
                        let isApple = l.rel && l.rel.toLowerCase().includes('apple-touch-icon');
                        
                        if (l.sizes && l.sizes.length > 0 && l.sizes.value && l.sizes.value !== 'any') {
                            let sizeStr = l.sizes.value.toLowerCase().split('x')[0];
                            let s = parseInt(sizeStr);
                            if (!isNaN(s)) score = s;
                        }
                        
                        if (score === 0) {
                            if (isApple) score = 180;
                            else if (isSvg) score = 150;
                            else score = 16;
                        } else {
                            if (isSvg && score < 150) score = 150;
                            if (isApple && score < 180) score = 180;
                        }
                        
                        if (score > maxScore) {
                            maxScore = score;
                            best = href;
                        }
                    });
                    
                    if (best) {
                        return new URL(best, window.location.href).href;
                    }
                    return window.location.origin + '/favicon.ico';
                } catch (e) {
                    return window.location.origin + '/favicon.ico';
                }
            })()";

        var iconUrl = await wv.CoreWebView2.ExecuteScriptAsync(script);
        iconUrl = iconUrl?.Trim('\"');

        if (string.IsNullOrEmpty(iconUrl) || iconUrl == "null") return null;

        if (iconUrl.StartsWith("data:"))
        {
            try
            {
                var commaIdx = iconUrl.IndexOf(',');
                if (commaIdx < 0) return null;
                var mimePart = iconUrl.Substring(5, commaIdx - 5);
                var ext = mimePart.Contains("svg") ? "svg"
                        : mimePart.Contains("png") ? "png"
                        : mimePart.Contains("jpeg") || mimePart.Contains("jpg") ? "jpg"
                        : mimePart.Contains("webp") ? "webp"
                        : mimePart.Contains("x-icon") || mimePart.Contains("ico") ? "ico"
                        : "png";
                var base64 = iconUrl.Substring(commaIdx + 1);
                var bytes = Convert.FromBase64String(base64);
                using var ms = new MemoryStream(bytes);
                return await SaveFaviconAsync(ms, domain, ext, QualityHighRes);
            }
            catch { return null; }
        }

        try
        {
            // Set up a request with basic browser headers to avoid 403 Forbidden on some sites
            using var request = new HttpRequestMessage(HttpMethod.Get, iconUrl);
            request.Headers.Add("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;
            
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var ext = GetExtensionFromContentType(contentType);
            
            // Re-detect extension from URL if contentType is generic
            if (ext == "png" && contentType == "application/octet-stream" && iconUrl.Contains(".ico")) ext = "ico";
            if (ext == "png" && iconUrl.Contains(".svg")) ext = "svg";
            
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;
            
            using var ms = new MemoryStream(bytes);
            return await SaveFaviconAsync(ms, domain, ext, QualityHighRes);
        }
        catch { return null; }
    }

    private async Task<string?> SaveFaviconAsync(Stream stream, string domain, string extension, int quality)
    {
        try
        {
            // Don't overwrite with lower quality
            if (_domainQuality.TryGetValue(domain, out var currentQuality) && quality < currentQuality)
            {
                return null;
            }

            if (stream.CanSeek)
                stream.Position = 0;

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var saved = await Task.Run(() => SaveFaviconToDisk(memoryStream.ToArray(), domain, extension, quality));
            if (saved == null)
                return null;

            _domainToRelativePath[domain] = saved.Value.RelativePath;
            _domainQuality[domain] = saved.Value.Quality;

            return new Uri(saved.Value.FullPath).AbsoluteUri;
        }
        catch { return null; }
    }

    private (string FullPath, string RelativePath, int Quality)? SaveFaviconToDisk(
        byte[] bytes,
        string domain,
        string extension,
        int quality)
    {
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

            if (Directory.Exists(_faviconsPath))
            {
                foreach (var oldFile in Directory.GetFiles(_faviconsPath, $"{safeDomain}*"))
                {
                    var oldFileName = Path.GetFileName(oldFile);
                    if (oldFileName.StartsWith(safeDomain + ".q") || oldFileName.StartsWith(safeDomain + "."))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }
                }
            }

            File.WriteAllBytes(fullPath, bytes);
            return (fullPath, $"Favicons/{fileName}", quality);
        }
        catch
        {
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

    public void CleanupStaleFavicons(HashSet<string> activeDomains)
    {
        try
        {
            foreach (var kvp in _domainToRelativePath.ToList())
            {
                if (!activeDomains.Contains(kvp.Key))
                {
                    var fullPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MundoBrowser", kvp.Value);
                    if (File.Exists(fullPath))
                    {
                        try { File.Delete(fullPath); } catch { }
                    }
                    _domainToRelativePath.Remove(kvp.Key);
                    _domainQuality.Remove(kvp.Key);
                }
            }
        }
        catch { }
    }
}
