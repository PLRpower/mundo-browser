using System.Collections.Frozen;

namespace MundoBrowser.Helpers;

public static class SearchEngineHelper
{
    private static readonly FrozenDictionary<string, string> SearchUrlTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["duckduckgo"] = "https://duckduckgo.com/?q={0}",
        ["qwant"] = "https://www.qwant.com/?q={0}",
        ["ecosia"] = "https://www.ecosia.org/search?q={0}",
        ["brave"] = "https://search.brave.com/search?q={0}",
        ["startpage"] = "https://www.startpage.com/sp/search?query={0}",
        ["searxng"] = "https://searx.be/search?q={0}",
        ["google"] = "https://www.google.com/search?q={0}"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> HomeUrlTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["duckduckgo"] = "https://duckduckgo.com",
        ["qwant"] = "https://www.qwant.com",
        ["ecosia"] = "https://www.ecosia.org",
        ["brave"] = "https://search.brave.com",
        ["startpage"] = "https://www.startpage.com",
        ["searxng"] = "https://searx.be",
        ["google"] = "https://www.google.com"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string BuildSearchUrl(string query, string? engine, string? customUrl)
    {
        var trimmedQuery = query.Trim();
        var encodedQuery = Uri.EscapeDataString(trimmedQuery);
        var engineKey = (engine ?? "google").Trim().ToLowerInvariant();

        if (engineKey == "custom")
            return FormatCustomUrl(customUrl, encodedQuery);

        if (SearchUrlTemplates.TryGetValue(engineKey, out var template))
            return string.Format(template, encodedQuery);

        return $"https://www.google.com/search?q={encodedQuery}";
    }

    public static string GetEngineHomeUrl(string? engine, string? customUrl = "")
    {
        var engineKey = (engine ?? "google").Trim().ToLowerInvariant();

        if (engineKey == "custom")
            return ExtractHomeUrlFromCustom(customUrl);

        if (HomeUrlTemplates.TryGetValue(engineKey, out var homeUrl))
            return homeUrl;

        return "https://www.google.com";
    }

    public static string NormalizeSearchEngine(string? engine)
    {
        var key = engine?.Trim().ToLowerInvariant();
        return key switch
        {
            "google" or "duckduckgo" or "qwant" or "ecosia" or "brave" or "startpage" or "searxng" or "custom" => key,
            _ => "google"
        };
    }

    private static string FormatCustomUrl(string? template, string encodedQuery)
    {
        if (string.IsNullOrWhiteSpace(template))
            return $"https://www.google.com/search?q={encodedQuery}";

        var trimmed = template.Trim();
        if (trimmed.Contains("{query}", StringComparison.OrdinalIgnoreCase))
            return trimmed.Replace("{query}", encodedQuery, StringComparison.OrdinalIgnoreCase);
        if (trimmed.Contains("{0}"))
            return string.Format(trimmed, encodedQuery);

        return trimmed.Contains('?') 
            ? $"{trimmed}&q={encodedQuery}" 
            : $"{trimmed}?q={encodedQuery}";
    }

    private static string ExtractHomeUrlFromCustom(string? customUrl)
    {
        if (string.IsNullOrWhiteSpace(customUrl)) return "https://www.google.com";
        var trimmed = customUrl.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }
        return "https://www.google.com";
    }
}
