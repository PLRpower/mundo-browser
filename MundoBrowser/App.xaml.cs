using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.DependencyInjection;
using MundoBrowser.Interfaces;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        // Set AppUserModelID to group processes in Task Manager
        Helpers.NativeMethods.SetCurrentProcessExplicitAppUserModelID("MundoBrowser.App");
        
        Ioc.Default.ConfigureServices(ConfigureServices());
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Services
        services.AddSingleton<IHistoryManager, HistoryManager>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IFaviconService, FaviconService>();
        services.AddSingleton<IWebViewService, WebViewService>();
        services.AddSingleton<IExtensionService, ExtensionService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        // TabViewModel is usually created dynamically, but we might want a factory if it has dependencies
        // For now, let's keep it simple.

        return services.BuildServiceProvider();
    }
}
