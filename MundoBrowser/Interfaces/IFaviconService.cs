using CefSharp.Wpf.HwndHost;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Interfaces;

public interface IFaviconService
{
    string? GetAbsoluteFaviconPath(string relativePath);

    string? GetFaviconUrlForPage(string pageUrl);

    string? GetCachedFaviconUrlForPage(string pageUrl);

    Task ResolveFaviconAsync(ChromiumWebBrowser browser, TabViewModel tab, bool forceReload = false);
}
