namespace MundoBrowser.Interfaces;

public interface IAdBlockerService
{
    bool IsAdBlockerEnabled { get; set; }
    bool IsCookieBlockerEnabled { get; set; }
    IReadOnlyCollection<string> BlockedDomains { get; }

    string? GetSiteHost(string? url);
    bool IsProtectionDisabledForSite(string? url);
    bool SetProtectionDisabledForSite(string? url, bool disabled);
    string GetCosmeticCss();
    string GetCookieCosmeticCss();
    string GetCookieRemovalScript();
}
