using MundoBrowser.ViewModels;

namespace MundoBrowser.Services;

public partial class WebViewService
{
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
                    var tabsToDiscard = new Queue<TabViewModel>();

                    foreach (var entry in _webViews)
                    {
                        var tab = entry.Key;
                        var webView = entry.Value;

                        if (webView == _activeWebView) continue;

                        bool isPlayingAudio = false;
                        try { isPlayingAudio = webView.CoreWebView2?.IsDocumentPlayingAudio ?? false; } catch { }
                        if (isPlayingAudio) continue;

                        bool hasActiveDownloads = false;
                        lock (_activeDownloads)
                        {
                            if (_activeDownloads.TryGetValue(webView, out var downloads) && downloads.Count > 0)
                                hasActiveDownloads = true;
                        }
                        if (hasActiveDownloads) continue;

                        if ((now - tab.LastAccessed).TotalMinutes > EcoModeMinutes)
                        {
                            tabsToDiscard.Enqueue(tab);
                        }
                        else
                        {
                            // Throttle memory and suspend inactive background tab
                            _ = TrySuspendWebView(webView);
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
            DiscardTab(tabs.Dequeue());
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

    private void DiscardTab(TabViewModel tab)
    {
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
            tab.IsDiscarded = true;
        }
        else if (_webViews.TryGetValue(tab, out var webView))
        {
            _container?.Children.Remove(webView);
            try { webView.Dispose(); } catch { }
            _webViews.Remove(tab);
            tab.IsDiscarded = true;
        }
    }
}
