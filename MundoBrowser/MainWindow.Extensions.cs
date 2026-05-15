using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        
        var extensionService = new ExtensionService();
        var extensions = await extensionService.LoadExtensionsAsync(profile);
        
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
                var downloader = new ExtensionDownloader();
                var extPath = await downloader.DownloadAndExtractExtension(vm.SelectedTab.InstallableExtensionId);
                InstallStatusText.Text = "Installation...";
                
                // Get profile from active WebView to install extension
                if (_webViewService.ActiveWebView?.CoreWebView2?.Profile != null)
                {
                    await _webViewService.ActiveWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(extPath);
                    await LoadExtensionsAsync();
                    vm.SelectedTab.IsExtensionStorePage = false;
                }
            } catch (Exception ex) { MessageBox.Show("Erreur installation: " + ex.Message); }
            finally { InstallProgressBar.Visibility = Visibility.Collapsed; InstallStatusText.Visibility = Visibility.Collapsed; }
        }
    }

    private void CloseInstallBar_Click(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm && vm.SelectedTab != null) vm.SelectedTab.IsExtensionStorePage = false; }

    private async void ExtensionIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string extId && DataContext is MainViewModel vm)
        {
            // If the popup was closed very recently (by a MouseDown that triggered this click), 
            // and it was the SAME extension, don't reopen it immediately.
            if (DateTime.Now - _lastExtensionPopupClosed < TimeSpan.FromMilliseconds(200) && _lastClosedExtensionId == extId)
            {
                return;
            }

            // Toggle logic fallback
            if (ExtensionPopup.IsOpen && _currentExtensionId == extId)
            {
                CloseExtensionPopup();
                return;
            }

            var ext = vm.InstalledExtensions.FirstOrDefault(x => x.Id == extId);
            if (ext != null && !string.IsNullOrEmpty(ext.PopupUrl) && _webViewService.WebViewEnvironment != null)
            {
                _currentExtensionId = extId;
                ExtensionPopup.PlacementTarget = btn;
                ExtensionPopup.IsOpen = true;

                try
                {
                    await ExtensionPopupWebView.EnsureCoreWebView2Async(_webViewService.WebViewEnvironment);
                    
                    // Force interaction settings
                    ExtensionPopupWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                    ExtensionPopupWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

                    ExtensionPopupWebView.CoreWebView2.Navigate(ext.PopupUrl);
                    
                    // Delay to allow loading/rendering then force focus
                    await Task.Delay(200);
                    if (ExtensionPopup.IsOpen)
                    {
                        ExtensionPopupWebView.Focus();
                        System.Windows.Input.FocusManager.SetFocusedElement(ExtensionPopup, ExtensionPopupWebView);
                        ExtensionPopupWebView.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Extension popup error: {ex.Message}");
                    CloseExtensionPopup();
                }
            }
        }
    }

    private void ExtensionPopup_Opened(object sender, EventArgs e)
    {
        if (ExtensionPopup.Child is FrameworkElement child)
        {
            if (PresentationSource.FromVisual(child) is System.Windows.Interop.HwndSource source)
            {
                Helpers.NativeMethods.SetWindowCorners(source.Handle, Helpers.NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
            }
        }
    }

    private void ExtensionPopupWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (ExtensionPopupWebView.CoreWebView2 != null)
        {
            ExtensionPopupWebView.Focus();
        }
    }

    private void ExtensionPopup_Closed(object sender, EventArgs e)
    {
        _lastExtensionPopupClosed = DateTime.Now;
        _lastClosedExtensionId = _currentExtensionId;
        _currentExtensionId = null;
    }

    private void CloseExtensionPopup()
    {
        ExtensionPopup.IsOpen = false;
        _currentExtensionId = null;
    }

    private void CloseExtensionPopup_Click(object sender, RoutedEventArgs e)
    {
        ExtensionPopup.IsOpen = false;
        _currentExtensionId = null;
    }

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string extId)
        {
            if (_webViewService.ActiveWebView?.CoreWebView2?.Profile != null)
            {
                var profile = _webViewService.ActiveWebView.CoreWebView2.Profile;
                var exts = await profile.GetBrowserExtensionsAsync();
                var ext = exts.FirstOrDefault(x => x.Id == extId);
                if (ext != null) { await ext.RemoveAsync(); await LoadExtensionsAsync(); }
            }
        }
    }
}