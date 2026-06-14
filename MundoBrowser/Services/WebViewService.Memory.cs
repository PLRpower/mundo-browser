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
}
