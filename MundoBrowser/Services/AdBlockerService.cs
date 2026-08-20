using System.Collections.Frozen;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public class AdBlockerService : IAdBlockerService
{
    private static readonly FrozenSet<string> BlockedDomainSet = new[]
    {
        "doubleclick.net", "googleadservices.com", "googlesyndication.com",
        "adsystem.com", "adservice.google.com", "criteo.com", "taboola.com",
        "outbrain.com", "ads.yahoo.com", "adnxs.com", "amazon-adsystem.com",
        "analytics.twitter.com", "pixel.facebook.com", "connect.facebook.net",
        "google-analytics.com", "googletagmanager.com", "mc.yandex.ru"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IAppSettingsService _settingsService;
    private bool _isAdBlockerEnabled;
    private bool _isCookieBlockerEnabled;

    public bool IsAdBlockerEnabled
    {
        get => _isAdBlockerEnabled;
        set
        {
            if (_isAdBlockerEnabled == value) return;
            _isAdBlockerEnabled = value;
            _settingsService.Update(settings => settings.IsAdBlockerEnabled = value);
        }
    }

    public bool IsCookieBlockerEnabled
    {
        get => _isCookieBlockerEnabled;
        set
        {
            if (_isCookieBlockerEnabled == value) return;
            _isCookieBlockerEnabled = value;
            _settingsService.Update(settings => settings.IsCookieBlockerEnabled = value);
        }
    }

    public AdBlockerService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        _isAdBlockerEnabled = settingsService.Current.IsAdBlockerEnabled;
        _isCookieBlockerEnabled = settingsService.Current.IsCookieBlockerEnabled;
    }

    public IReadOnlyCollection<string> BlockedDomains => BlockedDomainSet;

    public string? GetSiteHost(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    public bool IsAdBlockerEnabledForSite(string? url)
    {
        if (!IsAdBlockerEnabled) return false;
        string? host = GetSiteHost(url);
        if (host == null) return IsAdBlockerEnabled;

        return !_settingsService.Current.AdBlockDisabledSites.Contains(
            host,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool SetAdBlockerEnabledForSite(string? url, bool enabled)
    {
        string? host = GetSiteHost(url);
        if (host == null) return false;

        _settingsService.Update(settings =>
        {
            settings.AdBlockDisabledSites.RemoveAll(
                site => site.Equals(host, StringComparison.OrdinalIgnoreCase));

            if (!enabled)
                settings.AdBlockDisabledSites.Add(host);
        });

        return true;
    }

    public bool IsCookieBlockerEnabledForSite(string? url)
    {
        if (!IsCookieBlockerEnabled) return false;
        string? host = GetSiteHost(url);
        if (host == null) return IsCookieBlockerEnabled;

        return !_settingsService.Current.CookieBlockDisabledSites.Contains(
            host,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool SetCookieBlockerEnabledForSite(string? url, bool enabled)
    {
        string? host = GetSiteHost(url);
        if (host == null) return false;

        _settingsService.Update(settings =>
        {
            settings.CookieBlockDisabledSites.RemoveAll(
                site => site.Equals(host, StringComparison.OrdinalIgnoreCase));

            if (!enabled)
                settings.CookieBlockDisabledSites.Add(host);
        });

        return true;
    }

    public bool IsProtectionDisabledForSite(string? url)
    {
        string? host = GetSiteHost(url);
        if (host == null) return false;

        return !IsAdBlockerEnabledForSite(url) && !IsCookieBlockerEnabledForSite(url);
    }

    public bool SetProtectionDisabledForSite(string? url, bool disabled)
    {
        string? host = GetSiteHost(url);
        if (host == null) return false;

        SetAdBlockerEnabledForSite(url, !disabled);
        SetCookieBlockerEnabledForSite(url, !disabled);
        return true;
    }

    public string GetCosmeticCss()
    {
        if (!IsAdBlockerEnabled) return string.Empty;

        return """
            .ad-container, .ad-banner, .advertisement, 
            [id^='div-gpt-ad'], .adsbygoogle,
            .sponsor-post, .sponsored-content, 
            [data-ad-slot], [id^='google_ads_iframe'],
            .outbrain-tm, .taboola-tm
            { display: none !important; }
            """;
    }

    public string GetCookieCosmeticCss()
    {
        if (!IsCookieBlockerEnabled) return string.Empty;

        return """
            #cookie-notice, #cookie-banner, .cookie-banner, .cookie-consent,
            #qc-cmp2-container, #onetrust-consent-sdk, .cc-window,
            #didomi-host, #sp_message_container, .fc-consent-root,
            [id^='cookie-law'], .cookie-law,
            [id^='tarteaucitron'], #usercentrics-root
            { display: none !important; }
            """;
    }

    public string GetCookieRemovalScript()
    {
        if (!IsCookieBlockerEnabled) return string.Empty;

        return """
            (function() {
                const removeCookieModals = () => {
                    const selector = '#qc-cmp2-container, #onetrust-consent-sdk, #didomi-host, ' +
                                     '.fc-consent-root, #usercentrics-root, [id^="tarteaucitron"]';
                    document.querySelectorAll(selector).forEach(el => el.remove());
                    
                    // Force body scroll unlock
                    if (document.body && document.body.style.overflow === 'hidden') {
                        document.body.style.overflow = '';
                        document.body.style.position = '';
                    }
                };
                
                // Run once now and retry during an idle period for lazy-loaded banners.
                removeCookieModals();
                const retry = () => removeCookieModals();
                if ('requestIdleCallback' in window) {
                    requestIdleCallback(retry, { timeout: 2000 });
                } else {
                    setTimeout(retry, 1000);
                }
            })();
            """;
    }
}
