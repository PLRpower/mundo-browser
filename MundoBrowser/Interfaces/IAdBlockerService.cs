using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MundoBrowser.Interfaces;

public interface IAdBlockerService
{
    bool IsAdBlockerEnabled { get; set; }
    bool IsCookieBlockerEnabled { get; set; }
    IReadOnlyCollection<string> BlockedDomains { get; }
    IReadOnlyCollection<string> BlockedPathPatterns { get; }

    event Action? BlockedDomainsUpdated;

    string? GetSiteHost(string? url);
    bool IsAdBlockerEnabledForSite(string? url);
    bool ShouldBlockUrl(string? url);
    bool SetAdBlockerEnabledForSite(string? url, bool enabled);
    bool IsCookieBlockerEnabledForSite(string? url);
    bool SetCookieBlockerEnabledForSite(string? url, bool enabled);
    bool IsProtectionDisabledForSite(string? url);
    bool SetProtectionDisabledForSite(string? url, bool disabled);
    string GetCosmeticCss();
    string GetCookieCosmeticCss();
    string GetCookieRemovalScript();
    string GetYouTubeAdBlockScript();
    string GetInjectionScript();
    Task UpdateRemoteBlocklistsAsync(bool force = false, CancellationToken cancellationToken = default);
}
