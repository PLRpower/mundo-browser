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
    private const int HtMinButton = 8;
    private const int HtMaxButton = 9;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int HtClose = 20;
    private const int HtHelp = 21;


    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcHitTest)
        {
            if (_isFullscreen)
            {
                handled = true;
                return new IntPtr(HtClient);
            }

            var resizeHit = HitTestResizeBorder(hwnd, lParam);
            if (resizeHit != 0)
            {
                handled = true;
                return new IntPtr(resizeHit);
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
        UpdateResizeOverlayState();
        Dispatcher.BeginInvoke(
            new Action(() => {
                UpdateEdgeTriggerState(forceReopen: true);
                UpdateTopEdgeTriggerState(forceReopen: true);
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

        UpdateResizeOverlayState(forceReopen: true);
        Dispatcher.BeginInvoke(new Action(UpdateWindowFrameVisuals), System.Windows.Threading.DispatcherPriority.ContextIdle);
        UpdateEdgeTriggerState(forceReopen: true);
        UpdateTopEdgeTriggerState(forceReopen: true);
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
        SetResizeOverlayOpen(false);
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
        UpdateResizeOverlayState(forceReopen: true);
        UpdateEdgeTriggerState(forceReopen: true);
        UpdateTopEdgeTriggerState(forceReopen: true);
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

    private int HitTestResizeBorder(IntPtr hwnd, IntPtr lParam)
    {
        if (ResizeMode == ResizeMode.NoResize || WindowState == WindowState.Maximized)
            return 0;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return 0;

        int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        int border = GetResizeBorderThicknessInPixels();

        bool left = x >= rect.left && x < rect.left + border;
        bool right = x < rect.right && x >= rect.right - border;
        bool top = y >= rect.top && y < rect.top + border;
        bool bottom = y < rect.bottom && y >= rect.bottom - border;

        if (top && left) return HtTopLeft;
        if (top && right) return HtTopRight;
        if (bottom && left) return HtBottomLeft;
        if (bottom && right) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;

        return 0;
    }

    private int GetResizeBorderThicknessInPixels()
    {
        var source = PresentationSource.FromVisual(this);
        double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        return Math.Max(6, (int)Math.Ceiling(8 * dpiScale));
    }

    private void InitializeResizeOverlays()
    {
        foreach (var popup in GetResizeOverlayPopups())
        {
            popup.PlacementTarget = MainGrid;
            popup.StaysOpen = true;
            popup.PopupAnimation = PopupAnimation.None;
        }
    }

    private IEnumerable<Popup> GetResizeOverlayPopups()
    {
        yield return ResizeLeftPopup;
        yield return ResizeRightPopup;
        yield return ResizeTopPopup;
        yield return ResizeBottomPopup;
        yield return ResizeTopLeftPopup;
        yield return ResizeTopRightPopup;
        yield return ResizeBottomLeftPopup;
        yield return ResizeBottomRightPopup;
    }

    private void UpdateResizeOverlayState(bool forceReopen = false)
    {
        if (_isRestoringFromFullscreen || _isInSizeMove)
            return;

        bool isOpen = !_isFullscreen && WindowState == WindowState.Normal && ResizeMode != ResizeMode.NoResize;

        if (!isOpen)
        {
            SetResizeOverlayOpen(false);
            return;
        }

        SetResizeOverlayOffsets();

        if (forceReopen || !_resizeOverlaysOpen)
        {
            SetResizeOverlayOpen(false);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isFullscreen || WindowState != WindowState.Normal || ResizeMode == ResizeMode.NoResize)
                    return;

                InitializeResizeOverlays();
                SetResizeOverlayOffsets();
                SetResizeOverlayOpen(true);
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        else
        {
            SetResizeOverlayOpen(true);
        }
    }

    private void SetResizeOverlayOpen(bool isOpen)
    {
        foreach (var popup in GetResizeOverlayPopups())
            popup.IsOpen = isOpen;

        _resizeOverlaysOpen = isOpen;
    }

    private void SetResizeOverlayOffsets()
    {
        const double edge = 6;
        const double corner = 12;
        double width = Math.Max(0, MainGrid.ActualWidth);
        double height = Math.Max(0, MainGrid.ActualHeight);

        ResizeLeftPopup.HorizontalOffset = 0;
        ResizeLeftPopup.VerticalOffset = 0;
        ResizeRightPopup.HorizontalOffset = Math.Max(0, width - edge);
        ResizeRightPopup.VerticalOffset = 0;
        ResizeTopPopup.HorizontalOffset = 0;
        ResizeTopPopup.VerticalOffset = 0;
        ResizeBottomPopup.HorizontalOffset = 0;
        ResizeBottomPopup.VerticalOffset = Math.Max(0, height - edge);

        ResizeTopLeftPopup.HorizontalOffset = 0;
        ResizeTopLeftPopup.VerticalOffset = 0;
        ResizeTopRightPopup.HorizontalOffset = Math.Max(0, width - corner);
        ResizeTopRightPopup.VerticalOffset = 0;
        ResizeBottomLeftPopup.HorizontalOffset = 0;
        ResizeBottomLeftPopup.VerticalOffset = Math.Max(0, height - corner);
        ResizeBottomRightPopup.HorizontalOffset = Math.Max(0, width - corner);
        ResizeBottomRightPopup.VerticalOffset = Math.Max(0, height - corner);
    }

    private void ResizeOverlay_MouseLeftButtonDown(object sender, InputMouseButtonEventArgs e)
    {
        if (_isFullscreen || WindowState != WindowState.Normal || sender is not FrameworkElement { Tag: string hitTestText })
            return;

        if (!int.TryParse(hitTestText, out int hitTest))
            return;

        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(hitTest), IntPtr.Zero);
        e.Handled = true;
    }

    private void OnWindowStateChanged(bool forceResizeOverlayReopen = false)
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
        UpdateResizeOverlayState(forceResizeOverlayReopen);
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
