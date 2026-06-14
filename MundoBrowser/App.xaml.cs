using System.IO;
using System.IO.Pipes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.DependencyInjection;
using MundoBrowser.Interfaces;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;
using Velopack;

namespace MundoBrowser;

public partial class App : System.Windows.Application
{
    private const string UniqueEventName = "MundoBrowser-SingleInstance-Guid-v2";
    private static Mutex? _mutex;
    private string[]? _args;

    public App()
    {
        Helpers.NativeMethods.SetCurrentProcessExplicitAppUserModelID(Helpers.NativeMethods.AppUserModelId);
        Ioc.Default.ConfigureServices(ConfigureServices());
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        VelopackApp.Build().Run();

        _args = e.Args;

        // Check for single instance
        _mutex = new Mutex(true, UniqueEventName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // Another instance is running, send arguments to it
            SendArgsToRunningInstance(_args);
            
            // Allow the running instance to take focus
            Helpers.NativeMethods.AllowSetForegroundWindow(Helpers.NativeMethods.ASW_ANY);
            
            App.Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        var updateWindow = new UpdateWindow(_args);
        updateWindow.Show();
    }

    private static void SendArgsToRunningInstance(string[]? args)
    {
        if (args == null || args.Length == 0) return;

        try
        {
            using var client = new NamedPipeClientStream(".", UniqueEventName, PipeDirection.Out);
            client.Connect(500); // Wait 500ms max
            using var writer = new StreamWriter(client);
            writer.WriteLine(string.Join(" ", args));
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

                    if (!string.IsNullOrEmpty(argsLine))
                    {
                        string[] args = argsLine.Split(' ');
                        mainWindow.Dispatcher.Invoke(() =>
                        {
                            mainWindow.HandleExternalArguments(args);
                        });
                    }
                }
                catch
                {
                    // Ignore pipe errors
                }
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IHistoryManager, HistoryManager>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IFaviconService, FaviconService>();
        services.AddSingleton<IWebViewService, WebViewService>();
        services.AddSingleton<IExtensionService, ExtensionService>();
        services.AddSingleton<IAdBlockerService, AdBlockerService>();
        services.AddTransient<MainViewModel>();
        return services.BuildServiceProvider();
    }
}
