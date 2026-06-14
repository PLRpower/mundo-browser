using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public partial class FaviconService : IFaviconService, IDisposable
{
    private const int MaxFaviconDownloadBytes = 4 * 1024 * 1024;
    private const int MaxCachedDomains = 4096;
    private static readonly TimeSpan FailedDomainRetryDelay = TimeSpan.FromMinutes(10);

    private readonly string _faviconsPath;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, string> _domainToRelativePath = [];
    private readonly Dictionary<string, string> _domainToAbsoluteUrl = [];
    private readonly Dictionary<string, int> _domainQuality = [];
    private readonly object _cacheLock = new();

    private const int QualityFallback = 0;
    private const int QualityStandard = 1;
    private const int QualityHighRes = 2;

    public FaviconService()
    {
        _faviconsPath = Path.Combine(AppRuntime.LocalDataDirectory, "Favicons");
        Directory.CreateDirectory(_faviconsPath);

        _httpClient = new HttpClient(new HttpClientHandler { MaxConnectionsPerServer = 4 });
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);

        PreloadCache();
    }

    private readonly Dictionary<string, Task<string?>> _activeResolutions = [];
    private readonly Dictionary<string, DateTime> _failedDomains = [];
    private readonly Dictionary<string, DateTime> _lastForcedResolutions = [];

    public async Task ResolveFaviconAsync(WebView2 wv, TabViewModel tab, bool forceReload = false)
    {
        if (wv.CoreWebView2 == null) return;
        var source = wv.CoreWebView2.Source;
        if (string.IsNullOrEmpty(source)) return;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
            || (sourceUri.Scheme != Uri.UriSchemeHttp && sourceUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(sourceUri.Host))
            return;

        string domain = sourceUri.Host;

        if (forceReload)
        {
            lock (_cacheLock)
            {
                var now = DateTime.UtcNow;
                PruneResolutionState(now);
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
            string? cachedRelativePath = null;
            string? cachedAbsolutePath = null;
            string? fallbackUrl = null;

            lock (_cacheLock)
            {
                PruneResolutionState(DateTime.UtcNow);

                if (_domainToRelativePath.TryGetValue(domain, out var cachedRelative))
                {
                    var absolute = GetAbsoluteFaviconPath(cachedRelative);
                    if (absolute != null)
                    {
                        cachedRelativePath = cachedRelative;
                        cachedAbsolutePath = absolute;
                    }
                }

                // Negative caching: don't retry failed domains too often
                if (cachedAbsolutePath == null && _failedDomains.TryGetValue(domain, out var failedAt))
                {
                    if (DateTime.UtcNow - failedAt < FailedDomainRetryDelay)
                    {
                        fallbackUrl = $"https://www.google.com/s2/favicons?sz=128&domain_url={domain}";
                    }
                    else
                    {
                        _failedDomains.Remove(domain);
                    }
                }
            }

            if (cachedAbsolutePath != null)
            {
                tab.FaviconRelativePath = cachedRelativePath;
                tab.FaviconUrl = cachedAbsolutePath;
                return;
            }

            if (fallbackUrl != null)
            {
                tab.FaviconUrl = fallbackUrl;
                return;
            }
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

        try
        {
            var result = await resolutionTask;
            if (result != null)
            {
                string? relativePath = null;
                lock (_cacheLock)
                {
                    if (Uri.TryCreate(result, UriKind.Absolute, out var resultUri) && resultUri.IsFile)
                        _failedDomains.Remove(domain);
                    _domainToRelativePath.TryGetValue(domain, out relativePath);
                }

                if (relativePath != null)
                    tab.FaviconRelativePath = relativePath;
                tab.FaviconUrl = result;
            }
        }
        finally
        {
            lock (_activeResolutions)
            {
                if (_activeResolutions.TryGetValue(domain, out var activeTask)
                    && ReferenceEquals(activeTask, resolutionTask))
                    _activeResolutions.Remove(domain);
            }
        }
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
        lock (_cacheLock)
        {
            if (_domainToRelativePath.TryGetValue(domain, out var cachedRelative))
            {
                var absolute = GetAbsoluteFaviconPath(cachedRelative);
                if (absolute != null) return absolute;
            }
        }

        // 3. Fallback to Google Favicon Service
        try
        {
            var fallbackUrl = $"https://www.google.com/s2/favicons?sz=128&domain_url={domain}";
            using var response = await _httpClient.GetAsync(fallbackUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Favicon service returned {(int)response.StatusCode}.");

            var fallbackBytes = await ReadImageBytesAsync(response.Content);
            if (fallbackBytes == null)
                throw new InvalidDataException("Favicon response was empty or too large.");

            using var ms = new MemoryStream(fallbackBytes);
            var saved = await SaveFaviconAsync(ms, domain, DetectExtension(fallbackBytes, "png"), QualityFallback);
            if (saved != null) return saved;
        }
        catch { }

        lock (_cacheLock)
            _failedDomains[domain] = DateTime.UtcNow;
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
                var base64 = iconUrl.Substring(commaIdx + 1);
                int maxEncodedLength = ((MaxFaviconDownloadBytes + 2) / 3) * 4;
                if (base64.Length > maxEncodedLength)
                    return null;

                var ext = mimePart.Contains("svg") ? "svg"
                        : mimePart.Contains("png") ? "png"
                        : mimePart.Contains("jpeg") || mimePart.Contains("jpg") ? "jpg"
                        : mimePart.Contains("webp") ? "webp"
                        : mimePart.Contains("x-icon") || mimePart.Contains("ico") ? "ico"
                        : "png";
                var bytes = Convert.FromBase64String(base64);
                if (bytes.Length > MaxFaviconDownloadBytes)
                    return null;

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
            
            var bytes = await ReadImageBytesAsync(response.Content);
            if (bytes == null) return null;
            
            using var ms = new MemoryStream(bytes);
            return await SaveFaviconAsync(ms, domain, ext, QualityHighRes);
        }
        catch { return null; }
    }

    private void PruneResolutionState(DateTime now)
    {
        if (_lastForcedResolutions.Count > 512)
        {
            foreach (var staleDomain in _lastForcedResolutions
                         .Where(entry => now - entry.Value > TimeSpan.FromMinutes(15))
                         .Select(entry => entry.Key)
                         .ToList())
                _lastForcedResolutions.Remove(staleDomain);

            TrimOldestEntries(_lastForcedResolutions, 512);
        }

        if (_failedDomains.Count > 512)
        {
            foreach (var staleDomain in _failedDomains
                         .Where(entry => now - entry.Value > FailedDomainRetryDelay)
                         .Select(entry => entry.Key)
                         .ToList())
                _failedDomains.Remove(staleDomain);

            TrimOldestEntries(_failedDomains, 512);
        }
    }

    private static void TrimOldestEntries(Dictionary<string, DateTime> entries, int maxCount)
    {
        if (entries.Count <= maxCount)
            return;

        foreach (var key in entries
                     .OrderBy(entry => entry.Value)
                     .Take(entries.Count - maxCount)
                     .Select(entry => entry.Key)
                     .ToList())
            entries.Remove(key);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
