namespace MundoBrowser.Services;

public class AdBlockerService : MundoBrowser.Interfaces.IAdBlockerService
{
    private readonly MundoBrowser.Interfaces.IAppSettingsService _settingsService;
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

    public AdBlockerService(MundoBrowser.Interfaces.IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        _isAdBlockerEnabled = settingsService.Current.IsAdBlockerEnabled;
        _isCookieBlockerEnabled = settingsService.Current.IsCookieBlockerEnabled;
    }

    private static readonly string[] BlockedDomainList =
    [
        "doubleclick.net", "googleadservices.com", "googlesyndication.com",
        "adsystem.com", "adservice.google.com", "criteo.com", "taboola.com",
        "outbrain.com", "ads.yahoo.com", "adnxs.com", "amazon-adsystem.com",
        "analytics.twitter.com", "pixel.facebook.com", "connect.facebook.net",
        "google-analytics.com", "googletagmanager.com", "mc.yandex.ru"
    ];

    public IReadOnlyCollection<string> BlockedDomains => BlockedDomainList;

    public string GetCosmeticCss()
    {
        if (!IsAdBlockerEnabled) return "";

        // Universal cosmetic hiding selectors for common ad containers
        return @"
            .ad-container, .ad-banner, .advertisement, 
            [id^='div-gpt-ad'], .adsbygoogle,
            .sponsor-post, .sponsored-content, 
            [data-ad-slot], [id^='google_ads_iframe'],
            .outbrain-tm, .taboola-tm
            { display: none !important; }
        ";
    }

    public string GetCookieCosmeticCss()
    {
        if (!IsCookieBlockerEnabled) return "";

        // Selectors to hide cookie banners and consent popups
        return @"
            #cookie-notice, #cookie-banner, .cookie-banner, .cookie-consent,
            #qc-cmp2-container, #onetrust-consent-sdk, .cc-window,
            #didomi-host, #sp_message_container, .fc-consent-root,
            [id^='cookie-law'], .cookie-law,
            [id^='tarteaucitron'], #usercentrics-root
            { display: none !important; }
        ";
    }

    public string GetCookieRemovalScript()
    {
        if (!IsCookieBlockerEnabled) return "";

        // Advanced: Sometimes hiding the banner leaves a backdrop that blocks clicks.
        // This script aggressively removes known cookie backdrops from the DOM.
        return @"
            (function() {
                const removeCookieModals = () => {
                    const selector = '#qc-cmp2-container, #onetrust-consent-sdk, #didomi-host, ' +
                                     '.fc-consent-root, #usercentrics-root, [id^=""tarteaucitron""]';
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
        ";
    }
}
