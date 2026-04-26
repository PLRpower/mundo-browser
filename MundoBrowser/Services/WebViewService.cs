using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    public bool EcoModeEnabled { get; set; } = true;
    public int EcoModeMinutes { get; set; } = 10;

    private readonly Dictionary<TabViewModel, Task<WebView2>> _initializationTasks = new();
    private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

    public WebView2? ActiveWebView => _activeWebView;
    public CoreWebView2Environment? WebViewEnvironment => _environment;

    public WebView2? GetWebViewForTab(TabViewModel tab) => _webViews.TryGetValue(tab, out var wv) ? wv : null;

    public WebViewService()
    {
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
            AdditionalBrowserArguments = "--disable-features=DownloadBubble,DownloadBubbleV2 --process-per-site --app-id=MundoBrowser.App --app-name=\"MundoBrowser\""
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

        try { return await initTask; }
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
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            _container.Children.Add(webView);

            try { await webView.EnsureCoreWebView2Async(_environment); }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.ErrorCode == unchecked((int)0x80004004))
            {
                await Task.Delay(150);
                await webView.EnsureCoreWebView2Async(_environment);
            }

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

                        if (key == "ecoModeEnabled")
                            EcoModeEnabled = value.GetBoolean();
                        else if (key == "ecoModeDuration")
                        {
                            if (value.ValueKind == System.Text.Json.JsonValueKind.String)
                                EcoModeMinutes = int.Parse(value.GetString() ?? "10");
                            else
                                EcoModeMinutes = value.GetInt32();
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

    private void CheckMemoryOptimization(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!EcoModeEnabled) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var now = DateTime.Now;
            var tabsToDiscard = new List<TabViewModel>();
            foreach(var kvp in _webViews)
            {
                var tab = kvp.Key;
                var wv = kvp.Value;
                if (wv != _activeWebView && (now - tab.LastAccessed).TotalMinutes > EcoModeMinutes) tabsToDiscard.Add(tab);
            }
            foreach(var tab in tabsToDiscard) DiscardTab(tab);
        });
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
