using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public class AdBlockerService : IAdBlockerService
{
    private static readonly string[] DefaultBlockedDomainList =
    [
        // Google & DoubleClick
        "doubleclick.net", "googleadservices.com", "googlesyndication.com",
        "adservice.google.com", "googleads.g.doubleclick.net", "pubads.g.doubleclick.net",
        "static.doubleclick.net", "pagead2.googlesyndication.com", "ads.google.com",
        "admob.com", "2mdn.net", "invitemedia.com", "googletagservices.com", "partnerad.l.google.com",

        // Major DSP / SSP / Exchanges
        "criteo.com", "criteo.net", "taboola.com", "outbrain.com", "adnxs.com",
        "amazon-adsystem.com", "ads.yahoo.com", "advertising.com", "rubiconproject.com",
        "pubmatic.com", "openx.net", "openx.com", "casalemedia.com", "indexexchange.com",
        "smartadserver.com", "smartclip.net", "teads.tv", "zemanta.com", "serving-sys.com",
        "tribalfusion.com", "revcontent.com", "mgid.com", "bidswitch.net", "yieldmo.com",
        "media.net", "infolinks.com", "sovrn.com", "triplelift.com", "sharethrough.com",
        "undertone.com", "gumgum.com", "kargo.com", "nativo.com", "connatix.com",
        "unruly.co", "exponential.com", "zedo.com", "adroll.com", "adtech.de",
        "adform.net", "adtechus.com", "adblade.com", "bidtheatre.com", "districtm.io",
        "e-planning.net", "lijit.com", "mathtag.com", "pubmine.com", "quantserve.com",
        "quantcount.com", "scorecardresearch.com", "simpli.fi", "smaato.net", "sonobi.com",
        "spotx.tv", "stickyads.tv", "synacormedia.com", "yieldlab.net", "yieldoptimizer.com",
        "richaudience.com", "seedtag.com", "seedtag.net", "taboola.net", "outbrain.net",
        "ad-delivery.net", "adkernel.com", "adlooxtracking.com", "adnexus.com", "adspirit.de",
        "adzerk.net", "atdmt.com", "bidvertiser.com", "buysellads.com", "carbonads.net",

        // Analytics & Tracking & Telemetry
        "google-analytics.com", "googletagmanager.com", "analytics.twitter.com", "ads-twitter.com",
        "pixel.facebook.com", "connect.facebook.net", "an.facebook.com", "ads.pinterest.com",
        "ct.pinterest.com", "ads.tiktok.com", "analytics.tiktok.com", "ads.linkedin.com",
        "px.ads.linkedin.com", "mc.yandex.ru", "hotjar.com", "clarity.ms", "branch.io",
        "adjust.com", "appsflyer.com", "segment.io", "segment.com", "mixpanel.com",
        "fullstory.com", "mouseflow.com", "crazyegg.com", "nr-data.net", "optimizely.com",
        "chartbeat.com", "kissmetrics.com", "statcounter.com", "inspectlet.com", "loggly.com",
        "luckyorange.com", "woopra.com", "leadforensics.com", "alexametrics.com",
        "heapanalytics.com", "amplitude.com", "bugsnag.com",

        // Mobile & Video Ad SDKs
        "applovin.com", "unityads.unity3d.com", "vungle.com", "chartboost.com", "inmobi.com",
        "adcolony.com", "flurry.com", "ironsrc.com", "supersonicads.com", "moatads.com",
        "integralads.com", "iasds01.com", "innovid.com", "spotxchange.com", "tremorhub.com",
        "liverail.com", "springserve.com", "freewheel.tv", "conversantmedia.com", "adikteev.com",
        "fyber.com", "ogury.io", "mintegral.com", "liftoff.io", "tapjoy.com", "vungle-cdn.com",

        // Popups, Redirects & Malvertising
        "popads.net", "propellerads.com", "adcash.com", "exoclick.com", "juicyads.com",
        "clickadu.com", "hilltopads.com", "trafficstars.com", "zeropark.com", "popcash.net",
        "adsterra.com", "ad-maven.com", "yllix.com", "trafficjunky.com", "ero-advertising.com",
        "richads.com", "pushwoosh.com", "onesignal.com", "clickcease.com", "adplexity.com",
        "twinred.com", "trafficfactory.biz", "plugrush.com", "realsrv.com", "tsyndicate.com",
        "dtiserv2.com",

        // Cryptominers
        "coinhive.com", "coin-hive.com", "cryptoloot.pro", "minr.pw", "webminepool.com",
        "coin-have.com", "coinhiveproxy.com"
    ];

    private static readonly string[] DefaultBlockedPathPatternList =
    [
        "*://*.youtube.com/pagead/*",
        "*://*.youtube.com/api/stats/ads*",
        "*://*.youtube.com/ptracking*",
        "*://*.youtube.com/youtubei/v1/att/get*",
        "*://*.youtube.com/get_midroll_info*",
        "*://*.youtube-nocookie.com/pagead/*",
        "*://*.youtube-nocookie.com/api/stats/ads*"
    ];

    private static readonly FrozenSet<string> CookieConsentDomains = new string[]
    {
        "cookielaw.org", "onetrust.com", "optanon.blob.core.windows.net",
        "didomi.io", "privacy-center.org", "cookiebot.com", "cookiebot.eu",
        "usercentrics.eu", "usercentrics.com", "consentmanager.net",
        "consensu.org", "quantcast.com", "trustarc.com", "truste.com",
        "axept.io", "sirdata.com", "sddan.com", "privacy-mgmt.com",
        "sourcepoint.com", "iubenda.com", "cookieyes.com", "termly.io",
        "osano.com", "fundingchoicesmessages.google.com", "consent.google.com"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IAppSettingsService _settingsService;
    private volatile FrozenSet<string> _activeBlockedDomains;
    private bool _isAdBlockerEnabled;
    private bool _isCookieBlockerEnabled;

    public event Action? BlockedDomainsUpdated;

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

        _activeBlockedDomains = DefaultBlockedDomainList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        // Load cached domains and refresh remote lists asynchronously in the background
        Task.Run(async () =>
        {
            try
            {
                await UpdateRemoteBlocklistsAsync(force: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdBlockerService] Background blocklist update error: {ex.Message}");
            }
        });
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _blockedHostCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> BlockedDomains => _activeBlockedDomains;

    public IReadOnlyCollection<string> BlockedPathPatterns => DefaultBlockedPathPatternList;

    public async Task UpdateRemoteBlocklistsAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var remoteDomains = await AdBlockListDownloader.LoadCachedOrDownloadedDomainsAsync(force, cancellationToken).ConfigureAwait(false);
        if (remoteDomains.Count > 0)
        {
            var merged = new HashSet<string>(DefaultBlockedDomainList, StringComparer.OrdinalIgnoreCase);
            foreach (var domain in remoteDomains)
            {
                merged.Add(domain);
            }
            _activeBlockedDomains = merged.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            _blockedHostCache.Clear();

            BlockedDomainsUpdated?.Invoke();
        }
    }

    public bool ShouldBlockUrl(string? url, bool isCookieBlockerActive = true)
    {
        if (!IsAdBlockerEnabled || string.IsNullOrWhiteSpace(url)) return false;

        // Fast prefix check to avoid allocating Uri for data/blob/file/about schemes
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        string host = uri.IdnHost.TrimEnd('.');
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        if (!isCookieBlockerActive && IsDomainInSet(host, CookieConsentDomains))
            return false;

        if (_blockedHostCache.TryGetValue(host, out bool isBlocked))
            return isBlocked;

        if (IsDomainInSet(host, _activeBlockedDomains))
        {
            _blockedHostCache[host] = true;
            return true;
        }

        if (host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase))
        {
            string path = uri.AbsolutePath;
            if (path.StartsWith("/pagead/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/stats/ads", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/ptracking", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/youtubei/v1/att/get", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/get_midroll_info", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        _blockedHostCache[host] = false;
        return false;
    }

    private static bool IsDomainInSet(string host, FrozenSet<string> domainSet)
    {
        string currentHost = host;
        while (!string.IsNullOrEmpty(currentHost))
        {
            if (domainSet.Contains(currentHost))
                return true;

            int dotIndex = currentHost.IndexOf('.');
            if (dotIndex < 0 || dotIndex == currentHost.Length - 1)
                break;

            currentHost = currentHost[(dotIndex + 1)..];
        }
        return false;
    }

    private static bool IsSiteMatch(string host, string site)
    {
        return host.Equals(site, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("." + site, StringComparison.OrdinalIgnoreCase) ||
               site.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSiteDisabled(string host, List<string> disabledSites)
    {
        for (int i = 0; i < disabledSites.Count; i++)
        {
            if (IsSiteMatch(host, disabledSites[i]))
                return true;
        }
        return false;
    }

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

        return !IsSiteDisabled(host, _settingsService.Current.AdBlockDisabledSites);
    }

    public bool SetAdBlockerEnabledForSite(string? url, bool enabled)
    {
        string? host = GetSiteHost(url);
        if (host == null) return false;

        _settingsService.Update(settings =>
        {
            settings.AdBlockDisabledSites.RemoveAll(site => IsSiteMatch(host, site));

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

        return !IsSiteDisabled(host, _settingsService.Current.CookieBlockDisabledSites);
    }

    public bool SetCookieBlockerEnabledForSite(string? url, bool enabled)
    {
        string? host = GetSiteHost(url);
        if (host == null) return false;

        _settingsService.Update(settings =>
        {
            settings.CookieBlockDisabledSites.RemoveAll(site => IsSiteMatch(host, site));

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
            /* General Web Ad Banners & Containers */
            .ad-container, .ad-banner, .advertisement, 
            [id^='div-gpt-ad'], .adsbygoogle,
            .sponsor-post, .sponsored-content, 
            [data-ad-slot], [id^='google_ads_iframe'],
            .outbrain-tm, .taboola-tm,
            .ad-slot, .ad-wrapper, .ad_box, .banner-ad,
            .pub_300x250, .pub_728x90, .ad-header, .ad-sidebar,

            /* YouTube UI & Banner Ads */
            ytd-promoted-video-renderer,
            ytd-display-ad-renderer,
            ytd-statement-banner-renderer,
            ytd-banner-promo-renderer,
            ytd-in-feed-ad-layout-renderer,
            ytd-ad-slot-renderer,
            ytd-rich-item-renderer:has(.ytd-ad-slot-renderer),
            ytd-rich-section-renderer:has(#sparkles-container),
            ytd-reel-video-renderer:has(.ytd-ad-slot-renderer),
            #masthead-ad,
            #player-ads,
            #panels:has(ytd-ads-engagement-panel-content-view-model),
            .ytp-ad-overlay-container,
            .ytp-ad-message-container,
            .ytp-ad-action-interstitial,
            tp-yt-paper-dialog:has(ytd-enforcement-message-view-model)
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
                    const modals = document.querySelectorAll(selector);
                    if (modals.length === 0) return;
                    modals.forEach(el => el.remove());
                    
                    // Force body scroll unlock only when a cookie modal was actually removed
                    if (document.body && document.body.style.overflow === 'hidden') {
                        document.body.style.overflow = '';
                        document.body.style.position = '';
                    }
                };
                
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

    public string GetYouTubeAdBlockScript()
    {
        return """
            (function() {
                'use strict';
                const host = window.location.hostname || '';
                if (!host.includes('youtube.com') && !host.includes('youtube-nocookie.com')) return;
                if (window.__mundoYtAdBlockInjected) return;
                window.__mundoYtAdBlockInjected = true;

                function cleanYouTubeAds() {
                    try {
                        const player = document.querySelector('#movie_player, .html5-video-player');
                        const video = document.querySelector('video.html5-main-video, #movie_player video, video');
                        
                        // Check if player is showing an ad
                        const isAd = player && (
                            player.classList.contains('ad-showing') ||
                            player.classList.contains('ad-interrupting') ||
                            document.querySelector('.ytp-ad-player-overlay, .ytp-ad-preview-container') !== null
                        );

                        if (isAd && video) {
                            video.muted = true;
                            video.playbackRate = 16.0;
                            if (Number.isFinite(video.duration) && video.duration > 0) {
                                video.currentTime = video.duration;
                            }
                        }

                        // Auto-click skip buttons immediately
                        const skipButtons = document.querySelectorAll(
                            '.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button, ' +
                            '.ytp-ad-skip-button-slot button, button.ytp-ad-skip-button-modern, ' +
                            '.ytp-ad-overlay-close-button, .ytp-ad-image-overlay .ytp-ad-overlay-close-button'
                        );
                        for (let i = 0; i < skipButtons.length; i++) {
                            try { skipButtons[i].click(); } catch(e) {}
                        }

                        // Dismiss anti-adblock enforcement dialogs and resume playback
                        const enforcementDialog = document.querySelector('tp-yt-paper-dialog:has(ytd-enforcement-message-view-model), ytd-enforcement-message-view-model');
                        if (enforcementDialog) {
                            const parentDialog = enforcementDialog.closest('tp-yt-paper-dialog') || enforcementDialog;
                            parentDialog.remove();
                            document.querySelectorAll('tp-yt-iron-overlay-backdrop').forEach(b => b.remove());
                            if (video && video.paused) {
                                video.play().catch(() => {});
                            }
                        }
                    } catch (e) {}
                }

                // Observe DOM mutations on player & dialogs instead of 150ms tight polling loop
                const observer = new MutationObserver(() => cleanYouTubeAds());
                const observeTarget = () => {
                    const target = document.querySelector('#movie_player') || document.body;
                    if (target) {
                        observer.observe(target, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
                    }
                };

                if (document.body) observeTarget();
                else document.addEventListener('DOMContentLoaded', observeTarget, { once: true });

                // Event-driven triggers for video playback & SPA transitions
                document.addEventListener('play', (e) => {
                    if (e.target && e.target.tagName === 'VIDEO') cleanYouTubeAds();
                }, true);

                // Low-frequency fallback safety check (1s), paused when tab is in background
                setInterval(() => {
                    if (document.visibilityState === 'visible') {
                        cleanYouTubeAds();
                    }
                }, 1000);

                window.addEventListener('yt-navigate-finish', cleanYouTubeAds);
                window.addEventListener('spfdone', cleanYouTubeAds);
                window.addEventListener('load', cleanYouTubeAds);
            })();
            """;
    }

    public string GetInjectionScript()
    {
        bool adBlockGlobal = IsAdBlockerEnabled;
        bool cookieBlockGlobal = IsCookieBlockerEnabled;
        if (!adBlockGlobal && !cookieBlockGlobal)
            return string.Empty;

        string serializedAdCss = JsonSerializer.Serialize(adBlockGlobal ? GetCosmeticCss() : string.Empty);
        string serializedCookieCss = JsonSerializer.Serialize(cookieBlockGlobal ? GetCookieCosmeticCss() : string.Empty);
        string serializedAdDisabled = JsonSerializer.Serialize(_settingsService.Current.AdBlockDisabledSites);
        string serializedCookieDisabled = JsonSerializer.Serialize(_settingsService.Current.CookieBlockDisabledSites);
        string ytScript = adBlockGlobal ? GetYouTubeAdBlockScript() : string.Empty;
        string cookieScript = cookieBlockGlobal ? GetCookieRemovalScript() : string.Empty;

        return $$"""
            (function() {
                const normalizeHost = (h) => {
                    if (!h) return '';
                    const clean = h.replace(/\.$/, '').toLowerCase();
                    return clean.startsWith('www.') ? clean.slice(4) : clean;
                };

                const getTopHost = () => {
                    try {
                        if (window.location.ancestorOrigins && window.location.ancestorOrigins.length > 0) {
                            const origin = window.location.ancestorOrigins[window.location.ancestorOrigins.length - 1];
                            if (origin && origin !== 'null') {
                                return normalizeHost(new URL(origin).hostname);
                            }
                        }
                    } catch (e) {}
                    return normalizeHost(window.location.hostname);
                };

                const isHostDisabled = (host, list) => {
                    if (!host || !list) return false;
                    for (let i = 0; i < list.length; i++) {
                        const site = list[i];
                        if (host === site || host.endsWith('.' + site) || site.endsWith('.' + host)) return true;
                    }
                    return false;
                };

                const topHost = getTopHost();
                const selfHost = normalizeHost(window.location.hostname);
                const adDisabledSites = {{serializedAdDisabled}};
                const cookieDisabledSites = {{serializedCookieDisabled}};

                const adBlockActive = {{(adBlockGlobal ? "true" : "false")}} &&
                    !isHostDisabled(topHost, adDisabledSites) &&
                    !isHostDisabled(selfHost, adDisabledSites);
                const cookieBlockActive = {{(cookieBlockGlobal ? "true" : "false")}} &&
                    !isHostDisabled(topHost, cookieDisabledSites) &&
                    !isHostDisabled(selfHost, cookieDisabledSites);

                const injectStyle = (id, cssText) => {
                    if (!cssText || document.getElementById(id)) return;
                    const style = document.createElement('style');
                    style.id = id;
                    style.textContent = cssText;
                    (document.head || document.documentElement).appendChild(style);
                };

                if (adBlockActive) {
                    try {
                        const adCss = {{serializedAdCss}};
                        if (document.documentElement) injectStyle('mundo-adblock-css', adCss);
                        else document.addEventListener('DOMContentLoaded', () => injectStyle('mundo-adblock-css', adCss), { once: true });
                    } catch (e) {}
                    {{ytScript}}
                }

                if (cookieBlockActive) {
                    try {
                        const cookieCss = {{serializedCookieCss}};
                        if (document.documentElement) injectStyle('mundo-cookieblock-css', cookieCss);
                        else document.addEventListener('DOMContentLoaded', () => injectStyle('mundo-cookieblock-css', cookieCss), { once: true });
                    } catch (e) {}
                    {{cookieScript}}
                }
            })();
            """;
    }
}
