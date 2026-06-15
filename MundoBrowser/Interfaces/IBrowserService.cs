using CefSharp;
using CefSharp.Wpf.HwndHost;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Interfaces;

/// <summary>
/// Manages embedded Chromium browser instances and their lifecycle.
/// </summary>
public interface IBrowserService
{
    ChromiumWebBrowser? ActiveBrowser { get; }

    IRequestContext? RequestContext { get; }

    bool EcoModeEnabled { get; set; }

    int EcoModeMinutes { get; set; }

    Task InitializeAsync(System.Windows.Controls.Panel container);

    Task<ChromiumWebBrowser> GetOrCreateBrowserAsync(
        TabViewModel tab,
        Action<ChromiumWebBrowser> setupEvents);

    Task SwitchToTabAsync(TabViewModel tab, ChromiumWebBrowser browser);

    void RemoveTab(TabViewModel tab);

    ChromiumWebBrowser? GetBrowserForTab(TabViewModel tab);
}
