namespace MundoBrowser.Services;

public class AdBlockerService : MundoBrowser.Interfaces.IAdBlockerService
{
    public bool IsAdBlockerEnabled { get; set; } = true;
    public bool IsCookieBlockerEnabled { get; set; } = true;

    // A lightweight HashSet for quick domain lookups (O(1) complexity)
    private HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);

    public AdBlockerService()
    {
        _ = LoadBlockListsAsync();
    }

    private async Task LoadBlockListsAsync()
    {
        try
        {
            // We use a small, hardcoded list of common ad/tracking domains for the MVP
            // In a production app, this would be downloaded from an EasyList format file.
            var commonAdDomains = new[]
            {
                "doubleclick.net", "googleadservices.com", "googlesyndication.com",
                "adsystem.com", "adservice.google.com", "criteo.com", "taboola.com",
                "outbrain.com", "ads.yahoo.com", "adnxs.com", "amazon-adsystem.com",
                "analytics.twitter.com", "pixel.facebook.com", "connect.facebook.net",
                "google-analytics.com", "googletagmanager.com", "mc.yandex.ru"
            };

            foreach (var domain in commonAdDomains)
            {
                _blockedDomains.Add(domain);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading ad blocker lists: {ex.Message}");
        }
    }

    public bool ShouldBlockRequest(string url)
    {
        if (!IsAdBlockerEnabled) return false;

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                string host = uri.Host;
                
                // Exact match
                if (_blockedDomains.Contains(host)) return true;

                // Subdomain match (e.g., ads.example.com matches example.com if in list)
                var parts = host.Split('.');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    string subHost = string.Join('.', parts.Skip(i));
                    if (_blockedDomains.Contains(subHost)) return true;
                }
            }
        }
        catch { }

        return false;
    }

    public string GetCosmeticCss()
    {
        if (!IsAdBlockerEnabled) return "";

        // Universal cosmetic hiding selectors for common ad containers
        return @"
            .ad-container, .ad-banner, .advertisement, 
            [id^='div-gpt-ad'], [class*='adsbygoogle'], 
            .sponsor-post, .sponsored-content, 
            [data-ad-slot], [id*='google_ads_iframe'],
            .outbrain-tm, .taboola-tm
            { display: none !important; width: 0 !important; height: 0 !important; }
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
            [id^='cookie-law'], [class*='cookie-law'],
            [id*='tarteaucitron'], #usercentrics-root
            { display: none !important; z-index: -1 !important; visibility: hidden !important; }
            
            /* Unblock scrolling if the site locked it behind a modal */
            body { overflow: auto !important; }
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
                    const selectors = [
                        '#qc-cmp2-container', '#onetrust-consent-sdk', 
                        '#didomi-host', '.fc-consent-root', '#usercentrics-root',
                        '[id*=""tarteaucitron""]'
                    ];
                    selectors.forEach(sel => {
                        const els = document.querySelectorAll(sel);
                        els.forEach(el => el.remove());
                    });
                    
                    // Force body scroll unlock
                    if (document.body && document.body.style.overflow === 'hidden') {
                        document.body.style.overflow = '';
                        document.body.style.position = '';
                    }
                };
                
                // Run on load and periodically in case of lazy-loaded banners
                removeCookieModals();
                setTimeout(removeCookieModals, 1000);
                setTimeout(removeCookieModals, 3000);
            })();
        ";
    }
}
