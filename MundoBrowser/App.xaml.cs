using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;
using Velopack;

namespace MundoBrowser;

public partial class App : System.Windows.Application
{
    private const string UniqueEventName = AppRuntime.UniqueInstanceName;
    private static Mutex? _mutex;
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(NativeMethods.AppUserModelId);
        _serviceProvider = ConfigureServices();
    }

    protected override void OnStartup(StartupEventArgs e)
    {

        if (WindowsDefaultBrowserRegistration.TryHandleCommandLine(e.Args))
        {
            Shutdown();
            return;
        }

#if !DEBUG
        WindowsDefaultBrowserRegistration.Register();
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

#if DEBUG
        LaunchMainWindow(e.Args);
#else
        var updateWindow = new UpdateWindow(e.Args);
        updateWindow.Show();
#endif
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (Current.MainWindow is MainWindow mainWindow)
            mainWindow.PrepareForSystemShutdown();

        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
            writer.WriteLine(JsonSerializer.Serialize(args ?? []));
            writer.Flush();
        }
        catch
        {
            // Fail silently if pipe is not reachable
        }
    }

    internal static void StartArgsListener(MainWindow mainWindow)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(UniqueEventName, PipeDirection.In);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string? argsLine = reader.ReadLine();
                    string[] args = string.IsNullOrWhiteSpace(argsLine)
                        ? []
                        : JsonSerializer.Deserialize<string[]>(argsLine) ?? [];

                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        mainWindow.RestoreFromTray();

                        if (args.Length > 0)
                            mainWindow.HandleExternalArguments(args);
                    });
                }
                catch
                {
                    if (mainWindow.Dispatcher.HasShutdownStarted)
                        return;

                    // Avoid a tight retry loop if the pipe temporarily becomes unavailable.
                    Thread.Sleep(100);
                }
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
    }

    internal static void LaunchMainWindow(string[]? args, Window? startupWindow = null)
    {
        if (Current is not App app)
            throw new InvalidOperationException("The application service provider is unavailable.");

        var mainWindow = new MainWindow(
            app._serviceProvider.GetRequiredService<MainViewModel>(),
            app._serviceProvider.GetRequiredService<IWebViewService>(),
            app._serviceProvider.GetRequiredService<IExtensionService>(),
            app._serviceProvider.GetRequiredService<IAppSettingsService>(),
            args);
        Current.MainWindow = mainWindow;
        // var server = app._serviceProvider.GetRequiredService<MundoExtensionServer>();
        // server.Start();
        mainWindow.Show();
        StartArgsListener(mainWindow);
        startupWindow?.Close();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IHistoryManager, HistoryManager>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IFaviconService, FaviconService>();
        services.AddSingleton<IWebViewService, WebViewService>();
        services.AddSingleton<ExtensionDownloader>();
        services.AddSingleton<IExtensionService, ExtensionService>();
        services.AddSingleton<IAdBlockerService, AdBlockerService>();
        services.AddSingleton<MundoExtensionServer>();
        services.AddTransient<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
