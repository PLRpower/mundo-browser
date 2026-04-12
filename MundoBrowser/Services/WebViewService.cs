using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Services;

public class WebViewService
{
    private readonly Dictionary<TabViewModel, WebView2> _webViews = new();
    private readonly System.Windows.Controls.Panel _container;
    private CoreWebView2Environment? _environment;
    private WebView2? _activeWebView;
    private readonly System.Timers.Timer _memoryTimer;

    private readonly Dictionary<TabViewModel, Task<WebView2>> _initializationTasks = new();
    private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

    public WebView2? ActiveWebView => _activeWebView;
    public CoreWebView2Environment? WebViewEnvironment => _environment;

    public WebViewService(System.Windows.Controls.Panel container)
    {
        _container = container;
        
        // EcoMode: Retire de la mémoire RAM les onglets inactifs > 10 min
        _memoryTimer = new System.Timers.Timer(60000); 
        _memoryTimer.Elapsed += CheckMemoryOptimization;
        _memoryTimer.Start();
    }

    public async Task InitializeAsync()
    {
        var options = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
            AdditionalBrowserArguments = "--disable-features=DownloadBubble,DownloadBubbleV2 --process-per-site"
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

            webView.CoreWebView2.ProcessFailed += (sender, args) =>
            {
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited || 
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                    args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    try { webView.Reload(); } catch { }
                }
            };

            setupEvents(webView);
            if (!string.IsNullOrEmpty(tab.Url)) webView.CoreWebView2.Navigate(tab.Url);

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
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var now = DateTime.Now;
            var tabsToDiscard = new List<TabViewModel>();
            foreach(var kvp in _webViews)
            {
                var tab = kvp.Key;
                var wv = kvp.Value;
                if (wv != _activeWebView && (now - tab.LastAccessed).TotalMinutes > 10) tabsToDiscard.Add(tab);
            }
            foreach(var tab in tabsToDiscard) DiscardTab(tab);
        });
    }

    private void DiscardTab(TabViewModel tab)
    {
        if (_webViews.TryGetValue(tab, out var webView))
        {
            _container.Children.Remove(webView);
            webView.Dispose();
            _webViews.Remove(tab);
            tab.IsDiscarded = true;
        }
    }

    public void SwitchToTab(TabViewModel tab, WebView2 webView)
    {
        if (_activeWebView != null && _activeWebView != webView)
        {
            _activeWebView.Visibility = Visibility.Collapsed;
            try
            {
                if (_activeWebView.CoreWebView2 != null)
                {
                    // Lower memory priority for background tab WITHOUT suspending it (to keep media playing)
                    _activeWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
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
                // Note: We don't need Resume() here anymore since we don't Suspend, but it's safe to keep.
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
            _container.Children.Remove(webView);
            webView.Dispose();
        }
    }
}
