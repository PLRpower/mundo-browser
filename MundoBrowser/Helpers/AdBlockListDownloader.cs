using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MundoBrowser.Helpers;

/// <summary>
/// Downloads, parses, and caches remote adblock/tracker domain lists.
/// Supports standard Hosts format (0.0.0.0 domain), Adblock format (||domain^), and plain domain lists.
/// </summary>
public static class AdBlockListDownloader
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private const string PrimaryFilterUrl = "https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt";
    private const string FallbackFilterUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";

    private static string CacheFilePath => Path.Combine(AppRuntime.LocalDataDirectory, "adblock_domains_cache.txt");
    private static string MetaFilePath => Path.Combine(AppRuntime.LocalDataDirectory, "adblock_cache_meta.json");

    private static readonly Regex DomainRegex = new(
        @"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> IgnoredHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "localhost.localdomain", "local", "broadcasthost",
        "0.0.0.0", "127.0.0.1", "::1", "ip6-allnodes", "ip6-allrouters",
        "ip6-localhost", "ip6-loopback"
    };

    public record CacheMeta(DateTime LastUpdatedUtc, int DomainCount, string SourceUrl);

    /// <summary>
    /// Loads domains from local cache if fresh, otherwise downloads and updates cache.
    /// </summary>
    public static async Task<HashSet<string>> LoadCachedOrDownloadedDomainsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Try loading from local disk cache first
        if (File.Exists(CacheFilePath))
        {
            try
            {
                var cachedLines = await File.ReadAllLinesAsync(CacheFilePath, cancellationToken).ConfigureAwait(false);
                foreach (var line in cachedLines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && IsValidDomain(trimmed))
                    {
                        domains.Add(trimmed);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdBlockListDownloader] Error reading cache file: {ex.Message}");
            }
        }

        // 2. Check if cache is still fresh (< 24h) and not forced
        bool needsDownload = forceRefresh || domains.Count == 0;
        if (!needsDownload && File.Exists(MetaFilePath))
        {
            try
            {
                string? metaJson = await AtomicFileHelper.ReadAllTextSafeAsync(MetaFilePath, cancellationToken).ConfigureAwait(false);
                if (metaJson != null)
                {
                    var meta = JsonSerializer.Deserialize<CacheMeta>(metaJson);
                    if (meta != null && (DateTime.UtcNow - meta.LastUpdatedUtc).TotalHours < 24)
                    {
                        return domains;
                    }
                    needsDownload = true;
                }
            }
            catch
            {
                needsDownload = true;
            }
        }

        if (needsDownload)
        {
            var downloaded = await DownloadRemoteDomainsAsync(cancellationToken).ConfigureAwait(false);
            if (downloaded.Count > 0)
            {
                domains = downloaded;
            }
        }

        return domains;
    }

    /// <summary>
    /// Downloads and parses the latest blocklist from remote sources, saving to cache.
    /// </summary>
    public static async Task<HashSet<string>> DownloadRemoteDomainsAsync(CancellationToken cancellationToken = default)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string usedUrl = PrimaryFilterUrl;

        try
        {
            domains = await FetchAndParseAsync(PrimaryFilterUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AdBlockListDownloader] Failed to fetch primary blocklist: {ex.Message}. Trying fallback.");
        }

        if (domains.Count == 0)
        {
            try
            {
                usedUrl = FallbackFilterUrl;
                domains = await FetchAndParseAsync(FallbackFilterUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdBlockListDownloader] Failed to fetch fallback blocklist: {ex.Message}");
            }
        }

        if (domains.Count > 0)
        {
            try
            {
                string cacheText = string.Join('\n', domains);
                await AtomicFileHelper.WriteAllTextAtomicAsync(CacheFilePath, cacheText, cancellationToken).ConfigureAwait(false);

                var meta = new CacheMeta(DateTime.UtcNow, domains.Count, usedUrl);
                await AtomicFileHelper.WriteJsonAtomicAsync(MetaFilePath, meta, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdBlockListDownloader] Failed to save cache: {ex.Message}");
            }
        }

        return domains;
    }

    private static async Task<HashSet<string>> FetchAndParseAsync(string url, CancellationToken cancellationToken)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MundoBrowser/1.1");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            string? domain = ParseLine(line);
            if (domain != null && IsValidDomain(domain))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    public static string? ParseLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return null;

        string line = rawLine.Trim();

        // Skip comment lines
        if (line.StartsWith('#') || line.StartsWith('!') || line.StartsWith('[') || line.StartsWith('/'))
            return null;

        // Strip inline comments
        int hashIdx = line.IndexOf('#');
        if (hashIdx >= 0)
            line = line[..hashIdx].Trim();

        if (string.IsNullOrWhiteSpace(line)) return null;

        // 1. Hosts format: "0.0.0.0 domain.com" or "127.0.0.1 domain.com"
        if (line.StartsWith("0.0.0.0") || line.StartsWith("127.0.0.1"))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return NormalizeDomain(parts[1]);
            }
        }

        // 2. Adblock format: "||domain.com^" or "||domain.com^$third-party"
        if (line.StartsWith("||"))
        {
            int endIdx = line.IndexOf('^');
            string domainPart = endIdx > 2 ? line[2..endIdx] : line[2..];
            int slashIdx = domainPart.IndexOf('/');
            if (slashIdx >= 0) domainPart = domainPart[..slashIdx];
            return NormalizeDomain(domainPart);
        }

        // 3. Plain domain
        return NormalizeDomain(line);
    }

    private static string? NormalizeDomain(string domain)
    {
        string norm = domain.Trim().ToLowerInvariant().TrimEnd('.');
        if (norm.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            norm = norm[4..];

        if (IgnoredHosts.Contains(norm))
            return null;

        return norm;
    }

    public static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length is < 3 or > 253)
            return false;

        if (domain.Contains('/') || domain.Contains(':') || domain.Contains(' ') || domain.Contains('*'))
            return false;

        return DomainRegex.IsMatch(domain);
    }
}
