using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private void UpdateSidebarWidth(bool visible, bool animate = true)
    {
        if (SidebarColumn == null || SplitterColumn == null || DataContext is not MainViewModel vm) return;
        
        TranslateTransform? sidebarTransform = null;
        if (SidebarGrid != null)
        {
            if (SidebarGrid.RenderTransform is TranslateTransform tt)
                sidebarTransform = tt;
            else
            {
                sidebarTransform = new TranslateTransform(0, 0);
                SidebarGrid.RenderTransform = sidebarTransform;
            }
        }

        // Fullscreen never reserves layout space for the sidebar. It is shown temporarily as an overlay.
        if (_isFullscreen)
        {
            SidebarColumn.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, null);
            SidebarColumn.Width = new GridLength(0);
            SidebarColumn.MinWidth = 0;
            SplitterColumn.Width = new GridLength(0);
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
            if (SidebarGrid != null) SidebarGrid.Visibility = Visibility.Collapsed;
            if (sidebarTransform != null)
            {
                sidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
                sidebarTransform.X = 0;
            }

            if (_fullscreenHidesUi)
                HideFloatingSidebar(animate: false);

            if (FloatingSidebarPopup != null)
                FloatingSidebarPopup.VerticalOffset = 0;
            if (FloatingSidebarControl != null)
                FloatingSidebarControl.ShowNavButtons = false;
            if (SidebarGrid != null)
            {
                SidebarGrid.ShowNavButtons = false;
                SidebarGrid.Margin = new Thickness(0, 0, 0, 0);
            }

            UpdateEdgeTriggerState();
            UpdateTopEdgeTriggerState();
            return;
        }

        bool isTopBarPinned = (WindowState != WindowState.Maximized || vm.IsTopBarVisible) && !_isFullscreen;
        bool isSidebarPinned = visible && !_isFullscreen;
        if (FloatingSidebarPopup != null)
            FloatingSidebarPopup.VerticalOffset = isTopBarPinned ? 40 : 0;
        if (FloatingSidebarControl != null)
            FloatingSidebarControl.ShowNavButtons = false;
        if (SidebarGrid != null)
        {
            SidebarGrid.ShowNavButtons = isSidebarPinned && !isTopBarPinned;
            SidebarGrid.Margin = new Thickness(0, 0, 0, 0);
        }

        double targetWidth = visible ? vm.SidebarWidth : 0;

        // Cancel any running animations on SidebarColumn and sidebarTransform
        SidebarColumn.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, null);
        if (sidebarTransform != null)
        {
            sidebarTransform.BeginAnimation(TranslateTransform.XProperty, null);
            sidebarTransform.X = 0;
        }

        if (visible)
        {
            HideFloatingSidebar(animate: false);
            if (SidebarGrid != null) SidebarGrid.Visibility = Visibility.Visible;
            SidebarColumn.Width = new GridLength(targetWidth);
            SidebarColumn.MinWidth = 200;
            SplitterColumn.Width = GridLength.Auto;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Visible;
            UpdateEdgeTriggerState();
            UpdateTopEdgeTriggerState();
        }
        else
        {
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
            if (SidebarGrid != null) SidebarGrid.Visibility = Visibility.Collapsed;
            HideFloatingSidebar(animate: false);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                SidebarColumn.Width = new GridLength(0);
                SidebarColumn.MinWidth = 0;
                SplitterColumn.Width = new GridLength(0);
                UpdateEdgeTriggerState();
                UpdateTopEdgeTriggerState();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void UpdateTopBarHeight(bool visible, bool animate = true)
    {
        if (WindowTitleBar == null || DataContext is not MainViewModel vm) return;

        if (_isFullscreen)
        {
            WindowTitleBar.BeginAnimation(FrameworkElement.HeightProperty, null);
            WindowTitleBar.Height = 0;
            WindowTitleBar.Visibility = Visibility.Collapsed;
            if (_fullscreenHidesUi)
                HideFloatingTopBar(animate: false);

            if (FloatingSidebarPopup != null)
                FloatingSidebarPopup.VerticalOffset = 0;
            if (FloatingSidebarControl != null)
                FloatingSidebarControl.ShowNavButtons = false;
            if (SidebarGrid != null)
            {
                SidebarGrid.ShowNavButtons = false;
                SidebarGrid.Margin = new Thickness(0, 0, 0, 0);
            }

            UpdateTopEdgeTriggerState();
            return;
        }

        bool isForcedFixed = WindowState != WindowState.Maximized;
        bool effectiveVisible = isForcedFixed || visible;

        double targetHeight = effectiveVisible ? 40 : 0;
        bool isTopBarPinned = effectiveVisible && !_isFullscreen;
        bool isSidebarPinned = vm.IsSidebarVisible && !_isFullscreen;

        WindowTitleBar.BeginAnimation(FrameworkElement.HeightProperty, null);
        if (effectiveVisible)
        {
            HideFloatingTopBar(animate: false);
            WindowTitleBar.Visibility = Visibility.Visible;
            WindowTitleBar.Height = 40;
        }
        else
        {
            WindowTitleBar.Height = 0;
            WindowTitleBar.Visibility = Visibility.Collapsed;
            HideFloatingTopBar(animate: false);
        }

        if (FloatingSidebarPopup != null)
            FloatingSidebarPopup.VerticalOffset = isTopBarPinned ? 40 : 0;
        if (FloatingSidebarControl != null)
            FloatingSidebarControl.ShowNavButtons = false;
        if (SidebarGrid != null)
        {
            SidebarGrid.ShowNavButtons = isSidebarPinned && !isTopBarPinned;
            SidebarGrid.Margin = new Thickness(0, 0, 0, 0);
        }

        UpdateTopEdgeTriggerState();
        UpdateEdgeTriggerState();
    }

    private void ShowFloatingSidebar()
    {
        if (FloatingSidebarPopup == null || _fullscreenHidesUi) return;
        if (_isSidebarFloating && FloatingSidebarPopup.IsOpen) return;

        bool isTopBarPinned = DataContext is MainViewModel vm && (WindowState != WindowState.Maximized || vm.IsTopBarVisible) && !_isFullscreen;
        FloatingSidebarPopup.VerticalOffset = isTopBarPinned ? 40 : 0;
        if (FloatingSidebarControl != null)
        {
            FloatingSidebarControl.ShowNavButtons = false;
        }

        double sidebarWidth = (DataContext is MainViewModel vm2) ? vm2.SidebarWidth : 250;
        _isSidebarFloating = true;
        FloatingSidebarContent.DataContext = DataContext;
        FloatingSidebarPopup.IsOpen = true;
        var slideIn = new DoubleAnimation 
        { 
            From = -sidebarWidth, 
            To = 0, 
            Duration = TimeSpan.FromMilliseconds(220), 
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } 
        };
        if (FloatingSidebarContent.RenderTransform is not TranslateTransform)
            FloatingSidebarContent.RenderTransform = new TranslateTransform(-sidebarWidth, 0);

        FloatingSidebarContent.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

        // Let the current edge mouse event complete before closing its source popup.
        Dispatcher.BeginInvoke(
            new Action(() => UpdateEdgeTriggerState()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HideFloatingSidebar(bool animate = true)
    {
        if (FloatingSidebarPopup == null) return;

        if (!_isSidebarFloating)
        {
            if (FloatingSidebarPopup.IsOpen)
                FloatingSidebarPopup.IsOpen = false;
            UpdateEdgeTriggerState();
            return;
        }

        _isSidebarFloating = false;

        if (!animate)
        {
            FloatingSidebarContent.RenderTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);
            FloatingSidebarPopup.IsOpen = false;
            UpdateEdgeTriggerState();
            return;
        }

        double sidebarWidth = (DataContext is MainViewModel vm) ? vm.SidebarWidth : 250;
        var slideOut = new DoubleAnimation 
        { 
            From = 0, 
            To = -sidebarWidth, 
            Duration = TimeSpan.FromMilliseconds(200), 
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } 
        };
        slideOut.Completed += (_, _) =>
        {
            if (!_isSidebarFloating)
                FloatingSidebarPopup.IsOpen = false;
            UpdateEdgeTriggerState();
        };
        if (FloatingSidebarContent.RenderTransform is not TranslateTransform) 
            FloatingSidebarContent.RenderTransform = new TranslateTransform(0, 0);
        FloatingSidebarContent.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
    }

    private void FloatingSidebarContent_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel { IsDraggingTab: true })
            return;

        HideFloatingSidebar();
    }

    private FloatingTopBarZone _currentFloatingZone = FloatingTopBarZone.None;

    private FloatingTopBarZone DetermineZoneFromX(double x)
    {
        double width = MainGrid?.ActualWidth ?? ActualWidth;
        if (width <= 0) width = 1200;

        bool sidebarPinned = DataContext is MainViewModel vm && vm.IsSidebarVisible && !_isFullscreen;
        double sidebarWidth = (DataContext is MainViewModel vmW) ? vmW.SidebarWidth : 250;

        // Right zone: top-right corner for window controls (Min, Max, Close) + tools & extensions ~340px - 420px
        double rightBound = width - Math.Max(320, Math.Min(420, width * 0.25));

        if (sidebarPinned)
        {
            // When sidebar is pinned, left zone is disabled (nav buttons are permanently on the sidebar)
            if (x < sidebarWidth)
                return FloatingTopBarZone.None;
            if (x > rightBound)
                return FloatingTopBarZone.Right;

            return FloatingTopBarZone.Center;
        }
        else
        {
            // Left zone: top-left corner for navigation buttons (Back, Forward, Reload) ~180px - 220px
            double leftBound = Math.Max(180, Math.Min(240, width * 0.15));

            if (x < leftBound)
                return FloatingTopBarZone.Left;
            if (x > rightBound)
                return FloatingTopBarZone.Right;

            // Center zone: search & address bar in between
            return FloatingTopBarZone.Center;
        }
    }

    private void SetFloatingCaptionButtonsVisible(bool visible, bool animate = true)
    {
        if (FloatingTitleBar == null) return;

        FloatingTitleBar.ShowMinimize = visible;
        FloatingTitleBar.ShowMaximize = visible;
        FloatingTitleBar.ShowClose = visible;
        FloatingTitleBar.ShowHelp = false;

        foreach (var btn in FindVisualChildren<Wpf.Ui.Controls.TitleBarButton>(FloatingTitleBar))
        {
            if (btn.ButtonType == Wpf.Ui.Controls.TitleBarButtonType.Help)
            {
                btn.Visibility = Visibility.Collapsed;
            }
            else
            {
                btn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                btn.Background = System.Windows.Media.Brushes.Transparent;
            }
        }

        if (visible)
        {
            UpdateFloatingTitleBarButtonsBackground();
        }
    }

    private DispatcherTimer? _floatingTopBarCheckTimer;

    private void StartFloatingTopBarMonitor()
    {
        if (_floatingTopBarCheckTimer == null)
        {
            _floatingTopBarCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _floatingTopBarCheckTimer.Tick += (s, e) =>
            {
                if (!_isTopBarFloating || FloatingTopBarPopup == null || !FloatingTopBarPopup.IsOpen)
                {
                    _floatingTopBarCheckTimer.Stop();
                    return;
                }

                if (DataContext is MainViewModel { IsDraggingTab: true })
                    return;

                if (FloatingTopBarControl != null && (FloatingTopBarControl.AddressBar.IsKeyboardFocused ||
                                                      FloatingTopBarControl.AddressBar.IsKeyboardFocusWithin ||
                                                      FloatingTopBarControl.IsSuggestionsOpen ||
                                                      FloatingTopBarControl.IsAnyMenuOrPopupOpen))
                    return;

                if (_extensionPopupWindow?.IsVisible == true)
                    return;

                if (Helpers.NativeMethods.GetCursorPos(out var pt))
                {
                    try
                    {
                        var windowPoint = PointFromScreen(new System.Windows.Point(pt.x, pt.y));
                        double width = MainGrid?.ActualWidth ?? ActualWidth;

                        // Only hide if the cursor moved down into the web page content, or far outside the window horizontally
                        if (windowPoint.Y > 44 || windowPoint.X < -30 || windowPoint.X > width + 30)
                        {
                            HideFloatingTopBar();
                            return;
                        }

                        // Dynamically switch sections when moving cursor horizontally across the top
                        FloatingTopBarZone newZone = DetermineZoneFromX(windowPoint.X);
                        if (newZone == FloatingTopBarZone.None)
                        {
                            HideFloatingTopBar();
                            return;
                        }

                        if (newZone != _currentFloatingZone)
                        {
                            _currentFloatingZone = newZone;
                            FloatingTopBarControl?.SetVisibleSection(newZone, animate: true);
                            SetFloatingCaptionButtonsVisible(newZone == FloatingTopBarZone.Right || newZone == FloatingTopBarZone.All, animate: true);
                        }
                    }
                    catch
                    {
                        // Ignore if window is closing or coordinate translation unavailable
                    }
                }
            };
        }
        _floatingTopBarCheckTimer.Start();
    }

    private void ShowFloatingTopBar(FloatingTopBarZone zone = FloatingTopBarZone.All)
    {
        if (FloatingTopBarPopup == null || _fullscreenHidesUi) return;
        if (zone == FloatingTopBarZone.None) return;
        if (!_isFullscreen && WindowState != WindowState.Maximized) return;

        double width = MainGrid?.ActualWidth ?? ActualWidth;

        FloatingTopBarPopup.HorizontalOffset = 0;
        if (width > 0)
            FloatingTopBarPopup.Width = width;

        _currentFloatingZone = zone;
        FloatingTopBarControl?.SetVisibleSection(zone, animate: false);
        SetFloatingCaptionButtonsVisible(zone == FloatingTopBarZone.Right || zone == FloatingTopBarZone.All, animate: false);

        if (!_isTopBarFloating || !FloatingTopBarPopup.IsOpen)
        {
            _isTopBarFloating = true;
            FloatingTopBarContent.DataContext = DataContext;
            FloatingTopBarPopup.IsOpen = true;

            var slideDown = new DoubleAnimation 
            { 
                From = -40, 
                To = 0, 
                Duration = TimeSpan.FromMilliseconds(220), 
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } 
            };

            FloatingTopBarTransform.Y = -40;
            FloatingTopBarTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    UpdateTopEdgeTriggerState();
                    SetFloatingCaptionButtonsVisible(_currentFloatingZone == FloatingTopBarZone.Right || _currentFloatingZone == FloatingTopBarZone.All, animate: false);
                }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        StartFloatingTopBarMonitor();
    }

    private void HideFloatingTopBar(bool animate = true)
    {
        _floatingTopBarCheckTimer?.Stop();

        if (FloatingTopBarPopup == null) return;

        if (!_isTopBarFloating)
        {
            if (FloatingTopBarPopup.IsOpen)
                FloatingTopBarPopup.IsOpen = false;
            _currentFloatingZone = FloatingTopBarZone.None;
            UpdateTopEdgeTriggerState();
            return;
        }

        _isTopBarFloating = false;
        _currentFloatingZone = FloatingTopBarZone.None;

        if (!animate)
        {
            FloatingTopBarTransform.BeginAnimation(TranslateTransform.YProperty, null);
            FloatingTopBarPopup.IsOpen = false;
            UpdateTopEdgeTriggerState();
            return;
        }

        var slideUp = new DoubleAnimation 
        { 
            From = 0, 
            To = -40, 
            Duration = TimeSpan.FromMilliseconds(180), 
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } 
        };

        slideUp.Completed += (_, _) =>
        {
            if (!_isTopBarFloating)
                FloatingTopBarPopup.IsOpen = false;
            UpdateTopEdgeTriggerState();
        };

        FloatingTopBarTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
    }

    private void FloatingTopBarContent_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isTopBarFloating) return;

        if (FloatingTopBarControl != null && (FloatingTopBarControl.AddressBar.IsKeyboardFocused ||
                                              FloatingTopBarControl.AddressBar.IsKeyboardFocusWithin ||
                                              FloatingTopBarControl.IsSuggestionsOpen ||
                                              FloatingTopBarControl.IsAnyMenuOrPopupOpen))
            return;

        if (_extensionPopupWindow?.IsVisible == true)
            return;

        double x = -1;
        if (Helpers.NativeMethods.GetCursorPos(out var pt))
        {
            try
            {
                var windowPoint = PointFromScreen(new System.Windows.Point(pt.x, pt.y));
                x = windowPoint.X;
            }
            catch { }
        }

        if (x < 0)
        {
            System.Windows.Point p = e.GetPosition(this);
            x = p.X;
        }

        FloatingTopBarZone newZone = DetermineZoneFromX(x);
        if (newZone == FloatingTopBarZone.None)
        {
            HideFloatingTopBar();
            return;
        }

        if (newZone != _currentFloatingZone)
        {
            _currentFloatingZone = newZone;
            FloatingTopBarControl?.SetVisibleSection(newZone, animate: true);
            SetFloatingCaptionButtonsVisible(newZone == FloatingTopBarZone.Right || newZone == FloatingTopBarZone.All, animate: true);
        }
    }

    private void FloatingTopBarContent_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel { IsDraggingTab: true })
            return;

        if (FloatingTopBarControl != null && (FloatingTopBarControl.AddressBar.IsKeyboardFocused ||
                                              FloatingTopBarControl.AddressBar.IsKeyboardFocusWithin ||
                                              FloatingTopBarControl.IsSuggestionsOpen ||
                                              FloatingTopBarControl.IsAnyMenuOrPopupOpen))
            return;

        if (_extensionPopupWindow?.IsVisible == true)
            return;

        if (Helpers.NativeMethods.GetCursorPos(out var pt))
        {
            try
            {
                var windowPoint = PointFromScreen(new System.Windows.Point(pt.x, pt.y));
                double width = MainGrid?.ActualWidth ?? ActualWidth;
                double minX = (DataContext is MainViewModel vm && vm.IsSidebarVisible && !_isFullscreen)
                    ? vm.SidebarWidth
                    : -30;

                // If cursor is still in the top bar area, do NOT hide!
                if (windowPoint.Y <= 44 && windowPoint.X >= minX && windowPoint.X <= width + 30)
                {
                    return;
                }
            }
            catch
            {
                // Ignore
            }
        }

        HideFloatingTopBar();
    }

    public void OnFloatingTopBarFocusLost()
    {
        if (_isTopBarFloating && FloatingTopBarPopup != null && FloatingTopBarContent != null)
        {
            bool isBusy = (FloatingTopBarControl != null && (FloatingTopBarControl.AddressBar.IsKeyboardFocused ||
                                                            FloatingTopBarControl.AddressBar.IsKeyboardFocusWithin ||
                                                            FloatingTopBarControl.IsSuggestionsOpen ||
                                                            FloatingTopBarControl.IsAnyMenuOrPopupOpen)) ||
                          (_extensionPopupWindow?.IsVisible == true);

            if (!isBusy)
            {
                HideFloatingTopBar();
            }
        }
    }

    public void FocusAddressBar()
    {
        bool isFloating = _isFullscreen || (DataContext is MainViewModel vm && !vm.IsTopBarVisible && WindowState == WindowState.Maximized);
        var topBar = isFloating ? FloatingTopBarControl : TopBarControl;

        if (isFloating)
        {
            ShowFloatingTopBar(FloatingTopBarZone.Center);
        }

        if (topBar != null)
        {
            if (topBar == FloatingTopBarControl && PresentationSource.FromVisual(topBar) is System.Windows.Interop.HwndSource source && source.Handle != IntPtr.Zero)
            {
                NativeMethods.SetFocus(source.Handle);
            }
            topBar.AddressBar.Focus();
            topBar.AddressBar.SelectAll();
        }
    }

    private void MainViewModel_TabDragCompleted(object? sender, EventArgs e)
    {
        if (_isSidebarFloating && FloatingSidebarPopup != null && !FloatingSidebarContent.IsMouseOver)
        {
            HideFloatingSidebar();
        }
        if (_isTopBarFloating && FloatingTopBarPopup != null && !FloatingTopBarContent.IsMouseOver)
        {
            HideFloatingTopBar();
        }
    }

    private void UpdateEdgeTriggerState(bool forceReopen = false)
    {
        if (EdgeTriggerPopup == null) return;

        bool sidebarPinned = DataContext is MainViewModel vm && vm.IsSidebarVisible;
        bool shouldOpen = !_isInSizeMove
                          && !_fullscreenHidesUi
                          && !_isSidebarFloating
                          && (_isFullscreen || !sidebarPinned);

        EdgeTriggerPopup.Visibility = _fullscreenHidesUi ? Visibility.Collapsed : Visibility.Visible;
        EdgeTriggerPopup.HorizontalOffset = 0;

        bool isTopBarPinned = DataContext is MainViewModel vmTop && (WindowState != WindowState.Maximized || vmTop.IsTopBarVisible) && !_isFullscreen;
        double topOffset = (isTopBarPinned ? 40 : 0) + 44;
        double contentHeight = (ContentRow != null && ContentRow.ActualHeight > 0)
            ? ContentRow.ActualHeight
            : ((MainGrid != null ? MainGrid.ActualHeight : ActualHeight) - (isTopBarPinned ? 40 : 0));
        double triggerHeight = Math.Max(0, contentHeight - 44 - 20);

        EdgeTriggerPopup.VerticalOffset = topOffset;
        if (triggerHeight > 0)
            EdgeTriggerPopup.Height = triggerHeight;

        if (shouldOpen && forceReopen && EdgeTriggerPopup.IsOpen)
            EdgeTriggerPopup.IsOpen = false;

        EdgeTriggerPopup.IsOpen = shouldOpen;

        if (shouldOpen)
            RepositionEdgeTriggerPopup();
    }

    private void UpdateTopEdgeTriggerState(bool forceReopen = false)
    {
        if (TopEdgeTriggerPopup == null) return;

        bool topBarPinned = !_isFullscreen && (WindowState != WindowState.Maximized || (DataContext is MainViewModel vm && vm.IsTopBarVisible));
        bool shouldOpen = !_isInSizeMove
                          && !_fullscreenHidesUi
                          && !_isTopBarFloating
                          && !topBarPinned;

        TopEdgeTriggerPopup.Visibility = _fullscreenHidesUi ? Visibility.Collapsed : Visibility.Visible;

        double width = MainGrid?.ActualWidth ?? ActualWidth;
        double sidebarWidth = (DataContext is MainViewModel vmSide && vmSide.IsSidebarVisible && !_isFullscreen)
            ? vmSide.SidebarWidth
            : 0;

        TopEdgeTriggerPopup.HorizontalOffset = sidebarWidth;
        if (width > sidebarWidth)
            TopEdgeTriggerPopup.Width = width - sidebarWidth;
        else
            TopEdgeTriggerPopup.Width = width;
        TopEdgeTriggerPopup.VerticalOffset = 0;

        if (shouldOpen && forceReopen && TopEdgeTriggerPopup.IsOpen)
            TopEdgeTriggerPopup.IsOpen = false;

        TopEdgeTriggerPopup.IsOpen = shouldOpen;

        if (shouldOpen)
            RepositionTopEdgeTriggerPopup();
    }

    private void ToggleSidebar()
    {
        if (_fullscreenHidesUi || DataContext is not MainViewModel vm)
            return;

        if (_isFullscreen)
        {
            if (_isSidebarFloating)
                HideFloatingSidebar();
            else
                ShowFloatingSidebar();
            return;
        }

        vm.ToggleSidebarCommand.Execute(null);
    }

    private void ToggleTopBar()
    {
        if (_fullscreenHidesUi || DataContext is not MainViewModel vm)
            return;

        if (_isFullscreen)
        {
            if (_isTopBarFloating)
                HideFloatingTopBar();
            else
                ShowFloatingTopBar();
            return;
        }

        if (WindowState != WindowState.Maximized)
            return;

        vm.ToggleTopBarCommand.Execute(null);
    }

    private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (DataContext is MainViewModel vm && SidebarColumn != null && SidebarColumn.Width.IsAbsolute)
        {
            vm.SidebarWidth = SidebarColumn.Width.Value;
            UpdateTopEdgeTriggerState();
        }
    }

    private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm && SidebarColumn != null && SidebarColumn.Width.IsAbsolute)
        {
            vm.SetSidebarWidth(SidebarColumn.Width.Value);
            UpdateTopEdgeTriggerState();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var key = e.Key;
        if (key == Key.D && modifiers == ModifierKeys.Control) { ToggleSidebar(); e.Handled = true; }
        else if (key == Key.E && modifiers == ModifierKeys.Control) { ToggleTopBar(); e.Handled = true; }
        else if (key == Key.F5 || (key == Key.R && modifiers == ModifierKeys.Control)) { _webViewService.ActiveWebView?.Reload(); e.Handled = true; }
        else if (key == Key.F11) { SetFullscreen(!_isFullscreen); e.Handled = true; }
        else if (key == Key.F12 || (key == Key.I && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))) { if (DataContext is MainViewModel vm && vm.SelectedTab != null) { _ = _webViewService.ToggleDevToolsForTabAsync(vm.SelectedTab); e.Handled = true; } }
        else if (key == Key.S && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { if (DataContext is MainViewModel vm) { vm.ToggleSplitViewCommand.Execute(null); e.Handled = true; } }
        else if (key == Key.J && modifiers == ModifierKeys.Control) { _webViewService.OpenDownloadDialog(); e.Handled = true; }
        else if (key == Key.T && modifiers == ModifierKeys.Control) { ((MainViewModel)DataContext).AddNewTabCommand.Execute(null); e.Handled = true; }
        else if (key == Key.W && modifiers == ModifierKeys.Control) { if (DataContext is MainViewModel vm && vm.SelectedTab != null) { vm.CloseTabCommand.Execute(vm.SelectedTab); e.Handled = true; } }
        else if ((key == Key.L && modifiers == ModifierKeys.Control) || (key == Key.D && modifiers == ModifierKeys.Alt)) { FocusAddressBar(); e.Handled = true; }
        else if ((key == Key.Left && modifiers == ModifierKeys.Alt) || key == Key.Back) { if (key == Key.Back && e.OriginalSource is System.Windows.Controls.TextBox) return; if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoBack) { _webViewService.ActiveWebView.GoBack(); e.Handled = true; } }
        else if (key == Key.Right && modifiers == ModifierKeys.Alt) { if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoForward) { _webViewService.ActiveWebView.GoForward(); e.Handled = true; } }
        else if (key == Key.Escape && _extensionPopupWindow?.IsVisible == true) { CloseExtensionPopup(); e.Handled = true; }
        else if (modifiers == ModifierKeys.Control)
        {
            if (key == Key.OemPlus || key == Key.Add) { AdjustZoom(0.1); e.Handled = true; }
            else if (key == Key.OemMinus || key == Key.Subtract) { AdjustZoom(-0.1); e.Handled = true; }
            else if (key == Key.D0 || key == Key.NumPad0) { ResetZoom(); e.Handled = true; }
        }
    }

    private void AdjustZoom(double delta)
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab != null && _webViewService.ActiveWebView != null)
        {
            double newZoom = Math.Clamp(vm.SelectedTab.ZoomFactor + delta, 0.25, 5.0);
            vm.SelectedTab.ZoomFactor = newZoom;
            try { _webViewService.ActiveWebView.ZoomFactor = newZoom; } catch { }
        }
    }

    private void ResetZoom()
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab != null && _webViewService.ActiveWebView != null)
        {
            vm.SelectedTab.ZoomFactor = 1.0;
            try { _webViewService.ActiveWebView.ZoomFactor = 1.0; } catch { }
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 1. Close extension popup if click is outside
        if (_extensionPopupWindow?.IsVisible == true)
        {
            if (!(e.OriginalSource is DependencyObject d && FindAncestor<System.Windows.Controls.Button>(d) is System.Windows.Controls.Button btn && btn.Tag is string))
                CloseExtensionPopup();
        }

        // 2. Clear address bar focus if click is outside in WPF
        bool isAddressBarFocused = (TopBarControl?.AddressBar.IsFocused == true) || (FloatingTopBarControl?.AddressBar.IsFocused == true);
        if (isAddressBarFocused || (DataContext is MainViewModel vm && vm.IsPendingNewTab))
        {
            var clickedElement = e.OriginalSource as DependencyObject;
            bool clickedInsideAddressBar = clickedElement != null &&
                (FindAncestor<TopBarView>(clickedElement) != null ||
                 (TopBarControl != null && FindAncestor<System.Windows.Controls.Primitives.Popup>(clickedElement) == TopBarControl.SuggestionsPopupControl) ||
                 (FloatingTopBarControl != null && FindAncestor<System.Windows.Controls.Primitives.Popup>(clickedElement) == FloatingTopBarControl.SuggestionsPopupControl));

            if (!clickedInsideAddressBar)
            {
                if (DataContext is MainViewModel vmEsc)
                {
                    if (vmEsc.IsPendingNewTab)
                    {
                        vmEsc.IsPendingNewTab = false;
                        if (vmEsc.SelectedTab != null)
                            vmEsc.AddressBarText = vmEsc.SelectedTab.AddressUrl;
                    }
                }

                // Focus WebView if available, otherwise clear focus
                var wv = GetActiveWebView();
                if (wv != null) wv.Focus();
                else Keyboard.ClearFocus();
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            else if (current is FrameworkContentElement fce)
                current = fce.Parent;
            else
                current = LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                yield return typedChild;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void MainGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TopBarControl != null && TopBarControl.AddressBar.IsFocused)
        {
            MainGrid.Focus();
        }
    }

    private void Edge_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!IsActive || _fullscreenHidesUi || _isSidebarFloating) return;
        if (!_isFullscreen && DataContext is MainViewModel { IsSidebarVisible: true }) return;

        bool isTopBarPinned = DataContext is MainViewModel vm && (WindowState != WindowState.Maximized || vm.IsTopBarVisible) && !_isFullscreen;
        double topOffset = (isTopBarPinned ? 40 : 0) + 44;
        double contentHeight = (ContentRow != null && ContentRow.ActualHeight > 0)
            ? ContentRow.ActualHeight
            : ((MainGrid != null ? MainGrid.ActualHeight : ActualHeight) - (isTopBarPinned ? 40 : 0));
        double sidebarHeight = Math.Max(0, contentHeight - 44 - 20);
        double bottomLimit = topOffset + sidebarHeight;

        double y = -1;
        if (Helpers.NativeMethods.GetCursorPos(out var pt))
        {
            try
            {
                var windowPoint = PointFromScreen(new System.Windows.Point(pt.x, pt.y));
                y = windowPoint.Y;
            }
            catch { }
        }

        if (y < 0)
        {
            System.Windows.Point p = e.GetPosition(this);
            y = p.Y;
        }

        if (y >= topOffset && y <= bottomLimit)
        {
            ShowFloatingSidebar();
        }
    }

    private void TopEdge_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!IsActive || _fullscreenHidesUi) return;
        if (!_isFullscreen && (WindowState != WindowState.Maximized || DataContext is MainViewModel { IsTopBarVisible: true })) return;

        double x = -1;
        if (Helpers.NativeMethods.GetCursorPos(out var pt))
        {
            try
            {
                var windowPoint = PointFromScreen(new System.Windows.Point(pt.x, pt.y));
                x = windowPoint.X;
            }
            catch { }
        }

        if (x < 0)
        {
            System.Windows.Point p = e.GetPosition(this);
            x = p.X;
        }

        FloatingTopBarZone zone = DetermineZoneFromX(x);
        if (zone == FloatingTopBarZone.None) return;
        ShowFloatingTopBar(zone);
    }
}
