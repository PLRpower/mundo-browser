using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public sealed partial class WebViewService : IWebViewService, IDisposable
{
    private sealed class TabContainer
    {
        public required System.Windows.Controls.Grid ContainerGrid { get; init; }
        public required WebView2 MainWebView { get; init; }
    }

    private readonly Dictionary<TabViewModel, WebView2> _webViews = new();
    private readonly Dictionary<TabViewModel, TabContainer> _tabContainers = new();
    private TabContainer? _activeTabContainer;
    private System.Windows.Controls.Panel? _container;
    private CoreWebView2Environment? _environment;
    private WebView2? _activeWebView;
    private TabViewModel? _activeTab;
    private TabViewModel? _splitPrimaryTab;
    private TabViewModel? _splitSecondaryTab;
    private readonly System.Timers.Timer _memoryTimer;
    private int _memoryOptimizationRunning;
    private readonly IAppSettingsService _settingsService;
    private readonly IAdBlockerService _adBlockerService;
    private readonly IUpdateService _updateService;
    private bool _disposed;

    public bool EcoModeEnabled { get; set; } = true;
    public int EcoModeMinutes { get; set; } = 10;

    private readonly Dictionary<TabViewModel, Task<WebView2>> _initializationTasks = new();
    private readonly HashSet<TabViewModel> _removedTabs = [];
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly Dictionary<WebView2, HashSet<CoreWebView2DownloadOperation>> _activeDownloads = new();
    private readonly Dictionary<WebView2, string> _adBlockScriptIds = new();
    private readonly List<WebView2> _pendingDisposalWebViews = new();
    private readonly List<(WebView2 WebView, UIElement? Element)> _pendingGracefulDisposals = new();

    public WebView2? ActiveWebView => _activeWebView;
    public CoreWebView2Environment? WebViewEnvironment => _environment;
    public event Action? BrowserProcessExited;

    public WebView2? GetWebViewForTab(TabViewModel tab)
    {
        lock (_initializationTasks)
        {
            return _webViews.TryGetValue(tab, out var wv) ? wv : null;
        }
    }

    public WebView2? GetAnyActiveWebView()
    {
        if (_activeWebView != null) return _activeWebView;
        lock (_initializationTasks)
        {
            foreach (var w in _webViews.Values)
            {
                try { if (w.CoreWebView2 != null) return w; }
                catch { }
            }
        }
        return null;
    }

    public IReadOnlyList<WebView2> GetAllWebViews()
    {
        lock (_initializationTasks)
        {
            return [.. _webViews.Values];
        }
    }

    public WebViewService(IAppSettingsService settingsService, IAdBlockerService adBlockerService, IUpdateService updateService)
    {
        _settingsService = settingsService;
        _adBlockerService = adBlockerService;
        _updateService = updateService;

        EcoModeEnabled = _settingsService.Current.EcoModeEnabled;
        EcoModeMinutes = _settingsService.Current.EcoModeMinutes;

        // EcoMode: Retire de la mémoire RAM les onglets inactifs > 10 min
        _memoryTimer = new System.Timers.Timer(60000); 
        _memoryTimer.Elapsed += CheckMemoryOptimization;
        _memoryTimer.Start();
    }

    public async Task InitializeAsync(System.Windows.Controls.Panel container)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _container = container;
        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            EnableTrackingPrevention = true,
            AdditionalBrowserArguments = string.Join(" ",
                "--enable-gpu-rasterization",
                "--enable-zero-copy",
                "--enable-gpu-memory-buffer-video-frames",
                "--enable-hardware-overlays=single-fullscreen,single-on-top,underlay",
                "--process-per-site",
                "--enable-features=WebGPU,CanvasOopRasterization,ParallelDownloading,OverlayScrollbar,TouchpadAndWheelScrollLatching,ThrottleDisplayNoneAndVisibilityHiddenCrossOriginIframes,IntensiveWakeUpThrottling,BackForwardCacheMemoryControls",
                "--use-angle=d3d11",
                "--enable-webgl",
                "--enable-smooth-scrolling",
                "--enable-accelerated-2d-canvas",
                "--enable-accelerated-video-decode",
                "--disable-features=AutomaticTabDiscarding",
                "--enable-quic"
            )
        };

        var userDataFolder = Path.Combine(AppRuntime.LocalDataDirectory, "WebView2Data");
        
        Directory.CreateDirectory(userDataFolder);
        _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
    }

    public async Task<WebView2> GetOrCreateWebViewAsync(TabViewModel tab, Action<WebView2> setupEvents)
    {
        Task<WebView2> initTask;
        lock (_initializationTasks)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _removedTabs.Remove(tab);

            if (_webViews.TryGetValue(tab, out var existing))
            {
                bool isAlive = false;
                try
                {
                    isAlive = existing.CoreWebView2 != null;
                }
                catch
                {
                    isAlive = false;
                }

                if (isAlive)
                    return existing;

                DiscardTab(tab, force: true);
            }

            if (_initializationTasks.TryGetValue(tab, out var existingTask))
            {
                initTask = existingTask;
            }
            else
            {
                initTask = InitializeWebViewInternal(tab, setupEvents);
                _initializationTasks[tab] = initTask;
            }
        }

        try
        {
            var wv = await initTask;
            try
            {
                if (wv.CoreWebView2 is { } core)
                {
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
            }
            catch { }
            return wv;
        }
        finally
        {
            lock (_initializationTasks)
            {
                if (_initializationTasks.TryGetValue(tab, out var activeTask)
                    && ReferenceEquals(activeTask, initTask))
                    _initializationTasks.Remove(tab);
            }
        }
    }

    private async Task<WebView2> InitializeWebViewInternal(TabViewModel tab, Action<WebView2> setupEvents)
    {
        if (_container == null) throw new InvalidOperationException("WebViewService must be initialized with a container before use.");

        tab.IsLoading = true;

        await _initSemaphore.WaitAsync();
        WebView2? webView = null;
        System.Windows.Controls.Grid? containerGrid = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            containerGrid = new System.Windows.Controls.Grid
            {
                Background = System.Windows.Media.Brushes.White,
                Visibility = Visibility.Collapsed
            };

            webView = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.White,
                Visibility = Visibility.Collapsed
            };
            containerGrid.Children.Add(webView);

            _container.Children.Add(containerGrid);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await webView.EnsureCoreWebView2Async(_environment);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.ErrorCode == unchecked((int)0x80004004) && attempt < 20)
                {
                    await Task.Delay(150);
                }
            }

            ApplyTrackingPrevention(webView, tab.Url);
            ApplyAutofillSettings(webView);

            // Install the scrollbar style once per document without observing every DOM mutation.
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                (function() {
                    const css = `
                        ::-webkit-scrollbar {
                            width: 10px !important;
                            height: 10px !important;
                        }
                        ::-webkit-scrollbar-button {
                            display: none !important;
                        }
                        ::-webkit-scrollbar-track {
                            background: transparent !important;
                        }
                        ::-webkit-scrollbar-thumb {
                            background: rgba(128, 128, 128, 0.3) !important;
                            border-radius: 10px !important;
                            border: 3px solid transparent !important;
                            background-clip: content-box !important;
                        }
                        ::-webkit-scrollbar-thumb:hover {
                            background: rgba(128, 128, 128, 0.6) !important;
                            background-clip: content-box !important;
                        }
                        ::-webkit-scrollbar-corner {
                            background: transparent !important;
                        }
                    `;

                    const inject = () => {
                        if (document.getElementById('mundo-custom-scrollbar')) return;
                        const style = document.createElement('style');
                        style.id = 'mundo-custom-scrollbar';
                        style.textContent = css;
                        (document.head || document.documentElement).appendChild(style);
                    };

                    if (document.documentElement) inject();
                    else document.addEventListener('DOMContentLoaded', inject, { once: true });
                })();
            ");

            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ChromeWebStoreHelper.InjectionScript);

            // AdBlocker Integration (Network and Cosmetic)
            var adBlocker = _adBlockerService;
            string currentPageUrl = tab.Url;
            bool isAdBlockActiveForPage = true;
            bool isCookieBlockActiveForPage = true;

            void RefreshPageProtectionState(string? candidateUrl = null)
            {
                if (!string.IsNullOrWhiteSpace(candidateUrl) && !candidateUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    currentPageUrl = candidateUrl;

                if (adBlocker == null) return;
                string? effectiveUrl = ResolveEffectiveSiteUrl(tab, currentPageUrl, adBlocker);
                isAdBlockActiveForPage = IsAdBlockerActiveForTab(tab, effectiveUrl, adBlocker);
                isCookieBlockActiveForPage = IsCookieBlockerActiveForTab(tab, effectiveUrl, adBlocker);
                ApplyTrackingPrevention(webView, effectiveUrl);
            }

            if (adBlocker != null)
            {
                RefreshPageProtectionState(currentPageUrl);

                // Inject adblock, cosmetic CSS, YouTube skipper and cookie modal scripts on document creation
                string initialScript = adBlocker.GetInjectionScript();
                if (!string.IsNullOrEmpty(initialScript))
                {
                    var scriptId = await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(initialScript);
                    _adBlockScriptIds[webView] = scriptId;
                }

                // Intercept web resources with cached site status
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Script);
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
                webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Other);

                CoreWebView2WebResourceResponse? cachedBlockedResponse = null;
                webView.CoreWebView2.WebResourceRequested += (_, e) =>
                {
                    if (!isAdBlockActiveForPage || !adBlocker.IsAdBlockerEnabled) return;

                    bool cookieBlockActive = isCookieBlockActiveForPage && adBlocker.IsCookieBlockerEnabled;
                    if (adBlocker.ShouldBlockUrl(e.Request.Uri, cookieBlockActive))
                    {
                        cachedBlockedResponse ??= webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            null, 204, "No Content", "");
                        e.Response = cachedBlockedResponse;
                    }
                };

                webView.CoreWebView2.SourceChanged += (_, _) =>
                {
                    RefreshPageProtectionState(webView.CoreWebView2.Source);
                };
            }

            webView.CoreWebView2.DOMContentLoaded += (_, _) => PostSettingsToPage(webView);

            // Mapping virtuel pour les pages internes
            string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Pages");
            if (!Directory.Exists(assetsPath)) assetsPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Pages");
            
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "internals.mundobrowser", assetsPath, CoreWebView2HostResourceAccessKind.Allow);

            webView.CoreWebView2.NavigationStarting += (_, e) =>
            {
                string uri = e.Uri;
                // Si on demande une page de paramètres
                if (uri.StartsWith("about:preferences", StringComparison.OrdinalIgnoreCase)
                    || uri.StartsWith("edge://preferences", StringComparison.OrdinalIgnoreCase)
                    || uri.StartsWith("chrome://settings", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    int hashIdx = uri.IndexOf('#');
                    string hash = hashIdx >= 0 ? uri[hashIdx..] : "#general";
                    tab.AddressUrl = "about:preferences" + hash;
                    webView.CoreWebView2.Navigate("https://internals.mundobrowser/settings.html" + hash);
                    return;
                }

                RefreshPageProtectionState(uri);
            };

            webView.CoreWebView2.ProcessFailed += (sender, args) =>
            {
                HandleProcessFailed(webView, args);
            };

            webView.WebMessageReceived += (s, e) =>
            {
                try
                {
                    var json = e.WebMessageAsJson;
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var type) && type.GetString() == "settingChanged")
                    {
                        var key = root.GetProperty("key").GetString();
                        var value = root.GetProperty("value");

                        if (key == "makeDefault")
                        {
                            try
                            {
                                WindowsDefaultBrowserRegistration.Register();
                                // Open Windows Default Apps settings
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
                            }
                            catch { }
                        }
                        else if (key == "subPage")
                        {
                            var pageId = value.GetString();
                            tab.AddressUrl = $"about:preferences#{pageId}";
                        }
                        else
                        {
                            ApplySettingChange(key, value);
                            BroadcastSettingsToPages();
                        }
                    }
                    else if (root.TryGetProperty("type", out var updateType) && updateType.GetString() == "checkForUpdates")
                    {
                        _ = _updateService.CheckForUpdatesManualAsync();
                    }
                }
                catch { }
            };

            setupEvents(webView);
            
            if (!tab.IsCreatedFromNewWindow)
            {
                string initialUrl = tab.Url;
                if (initialUrl.StartsWith("about:preferences", StringComparison.OrdinalIgnoreCase)
                    || initialUrl.StartsWith("edge://preferences", StringComparison.OrdinalIgnoreCase)
                    || initialUrl.StartsWith("chrome://settings", StringComparison.OrdinalIgnoreCase))
                {
                    int hashIdx = initialUrl.IndexOf('#');
                    string hash = hashIdx >= 0 ? initialUrl[hashIdx..] : "#general";
                    initialUrl = "https://internals.mundobrowser/settings.html" + hash;
                    tab.AddressUrl = "about:preferences" + hash;
                }

                if (!string.IsNullOrEmpty(initialUrl))
                {
                    try
                    {
                        webView.CoreWebView2.Navigate(initialUrl);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to navigate to initial URL '{initialUrl}': {ex.Message}");
                        tab.IsLoading = false;
                    }
                }
                else tab.IsLoading = false;
            }

            lock (_initializationTasks)
            {
                ObjectDisposedException.ThrowIf(_disposed || _removedTabs.Contains(tab), this);

                _webViews[tab] = webView;
                _tabContainers[tab] = new TabContainer
                {
                    ContainerGrid = containerGrid,
                    MainWebView = webView
                };
            }
            return webView;
        }
        catch (Exception ex)
        {
            tab.IsLoading = false;
            RemoveFromParentPanel(containerGrid);
            webView?.Dispose();

            System.Diagnostics.Debug.WriteLine($"WebView initialization failed: {ex.Message}");
            throw;
        }
        finally { _initSemaphore.Release(); }
    }



    private void HandleProcessFailed(WebView2 webView, CoreWebView2ProcessFailedEventArgs args)
    {
        System.Diagnostics.Debug.WriteLine($"WebView ProcessFailed: Kind={args.ProcessFailedKind}, Reason={args.Reason}, ExitCode={args.ExitCode}");

        if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var deadTabs = _webViews.Keys.ToList();
                    foreach (var deadTab in deadTabs)
                    {
                        DiscardTab(deadTab, force: true);
                    }
                    _activeWebView = null;
                    _activeTabContainer = null;
                    _activeTab = null;
                    _splitPrimaryTab = null;
                    _splitSecondaryTab = null;

                    BrowserProcessExited?.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error recovering from browser process exit: {ex.Message}");
                }
            });
        }
        else if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                 args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            try
            {
                webView.Reload();
            }
            catch { }
        }
    }

    public Task SwitchToTabAsync(TabViewModel tab, WebView2 webView)
    {
        foreach (var (otherTab, container) in _tabContainers)
        {
            if (_container != null)
                ReParentContainer(container.ContainerGrid, _container);

            if (otherTab != tab)
            {
                container.ContainerGrid.Visibility = Visibility.Collapsed;
                container.MainWebView.Visibility = Visibility.Collapsed;
                if (!IsTabProtected(otherTab, container.MainWebView))
                    TrySetLowMemoryTarget(container.MainWebView);
            }
        }

        if (_tabContainers.TryGetValue(tab, out var currentContainer))
        {
            _activeTab = tab;
            _splitPrimaryTab = null;
            _splitSecondaryTab = null;
            _activeTabContainer = currentContainer;
            _activeTabContainer.ContainerGrid.Visibility = Visibility.Visible;
            _activeTabContainer.MainWebView.Visibility = Visibility.Visible;
            _activeWebView = webView;
            try
            {
                _activeWebView.ZoomFactor = tab.ZoomFactor;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set ZoomFactor: {ex.Message}");
            }
            tab.LastAccessed = DateTime.Now;
            tab.IsDiscarded = false;
            
            TryResumeWebView(_activeWebView);
            ApplyTrackingPrevention(_activeWebView, tab.AddressUrl);
        }

        return Task.CompletedTask;
    }

    public Task UpdateSplitViewLayoutAsync(
        bool isSplit,
        TabViewModel? primaryTab,
        TabViewModel? secondaryTab,
        TabViewModel? activeTab,
        System.Windows.Controls.Panel? primaryHost,
        System.Windows.Controls.Panel? secondaryHost)
    {
        if (_container == null) return Task.CompletedTask;

        _splitPrimaryTab = isSplit ? primaryTab : null;
        _splitSecondaryTab = isSplit ? secondaryTab : null;
        _activeTab = activeTab ?? primaryTab;

        if (isSplit && primaryTab != null && secondaryTab != null && primaryHost != null && secondaryHost != null)
        {
            primaryTab.LastAccessed = DateTime.Now;
            secondaryTab.LastAccessed = DateTime.Now;
            primaryTab.IsDiscarded = false;
            secondaryTab.IsDiscarded = false;

            foreach (var kvp in _tabContainers)
            {
                var tab = kvp.Key;
                var container = kvp.Value;

                if (tab == primaryTab)
                {
                    ReParentContainer(container.ContainerGrid, primaryHost);
                    container.ContainerGrid.Visibility = Visibility.Visible;
                    container.MainWebView.Visibility = Visibility.Visible;
                    TryResumeWebView(container.MainWebView);
                }
                else if (tab == secondaryTab)
                {
                    ReParentContainer(container.ContainerGrid, secondaryHost);
                    container.ContainerGrid.Visibility = Visibility.Visible;
                    container.MainWebView.Visibility = Visibility.Visible;
                    TryResumeWebView(container.MainWebView);
                }
                else
                {
                    ReParentContainer(container.ContainerGrid, _container);
                    container.ContainerGrid.Visibility = Visibility.Collapsed;
                    container.MainWebView.Visibility = Visibility.Collapsed;
                    if (!IsTabProtected(tab, container.MainWebView))
                        TrySetLowMemoryTarget(container.MainWebView);
                }
            }

            if (activeTab != null && _webViews.TryGetValue(activeTab, out var activeWv))
            {
                _activeWebView = activeWv;
            }
        }
        else
        {
            if (activeTab != null)
            {
                activeTab.LastAccessed = DateTime.Now;
                activeTab.IsDiscarded = false;
            }

            foreach (var kvp in _tabContainers)
            {
                var tab = kvp.Key;
                var container = kvp.Value;

                ReParentContainer(container.ContainerGrid, _container);
                if (tab == activeTab)
                {
                    container.ContainerGrid.Visibility = Visibility.Visible;
                    container.MainWebView.Visibility = Visibility.Visible;
                    TryResumeWebView(container.MainWebView);
                    _activeWebView = container.MainWebView;
                    _activeTabContainer = container;
                }
                else
                {
                    container.ContainerGrid.Visibility = Visibility.Collapsed;
                    container.MainWebView.Visibility = Visibility.Collapsed;
                    if (!IsTabProtected(tab, container.MainWebView))
                        TrySetLowMemoryTarget(container.MainWebView);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void ReParentContainer(System.Windows.Controls.Grid grid, System.Windows.Controls.Panel newParent)
    {
        if (grid.Parent is System.Windows.Controls.Panel oldParent)
        {
            if (oldParent == newParent) return;
            oldParent.Children.Remove(grid);
        }
        newParent.Children.Add(grid);
    }

    private void RemoveFromParentPanel(UIElement? element)
    {
        if (element == null) return;
        if (element is FrameworkElement { Parent: System.Windows.Controls.Panel parentPanel })
            parentPanel.Children.Remove(element);
        else
            _container?.Children.Remove(element);
    }

    private static void TryResumeWebView(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2 != null)
            {
                if (webView.CoreWebView2.IsSuspended)
                    webView.CoreWebView2.Resume();
                if (webView.CoreWebView2.MemoryUsageTargetLevel != CoreWebView2MemoryUsageTargetLevel.Normal)
                    webView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
        }
        catch { }
    }

    private static void TrySuspendWebView(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2 is { IsSuspended: false, IsDocumentPlayingAudio: false })
            {
                _ = webView.CoreWebView2.TrySuspendAsync();
            }
        }
        catch { }
    }

    private static void TrySetLowMemoryTarget(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2 != null && webView.CoreWebView2.MemoryUsageTargetLevel != CoreWebView2MemoryUsageTargetLevel.Low)
            {
                webView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
        }
        catch { }
    }

    public Task<WebView2?> OpenDevToolsForTabAsync(TabViewModel tab)
    {
        var webView = GetWebViewForTab(tab);
        if (webView != null)
        {
            try
            {
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.OpenDevToolsWindow();
                    return Task.FromResult<WebView2?>(webView);
                }
            }
            catch { }
        }
        return Task.FromResult<WebView2?>(null);
    }

    public Task ToggleDevToolsForTabAsync(TabViewModel tab)
    {
        return OpenDevToolsForTabAsync(tab);
    }

    public bool HasActiveDownloads
    {
        get
        {
            lock (_activeDownloads)
            {
                return _activeDownloads.Values.Any(set => set.Count > 0);
            }
        }
    }

    public int ActiveDownloadCount
    {
        get
        {
            lock (_activeDownloads)
            {
                return _activeDownloads.Values.Sum(set => set.Count);
            }
        }
    }

    public event Action? ActiveDownloadsChanged;

    public void OpenDownloadDialog()
    {
        try
        {
            var targetWebView = _activeWebView ?? _webViews.Values.FirstOrDefault(w => w.CoreWebView2 != null);
            if (targetWebView?.CoreWebView2 != null)
            {
                if (targetWebView.CoreWebView2.IsDefaultDownloadDialogOpen)
                {
                    targetWebView.CoreWebView2.CloseDefaultDownloadDialog();
                }
                else
                {
                    targetWebView.CoreWebView2.OpenDefaultDownloadDialog();
                }
            }
        }
        catch { }
    }

    public async Task ConfigurePopupWebViewAsync(WebView2 popupWebView, string? initialUrl)
    {
        if (popupWebView.CoreWebView2 == null)
            return;

        ApplyTrackingPrevention(popupWebView, initialUrl);
        ApplyAutofillSettings(popupWebView);

        popupWebView.CoreWebView2.DownloadStarting += (_, args) =>
        {
            RegisterActiveDownload(popupWebView, args.DownloadOperation);
        };

        var adBlocker = _adBlockerService;
        if (adBlocker == null)
            return;

        string currentPageUrl = initialUrl ?? string.Empty;
        bool isAdBlockActiveForPage = adBlocker.IsAdBlockerEnabledForSite(currentPageUrl);
        bool isCookieBlockActiveForPage = adBlocker.IsCookieBlockerEnabledForSite(currentPageUrl);

        void RefreshPopupProtectionState(string? candidateUrl)
        {
            if (!string.IsNullOrWhiteSpace(candidateUrl) && !candidateUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                currentPageUrl = candidateUrl;

            isAdBlockActiveForPage = adBlocker.IsAdBlockerEnabledForSite(currentPageUrl);
            isCookieBlockActiveForPage = adBlocker.IsCookieBlockerEnabledForSite(currentPageUrl);
            ApplyTrackingPrevention(popupWebView, currentPageUrl);
        }

        string initialScript = adBlocker.GetInjectionScript();
        if (!string.IsNullOrEmpty(initialScript))
        {
            await popupWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(initialScript);
        }

        popupWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Script);
        popupWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
        popupWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);
        popupWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
        popupWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Other);

        popupWebView.CoreWebView2.WebResourceRequested += (_, e) =>
        {
            if (!isAdBlockActiveForPage || !adBlocker.IsAdBlockerEnabled) return;
            bool cookieBlockActive = isCookieBlockActiveForPage && adBlocker.IsCookieBlockerEnabled;
            if (adBlocker.ShouldBlockUrl(e.Request.Uri, cookieBlockActive))
            {
                e.Response = popupWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 204, "No Content", "");
            }
        };

        popupWebView.CoreWebView2.NavigationStarting += (_, e) => RefreshPopupProtectionState(e.Uri);
        popupWebView.CoreWebView2.SourceChanged += (_, _) => RefreshPopupProtectionState(popupWebView.CoreWebView2.Source);
    }

    public void RegisterActiveDownload(WebView2 webView, CoreWebView2DownloadOperation download)
    {
        lock (_activeDownloads)
        {
            if (!_activeDownloads.TryGetValue(webView, out var downloads))
            {
                downloads = new HashSet<CoreWebView2DownloadOperation>();
                _activeDownloads[webView] = downloads;
            }
            downloads.Add(download);
        }

        ActiveDownloadsChanged?.Invoke();

        void OnStateChanged(object? s, object e)
        {
            if (download.State is CoreWebView2DownloadState.Completed or CoreWebView2DownloadState.Interrupted)
            {
                download.StateChanged -= OnStateChanged;
                OnDownloadEnded(webView, download);
                return;
            }
            ActiveDownloadsChanged?.Invoke();
        }

        download.StateChanged += OnStateChanged;
    }

    private void OnDownloadEnded(WebView2 webView, CoreWebView2DownloadOperation download)
    {
        bool shouldDispose = false;
        lock (_activeDownloads)
        {
            if (_activeDownloads.TryGetValue(webView, out var downloads))
            {
                downloads.Remove(download);
                if (downloads.Count == 0)
                {
                    _activeDownloads.Remove(webView);
                    if (_pendingDisposalWebViews.Remove(webView))
                    {
                        shouldDispose = true;
                    }
                }
            }
        }

        ActiveDownloadsChanged?.Invoke();

        if (shouldDispose)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (webView.Parent is UIElement parent && parent != _container)
                    {
                        RemoveFromParentPanel(parent);
                    }
                    RemoveFromParentPanel(webView);
                    webView.Dispose();
                }
                catch { }
            });
        }
    }

    public void RemoveTab(TabViewModel tab)
    {
        TabContainer? container;
        WebView2? webView = null;
        lock (_initializationTasks)
        {
            _removedTabs.Add(tab);

            if (_activeTab == tab) _activeTab = null;
            if (_splitPrimaryTab == tab) _splitPrimaryTab = null;
            if (_splitSecondaryTab == tab) _splitSecondaryTab = null;

            if (_tabContainers.Remove(tab, out container))
            {
                _webViews.Remove(tab);
                _adBlockScriptIds.Remove(container.MainWebView);
                if (_activeWebView == container.MainWebView) _activeWebView = null;
                if (_activeTabContainer == container) _activeTabContainer = null;
            }
            else if (_webViews.Remove(tab, out webView))
            {
                _adBlockScriptIds.Remove(webView);
                if (_activeWebView == webView) _activeWebView = null;
            }
        }

        if (container != null)
            ScheduleGracefulDisposal(container.MainWebView, container.ContainerGrid);
        else if (webView != null)
            ScheduleGracefulDisposal(webView, null);
    }

    private void ScheduleGracefulDisposal(WebView2 webView, UIElement? element)
    {
        if (element != null) element.Visibility = Visibility.Collapsed;
        webView.Visibility = Visibility.Collapsed;

        try
        {
            if (webView.CoreWebView2 != null)
            {
                _ = webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }));
                            Object.defineProperty(document, 'visibilityState', { value: 'hidden', writable: true });
                            document.dispatchEvent(new Event('visibilitychange'));
                            window.dispatchEvent(new Event('beforeunload'));
                        } catch(e) {}
                    })();
                ");
            }
        }
        catch { }

        lock (_activeDownloads)
        {
            if (_activeDownloads.TryGetValue(webView, out var downloads) && downloads.Count > 0)
            {
                _pendingDisposalWebViews.Add(webView);
                return;
            }
        }

        lock (_pendingGracefulDisposals)
        {
            if (_disposed)
            {
                if (element != null) RemoveFromParentPanel(element);
                else if (webView.Parent is UIElement parent && parent != _container) RemoveFromParentPanel(parent);
                else RemoveFromParentPanel(webView);
                try { webView.Dispose(); } catch { }
                return;
            }
            _pendingGracefulDisposals.Add((webView, element));
        }

        _ = CompleteGracefulDisposalAsync(webView, element);
    }

    private async Task CompleteGracefulDisposalAsync(WebView2 webView, UIElement? element)
    {
        await Task.Delay(400).ConfigureAwait(false);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;

        _ = dispatcher.BeginInvoke(() =>
        {
            lock (_pendingGracefulDisposals)
            {
                if (!_pendingGracefulDisposals.Remove((webView, element)))
                    return;
            }

            lock (_activeDownloads)
            {
                if (_activeDownloads.TryGetValue(webView, out var downloads) && downloads.Count > 0)
                {
                    _pendingDisposalWebViews.Add(webView);
                    return;
                }
            }

            if (element != null) RemoveFromParentPanel(element);
            else if (webView.Parent is UIElement parent && parent != _container) RemoveFromParentPanel(parent);
            else RemoveFromParentPanel(webView);

            try { webView.Dispose(); } catch { }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _memoryTimer.Stop();
        _memoryTimer.Elapsed -= CheckMemoryOptimization;
        _memoryTimer.Dispose();

        lock (_pendingGracefulDisposals)
        {
            foreach (var (wv, el) in _pendingGracefulDisposals)
            {
                if (el != null) RemoveFromParentPanel(el);
                else if (wv.Parent is UIElement p && p != _container) RemoveFromParentPanel(p);
                else RemoveFromParentPanel(wv);
                try { wv.Dispose(); } catch { }
            }
            _pendingGracefulDisposals.Clear();
        }

        lock (_activeDownloads)
        {
            foreach (var wv in _pendingDisposalWebViews)
            {
                if (wv.Parent is UIElement p && p != _container)
                    RemoveFromParentPanel(p);
                RemoveFromParentPanel(wv);
                try { wv.Dispose(); } catch { }
            }
            _pendingDisposalWebViews.Clear();
        }

        List<TabContainer> containersToDispose;
        lock (_initializationTasks)
        {
            containersToDispose = [.. _tabContainers.Values];
            _tabContainers.Clear();
            _webViews.Clear();
            _adBlockScriptIds.Clear();
            _initializationTasks.Clear();
            _removedTabs.Clear();
        }

        foreach (var container in containersToDispose)
        {
            RemoveFromParentPanel(container.ContainerGrid);
            try { container.MainWebView.Dispose(); } catch { }
        }

        _activeWebView = null;
        _activeTabContainer = null;
        _container = null;
        GC.SuppressFinalize(this);
    }
}
