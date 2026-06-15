using System.ComponentModel;
using System.Windows;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private bool _isExitRequested;
    private bool _hasShownTrayNotification;

    private void InitializeTrayIcon()
    {
        System.Windows.Forms.ContextMenuStrip? menu = null;

        try
        {
            _trayIconImage = Environment.ProcessPath is { } processPath
                ? System.Drawing.Icon.ExtractAssociatedIcon(processPath)
                : null;

            menu = new System.Windows.Forms.ContextMenuStrip();
            var openItem = new System.Windows.Forms.ToolStripMenuItem("Ouvrir MundoBrowser");
            openItem.Click += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
            menu.Items.Add(openItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Quitter");
            exitItem.Click += (_, _) => Dispatcher.BeginInvoke(RequestExit);
            menu.Items.Add(exitItem);

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = _trayIconImage ?? System.Drawing.SystemIcons.Application,
                Text = AppRuntime.DisplayName,
                ContextMenuStrip = menu,
                Visible = false
            };
            _trayIcon.MouseClick += TrayIcon_MouseClick;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize notification icon: {ex.Message}");
            menu?.Dispose();
            _trayIconImage?.Dispose();
            _trayIconImage = null;
            _trayIcon = null;
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosingSafe)
            return;

        e.Cancel = true;
        if (_isSavingSession)
            return;

        _isSavingSession = true;
        try
        {
            SyncWindowPlacementToViewModel();
            if (DataContext is MainViewModel vm)
                await vm.SaveCurrentSessionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save state before closing: {ex}");
        }
        finally
        {
            _isSavingSession = false;
        }

        if (!_isExitRequested
            && _trayIcon != null
            && _settingsService.Current.MinimizeToTrayOnClose)
        {
            HideToTray();
            return;
        }

        _isClosingSafe = true;
        Close();
    }

    private void HideToTray()
    {
        _windowStateBeforeTray = WindowState == WindowState.Minimized
            ? WindowState.Normal
            : WindowState;

        HideFloatingSidebar(animate: false);
        CloseExtensionPopup();
        ShowInTaskbar = false;
        Hide();

        if (_trayIcon == null)
            return;

        _trayIcon.Visible = true;
        if (_hasShownTrayNotification)
            return;

        _hasShownTrayNotification = true;
        _trayIcon.BalloonTipTitle = "MundoBrowser fonctionne en arrière-plan";
        _trayIcon.BalloonTipText = "Cliquez sur l'icône pour rouvrir le navigateur.";
        _trayIcon.ShowBalloonTip(3000);
    }

    private void TrayIcon_MouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Left)
            _ = Dispatcher.BeginInvoke(RestoreFromTray);
    }

    internal void RestoreFromTray()
    {
        bool wasHidden = !IsVisible;
        ShowInTaskbar = true;
        if (wasHidden)
            Show();

        if (wasHidden && !_isFullscreen)
            WindowState = _windowStateBeforeTray;

        if (_trayIcon != null)
            _trayIcon.Visible = false;

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        ForceForeground(handle);
        Activate();
        Focus();
    }

    private void RequestExit()
    {
        _isExitRequested = true;
        Close();
    }

    private void RequestRestart()
    {
        _restartRequested = true;
        _isExitRequested = true;
        Close();
    }

    internal void PrepareForSystemShutdown()
    {
        _isExitRequested = true;
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.MouseClick -= TrayIcon_MouseClick;
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }
}
