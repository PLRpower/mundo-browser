using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MundoBrowser.Interfaces;

namespace MundoBrowser.ViewModels
{
    public partial class MainViewModel
    {
        // ════════════════════════════════════════════════════════════════
        // Update Service Integration & State
        // ════════════════════════════════════════════════════════════════

        [ObservableProperty]
        private bool _isUpdateAvailable;

        [ObservableProperty]
        private bool _isUpdateDownloading;

        [ObservableProperty]
        private bool _isUpdateReady;

        [ObservableProperty]
        private double _updateProgress;

        [ObservableProperty]
        private string? _updateVersionText;

        [ObservableProperty]
        private string _updateToolTipText = "Mise à jour disponible";

        [ObservableProperty]
        private string _updateMenuHeader = "Mise à jour disponible";

        [RelayCommand]
        private void ApplyUpdate() => _updateService.ApplyUpdateAndRestart();

        [RelayCommand]
        private async Task CheckForUpdates() => await _updateService.CheckForUpdatesManualAsync();

        private void OnUpdateStatusChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                IsUpdateAvailable = _updateService.IsUpdateAvailable;
                IsUpdateDownloading = _updateService.IsDownloading;
                IsUpdateReady = _updateService.IsUpdateReady;
                UpdateProgress = _updateService.DownloadProgress;
                UpdateVersionText = _updateService.NewVersionText;

                if (IsUpdateReady)
                {
                    UpdateToolTipText = $"Mise à jour v{UpdateVersionText} prête. Cliquez pour installer et redémarrer.";
                    UpdateMenuHeader = $"Mise à jour v{UpdateVersionText} prête";
                }
                else if (IsUpdateDownloading)
                {
                    UpdateToolTipText = $"Téléchargement de la mise à jour v{UpdateVersionText} ({UpdateProgress:F0}%)...";
                    UpdateMenuHeader = $"Téléchargement v{UpdateVersionText} ({UpdateProgress:F0}%)";
                }
                else if (IsUpdateAvailable)
                {
                    UpdateToolTipText = $"Mise à jour v{UpdateVersionText} disponible";
                    UpdateMenuHeader = $"Mise à jour v{UpdateVersionText} disponible";
                }
            });
        }
    }
}
