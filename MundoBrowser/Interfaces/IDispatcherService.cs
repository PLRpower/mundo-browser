namespace MundoBrowser.Interfaces;

public interface IDispatcherService
{
    void BeginInvoke(Action action);
    void Invoke(Action action);
    Task InvokeAsync(Action action);
    Task<T?> InvokeAsync<T>(Func<T> callback);
    bool CheckAccess();
}
