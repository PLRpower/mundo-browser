using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public partial class WebViewService : IWebViewService, IDisposable
{
    private readonly Dictionary<TabViewModel, WebView2> _webViews = new();
    private System.Windows.Controls.Panel? _container;
    private CoreWebView2Environment? _environment;
    private WebView2? _activeWebView;
    private readonly System.Timers.Timer _memoryTimer;
    private int _memoryOptimizationRunning;
    private readonly IAppSettingsService _settingsService;
    private readonly IAdBlockerService _adBlockerService;
    private bool _disposed;

    public bool EcoModeEnabled { get; set; } = true;
    public int EcoModeMinutes { get; set; } = 10;

    private readonly Dictionary<TabViewModel, Task<WebView2>> _initializationTasks = new();
    private readonly HashSet<TabViewModel> _removedTabs = [];
    private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

    public WebView2? ActiveWebView => _activeWebView;
    public CoreWebView2Environment? WebViewEnvironment => _environment;

    public WebView2? GetWebViewForTab(TabViewModel tab) => _webViews.TryGetValue(tab, out var wv) ? wv : null;

    public WebViewService(IAppSettingsService settingsService, IAdBlockerService adBlockerService)
    {
        _settingsService = settingsService;
        _adBlockerService = adBlockerService;

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
            AdditionalBrowserArguments = "--disable-features=DownloadBubble,DownloadBubbleV2"
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
                return existing;

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
                if (wv.CoreWebView2 != null)
                {
                    wv.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                }
            }
            catch (ObjectDisposedException) { }
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

        await _initSemaphore.WaitAsync();
        WebView2? webView = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            webView = new WebView2();
            webView.DefaultBackgroundColor = System.Drawing.Color.White;
            _container.Children.Add(webView);

            try { await webView.EnsureCoreWebView2Async(_environment); }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.ErrorCode == unchecked((int)0x80004004))
            {
                await Task.Delay(150);
                await webView.EnsureCoreWebView2Async(_environment);
            }

            ApplyTrackingPrevention(webView);
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

            // AdBlocker Integration (Network and Cosmetic)
            var adBlocker = _adBlockerService;
            if (adBlocker != null)
            {
                string currentPageUrl = tab.Url;

                // Only cross into managed code for known blocked domains.
                foreach (var domain in adBlocker.BlockedDomains)
                {
                    webView.CoreWebView2.AddWebResourceRequestedFilter(
                        $"*://{domain}/*",
                        CoreWebView2WebResourceContext.All,
                        CoreWebView2WebResourceRequestSourceKinds.Document);
                    webView.CoreWebView2.AddWebResourceRequestedFilter(
                        $"*://*.{domain}/*",
                        CoreWebView2WebResourceContext.All,
                        CoreWebView2WebResourceRequestSourceKinds.Document);
                }

                webView.CoreWebView2.WebResourceRequested += (s, e) =>
                {
                    if (adBlocker.IsAdBlockerEnabled
                        && !adBlocker.IsProtectionDisabledForSite(currentPageUrl))
                    {
                        var response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            null, 204, "No Content", ""
                        );
                        e.Response = response;
                    }
                };

                // Apply cosmetic filtering once after the DOM is ready, without a permanent observer.
                webView.CoreWebView2.DOMContentLoaded += async (_, _) =>
                {
                    try
                    {
                        string script = BuildCosmeticFilteringScript(adBlocker, currentPageUrl);
                        if (!string.IsNullOrEmpty(script))
                            await webView.CoreWebView2.ExecuteScriptAsync(script);
                    }
                    catch (ObjectDisposedException)
                    {
                        // WebView2 was disposed while we were awaiting. Just ignore.
                    }
                    catch (Exception)
                    {
                        // Ignore other errors during script injection
                    }
                };

                webView.CoreWebView2.NavigationStarting += (_, e) => currentPageUrl = e.Uri;
            }

            webView.CoreWebView2.DOMContentLoaded += (_, _) => PostSettingsToPage(webView);

            // Mapping virtuel pour les pages internes
            string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Pages");
            if (!Directory.Exists(assetsPath)) assetsPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Pages");
            
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "internals.mundobrowser", assetsPath, CoreWebView2HostResourceAccessKind.Allow);

            webView.CoreWebView2.NavigationStarting += (s, e) =>
            {
                string uri = e.Uri;
                // Si on demande une page de paramètres
                if (uri.StartsWith("about:preferences") || uri.StartsWith("edge://preferences") || uri.StartsWith("chrome://settings"))
                {
                    e.Cancel = true;
                    string hash = uri.Contains("#") ? uri.Substring(uri.IndexOf("#")) : "#general";
                    tab.AddressUrl = "about:preferences" + hash;
                    webView.CoreWebView2.Navigate("https://internals.mundobrowser/settings.html" + hash);
                }
            };

            webView.CoreWebView2.ProcessFailed += (sender, args) =>
            {
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited || 
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    try { webView.Reload(); } catch { }
                }
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
                            // Notify UI to update the address box text without triggering OnTabPropertyChanged
                            if (System.Windows.Application.Current.MainWindow is MainWindow mw && 
                                mw.DataContext is MainViewModel vm && vm.SelectedTab == tab)
                            {
                                // We use a trick to update the ViewModel property directly
                                // but we need to ensure the UI follows
                                vm.AddressBarText = tab.AddressUrl;
                            }
                        }
                        else
                        {
                            ApplySettingChange(key, value);
                            BroadcastSettingsToPages();
                        }
                    }
                }
                catch { }
            };

            setupEvents(webView);
            
            string initialUrl = tab.Url;
            if (initialUrl == "about:preferences" || initialUrl.StartsWith("edge://preferences") || initialUrl.StartsWith("chrome://settings"))
            {
                initialUrl = "https://internals.mundobrowser/settings.html#general";
                tab.AddressUrl = "about:preferences#general";
            }

            if (!string.IsNullOrEmpty(initialUrl)) webView.CoreWebView2.Navigate(initialUrl);

            lock (_initializationTasks)
            {
                if (_disposed || _removedTabs.Contains(tab))
                    throw new ObjectDisposedException(nameof(WebViewService));
            }

            _webViews[tab] = webView;
            return webView;
        }
        catch (Exception ex)
        {
            if (webView != null)
            {
                _container?.Children.Remove(webView);
                webView.Dispose();
            }

            System.Diagnostics.Debug.WriteLine($"WebView initialization failed: {ex.Message}");
            throw;
        }
        finally { _initSemaphore.Release(); }
    }

    private static string BuildCosmeticFilteringScript(IAdBlockerService adBlocker, string? pageUrl)
    {
        if (adBlocker.IsProtectionDisabledForSite(pageUrl))
            return "";

        string css = adBlocker.GetCosmeticCss() + adBlocker.GetCookieCosmeticCss();
        string cookieScript = adBlocker.GetCookieRemovalScript();

        if (string.IsNullOrWhiteSpace(css) && string.IsNullOrWhiteSpace(cookieScript))
            return "";

        string serializedCss = System.Text.Json.JsonSerializer.Serialize(css);
        return $@"
            (() => {{
                const css = {serializedCss};
                if (css && !document.getElementById('mundo-adblock-css')) {{
                    const style = document.createElement('style');
                    style.id = 'mundo-adblock-css';
                    style.textContent = css;
                    (document.head || document.documentElement).appendChild(style);
                }}

                {cookieScript}
            }})();
        ";
    }

    public async Task SwitchToTabAsync(TabViewModel tab, WebView2 webView)
    {
        if (_activeWebView != null && _activeWebView != webView)
        {
            _activeWebView.Visibility = Visibility.Collapsed;
            try
            {
                if (_activeWebView.CoreWebView2 != null)
                {
                    // Lower memory priority for background tab
                    _activeWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    
                    // Suspend the tab if it's not playing audio to save CPU/RAM
                    bool isPlayingAudio = false;
                    try { isPlayingAudio = _activeWebView.CoreWebView2.IsDocumentPlayingAudio; } catch { }
                    
                    if (!isPlayingAudio)
                    {
                        await _activeWebView.CoreWebView2.TrySuspendAsync();
                    }
                }
            }
            catch { }
        }

        _activeWebView = webView;
        _activeWebView.Visibility = Visibility.Visible;
        _activeWebView.ZoomFactor = tab.ZoomFactor;
        tab.LastAccessed = DateTime.Now;
        tab.IsDiscarded = false;
        
        try
        {
            if (_activeWebView.CoreWebView2 != null)
            {
                // Resume and restore normal memory priority
                _activeWebView.CoreWebView2.Resume();
                _activeWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
        }
        catch { }
    }

    public void RemoveTab(TabViewModel tab)
    {
        lock (_initializationTasks)
            _removedTabs.Add(tab);

        if (_webViews.TryGetValue(tab, out var webView))
        {
            _webViews.Remove(tab);
            if (_activeWebView == webView) _activeWebView = null;
            _container?.Children.Remove(webView);
            webView.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _memoryTimer.Stop();
        _memoryTimer.Elapsed -= CheckMemoryOptimization;
        _memoryTimer.Dispose();

        var webViewsToDispose = _webViews.Values
            .Concat(_container?.Children.OfType<WebView2>() ?? Enumerable.Empty<WebView2>())
            .Distinct()
            .ToList();

        foreach (var webView in webViewsToDispose)
        {
            _container?.Children.Remove(webView);
            webView.Dispose();
        }

        _webViews.Clear();
        _initializationTasks.Clear();
        _removedTabs.Clear();
        _activeWebView = null;
        _container = null;
        GC.SuppressFinalize(this);
    }
}
