using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private void CheckForExtensionStorePage(TabViewModel tab, string url)
    {
        if (string.IsNullOrEmpty(url)) { tab.IsExtensionStorePage = false; tab.InstallableExtensionId = null; return; }
        var extensionId = ExtensionDownloader.ExtractExtensionIdFromUrl(url);
        if (DataContext is MainViewModel vm && extensionId != null && vm.InstalledExtensions.Any(e => e.Id == extensionId)) { tab.IsExtensionStorePage = false; tab.InstallableExtensionId = null; return; }
        tab.InstallableExtensionId = extensionId;
        tab.IsExtensionStorePage = !string.IsNullOrEmpty(extensionId);
    }

    private async Task LoadExtensionsAsync()
    {
        if (_webViewService.ActiveWebView?.CoreWebView2 == null || DataContext is not MainViewModel vm) return;
        var profile = _webViewService.ActiveWebView.CoreWebView2.Profile;
        
        var extensions = await _extensionService.LoadExtensionsAsync(profile);
        
        vm.InstalledExtensions.Clear();
        foreach (var ext in extensions)
        {
            vm.InstalledExtensions.Add(ext);
        }
    }

    private async void InstallExtensionFromBar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab?.InstallableExtensionId != null)
        {
            InstallProgressBar.Visibility = Visibility.Visible;
            InstallStatusText.Visibility = Visibility.Visible;
            InstallStatusText.Text = "Téléchargement...";
            try {
                // Get profile from active WebView to install extension
                if (_webViewService.ActiveWebView?.CoreWebView2?.Profile != null)
                {
                    InstallStatusText.Text = "Installation...";
                    await _extensionService.InstallExtensionAsync(vm.SelectedTab.InstallableExtensionId, _webViewService.ActiveWebView.CoreWebView2.Profile);
                    await LoadExtensionsAsync();
                    vm.SelectedTab.IsExtensionStorePage = false;
                }
            } catch (Exception ex) { MessageBox.Show("Erreur installation: " + ex.Message); }
            finally { InstallProgressBar.Visibility = Visibility.Collapsed; InstallStatusText.Visibility = Visibility.Collapsed; }
        }
    }

    private void CloseInstallBar_Click(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm && vm.SelectedTab != null) vm.SelectedTab.IsExtensionStorePage = false; }

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
}
