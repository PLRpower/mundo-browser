namespace MundoBrowser.Helpers;

public static class SearchEngineHelper
{
    public static string BuildSearchUrl(string query, string? engine, string? customUrl)
    {
        var trimmedQuery = query.Trim();
        var encodedQuery = Uri.EscapeDataString(trimmedQuery);
        var engineKey = (engine ?? "google").ToLowerInvariant();

        return engineKey switch
        {
            "duckduckgo" => $"https://duckduckgo.com/?q={encodedQuery}",
            "qwant" => $"https://www.qwant.com/?q={encodedQuery}",
            "ecosia" => $"https://www.ecosia.org/search?q={encodedQuery}",
            "brave" => $"https://search.brave.com/search?q={encodedQuery}",
            "startpage" => $"https://www.startpage.com/sp/search?query={encodedQuery}",
            "searxng" => $"https://searx.be/search?q={encodedQuery}",
            "custom" => FormatCustomUrl(customUrl, encodedQuery),
            _ => $"https://www.google.com/search?q={encodedQuery}",
        };
    }

    public static string GetEngineHomeUrl(string? engine, string? customUrl = "")
    {
        var engineKey = (engine ?? "google").ToLowerInvariant();

        return engineKey switch
        {
            "duckduckgo" => "https://duckduckgo.com",
            "qwant" => "https://www.qwant.com",
            "ecosia" => "https://www.ecosia.org",
            "brave" => "https://search.brave.com",
            "startpage" => "https://www.startpage.com",
            "searxng" => "https://searx.be",
            "custom" => ExtractHomeUrlFromCustom(customUrl),
            _ => "https://www.google.com",
        };
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
