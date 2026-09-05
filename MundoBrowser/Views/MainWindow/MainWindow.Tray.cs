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

    private void InitializeTrayIcon()
    {
        System.Windows.Forms.ContextMenuStrip? menu = null;

        try
        {
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
                Icon = System.Drawing.SystemIcons.Application,
                Text = AppRuntime.DisplayName,
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.MouseClick += TrayIcon_MouseClick;

            // Load associated icon in background to avoid blocking UI thread
            Task.Run(() =>
            {
                try
                {
                    if (Environment.ProcessPath is { } processPath)
                    {
                        var icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                        if (icon != null)
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                if (_trayIcon != null)
                                {
                                    _trayIconImage = icon;
                                    _trayIcon.Icon = icon;
                                }
                                else
                                {
                                    icon.Dispose();
                                }
                            });
                        }
                    }
                }
                catch { }
            });
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

        if (_trayIcon != null)
            _trayIcon.Visible = true;
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
        _isClosingSafe = true;
        
        try
        {
            SyncWindowPlacementToViewModel();
            if (DataContext is MainViewModel vm)
            {
                Task.Run(async () => await vm.SaveCurrentSessionAsync()).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save state during shutdown: {ex}");
        }
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
