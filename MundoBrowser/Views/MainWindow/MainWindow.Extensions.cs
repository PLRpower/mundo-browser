using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using MundoBrowser.Helpers;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private void CheckForExtensionStorePage(TabViewModel tab, string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            tab.IsExtensionStorePage = false;
            tab.InstallableExtensionId = null;
            return;
        }

        var extensionId = ChromeWebStoreHelper.ExtractExtensionId(url) ?? ExtensionDownloader.ExtractExtensionIdFromUrl(url);
        tab.InstallableExtensionId = extensionId;
        tab.IsExtensionStorePage = false;
    }

    private async Task LoadExtensionsAsync()
    {
        var webView = _webViewService.ActiveWebView ?? _webViewService.GetAnyActiveWebView();
        if (webView?.CoreWebView2 == null || DataContext is not MainViewModel vm) return;
        var profile = webView.CoreWebView2.Profile;
        
        var extensions = await _extensionService.LoadExtensionsAsync(profile);
        
        vm.InstalledExtensions.Clear();
        foreach (var ext in extensions)
        {
            vm.InstalledExtensions.Add(ext);
        }
    }

    private async Task HandleExtensionWebMessageAsync(WebView2 webView, TabViewModel tab, string json)
    {
        if (string.IsNullOrWhiteSpace(json) || webView.CoreWebView2 == null) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;

            var type = typeProp.GetString();
            if (type == "checkExtensionStatus" && root.TryGetProperty("extensionId", out var extIdElem))
            {
                var extId = extIdElem.GetString();
                if (!string.IsNullOrEmpty(extId) && DataContext is MainViewModel vm)
                {
                    bool isInstalled = vm.InstalledExtensions.Any(e =>
                        e.Id.Equals(extId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(e.StoreId) && e.StoreId.Equals(extId, StringComparison.OrdinalIgnoreCase)));
                    var response = JsonSerializer.Serialize(new
                    {
                        type = "extensionStatus",
                        extensionId = extId,
                        isInstalled
                    });
                    webView.CoreWebView2.PostWebMessageAsJson(response);
                }
            }
            else if (type == "installExtension" && root.TryGetProperty("extensionId", out var installExtIdElem))
            {
                var extId = installExtIdElem.GetString();
                if (!string.IsNullOrEmpty(extId))
                {
                    await HandleInstallExtensionFromPageAsync(webView, extId);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtensionWebMessage] Error: {ex.Message}");
        }
    }

    private async Task HandleInstallExtensionFromPageAsync(WebView2 webView, string extensionId)
    {
        if (DataContext is not MainViewModel vm || webView.CoreWebView2?.Profile == null)
            return;

        try
        {
            var progressJson = JsonSerializer.Serialize(new
            {
                type = "extensionInstallProgress",
                extensionId,
                status = "downloading"
            });
            webView.CoreWebView2.PostWebMessageAsJson(progressJson);

            await _extensionService.InstallExtensionAsync(extensionId, webView.CoreWebView2.Profile);
            await LoadExtensionsAsync();

            var installedJson = JsonSerializer.Serialize(new
            {
                type = "extensionInstallProgress",
                extensionId,
                status = "installed"
            });
            webView.CoreWebView2.PostWebMessageAsJson(installedJson);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error installing extension {extensionId}: {ex.Message}");

            var errorJson = JsonSerializer.Serialize(new
            {
                type = "extensionInstallProgress",
                extensionId,
                status = "error",
                message = ex.Message
            });
            webView.CoreWebView2.PostWebMessageAsJson(errorJson);

            MessageBox.Show($"Impossible d'installer l'extension : {ex.Message}", "Erreur d'installation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NotifyExtensionStatusToWebView(WebView2 webView, string? url)
    {
        if (string.IsNullOrEmpty(url) || webView.CoreWebView2 == null || DataContext is not MainViewModel vm)
            return;

        var extId = ChromeWebStoreHelper.ExtractExtensionId(url) ?? ExtensionDownloader.ExtractExtensionIdFromUrl(url);
        if (string.IsNullOrEmpty(extId))
            return;

        bool isInstalled = vm.InstalledExtensions.Any(e =>
            e.Id.Equals(extId, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(e.StoreId) && e.StoreId.Equals(extId, StringComparison.OrdinalIgnoreCase)));
        var response = JsonSerializer.Serialize(new
        {
            type = "extensionStatus",
            extensionId = extId,
            isInstalled
        });
        webView.CoreWebView2.PostWebMessageAsJson(response);
    }

    public async void ShowExtensionPopup(string extId, Button btn)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (_extensionPopupWindow?.IsVisible == true)
        {
            if (_currentExtensionId == extId)
            {
                CloseExtensionPopup();
                return;
            }

            CloseExtensionPopup();
        }

        var ext = vm.InstalledExtensions.FirstOrDefault(extension =>
            extension.Id == extId ||
            (!string.IsNullOrEmpty(extension.StoreId) && extension.StoreId == extId));
        if (ext == null
            || string.IsNullOrEmpty(ext.PopupUrl)
            || _webViewService.WebViewEnvironment == null)
            return;

        var popupWindow = new ExtensionPopupWindow(this, btn);
        _extensionPopupWindow = popupWindow;
        _currentExtensionId = extId;

        popupWindow.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_extensionPopupWindow, popupWindow)) return;

            _extensionPopupWindow = null;
            _currentExtensionId = null;
        };

        popupWindow.PositionNextToTarget();
        popupWindow.Show();
        popupWindow.PositionNextToTarget();

        try
        {
            await popupWindow.InitializeAsync(_webViewService.WebViewEnvironment, ext.PopupUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Extension popup error: {ex.Message}");
            if (ReferenceEquals(_extensionPopupWindow, popupWindow))
                CloseExtensionPopup();
        }
    }

    private void CloseExtensionPopup()
    {
        var popupWindow = _extensionPopupWindow;
        _extensionPopupWindow = null;
        _currentExtensionId = null;

        if (popupWindow?.IsVisible == true)
            popupWindow.Close();
    }

    public async Task UninstallExtensionAsync(string extensionId)
    {
        if (DataContext is not MainViewModel vm) return;

        var ext = vm.InstalledExtensions.FirstOrDefault(x =>
            x.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(x.StoreId) && x.StoreId.Equals(extensionId, StringComparison.OrdinalIgnoreCase)));

        if (ext != null)
        {
            vm.InstalledExtensions.Remove(ext);
        }

        var webView = _webViewService.ActiveWebView ?? _webViewService.GetAnyActiveWebView();
        if (webView?.CoreWebView2 != null)
        {
            var profile = webView.CoreWebView2.Profile;
            if (ext != null)
            {
                await _extensionService.RemoveExtensionAsync(ext, profile);
            }
            else
            {
                await _extensionService.RemoveExtensionAsync(extensionId, profile);
            }
        }

        NotifyExtensionUninstalled(extensionId, ext?.StoreId);
    }

    private void NotifyExtensionUninstalled(string extensionId, string? storeId)
    {
        try
        {
            var targetId = storeId ?? extensionId;
            var response = JsonSerializer.Serialize(new
            {
                type = "extensionStatus",
                extensionId = targetId,
                isInstalled = false
            });

            var webViews = _webViewService.GetAllWebViews();
            foreach (var wv in webViews)
            {
                if (wv.CoreWebView2 != null && ChromeWebStoreHelper.IsChromeWebStoreUrl(wv.CoreWebView2.Source))
                {
                    wv.CoreWebView2.PostWebMessageAsJson(response);
                }
            }
        }
        catch { }
    }
}
