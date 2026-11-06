using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class App : System.Windows.Application
{
    private const string UniqueEventName = AppRuntime.UniqueInstanceName;
    private static Mutex? _mutex;
    private static CancellationTokenSource? _argsListenerCts;
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        _ = NativeMethods.SetCurrentProcessExplicitAppUserModelID(NativeMethods.AppUserModelId);
        ConfigurePerformanceSettings();
        _serviceProvider = ConfigureServices();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is ArgumentNullException { ParamName: "window" })
        {
            e.Handled = true;
            return;
        }

        if (e.Exception is InvalidOperationException ex && ex.Message.Contains("WebView", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            LogException("DispatcherUnhandledException (WebView2 Recovered)", e.Exception);
            return;
        }

        LogException("DispatcherUnhandledException", e.Exception);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogException("AppDomainUnhandledException", e.ExceptionObject as Exception);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("TaskSchedulerUnobservedException", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string context, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var logPath = Path.Combine(AppRuntime.LocalDataDirectory, "crash.log");
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {ex}\n\n";
            File.AppendAllText(logPath, text);
        }
        catch { }
    }

    private static void ConfigurePerformanceSettings()
    {
        try
        {
            // Force hardware acceleration for WPF rendering pipeline
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

            // Match display refresh rate up to 120 FPS for smooth UI animations without saturating 240Hz+ displays.
            int targetFps = Math.Clamp(NativeMethods.GetMaxDisplayRefreshRate(), 60, 120);
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(System.Windows.Media.Animation.Timeline),
                new FrameworkPropertyMetadata(targetFps));
        }
        catch
        {
            // Fallback gracefully if display configuration fails
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {

        if (WindowsDefaultBrowserRegistration.TryHandleCommandLine(e.Args))
        {
            Shutdown();
            return;
        }

#if !DEBUG
        Task.Run(WindowsDefaultBrowserRegistration.Register);
#endif

        // Check for single instance
        _mutex = new Mutex(true, UniqueEventName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // Allow the running instance to restore and take focus.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASW_ANY);

            // Another instance is running, send arguments to it
            SendArgsToRunningInstance(e.Args);

            App.Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        LaunchMainWindow(e.Args);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (Current.MainWindow is MainWindow mainWindow)
            mainWindow.PrepareForSystemShutdown();

        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _argsListenerCts?.Cancel();
        _argsListenerCts = null;
        _serviceProvider.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void SendArgsToRunningInstance(string[]? args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", UniqueEventName, PipeDirection.Out);
            client.Connect(500); // Wait 500ms max
            using var writer = new StreamWriter(client);
            writer.WriteLine(JsonSerializer.Serialize(args ?? [], AppJsonContext.Default.StringArray));
            writer.Flush();
            client.WaitForPipeDrain();
        }
        catch
        {
            // Fail silently if pipe is not reachable
        }
    }

    internal static void StartArgsListener(MainWindow mainWindow)
    {
        _argsListenerCts?.Cancel();
        var cts = new CancellationTokenSource();
        _argsListenerCts = cts;
        _ = ListenForExternalArgsAsync(mainWindow, cts);
    }

    private static async Task ListenForExternalArgsAsync(MainWindow mainWindow, CancellationTokenSource cts)
    {
        var cancellationToken = cts.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(UniqueEventName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    string? argsLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    string[] args = string.IsNullOrWhiteSpace(argsLine)
                        ? []
                        : JsonSerializer.Deserialize(argsLine, AppJsonContext.Default.StringArray) ?? [];

                    _ = mainWindow.Dispatcher.BeginInvoke(() =>
                    {
                        mainWindow.RestoreFromTray();

                        if (args.Length > 0)
                            mainWindow.HandleExternalArguments(args);
                    });
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    if (mainWindow.Dispatcher.HasShutdownStarted || cancellationToken.IsCancellationRequested)
                        return;

                    try
                    {
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            cts.Dispose();
        }
    }

    internal static void LaunchMainWindow(string[]? args, Window? startupWindow = null)
    {
        if (Current is not App app)
            throw new InvalidOperationException("The application service provider is unavailable.");

        var mainWindow = app._serviceProvider.GetRequiredService<MainWindow>();
        if (args != null && args.Length > 0)
        {
            mainWindow.SetStartupArgs(args);
        }
        Current.MainWindow = mainWindow;
        mainWindow.Show();
        StartArgsListener(mainWindow);
        startupWindow?.Close();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcherService, WpfDispatcherService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IHistoryManager, HistoryManager>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IFaviconService, FaviconService>();
        services.AddSingleton<IWebViewService, WebViewService>();
        services.AddSingleton<ExtensionDownloader>();
        services.AddSingleton<IExtensionService, ExtensionService>();
        services.AddSingleton<IAdBlockerService, AdBlockerService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
        return services.BuildServiceProvider();
    }
}
