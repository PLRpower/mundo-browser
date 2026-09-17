using System.Windows;
using System.Windows.Threading;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public class WpfDispatcherService : IDispatcherService
{
    private static Dispatcher? CurrentDispatcher => System.Windows.Application.Current?.Dispatcher;

    public void BeginInvoke(Action action)
    {
        var dispatcher = CurrentDispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public void Invoke(Action action)
    {
        var dispatcher = CurrentDispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public Task InvokeAsync(Action action)
    {
        var dispatcher = CurrentDispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
        {
            return dispatcher.InvokeAsync(action).Task;
        }
        action();
        return Task.CompletedTask;
    }

    public async Task<T?> InvokeAsync<T>(Func<T> callback)
    {
        var dispatcher = CurrentDispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            return default;
        }

        if (dispatcher.CheckAccess())
        {
            return callback();
        }

        return await dispatcher.InvokeAsync(callback);
    }

    public bool CheckAccess() => CurrentDispatcher?.CheckAccess() ?? true;
}
