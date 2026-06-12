namespace MundoBrowser.Interfaces;

public interface IAdBlockerService
{
    bool IsAdBlockerEnabled { get; set; }
    bool IsCookieBlockerEnabled { get; set; }
    IReadOnlyCollection<string> BlockedDomains { get; }

    string GetCosmeticCss();
    string GetCookieCosmeticCss();
    string GetCookieRemovalScript();
}
