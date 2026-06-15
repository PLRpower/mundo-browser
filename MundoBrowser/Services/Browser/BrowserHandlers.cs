using CefSharp;
using CefSharp.Handler;
using CefSharp.Structs;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services.Browser;

internal sealed class BrowserDisplayHandler(
    Action<IList<string>> faviconChanged,
    Action<bool> fullscreenChanged) : DisplayHandler
{
    protected override void OnFaviconUrlChange(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IList<string> urls) => faviconChanged(urls);

    protected override void OnFullscreenModeChange(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        bool fullscreen) => fullscreenChanged(fullscreen);
}

internal sealed class BrowserLifeSpanHandler(
    Action<string> popupRequested,
    Action closeRequested) : LifeSpanHandler
{
    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser? newBrowser)
    {
        newBrowser = null;
        if (!string.IsNullOrWhiteSpace(targetUrl))
            popupRequested(targetUrl);
        return true;
    }

    protected override bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        closeRequested();
        return true;
    }
}

internal sealed class BrowserDownloadHandler : DownloadHandler
{
    protected override bool CanDownload(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        string url,
        string requestMethod) => true;

    protected override bool OnBeforeDownload(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        DownloadItem downloadItem,
        IBeforeDownloadCallback callback)
    {
        using (callback)
        {
            if (!callback.IsDisposed)
                callback.Continue("", showDialog: true);
        }
        return true;
    }
}

internal sealed class BrowserRequestHandler : RequestHandler
{
    private readonly Action<string> _internalNavigationRequested;
    private readonly Action _renderProcessTerminated;
    private readonly ResourceRequestHandler _resourceHandler;
    private string? _currentPageUrl;

    public BrowserRequestHandler(
        IAdBlockerService adBlocker,
        string? initialPageUrl,
        Action<string> internalNavigationRequested,
        Action renderProcessTerminated)
    {
        _currentPageUrl = initialPageUrl;
        _internalNavigationRequested = internalNavigationRequested;
        _renderProcessTerminated = renderProcessTerminated;
        _resourceHandler = new AdBlockResourceRequestHandler(
            adBlocker,
            () => Volatile.Read(ref _currentPageUrl));
    }

    protected override bool OnBeforeBrowse(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        bool userGesture,
        bool isRedirect)
    {
        if (!frame.IsMain)
            return false;

        Volatile.Write(ref _currentPageUrl, request.Url);

        if (BrowserService.IsSettingsUrl(request.Url))
        {
            _internalNavigationRequested(request.Url);
            return true;
        }

        return false;
    }

    protected override IResourceRequestHandler? GetResourceRequestHandler(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        bool isNavigation,
        bool isDownload,
        string requestInitiator,
        ref bool disableDefaultHandling) => _resourceHandler;

    protected override void OnRenderProcessTerminated(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefTerminationStatus status,
        int errorCode,
        string errorMessage) => _renderProcessTerminated();

    private sealed class AdBlockResourceRequestHandler(
        IAdBlockerService adBlocker,
        Func<string?> currentPageUrl) : ResourceRequestHandler
    {
        protected override CefReturnValue OnBeforeResourceLoad(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            IFrame frame,
            IRequest request,
            IRequestCallback callback)
        {
            if (!adBlocker.IsAdBlockerEnabled
                || adBlocker.IsProtectionDisabledForSite(currentPageUrl())
                || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
                return CefReturnValue.Continue;

            bool blocked = adBlocker.BlockedDomains.Any(domain =>
                uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

            return blocked ? CefReturnValue.Cancel : CefReturnValue.Continue;
        }
    }
}
