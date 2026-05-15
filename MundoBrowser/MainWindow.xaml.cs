using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow : Window
{
    private readonly WebViewService _webViewService;
    private CancellationTokenSource? _suggestionCts;
    private bool _isUpdatingAddressBar;
    private bool _isFullscreen;
    private bool _isSidebarFloating;
    private string? _currentExtensionId;
    private string? _lastClosedExtensionId;
    private DateTime _lastExtensionPopupClosed = DateTime.MinValue;
    private System.Windows.Point? _dragStartPos;
    private (WindowState State, WindowStyle Style, ResizeMode Resize) _prevWindowState;

    private readonly System.Windows.Threading.DispatcherTimer _globalMediaTimer;

    public MainWindow()
    {
        InitializeComponent();
        
        var vm = (MainViewModel)DataContext;
        _webViewService = new WebViewService();

        InitializeWindow();
        InitializeEvents(vm);

        // Single global timer for media updates to save resources
        _globalMediaTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _globalMediaTimer.Tick += UpdateActiveMediaInfo;
        _globalMediaTimer.Start();

        // Hook for taskbar respect
        SourceInitialized += (s, e) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowAppId(handle, "MundoBrowser.App");
            
            // Apply Windows 11 Backdrop effects
            NativeMethods.SetWindowCorners(handle, NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
            NativeMethods.SetWindowBackdrop(this, NativeMethods.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW); // Acrylic
            NativeMethods.SetWindowDarkMode(this, true);

            System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024) // WM_GETMINMAXINFO
        {
            handled = true;
            NativeMethods.WmGetMinMaxInfo(hwnd, lParam, _isFullscreen);
        }
        return IntPtr.Zero;
    }

    private void InitializeWindow()
    {
        StateChanged += (_, _) => OnWindowStateChanged();
        Closing += async (_, _) => {
            if (DataContext is MainViewModel vm)
            {
                await vm.SaveCurrentSessionAsync();
            }
        };
        
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);

        // Fix WPF bug: ElementName bindings on Popups often get lost after IsOpen toggles
        FloatingSidebarPopup.PlacementTarget = MainGrid;
        EdgeTriggerPopup.PlacementTarget = MainGrid;
        if (FindName("QuickUrlPopup") is System.Windows.Controls.Primitives.Popup quickPopup)
            quickPopup.PlacementTarget = MainGrid;

        ContentRendered += async (_, _) => {
            await _webViewService.InitializeAsync(WebViewsContainer);
            if (DataContext is MainViewModel vm && vm.SelectedTab != null)
            {
                await SwitchToTabAsync(vm.SelectedTab);
                UpdateSidebarWidth(vm.IsSidebarVisible);
            }
            await LoadExtensionsAsync();
        };
    }

    private void InitializeEvents(MainViewModel vm)
    {
        vm.PropertyChanged += async (s, e) => {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab) && vm.SelectedTab != null)
                await SwitchToTabAsync(vm.SelectedTab);
            else if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible))
            {
                if (!_isFullscreen)
                    UpdateSidebarWidth(vm.IsSidebarVisible);
            }
        };

        vm.Tabs.CollectionChanged += (_, e) => {
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TabViewModel tab in e.OldItems)
                {
                    tab.PropertyChanged -= OnTabPropertyChanged;
                    _webViewService.RemoveTab(tab);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (TabViewModel tab in e.NewItems)
                {
                    tab.PropertyChanged += OnTabPropertyChanged;
                }
            }
        };

        foreach (var tab in vm.Tabs)
        {
            tab.PropertyChanged += OnTabPropertyChanged;
        }

        vm.NewTabRequested += (_, _) => { AddressTextBox.Focus(); AddressTextBox.SelectAll(); };
        vm.MediaActionRequested += OnMediaActionRequested;
    }

    private async Task SwitchToTabAsync(TabViewModel tab)
    {
        if (tab == null) return;

        var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
        await _webViewService.SwitchToTabAsync(tab, webView);

        if (DataContext is MainViewModel vm)
        {
            _isUpdatingAddressBar = true;
            vm.AddressBarText = tab.AddressUrl;
            _isUpdatingAddressBar = false;
        }
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.Url) && sender is TabViewModel tab)
        {
            Dispatcher.Invoke(async () => {
                var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
                if (webView.CoreWebView2.Source != tab.Url)
                {
                    webView.CoreWebView2.Navigate(tab.Url);
                }
            });
        }
    }

    private void SetupWebViewEvents(WebView2 wv, TabViewModel tab)
    {
        wv.CoreWebView2.IsDocumentPlayingAudioChanged += (s, e) =>
        {
            tab.IsPlayingAudio = wv.CoreWebView2.IsDocumentPlayingAudio;
            if (tab.IsPlayingAudio && DataContext is MainViewModel vm)
            {
                vm.ActiveMediaTab = tab;
                vm.IsMediaBarVisible = true; // Re-show if it was manually closed and music starts again
            }
        };

        wv.CoreWebView2.NavigationCompleted += (_, args) => {
            if (args.IsSuccess && DataContext is MainViewModel vm && vm.SelectedTab == tab) {
                var source = wv.CoreWebView2.Source;
                if (source.Contains("internals.mundobrowser"))
                {
                    // For settings, we trust SourceChanged or initial mapping
                    if (string.IsNullOrEmpty(tab.AddressUrl) || !tab.AddressUrl.StartsWith("about:preferences"))
                    {
                        string hash = source.Contains("#") ? source.Substring(source.IndexOf("#")) : "#general";
                        tab.AddressUrl = "about:preferences" + hash;
                    }
                }
                else
                {
                    tab.Url = tab.AddressUrl = source;
                }
                
                UpdateTitle();
                vm.HistoryManager.AddEntry(tab.Url, wv.CoreWebView2.DocumentTitle);

                _isUpdatingAddressBar = true;
                vm.AddressBarText = tab.AddressUrl;
                _isUpdatingAddressBar = false;

                CheckForExtensionStorePage(tab, tab.Url);
            }
        };

        wv.CoreWebView2.DocumentTitleChanged += (_, _) => {
            if (((MainViewModel)DataContext).SelectedTab == tab) UpdateTitle();
        };

        wv.CoreWebView2.SourceChanged += async (_, _) => {
            var source = wv.CoreWebView2.Source;
            // Precise detection for our settings page
            if (wv.CoreWebView2.DocumentTitle == "about:preferences" || source.Contains("internals.mundobrowser"))
            {
                // If it's our internal settings URL, parse the hash
                if (source.Contains("settings.html"))
                {
                    string hash = source.Contains("#") ? source.Substring(source.IndexOf("#")) : "#general";
                    tab.AddressUrl = "about:preferences" + hash;
                }
                // Otherwise keep current AddressUrl if it's already about:preferences
                else if (string.IsNullOrEmpty(tab.AddressUrl) || !tab.AddressUrl.StartsWith("about:preferences"))
                {
                    tab.AddressUrl = "about:preferences#general";
                }
            }
            else if (source != "about:blank")
            {
                tab.AddressUrl = source;
            }

            if (DataContext is MainViewModel vm && vm.SelectedTab == tab) {
                _isUpdatingAddressBar = true;
                vm.AddressBarText = tab.AddressUrl;
                _isUpdatingAddressBar = false;

                CheckForExtensionStorePage(tab, tab.AddressUrl);
            }
            if (DataContext is MainViewModel mainVm)
            {
                await mainVm.FaviconService.ResolveFaviconAsync(wv, tab);
            }
        };

        wv.CoreWebView2.FaviconChanged += async (s, args) => {
            if (DataContext is MainViewModel vm)
            {
                await vm.FaviconService.ResolveFaviconAsync(wv, tab, forceReload: true);
            }
        };

        wv.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => 
            SetFullscreen(wv.CoreWebView2.ContainsFullScreenElement, true);

        wv.CoreWebView2.NewWindowRequested += (s, args) => {
            args.Handled = true;
            if (DataContext is MainViewModel vm)
            {
                vm.AddTabWithUrl(args.Uri);
            }
        };

        wv.CoreWebView2.WindowCloseRequested += (s, args) => {
            if (DataContext is MainViewModel vm)
            {
                vm.CloseTab(tab);
            }
        };
    }

    private void UpdateTitle()
    {
        if (_webViewService.ActiveWebView?.CoreWebView2 == null || DataContext is not MainViewModel vm || vm.SelectedTab == null) return;
        var title = _webViewService.ActiveWebView.CoreWebView2.DocumentTitle;
        vm.SelectedTab.Title = !string.IsNullOrWhiteSpace(title) ? title : (vm.SelectedTab.Url ?? "New Tab");
    }
}