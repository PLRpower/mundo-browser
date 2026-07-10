using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace MundoBrowser;

public partial class UpdateWindow : Window
{
    private readonly string[]? _args;

    public UpdateWindow(string[]? args)
    {
        InitializeComponent();
        _args = args;
        Loaded += UpdateWindow_Loaded;
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settingsService = new Services.AppSettingsService();
            var isBeta = settingsService.Current.IsBetaChannelEnabled;
            var options = new UpdateOptions 
            { 
                ExplicitChannel = isBeta ? "beta" : "release", 
                AllowVersionDowngrade = true 
            };
            var manager = new UpdateManager(new GithubSource("https://github.com/PLRpower/mundo-browser", null, isBeta), options);

            // Small delay for UI to render and feel less sudden
            await Task.Delay(1000);

            var updateInfo = await manager.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                StatusText.Text = "Téléchargement de la mise à jour...";
                UpdateProgress.Visibility = Visibility.Visible;

                await manager.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    Dispatcher.BeginInvoke(() => UpdateProgress.Value = progress);
                });

                StatusText.Text = "Installation en cours...";
                manager.ApplyUpdatesAndRestart(updateInfo, _args);
                return;
            }
        }
        catch (Exception)
        {
            // Ignore error and launch normally
        }

        LaunchMainWindow();
    }

    private void LaunchMainWindow()
    {
        App.LaunchMainWindow(_args, this);
    }
}
