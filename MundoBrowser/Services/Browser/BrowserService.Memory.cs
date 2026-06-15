using MundoBrowser.ViewModels;

namespace MundoBrowser.Services.Browser;

public partial class BrowserService
{
    private void CheckMemoryOptimization(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_disposed || !EcoModeEnabled || Interlocked.Exchange(ref _memoryOptimizationRunning, 1) == 1)
            return;

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
                    var tabs = new Queue<TabViewModel>(_browsers
                        .Where(entry =>
                            entry.Value != _activeBrowser
                            && !entry.Key.IsPlayingAudio
                            && (now - entry.Key.LastAccessed).TotalMinutes > EcoModeMinutes)
                        .Select(entry => entry.Key));
                    DiscardTabsAtIdle(tabs);
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

        DiscardTab(tabs.Dequeue());
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            new Action(() => DiscardTabsAtIdle(tabs)),
            System.Windows.Threading.DispatcherPriority.SystemIdle);
    }

    private void DiscardTab(TabViewModel tab)
    {
        if (!_browsers.Remove(tab, out var browser))
            return;

        _container?.Children.Remove(browser);
        browser.Dispose();
        tab.IsDiscarded = true;
    }
}
