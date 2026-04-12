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

    public WebView2? ActiveWebView => _activeWebView;

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

        // ExclusiveUserDataFolderAccess was removed to prevent E_UNEXPECTED if old processes linger

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

        var webView = new WebView2();
        webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        
        _webViews[tab] = webView;
        _container.Children.Add(webView);

        await webView.EnsureCoreWebView2Async(_environment);
        
        // Handle Process Crashes to prevent app crashes and try to recover
        webView.CoreWebView2.ProcessFailed += (sender, args) =>
        {
            // If the browser process crashed, we can attempt to reload it
            if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited || 
                args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                // Re-initialize or handle the crash
                try { webView.Reload(); } catch { }
            }
        };

        setupEvents(webView);

        if (!string.IsNullOrEmpty(tab.Url))
            webView.CoreWebView2.Navigate(tab.Url);

        return webView;
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
                // Si l'onglet n'est pas actif et n'a pas été vu depuis 10 minutes, on le libère
                if (wv != _activeWebView && (now - tab.LastAccessed).TotalMinutes > 10)
                {
                    tabsToDiscard.Add(tab);
                }
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
                    _activeWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    _activeWebView.CoreWebView2.TrySuspendAsync(); // Extrêmement efficace pour le CPU
                }
            }
            catch (Exception) { /* Ignorer les erreurs si le processus est fermé/planté */ }
        }

        _activeWebView = webView;
        _activeWebView.Visibility = Visibility.Visible;
        tab.LastAccessed = DateTime.Now;
        tab.IsDiscarded = false;
        
        try
        {
            if (_activeWebView.CoreWebView2 != null)
            {
                _activeWebView.CoreWebView2.Resume();
                _activeWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
        }
        catch (Exception) { /* Ignorer */ }
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
