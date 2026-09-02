using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using InputMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;


namespace MundoBrowser;

public partial class MainWindow
{
    private bool _isInSizeMove;
    private bool _mediaTimerWasEnabledBeforeSizeMove;

    private const int WmNcHitTest = 0x0084;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmWindowPosChanging = 0x0046;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int HtClient = 1;
    private const int HtCaption = 2;


    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcHitTest)
        {
            if (_isFullscreen)
            {
                handled = true;
                return new IntPtr(HtClient);
            }
        }

        if (_isFullscreen && msg == WmWindowPosChanging)
        {
            ConstrainFullscreenWindowPos(hwnd, lParam);
            return IntPtr.Zero;
        }

        if (_isFullscreen && msg == WmMoving)
        {
            ConstrainFullscreenMovingRect(hwnd, lParam);
            handled = true;
            return new IntPtr(1);
        }

        if (msg == WmEnterSizeMove)
        {
            EnterNativeSizeMove();
        }

        if (msg == WmExitSizeMove)
        {
            ExitNativeSizeMove();
        }

        if (msg == WmGetMinMaxInfo)
        {
            handled = true;
            NativeMethods.WmGetMinMaxInfo(hwnd, lParam, _isFullscreen);
        }

        return IntPtr.Zero;
    }

    private void SetFullscreen(bool enable, bool hideUI = false)
    {
        if (enable == _isFullscreen)
            return;

        _isFullscreen = enable;

        if (enable)
        {
            EnterFullscreen(hideUI);
        }
        else
        {
            ExitFullscreen();
        }
    }

    private void EnterFullscreen(bool hideUI)
    {
        _fullscreenHidesUi = hideUI;
        _prevWindowState = (WindowState, WindowStyle, ResizeMode, WindowBackdropType, WindowCornerPreference, Topmost, Left, Top, Width, Height);

        NativeMethods.SuppressAccentBorder(this);

        WindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference.DoNotRound;
        WindowBackdropType = hideUI ? Wpf.Ui.Controls.WindowBackdropType.None : _prevWindowState.Backdrop;
        Background = hideUI ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;

        Topmost = true;
        WindowState = WindowState.Normal;
        NativeMethods.ShowWindow(new WindowInteropHelper(this).Handle, NativeMethods.SW_RESTORE);

        if (WindowTitleBar != null)
            WindowTitleBar.Visibility = Visibility.Collapsed;

        HideFloatingSidebar(animate: false);
        HideFloatingTopBar(animate: false);

        if (DataContext is MainViewModel vm)
        {
            UpdateSidebarWidth(hideUI ? false : vm.IsSidebarVisible);
            UpdateTopBarHeight(hideUI ? false : vm.IsTopBarVisible, animate: false);
        }

        ApplyFullscreenBounds();
        ReapplyFullscreenBoundsAfterMaximizeRestore();

        NativeMethods.SuppressAccentBorder(this);
        Dispatcher.BeginInvoke(
            new Action(() => {
                UpdateEdgeTriggerState();
                UpdateTopEdgeTriggerState();
            }),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void ExitFullscreen()
    {
        _isRestoringFromFullscreen = true;

        try
        {
            NativeMethods.SuppressAccentBorder(this);

            WindowCornerPreference = _prevWindowState.Corners;
            WindowBackdropType = _prevWindowState.Backdrop;
            Background = System.Windows.Media.Brushes.Transparent;
            WindowStyle = _prevWindowState.Style;
            ResizeMode = _prevWindowState.Resize;

            if (_prevWindowState.State == WindowState.Maximized && !_fullscreenHidesUi)
            {
                ApplyMonitorBounds(useWorkArea: true, topmost: _prevWindowState.Topmost);
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Left = _prevWindowState.Left;
                Top = _prevWindowState.Top;
                Width = _prevWindowState.Width;
                Height = _prevWindowState.Height;
                WindowState = _prevWindowState.State;
            }

            Topmost = _prevWindowState.Topmost;
            _fullscreenHidesUi = false;
            UpdateWindowFrameVisuals();

            if (WindowTitleBar != null)
                WindowTitleBar.Visibility = Visibility.Visible;

            HideFloatingSidebar(animate: false);
            HideFloatingTopBar(animate: false);

            if (DataContext is MainViewModel vm)
            {
                UpdateSidebarWidth(vm.IsSidebarVisible);
                UpdateTopBarHeight(vm.IsTopBarVisible, animate: false);
            }

            OnWindowStateChanged();
        }
        finally
        {
            _isRestoringFromFullscreen = false;
        }

        Dispatcher.BeginInvoke(new Action(UpdateWindowFrameVisuals), System.Windows.Threading.DispatcherPriority.ContextIdle);
        UpdateEdgeTriggerState();
        UpdateTopEdgeTriggerState();
    }

    private void UpdateWindowFrameVisuals()
    {
        NativeMethods.SetWindowFrameColors(this, showSubtleBorder: !_isFullscreen && WindowState == WindowState.Normal);
    }

    private void EnterNativeSizeMove()
    {
        if (_isInSizeMove)
            return;

        _isInSizeMove = true;
        HideFloatingSidebar(animate: false);
        HideFloatingTopBar(animate: false);
        UpdateEdgeTriggerState();
        UpdateTopEdgeTriggerState();

        _mediaTimerWasEnabledBeforeSizeMove = _globalMediaTimer.IsEnabled;
        _globalMediaTimer.Stop();
    }

    private void ExitNativeSizeMove()
    {
        if (!_isInSizeMove)
            return;

        _isInSizeMove = false;

        if (_mediaTimerWasEnabledBeforeSizeMove)
            _globalMediaTimer.Start();

        SyncWindowPlacementToViewModel();
        UpdateEdgeTriggerState();
        UpdateTopEdgeTriggerState();
    }

    private void SyncWindowPlacementToViewModel()
    {
        if (DataContext is not MainViewModel vm)
            return;

        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
        {
            vm.WindowLeft = bounds.Left;
            vm.WindowTop = bounds.Top;
            vm.WindowWidth = bounds.Width;
            vm.WindowHeight = bounds.Height;
        }

        vm.WindowState = WindowState;
        vm.RequestSessionSave();
    }

    private void ApplyFullscreenBounds()
    {
        ApplyMonitorBounds(useWorkArea: false, topmost: true);
    }

    private void ApplyMonitorBounds(bool useWorkArea, bool? topmost)
    {
        _isApplyingFullscreenBounds = true;

        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var monitorRect = NativeMethods.GetMonitorRect(handle, useWorkArea);
            IntPtr insertAfter = IntPtr.Zero;
            uint flags = NativeMethods.SWP_NOOWNERZORDER
                         | NativeMethods.SWP_FRAMECHANGED
                         | NativeMethods.SWP_SHOWWINDOW;

            if (topmost == true)
            {
                insertAfter = NativeMethods.HWND_TOPMOST;
            }
            else if (topmost == false)
            {
                insertAfter = NativeMethods.HWND_NOTOPMOST;
                flags |= NativeMethods.SWP_NOACTIVATE;
            }
            else
            {
                flags |= NativeMethods.SWP_NOACTIVATE;
                flags |= NativeMethods.SWP_NOZORDER;
            }

            NativeMethods.SetWindowPos(
                handle,
                insertAfter,
                monitorRect.left,
                monitorRect.top,
                monitorRect.right - monitorRect.left,
                monitorRect.bottom - monitorRect.top,
                flags);
        }
        finally
        {
            _isApplyingFullscreenBounds = false;
        }
    }

    private void ReapplyFullscreenBoundsAfterMaximizeRestore()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isFullscreen)
                ApplyFullscreenBounds();
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static void ConstrainFullscreenWindowPos(IntPtr hwnd, IntPtr lParam)
    {
        var monitorRect = NativeMethods.GetMonitorRect(hwnd, useWorkArea: false);
        var windowPos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);

        windowPos.x = monitorRect.left;
        windowPos.y = monitorRect.top;
        windowPos.cx = monitorRect.right - monitorRect.left;
        windowPos.cy = monitorRect.bottom - monitorRect.top;

        Marshal.StructureToPtr(windowPos, lParam, false);
    }

    private static void ConstrainFullscreenMovingRect(IntPtr hwnd, IntPtr lParam)
    {
        var monitorRect = NativeMethods.GetMonitorRect(hwnd, useWorkArea: false);
        Marshal.StructureToPtr(monitorRect, lParam, false);
    }

    private void OnWindowStateChanged()
    {
        if (_isRestoringFromFullscreen)
            return;

        MainGrid.Margin = new Thickness(0);

        if (DataContext is MainViewModel vm)
        {
            UpdateTopBarHeight(vm.IsTopBarVisible, animate: false);
            UpdateSidebarWidth(vm.IsSidebarVisible, animate: false);
        }

        UpdateWindowFrameVisuals();
    }

    private void BlockFullscreenTitleBarDrag(object sender, InputMouseButtonEventArgs e) => BlockFullscreenTitleBarDragCore(e);

    private void BlockFullscreenTitleBarDrag(object sender, InputMouseEventArgs e) => BlockFullscreenTitleBarDragCore(e);

    private void TitleBar_PreviewMouseLeftButtonDown(object sender, InputMouseButtonEventArgs e)
    {
        if (IsInteractiveTitleBarSource(e.OriginalSource as DependencyObject))
            return;

        if (_isFullscreen)
        {
            e.Handled = true;
            KeepFullscreenBounds();
            return;
        }

        if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(HtCaption), IntPtr.Zero);
        e.Handled = true;
    }

    private void BlockFullscreenTitleBarDragCore(InputMouseEventArgs e)
    {
        if (!_isFullscreen || IsInteractiveTitleBarSource(e.OriginalSource as DependencyObject))
            return;

        if (e is InputMouseButtonEventArgs || e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            KeepFullscreenBounds();
        }
    }

    private void TitleBar_PreviewMouseRightButtonUp(object sender, InputMouseButtonEventArgs e)
    {
        if (IsInteractiveTitleBarSource(e.OriginalSource as DependencyObject))
            return;

        e.Handled = true;
        ShowSystemMenuSafe(e);
    }

    private void TitleBar_MouseRightButtonUp(object sender, InputMouseButtonEventArgs e)
    {
        if (IsInteractiveTitleBarSource(e.OriginalSource as DependencyObject))
            return;

        e.Handled = true;
    }

    private void ShowSystemMenuSafe(InputMouseButtonEventArgs e)
    {
        try
        {
            if (_isFullscreen)
                return;

            System.Windows.Point screenPoint;
            if (PresentationSource.FromVisual(this) != null)
            {
                screenPoint = PointToScreen(e.GetPosition(this));
            }
            else if (e.Source is Visual visual && PresentationSource.FromVisual(visual) != null)
            {
                screenPoint = visual.PointToScreen(e.GetPosition((IInputElement)visual));
            }
            else
            {
                NativeMethods.GetCursorPos(out var pt);
                screenPoint = new System.Windows.Point(pt.x, pt.y);
            }

            SystemCommands.ShowSystemMenu(this, screenPoint);
        }
        catch
        {
            // Ignore exceptions showing system menu
        }
    }

    private static bool IsInteractiveTitleBarSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.TextBox
                or Popup
                or System.Windows.Controls.ListBox
                or System.Windows.Controls.ListBoxItem
                or System.Windows.Controls.Menu
                or System.Windows.Controls.MenuItem)
                return true;

            if (source is FrameworkElement fe && fe.Name == "UrlBarBorder")
                return true;

            if (source is Visual or System.Windows.Media.Media3D.Visual3D)
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            else if (source is FrameworkContentElement fce)
                source = fce.Parent;
            else
                source = LogicalTreeHelper.GetParent(source);
        }

        return false;
    }


    private void KeepFullscreenBounds()
    {
        if (!_isFullscreen || _isApplyingFullscreenBounds || _isRestoringFromFullscreen)
            return;

        Dispatcher.BeginInvoke(new Action(ApplyFullscreenBounds), System.Windows.Threading.DispatcherPriority.Send);
    }
}
