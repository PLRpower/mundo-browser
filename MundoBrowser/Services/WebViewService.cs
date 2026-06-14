using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.ViewModels;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public class WebViewService : IWebViewService
{
    private readonly Dictionary<TabViewModel, WebView2> _webViews = new();
    private System.Windows.Controls.Panel? _container;
    private CoreWebView2Environment? _environment;
    private WebView2? _activeWebView;
    private readonly System.Timers.Timer _memoryTimer;
    private int _memoryOptimizationRunning;
    private readonly IAppSettingsService _settingsService;
    private readonly IAdBlockerService _adBlockerService;

    public bool EcoModeEnabled { get; set; } = true;
    public int EcoModeMinutes { get; set; } = 10;

    private readonly Dictionary<TabViewModel, Task<WebView2>> _initializationTasks = new();
    private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

    public WebView2? ActiveWebView => _activeWebView;
    public CoreWebView2Environment? WebViewEnvironment => _environment;

    public WebView2? GetWebViewForTab(TabViewModel tab) => _webViews.TryGetValue(tab, out var wv) ? wv : null;

    public WebViewService()
    {
        _settingsService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<IAppSettingsService>()
            ?? throw new InvalidOperationException("App settings service is not configured.");
        _adBlockerService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<IAdBlockerService>()
            ?? throw new InvalidOperationException("Ad blocker service is not configured.");

        EcoModeEnabled = _settingsService.Current.EcoModeEnabled;
        EcoModeMinutes = _settingsService.Current.EcoModeMinutes;

        // EcoMode: Retire de la mémoire RAM les onglets inactifs > 10 min
        _memoryTimer = new System.Timers.Timer(60000); 
        _memoryTimer.Elapsed += CheckMemoryOptimization;
        _memoryTimer.Start();
    }

    public async Task InitializeAsync(System.Windows.Controls.Panel container)
    {
        _container = container;
        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            EnableTrackingPrevention = true,
            AdditionalBrowserArguments = "--disable-features=DownloadBubble,DownloadBubbleV2"
        };

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MundoBrowser", "WebView2Data");
        
        Directory.CreateDirectory(userDataFolder);
        _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
    }

    public async Task<WebView2> GetOrCreateWebViewAsync(TabViewModel tab, Action<WebView2> setupEvents)
    {
        if (_webViews.TryGetValue(tab, out var existing))
            return existing;

        Task<WebView2>? existingTask = null;
        lock (_initializationTasks)
        {
            if (_initializationTasks.TryGetValue(tab, out existingTask)) { }
        }

        if (existingTask != null) return await existingTask;

        var initTask = InitializeWebViewInternal(tab, setupEvents);
        lock (_initializationTasks)
        {
            _initializationTasks[tab] = initTask;
        }

        try { 
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
            lock (_initializationTasks) { _initializationTasks.Remove(tab); }
        }
    }

    private async Task<WebView2> InitializeWebViewInternal(TabViewModel tab, Action<WebView2> setupEvents)
    {
        if (_container == null) throw new InvalidOperationException("WebViewService must be initialized with a container before use.");

        await _initSemaphore.WaitAsync();
        try
        {
            var webView = new WebView2();
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

            _webViews[tab] = webView;
            return webView;
        }
        catch (Exception ex)
        {
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

    private void ApplySettingChange(string? key, System.Text.Json.JsonElement value)
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext as MainViewModel;

        switch (key)
        {
            case "startPage":
                _settingsService.Update(settings => settings.StartPage = value.GetString() ?? "");
                break;

            case "ecoModeEnabled":
                EcoModeEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.EcoModeEnabled = EcoModeEnabled);
                break;

            case "ecoModeDuration":
                EcoModeMinutes = ReadInt(value, 10);
                _settingsService.Update(settings => settings.EcoModeMinutes = EcoModeMinutes);
                EcoModeMinutes = _settingsService.Current.EcoModeMinutes;
                break;

            case "sidebarVisible":
                if (vm != null)
                    vm.IsSidebarVisible = value.GetBoolean();
                else
                    _settingsService.Update(settings => settings.IsSidebarVisible = value.GetBoolean());
                break;

            case "sidebarWidth":
                var width = ReadDouble(value, 250);
                if (vm != null)
                    vm.SetSidebarWidth(width);
                else
                    _settingsService.Update(settings => settings.SidebarWidth = width);
                break;

            case "adBlockerEnabled":
                if (vm != null)
                    vm.IsAdBlockerEnabled = value.GetBoolean();
                else
                    _adBlockerService.IsAdBlockerEnabled = value.GetBoolean();
                break;

            case "cookieBlockerEnabled":
                if (vm != null)
                    vm.IsCookieBlockerEnabled = value.GetBoolean();
                else
                    _adBlockerService.IsCookieBlockerEnabled = value.GetBoolean();
                break;

            case "trackingPreventionEnabled":
                var enabled = value.GetBoolean();
                _settingsService.Update(settings => settings.IsTrackingPreventionEnabled = enabled);
                foreach (var webView in _webViews.Values)
                    ApplyTrackingPrevention(webView);
                break;

            case "passwordAutosaveEnabled":
                var passwordEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.IsPasswordAutosaveEnabled = passwordEnabled);
                foreach (var webView in _webViews.Values)
                    ApplyAutofillSettings(webView);
                break;

            case "generalAutofillEnabled":
                var generalEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.IsGeneralAutofillEnabled = generalEnabled);
                foreach (var webView in _webViews.Values)
                    ApplyAutofillSettings(webView);
                break;
        }
    }

    private void ApplyAutofillSettings(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2 != null && webView.CoreWebView2.Settings != null)
            {
                webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = _settingsService.Current.IsPasswordAutosaveEnabled;
                webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = _settingsService.Current.IsGeneralAutofillEnabled;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply autofill settings: {ex.Message}");
        }
    }

    private void ApplyTrackingPrevention(WebView2 webView)
    {
        try
        {
            webView.CoreWebView2.Profile.PreferredTrackingPreventionLevel =
                _settingsService.Current.IsTrackingPreventionEnabled
                    ? CoreWebView2TrackingPreventionLevel.Balanced
                    : CoreWebView2TrackingPreventionLevel.None;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply tracking prevention setting: {ex.Message}");
        }
    }

    private void BroadcastSettingsToPages()
    {
        foreach (var webView in _webViews.Values)
            PostSettingsToPage(webView);
    }

    private void PostSettingsToPage(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2 == null
                || !webView.CoreWebView2.Source.StartsWith(
                    "https://internals.mundobrowser/settings.html",
                    StringComparison.OrdinalIgnoreCase))
                return;

            var settings = _settingsService.Current;
            var message = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "initSettings",
                startPage = settings.StartPage,
                ecoModeEnabled = settings.EcoModeEnabled,
                ecoModeDuration = settings.EcoModeMinutes,
                sidebarVisible = settings.IsSidebarVisible,
                sidebarWidth = settings.SidebarWidth,
                adBlockerEnabled = settings.IsAdBlockerEnabled,
                cookieBlockerEnabled = settings.IsCookieBlockerEnabled,
                trackingPreventionEnabled = settings.IsTrackingPreventionEnabled,
                passwordAutosaveEnabled = settings.IsPasswordAutosaveEnabled,
                generalAutofillEnabled = settings.IsGeneralAutofillEnabled
            });

            webView.CoreWebView2.PostWebMessageAsJson(message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to synchronize settings page: {ex.Message}");
        }
    }

    private static int ReadInt(System.Text.Json.JsonElement value, int fallback)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        return int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    private static double ReadDouble(System.Text.Json.JsonElement value, double fallback)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        return double.TryParse(
            value.GetString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number)
            ? number
            : fallback;
    }

    private void CheckMemoryOptimization(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!EcoModeEnabled) return;
        if (Interlocked.Exchange(ref _memoryOptimizationRunning, 1) == 1) return;

        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var now = DateTime.Now;
                var tabsToDiscard = new Queue<TabViewModel>(
                    _webViews
                        .Where(entry =>
                            entry.Value != _activeWebView
                            && (now - entry.Key.LastAccessed).TotalMinutes > EcoModeMinutes)
                        .Select(entry => entry.Key));

                DiscardTabsAtIdle(tabsToDiscard);
            }
            catch
            {
                Volatile.Write(ref _memoryOptimizationRunning, 0);
            }
        }), System.Windows.Threading.DispatcherPriority.SystemIdle);
    }

    private void DiscardTabsAtIdle(Queue<TabViewModel> tabs)
    {
        if (tabs.Count == 0)
        {
            Volatile.Write(ref _memoryOptimizationRunning, 0);
            return;
        }

        try
        {
            DiscardTab(tabs.Dequeue());
        }
        catch
        {
            Volatile.Write(ref _memoryOptimizationRunning, 0);
            return;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            new Action(() => DiscardTabsAtIdle(tabs)),
            System.Windows.Threading.DispatcherPriority.SystemIdle);
    }

    private void DiscardTab(TabViewModel tab)
    {
        if (_webViews.TryGetValue(tab, out var webView))
        {
            _container?.Children.Remove(webView);
            webView.Dispose();
            _webViews.Remove(tab);
            tab.IsDiscarded = true;
        }
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
        if (_webViews.TryGetValue(tab, out var webView))
        {
            _webViews.Remove(tab);
            if (_activeWebView == webView) _activeWebView = null;
            _container?.Children.Remove(webView);
            webView.Dispose();
        }
    }
}
