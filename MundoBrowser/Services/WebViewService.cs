using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

    public WebView2? ActiveWebView => _activeWebView;

    public WebViewService(System.Windows.Controls.Panel container)
    {
        _container = container;
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

        var webView = new WebView2();
        _webViews[tab] = webView;
        _container.Children.Add(webView);

        await webView.EnsureCoreWebView2Async(_environment);
        setupEvents(webView);

        if (!string.IsNullOrEmpty(tab.Url))
            webView.CoreWebView2.Navigate(tab.Url);

        return webView;
    }

    public void SwitchToTab(TabViewModel tab, WebView2 webView)
    {
        if (_activeWebView != null)
        {
            _activeWebView.Visibility = Visibility.Collapsed;
            if (_activeWebView.CoreWebView2 != null)
                _activeWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        }

        _activeWebView = webView;
        _activeWebView.Visibility = Visibility.Visible;
        
        if (_activeWebView.CoreWebView2 != null)
            _activeWebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
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
