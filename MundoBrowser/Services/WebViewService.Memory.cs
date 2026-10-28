using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Services;

public partial class WebViewService
{
    public bool IsTabProtected(TabViewModel tab, WebView2? webView = null)
    {
        if (_disposed) return false;
        if (!EcoModeEnabled) return true;

        // Active tab currently selected
        if (tab == _activeTab) return true;

        // Visible tabs in split view
        if (tab == _splitPrimaryTab || tab == _splitSecondaryTab) return true;

        // Active WebView reference
        if (webView != null && webView == _activeWebView) return true;

        // Pinned tabs are always kept alive
        if (tab.IsPinned) return true;

        // Tabs playing audio or holding media session / paused media
        if (tab.IsPlayingAudio || tab.IsMediaPaused || tab.MediaDuration > 0 || !string.IsNullOrWhiteSpace(tab.MediaTitle))
            return true;

        // CoreWebView2 audio state check
        if (webView?.CoreWebView2 != null)
        {
            try
            {
                if (webView.CoreWebView2.IsDocumentPlayingAudio) return true;
            }
            catch { }
        }

        // Active downloads protect tab from discard
        if (webView != null)
        {
            lock (_activeDownloads)
            {
                if (_activeDownloads.TryGetValue(webView, out var downloads) && downloads.Count > 0)
                    return true;
            }
        }

        return false;
    }

    private void CheckMemoryOptimization(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_disposed || !EcoModeEnabled) return;
        if (Interlocked.Exchange(ref _memoryOptimizationRunning, 1) == 1) return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            Volatile.Write(ref _memoryOptimizationRunning, 0);
            return;
        }

        try
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var now = DateTime.Now;
                    if (_activeTab != null) _activeTab.LastAccessed = now;
                    if (_splitPrimaryTab != null) _splitPrimaryTab.LastAccessed = now;
                    if (_splitSecondaryTab != null) _splitSecondaryTab.LastAccessed = now;

                    var tabsToDiscard = new Queue<TabViewModel>();

                    foreach (var entry in _webViews)
                    {
                        var tab = entry.Key;
                        var webView = entry.Value;

                        if (IsTabProtected(tab, webView))
                        {
                            TrySetNormalMemoryTarget(webView);
                            continue;
                        }

                        if ((now - tab.LastAccessed).TotalMinutes > EcoModeMinutes)
                        {
                            tabsToDiscard.Enqueue(tab);
                        }
                    }

                    DiscardTabsAtIdle(tabsToDiscard);
                }
                catch
                {
                    Volatile.Write(ref _memoryOptimizationRunning, 0);
                }
            }), System.Windows.Threading.DispatcherPriority.SystemIdle);
        }
        catch
        {
            Volatile.Write(ref _memoryOptimizationRunning, 0);
        }
    }

    private void DiscardTabsAtIdle(Queue<TabViewModel> tabs)
    {
        if (_disposed || !EcoModeEnabled || tabs.Count == 0)
        {
            Volatile.Write(ref _memoryOptimizationRunning, 0);
            return;
        }

        try
        {
            var tab = tabs.Dequeue();
            _webViews.TryGetValue(tab, out var webView);
            if (!IsTabProtected(tab, webView))
            {
                DiscardTab(tab);
            }
        }
        catch
        {
            Volatile.Write(ref _memoryOptimizationRunning, 0);
            return;
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            new Action(() => DiscardTabsAtIdle(tabs)),
            System.Windows.Threading.DispatcherPriority.SystemIdle);
    }

    private void DiscardTab(TabViewModel tab, bool force = false)
    {
        if (!force && IsTabProtected(tab))
            return;

        if (_tabContainers.TryGetValue(tab, out var container))
        {
            if (container.ContainerGrid.Parent is System.Windows.Controls.Panel parent)
            {
                parent.Children.Remove(container.ContainerGrid);
            }
            else
            {
                _container?.Children.Remove(container.ContainerGrid);
            }

            try { container.MainWebView.Dispose(); } catch { }
            _tabContainers.Remove(tab);
            _webViews.Remove(tab);
            _adBlockScriptIds.Remove(container.MainWebView);
            tab.IsLoading = false;
            tab.IsDiscarded = true;
        }
        else if (_webViews.TryGetValue(tab, out var webView))
        {
            _container?.Children.Remove(webView);
            try { webView.Dispose(); } catch { }
            _webViews.Remove(tab);
            _adBlockScriptIds.Remove(webView);
            tab.IsLoading = false;
            tab.IsDiscarded = true;
        }
    }
}
