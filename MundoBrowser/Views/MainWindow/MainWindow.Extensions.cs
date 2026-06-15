using System.Windows;
using System.Text.Json;
using CefSharp;
using CefSharp.Wpf.HwndHost;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using MundoBrowser.Services;
using MundoBrowser.Services.Extensions;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private async Task LoadExtensionsAsync()
    {
        if (DataContext is not MainViewModel vm) return;
        var extensions = await _extensionService.LoadExtensionsAsync();
        
        vm.InstalledExtensions.Clear();
        foreach (var ext in extensions)
        {
            vm.InstalledExtensions.Add(ext);
        }
    }

    private async Task HandleExtensionStoreMessageAsync(
        ChromiumWebBrowser browser,
        object? message)
    {
        string? extensionId = GetRequestedExtensionId(message);
        if (extensionId == null
            || !string.Equals(
                ExtensionDownloader.ExtractExtensionIdFromUrl(browser.Address),
                extensionId,
                StringComparison.Ordinal)
            || ExtensionRuntime.IsInstalled(extensionId)
            || !_installingExtensionIds.Add(extensionId))
            return;

        SetExtensionStoreState(browser, "installing");
        try
        {
            await _extensionService.InstallExtensionAsync(extensionId);
            SetExtensionStoreState(browser, "installed");

            var restart = MessageBox.Show(
                "L'extension est installée. Chromium doit redémarrer pour l'activer.\n\nRedémarrer MundoBrowser maintenant ?",
                "Extension installée",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (restart == MessageBoxResult.Yes)
                RequestRestart();
        }
        catch (Exception ex)
        {
            SetExtensionStoreState(browser, "error");
            MessageBox.Show("Erreur installation: " + ex.Message);
        }
        finally
        {
            _installingExtensionIds.Remove(extensionId);
        }
    }

    private static string? GetRequestedExtensionId(object? message)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(message));
            var root = doc.RootElement;
            return root.TryGetProperty("type", out var type)
                   && type.GetString() == "extensionInstallRequested"
                   && root.TryGetProperty("extensionId", out var id)
                ? id.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SetExtensionStoreState(ChromiumWebBrowser browser, string state)
    {
        string serializedState = JsonSerializer.Serialize(state);
        browser.ExecuteScriptAsync(
            $"window.__mundoExtensionStore?.setState({serializedState});");
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

        var ext = vm.InstalledExtensions.FirstOrDefault(extension => extension.Id == extId);
        if (ext == null
            || string.IsNullOrEmpty(ext.PopupUrl))
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
            await popupWindow.InitializeAsync(ext.PopupUrl);
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
}
