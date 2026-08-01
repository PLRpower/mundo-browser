using System;
using System.Threading.Tasks;

namespace MundoBrowser.Interfaces;

public interface IUpdateService
{
    bool IsUpdateAvailable { get; }
    bool IsDownloading { get; }
    bool IsUpdateReady { get; }
    double DownloadProgress { get; }
    string? NewVersionText { get; }

    event EventHandler? UpdateStatusChanged;

    void CheckForUpdatesInBackground(string[]? args);
    Task CheckForUpdatesManualAsync();
    void ApplyUpdateAndRestart();
}
