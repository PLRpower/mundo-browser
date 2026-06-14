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
            var manager = new UpdateManager(new GithubSource("https://github.com/PLRpower/mundo-browser", null, false));

            // Small delay for UI to render and feel less sudden
            await Task.Delay(1000);

            var updateInfo = await manager.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                StatusText.Text = "Téléchargement de la mise à jour...";
                UpdateProgress.Visibility = Visibility.Visible;

                await manager.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    Dispatcher.Invoke(() => UpdateProgress.Value = progress);
                });

                StatusText.Text = "Installation en cours...";
                manager.ApplyUpdatesAndRestart(updateInfo);
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
        var mainWindow = new MainWindow(_args);
        mainWindow.Show();
        App.StartArgsListener(mainWindow);
        Close();
    }
}
