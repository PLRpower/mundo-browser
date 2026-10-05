using Velopack;
using MundoBrowser.Helpers;

namespace MundoBrowser;

public class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build()
            .SetAppUserModelId(AppRuntime.AppUserModelId)
            .OnAfterInstallFastCallback(_ => WindowsDefaultBrowserRegistration.Register())
            .OnAfterUpdateFastCallback(_ => WindowsDefaultBrowserRegistration.Register())
            .OnBeforeUninstallFastCallback(_ => WindowsDefaultBrowserRegistration.Unregister())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
