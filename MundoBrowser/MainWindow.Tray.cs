using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
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
            _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
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
        finally
        {
            _isSavingSession = false;
        }

        var settings = Ioc.Default.GetService<IAppSettingsService>();
        if (!_isExitRequested
            && _trayIcon != null
            && settings?.Current.MinimizeToTrayOnClose != false)
        {
            HideToTray();
            return;
        }

        _isClosingSafe = true;
        Close();
    }

    private void HideToTray()
    {
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
        _trayIcon.BalloonTipText = "Double-cliquez sur l'icône pour rouvrir le navigateur.";
        _trayIcon.ShowBalloonTip(3000);
    }

    internal void RestoreFromTray()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
            Show();

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

    internal void PrepareForSystemShutdown()
    {
        _isExitRequested = true;
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }
}
