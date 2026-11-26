using System.Diagnostics;
using MundoBrowser.Interfaces;
using Velopack;
using Velopack.Sources;

namespace MundoBrowser.Services;

public class UpdateService : IUpdateService, IDisposable
{
    private static readonly TimeSpan DefaultStartupCheckDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(6);

    private readonly IAppSettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly TimeSpan _startupCheckDelay;
    private readonly TimeSpan _checkInterval;
    private readonly Func<bool, CancellationToken, Task>? _customCheckRunner;
    private readonly CancellationTokenSource _disposeCts = new();
    private UpdateManager? _updateManager;
    private UpdateInfo? _updateInfo;
    private string[]? _startArgs;
    private int _isChecking;
    private int _backgroundLoopStarted;
    private int _disposed;

    public bool IsUpdateAvailable { get; private set; }
    public bool IsDownloading { get; private set; }
    public bool IsUpdateReady { get; private set; }
    public double DownloadProgress { get; private set; }
    public string? NewVersionText { get; private set; }

    public event EventHandler? UpdateStatusChanged;

    public UpdateService(IAppSettingsService settingsService, IDialogService dialogService)
        : this(settingsService, dialogService, DefaultStartupCheckDelay, DefaultCheckInterval, null)
    {
    }

    internal UpdateService(
        IAppSettingsService settingsService,
        IDialogService dialogService,
        TimeSpan startupCheckDelay,
        TimeSpan checkInterval,
        Func<bool, CancellationToken, Task>? customCheckRunner = null)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _startupCheckDelay = startupCheckDelay;
        _checkInterval = checkInterval;
        _customCheckRunner = customCheckRunner;
    }

    public void CheckForUpdatesInBackground(string[]? args)
    {
        _startArgs = args;
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _backgroundLoopStarted, 1) != 0) return;

        _ = RunBackgroundUpdateLoopAsync(_disposeCts.Token);
    }

    private async Task RunBackgroundUpdateLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Delay initial check so startup rendering and initial tab load stay responsive.
            await Task.Delay(_startupCheckDelay, cancellationToken).ConfigureAwait(false);
            await PerformUpdateCheckAsync(isManualCheck: false, cancellationToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(_checkInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await PerformUpdateCheckAsync(isManualCheck: false, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutting down
        }
    }

    public Task CheckForUpdatesManualAsync()
    {
        return PerformUpdateCheckAsync(isManualCheck: true, _disposeCts.Token);
    }

    private async Task PerformUpdateCheckAsync(bool isManualCheck, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested)
            return;
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
            return;

        try
        {
            if (_customCheckRunner != null)
            {
                await _customCheckRunner(isManualCheck, cancellationToken).ConfigureAwait(false);
                return;
            }

            var isBeta = _settingsService.Current.IsBetaChannelEnabled;
            var options = new UpdateOptions
            {
                ExplicitChannel = isBeta ? "beta" : "release",
                AllowVersionDowngrade = true
            };

            var manager = new UpdateManager(new GithubSource("https://github.com/PLRpower/mundo-browser", null, isBeta), options);
            _updateManager = manager;

            if (!isManualCheck && !manager.IsInstalled)
                return;

            var updateInfo = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updateInfo == null)
            {
                if (isManualCheck)
                {
                    _dialogService.ShowInformation(
                        "Vous utilisez déjà la dernière version de MundoBrowser.",
                        "Mise à jour");
                }
                return;
            }

            string targetVersion = updateInfo.TargetFullRelease.Version.ToString();
            if (IsUpdateReady && string.Equals(NewVersionText, targetVersion, StringComparison.Ordinal))
            {
                if (isManualCheck)
                {
                    _dialogService.ShowInformation(
                        $"La mise à jour ({targetVersion}) est déjà téléchargée et prête à être installée.",
                        "Mise à jour prête");
                }
                return;
            }

            _updateInfo = updateInfo;
            NewVersionText = targetVersion;
            IsUpdateAvailable = true;
            IsDownloading = true;
            IsUpdateReady = false;
            DownloadProgress = 0;
            NotifyStatusChanged();

            if (isManualCheck)
            {
                _dialogService.ShowInformation(
                    $"Une nouvelle version ({NewVersionText}) est disponible ! Le téléchargement a démarré en arrière-plan.",
                    "Mise à jour disponible");
            }

            int lastReportedProgress = -1;
            await manager.DownloadUpdatesAsync(updateInfo, progress =>
            {
                if (progress == lastReportedProgress)
                    return;

                lastReportedProgress = progress;
                DownloadProgress = progress;
                NotifyStatusChanged();
            }, cancellationToken).ConfigureAwait(false);

            IsDownloading = false;
            IsUpdateReady = true;
            DownloadProgress = 100;
            NotifyStatusChanged();
        }
        catch (OperationCanceledException)
        {
            ResetFailedDownloadState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            ResetFailedDownloadState();
            if (isManualCheck)
            {
                _dialogService.ShowError(
                    $"Impossible de vérifier les mises à jour : {ex.Message}",
                    "Erreur");
            }
        }
        finally
        {
            Volatile.Write(ref _isChecking, 0);
        }
    }

    private void ResetFailedDownloadState()
    {
        if (!IsDownloading)
            return;

        IsDownloading = false;
        IsUpdateAvailable = IsUpdateReady;
        DownloadProgress = IsUpdateReady ? 100 : 0;
        NotifyStatusChanged();
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
                _dialogService.ShowError(
                    $"Erreur lors de l'application de la mise à jour : {ex.Message}",
                    "Erreur");
            }
        }
    }

    private void NotifyStatusChanged()
    {
        UpdateStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
