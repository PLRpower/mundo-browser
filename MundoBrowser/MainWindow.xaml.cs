using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly WebViewService _webViewService;
    private bool _isFullscreen;
    private bool _isApplyingFullscreenBounds;
    private bool _isRestoringFromFullscreen;
    private bool _fullscreenHidesUi;
    private bool _resizeOverlaysOpen;
    private bool _isSidebarFloating;
    private bool _isClosingSafe;
    private bool _isSavingSession;
    private string? _currentExtensionId;
    private ExtensionPopupWindow? _extensionPopupWindow;
    private string? _lastClosedExtensionId;
    private DateTime _lastExtensionPopupClosed = DateTime.MinValue;
    private (WindowState State, WindowStyle Style, ResizeMode Resize, Wpf.Ui.Controls.WindowBackdropType Backdrop, Wpf.Ui.Controls.WindowCornerPreference Corners, bool Topmost, double Left, double Top, double Width, double Height) _prevWindowState;

    private readonly System.Windows.Threading.DispatcherTimer _globalMediaTimer;
    private int _mediaUpdateRunning;
    private DateTime _lastBackgroundMediaUpdate = DateTime.MinValue;
    private readonly string[]? _startArgs;

    public MainWindow(string[]? args = null)
    {
        _startArgs = args;
        InitializeComponent();
        
        var vm = (MainViewModel)DataContext;
        _webViewService = new WebViewService();

        InitializeWindow();
        InitializeEvents(vm);

        // Single global timer for media updates to save resources
        _globalMediaTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _globalMediaTimer.Tick += UpdateActiveMediaInfo;
        _globalMediaTimer.Start();

        // Hook for taskbar respect
        SourceInitialized += (s, e) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowAppId(handle, NativeMethods.AppUserModelId);
            UpdateWindowFrameVisuals();
            
            System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
    }

    private void InitializeWindow()
    {
        StateChanged += (_, _) => {
            OnWindowStateChanged(forceResizeOverlayReopen: true);
            UpdateEdgeTriggerState(forceReopen: true);
        };
        LocationChanged += (_, _) => {
            KeepFullscreenBounds();
            RepositionEdgeTriggerPopup();
        };
        SizeChanged += (_, _) => {
            KeepFullscreenBounds();
            UpdateResizeOverlayState();
            UpdateEdgeTriggerState(forceReopen: true);
        };
        Activated += (_, _) => UpdateEdgeTriggerState(forceReopen: true);
        Deactivated += (_, _) => HideFloatingSidebar(animate: false);
        Closing += async (_, e) => {
            if (!_isClosingSafe)
            {
                e.Cancel = true;
                if (_isSavingSession)
                    return;

                _isSavingSession = true;
                try
                {
                    SyncWindowPlacementToViewModel();
                    if (DataContext is MainViewModel vm)
                    {
                        await vm.SaveCurrentSessionAsync();
                    }
                }
                finally
                {
                    _isSavingSession = false;
                }
                _isClosingSafe = true;
                Close();
            }
        };
        
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);

        // Fix WPF bug: ElementName bindings on Popups often get lost after IsOpen toggles
        FloatingSidebarPopup.PlacementTarget = MainGrid;
        EdgeTriggerPopup.PlacementTarget = MainGrid;
        InitializeResizeOverlays();
        if (FindName("QuickUrlPopup") is System.Windows.Controls.Primitives.Popup quickPopup)
            quickPopup.PlacementTarget = MainGrid;

        ContentRendered += async (_, _) => {
            WindowTitleBar.PreviewMouseLeftButtonDown += TitleBar_PreviewMouseLeftButtonDown;
            WindowTitleBar.PreviewMouseMove += BlockFullscreenTitleBarDrag;
            UpdateResizeOverlayState(forceReopen: true);
            UpdateEdgeTriggerState(forceReopen: true);

            await _webViewService.InitializeAsync(WebViewsContainer);
            
            if (DataContext is MainViewModel vm)
            {
                // Process startup arguments (URLs or files)
                if (_startArgs != null && _startArgs.Length > 0)
                {
                    string input = _startArgs[0];
                    // Handle local paths for PDF or HTML files
                    if (System.IO.File.Exists(input))
                    {
                        input = new Uri(System.IO.Path.GetFullPath(input)).AbsoluteUri;
                    }
                    
                    vm.AddTabWithUrl(input);
                }
                else if (vm.SelectedTab != null)
                {
                    await SwitchToTabAsync(vm.SelectedTab);
                    UpdateSidebarWidth(vm.IsSidebarVisible);
                }
                
                await LoadExtensionsAsync();
            }
        };
    }

    private void RepositionEdgeTriggerPopup()
    {
        if (EdgeTriggerPopup != null && EdgeTriggerPopup.IsOpen)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (EdgeTriggerPopup != null && EdgeTriggerPopup.IsOpen)
                {
                    var offset = EdgeTriggerPopup.HorizontalOffset;
                    EdgeTriggerPopup.HorizontalOffset = offset + 0.1;
                    EdgeTriggerPopup.HorizontalOffset = offset;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void InitializeEvents(MainViewModel vm)
    {
        vm.PropertyChanged += async (s, e) => {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab) && vm.SelectedTab != null)
                await SwitchToTabAsync(vm.SelectedTab);
            else if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible))
            {
                UpdateSidebarWidth(vm.IsSidebarVisible);
            }
            else if (e.PropertyName == nameof(MainViewModel.SidebarWidth) && vm.IsSidebarVisible)
            {
                UpdateSidebarWidth(visible: true);
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

        vm.NewTabRequested += (_, _) => { TopBarControl.AddressBar.Focus(); TopBarControl.AddressBar.SelectAll(); };
        vm.MediaActionRequested += OnMediaActionRequested;
    }

    private async Task SwitchToTabAsync(TabViewModel tab)
    {
        if (tab == null) return;

        var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
        await _webViewService.SwitchToTabAsync(tab, webView);

        if (DataContext is MainViewModel vm)
        {
            TopBarControl?.SetAddressBarText(tab.AddressUrl);
            vm.AddressBarText = tab.AddressUrl;
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
                    if (source.Contains("settings.html"))
                    {
                        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
                        wv.CoreWebView2.ExecuteScriptAsync($"if(document.getElementById('app-version')) document.getElementById('app-version').innerText = 'Version {version} (Build stable)';");
                    }

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

                if (TopBarControl?.AddressBar.IsFocused == false)
                {
                    TopBarControl?.SetAddressBarText(tab.AddressUrl);
                    vm.AddressBarText = tab.AddressUrl;
                }

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
                if (TopBarControl?.AddressBar.IsFocused == false)
                {
                    TopBarControl?.SetAddressBarText(tab.AddressUrl);
                    vm.AddressBarText = tab.AddressUrl;
                }

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
                bool shouldRefreshImmediately = vm.SelectedTab == tab || string.IsNullOrEmpty(tab.FaviconUrl);
                if (shouldRefreshImmediately)
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

    public Microsoft.Web.WebView2.Wpf.WebView2? GetActiveWebView() => _webViewService.ActiveWebView;

    public void HandleExternalArguments(string[] args)
    {
        if (args.Length > 0 && DataContext is MainViewModel vm)
        {
            string input = args[0];
            if (System.IO.File.Exists(input))
            {
                input = new Uri(System.IO.Path.GetFullPath(input)).AbsoluteUri;
            }
            
            vm.AddTabWithUrl(input);
            
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            
            ForceForeground(handle);
            
            Activate();
            Focus();
        }
    }

    private void ForceForeground(IntPtr hWnd)
    {
        // 1. Try standard Restore
        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);

        // 2. Aggressive Thread Attachment
        IntPtr foregroundWnd = NativeMethods.GetForegroundWindow();
        uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundWnd, IntPtr.Zero);
        uint currentThreadId = NativeMethods.GetCurrentThreadId();

        if (foregroundThreadId != currentThreadId)
        {
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
        else
        {
            NativeMethods.SetForegroundWindow(hWnd);
        }
    }
}
