namespace MundoBrowser.Interfaces;

public interface IAdBlockerService
{
    bool IsAdBlockerEnabled { get; set; }
    bool IsCookieBlockerEnabled { get; set; }

    bool ShouldBlockRequest(string url);
    string GetCosmeticCss();
    string GetCookieCosmeticCss();
    string GetCookieRemovalScript();
}
