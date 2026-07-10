using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace MundoBrowser;

public partial class UpdateWindow : Window
{
    private readonly string[]? _args;
    private readonly bool _isManualCheck;

    public UpdateWindow(string[]? args, bool isManualCheck = false)
    {
        InitializeComponent();
        _args = args;
        _isManualCheck = isManualCheck;
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
            if (_isManualCheck)
            {
                MessageBox.Show("Vous êtes à jour !", "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
                return;
            }
        }
        catch (Exception ex)
        {
            if (_isManualCheck)
            {
                MessageBox.Show($"Erreur lors de la vérification : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }
        }

        if (!_isManualCheck)
        {
            LaunchMainWindow();
        }
    }

    private void LaunchMainWindow()
    {
        App.LaunchMainWindow(_args, this);
    }
}
