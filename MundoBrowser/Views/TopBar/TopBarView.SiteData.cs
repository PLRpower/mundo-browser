using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfMessageBox = System.Windows.MessageBox;

namespace MundoBrowser;

public partial class TopBarView
{
    private void SiteDataButton_Click(object sender, RoutedEventArgs e)
    {
        SiteDataContextMenu.PlacementTarget = SiteDataButton;
        SiteDataContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        SiteDataContextMenu.IsOpen = true;
    }

    private void SiteDataContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var siteUri = GetCurrentSiteUri();
        bool hasSite = siteUri != null;

        CurrentSiteDataHostMenuItem.Header = hasSite
            ? siteUri!.Host
            : "Indisponible sur cette page";
        ClearCurrentSiteDataMenuItem.IsEnabled = hasSite;
    }

    private async void ClearCurrentSiteData_Click(object sender, RoutedEventArgs e)
    {
        var webView = GetWebView();
        var siteUri = GetCurrentSiteUri();
        if (webView?.CoreWebView2 == null || siteUri == null)
            return;

        string host = siteUri.Host;
        var confirmation = WpfMessageBox.Show(
            $"Supprimer les cookies, le stockage local et les caches accessibles pour {host} ?\n\nVous serez probablement déconnecté de ce site.",
            "Supprimer les données du site",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            await DeleteCookiesForCurrentSiteAsync(webView, siteUri);

            bool storageCleared = await TryClearOriginDataWithDevToolsAsync(webView, siteUri);
            if (!storageCleared)
                await TryClearDocumentStorageFallbackAsync(webView);

            webView.Reload();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Impossible de supprimer les données de {host}.\n\n{ex.Message}",
                "Données du site",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private Uri? GetCurrentSiteUri()
    {
        string? source = GetWebView()?.CoreWebView2?.Source;
        if (string.IsNullOrWhiteSpace(source)
            && DataContext is ViewModels.MainViewModel vm)
        {
            source = vm.SelectedTab?.AddressUrl;
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.Host.Equals("internals.mundobrowser", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri;
    }

    private static async Task DeleteCookiesForCurrentSiteAsync(WebView2 webView, Uri siteUri)
    {
        var cookieManager = webView.CoreWebView2.CookieManager;
        IReadOnlyList<CoreWebView2Cookie> cookies;

        try
        {
            cookies = await cookieManager.GetCookiesAsync(null!);
        }
        catch
        {
            cookies = await cookieManager.GetCookiesAsync(siteUri.AbsoluteUri);
        }

        foreach (var cookie in cookies)
        {
            if (CookieMatchesHost(cookie.Domain, siteUri.Host))
                cookieManager.DeleteCookie(cookie);
        }
    }

    private static bool CookieMatchesHost(string cookieDomain, string host)
    {
        string normalizedCookieDomain = cookieDomain.TrimStart('.').TrimEnd('.').ToLowerInvariant();
        string normalizedHost = host.TrimEnd('.').ToLowerInvariant();

        return normalizedHost == normalizedCookieDomain
               || normalizedHost.EndsWith("." + normalizedCookieDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> TryClearOriginDataWithDevToolsAsync(WebView2 webView, Uri siteUri)
    {
        try
        {
            string origin = siteUri.GetLeftPart(UriPartial.Authority);
            string parameters = JsonSerializer.Serialize(new
            {
                origin,
                storageTypes = "all"
            });

            await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Storage.clearDataForOrigin",
                parameters);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clear site data via DevTools protocol: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TryClearDocumentStorageFallbackAsync(WebView2 webView)
    {
        const string script = @"
(async () => {
    try { localStorage.clear(); } catch {}
    try { sessionStorage.clear(); } catch {}

    try {
        if (globalThis.indexedDB && indexedDB.databases) {
            const databases = await indexedDB.databases();
            await Promise.all(databases
                .map(database => database && database.name)
                .filter(Boolean)
                .map(name => new Promise(resolve => {
                    const request = indexedDB.deleteDatabase(name);
                    request.onsuccess = request.onerror = request.onblocked = () => resolve();
                })));
        }
    } catch {}

    try {
        if (globalThis.caches) {
            const keys = await caches.keys();
            await Promise.all(keys.map(key => caches.delete(key)));
        }
    } catch {}

    try {
        if (navigator.serviceWorker) {
            const registrations = await navigator.serviceWorker.getRegistrations();
            await Promise.all(registrations.map(registration => registration.unregister()));
        }
    } catch {}

    return true;
})()";

        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync(script);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clear site data via document script: {ex.Message}");
            return false;
        }
    }
}
