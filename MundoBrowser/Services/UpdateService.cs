using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MundoBrowser.Interfaces;
using Velopack;
using Velopack.Sources;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;

namespace MundoBrowser.Services;

public class UpdateService : IUpdateService
{
    private readonly IAppSettingsService _settingsService;
    private UpdateManager? _updateManager;
    private UpdateInfo? _updateInfo;
    private string[]? _startArgs;
    private bool _isChecking;

    public bool IsUpdateAvailable { get; private set; }
    public bool IsDownloading { get; private set; }
    public bool IsUpdateReady { get; private set; }
    public double DownloadProgress { get; private set; }
    public string? NewVersionText { get; private set; }

    public event EventHandler? UpdateStatusChanged;

    public UpdateService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void CheckForUpdatesInBackground(string[]? args)
    {
        _startArgs = args;
        _ = Task.Run(async () =>
        {
            // Small delay so application startup completes smoothly before network check
            await Task.Delay(3000);
            await PerformUpdateCheckAsync(isManualCheck: false);
        });
    }

    public async Task CheckForUpdatesManualAsync()
    {
        if (_isChecking) return;
        await PerformUpdateCheckAsync(isManualCheck: true);
    }

    private async Task PerformUpdateCheckAsync(bool isManualCheck)
    {
        if (_isChecking) return;
        _isChecking = true;

        try
        {
            var isBeta = _settingsService.Current.IsBetaChannelEnabled;
            var options = new UpdateOptions
            {
                ExplicitChannel = isBeta ? "beta" : "release",
                AllowVersionDowngrade = true
            };

            _updateManager = new UpdateManager(new GithubSource("https://github.com/PLRpower/mundo-browser", null, isBeta), options);

            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                _updateInfo = updateInfo;
                NewVersionText = updateInfo.TargetFullRelease.Version.ToString();
                IsUpdateAvailable = true;
                IsDownloading = true;
                NotifyStatusChanged();

                if (isManualCheck)
                {
                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        WpfMessageBox.Show(
                            $"Une nouvelle version ({NewVersionText}) est disponible ! Le téléchargement a démarré en arrière-plan.",
                            "Mise à jour disponible",
                            WpfMessageBoxButton.OK,
                            WpfMessageBoxImage.Information);
                    });
                }

                await _updateManager.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    DownloadProgress = progress;
                    NotifyStatusChanged();
                });

                IsDownloading = false;
                IsUpdateReady = true;
                NotifyStatusChanged();
            }
            else if (isManualCheck)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    WpfMessageBox.Show(
                        "Vous utilisez déjà la dernière version de MundoBrowser.",
                        "Mise à jour",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Information);
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            if (isManualCheck)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    WpfMessageBox.Show(
                        $"Impossible de vérifier les mises à jour : {ex.Message}",
                        "Erreur",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Error);
                });
            }
        }
        finally
        {
            _isChecking = false;
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (_updateManager != null && _updateInfo != null)
        {
            try
            {
                _updateManager.ApplyUpdatesAndRestart(_updateInfo, _startArgs);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"Erreur lors de l'application de la mise à jour : {ex.Message}",
                    "Erreur",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error);
            }
        }
    }

    private void NotifyStatusChanged()
    {
        UpdateStatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
