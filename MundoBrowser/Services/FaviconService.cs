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
    private readonly HashSet<string> _resolvedDomains = [];
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
                _resolvedDomains.Add(domain);
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

    public async Task ResolveFaviconAsync(WebView2 wv, TabViewModel tab, bool forceReload = false)
    {
        if (wv.CoreWebView2 == null) return;
        var source = wv.CoreWebView2.Source;
        if (string.IsNullOrEmpty(source)) return;

        string domain;
        try { domain = new Uri(source).Host; }
        catch { return; }

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
                resolutionTask = PerformResolveFaviconAsync(wv, domain, forceReload);
                _activeResolutions[domain] = resolutionTask;
            }
        }

        var result = await resolutionTask;
        if (result != null) tab.FaviconUrl = result;
        
        lock (_activeResolutions) { _activeResolutions.Remove(domain); }
    }

    private async Task<string?> PerformResolveFaviconAsync(WebView2 wv, string domain, bool forceReload)
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

        // 2. Try to find a high-res one via script if we didn't get one or if we want to improve it
        if (bestLocalPath == null || forceReload || (_domainQuality.TryGetValue(domain, out var q) && q < QualityHighRes))
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
                let links = Array.from(document.querySelectorAll('link[rel*=""icon""], link[rel*=""apple-touch-icon""]'));
                let best = null;
                let maxSize = 0;
                links.forEach(l => {
                    let size = 0;
                    if (l.sizes && l.sizes.value) {
                        size = parseInt(l.sizes.value.split('x')[0]);
                    } else if (l.rel.includes('apple-touch-icon')) {
                        size = 180;
                    }
                    if (size >= maxSize) {
                        maxSize = size;
                        best = l.href;
                    }
                });
                return best;
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
            var response = await _httpClient.GetAsync(iconUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var ext = GetExtensionFromContentType(contentType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
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

            var safeDomain = domain.Replace('.', '_');
            var fileName = $"{safeDomain}.q{quality}.{extension}";
            var fullPath = Path.Combine(_faviconsPath, fileName);

            // Clean up old files for this domain
            if (Directory.Exists(_faviconsPath))
            {
                foreach (var oldFile in Directory.GetFiles(_faviconsPath, $"{safeDomain}*"))
                {
                    var oldFileName = Path.GetFileName(oldFile);
                    // Match exactly safeDomain.something or safeDomain.qN.something
                    if (oldFileName.StartsWith(safeDomain + ".q") || oldFileName.StartsWith(safeDomain + "."))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }
                }
            }

            using (var fileStream = File.Create(fullPath))
            {
                await stream.CopyToAsync(fileStream);
            }

            var relativePath = $"Favicons/{fileName}";
            _domainToRelativePath[domain] = relativePath;
            _domainQuality[domain] = quality;
            _resolvedDomains.Add(domain);

            return new Uri(fullPath).AbsoluteUri;
        }
        catch { return null; }
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
                    _resolvedDomains.Remove(kvp.Key);
                }
            }
        }
        catch { }
    }
}
