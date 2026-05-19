using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using System.Windows.Shell;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private void SetFullscreen(bool enable, bool hideUI = false)
    {
        if (enable == _isFullscreen) return;
        _isFullscreen = enable;

        if (enable) {
            _prevWindowState = (WindowState, WindowStyle, ResizeMode);
            
            // Disable rounding and transparency effects for true fullscreen to avoid artifacts
            NativeMethods.SetWindowCorners(this, NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND);
            NativeMethods.SetWindowBackdrop(this, NativeMethods.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE);
            this.Background = System.Windows.Media.Brushes.Black;

            WindowChrome.SetWindowChrome(this, null);
            
            if (hideUI) {
                if (TopBarControl != null) TopBarControl.Visibility = Visibility.Collapsed;
                if (EdgeTriggerPopup != null) EdgeTriggerPopup.Visibility = Visibility.Collapsed;
                UpdateSidebarWidth(false);
            }

            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            
            // Force re-calculation of min/max info (rcMonitor will be used)
            if (this.WindowState == WindowState.Maximized) this.WindowState = WindowState.Normal;
            this.WindowState = WindowState.Maximized;
        } else {
            // Restore Acrylic transparency and rounding
            NativeMethods.SetWindowCorners(this, NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
            NativeMethods.SetWindowBackdrop(this, NativeMethods.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW);
            this.Background = System.Windows.Media.Brushes.Transparent;

            // Restore window style first to let Windows know we are no longer "borderless fullscreen"
            this.WindowStyle = _prevWindowState.Style;
            this.ResizeMode = _prevWindowState.Resize;

            // Force a transition to Normal then back to previous state to ensure taskbar respect
            if (_prevWindowState.State == WindowState.Maximized) {
                this.WindowState = WindowState.Normal;
                this.WindowState = WindowState.Maximized;
            } else {
                this.WindowState = _prevWindowState.State;
            }
            
            if (TopBarControl != null) TopBarControl.Visibility = Visibility.Visible;
            if (EdgeTriggerPopup != null) EdgeTriggerPopup.Visibility = Visibility.Visible;
            if (DataContext is MainViewModel vm) UpdateSidebarWidth(vm.IsSidebarVisible);
            
            WindowChrome.SetWindowChrome(this, new WindowChrome { 
                CaptionHeight = 0, 
                ResizeBorderThickness = new Thickness(6), 
                GlassFrameThickness = new Thickness(-1), 
                CornerRadius = new CornerRadius(0) 
            });

            OnWindowStateChanged();
        }
    }

    private void UpdateSidebarWidth(bool visible)
    {
        if (SidebarColumn == null || SplitterColumn == null || DataContext is not MainViewModel vm) return;
        
        // In fullscreen, the sidebar MUST be floating (overlay) and not take space in the Grid
        if (_isFullscreen)
        {
            SidebarColumn.Width = new GridLength(0);
            SidebarColumn.MinWidth = 0;
            SplitterColumn.Width = new GridLength(0);
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
            
            // If the user toggles it visible in F11, we show the floating one
            if (visible && !_isSidebarFloating) ShowFloatingSidebar();
            else if (!visible && _isSidebarFloating) HideFloatingSidebar();
            
            return;
        }

        if (visible) {
            if (_isSidebarFloating) HideFloatingSidebar();
            SidebarColumn.Width = new GridLength(vm.SidebarWidth);
            SidebarColumn.MinWidth = 200;
            SplitterColumn.Width = GridLength.Auto;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Visible;
        } else {
            SidebarColumn.Width = new GridLength(0);
            SidebarColumn.MinWidth = 0;
            SplitterColumn.Width = new GridLength(0);
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowFloatingSidebar()
    {
        if (FloatingSidebarPopup == null || _isSidebarFloating) return;
        _isSidebarFloating = true;
        FloatingSidebarPopup.IsOpen = true;
        var slideIn = new System.Windows.Media.Animation.DoubleAnimation { From = -250, To = 0, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        FloatingSidebarContent.RenderTransform = new System.Windows.Media.TranslateTransform(-250, 0);
        FloatingSidebarContent.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);
    }

    private void HideFloatingSidebar()
    {
        if (FloatingSidebarPopup == null || !_isSidebarFloating) return;
        _isSidebarFloating = false;
        var slideOut = new System.Windows.Media.Animation.DoubleAnimation { From = 0, To = -250, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
        slideOut.Completed += (s, e) => { if (!_isSidebarFloating) FloatingSidebarPopup.IsOpen = false; };
        if (FloatingSidebarContent.RenderTransform is not System.Windows.Media.TranslateTransform) FloatingSidebarContent.RenderTransform = new System.Windows.Media.TranslateTransform(0, 0);
        FloatingSidebarContent.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOut);
    }

    private void FloatingSidebarContent_MouseLeave(object sender, MouseEventArgs e) => HideFloatingSidebar();

    private void OnWindowStateChanged()
    {
        bool isMax = WindowState == WindowState.Maximized;
        MainGrid.Margin = isMax ? new Thickness(0) : new Thickness(0);
        if (TopBarControl != null) { TopBarControl.Height = 40; }
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.ResizeBorderThickness = isMax ? new Thickness(0) : new Thickness(6);
        if (RightResizeBorder != null) RightResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (BottomResizeBorder != null) BottomResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (BottomRightResizeBorder != null) BottomRightResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isFullscreen) return;
        if (e.ChangedButton == MouseButton.Left) { if (e.OriginalSource != sender) return; if (e.ClickCount == 2) { 
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            _dragStartPos = null; 
        } else { if (WindowState == WindowState.Maximized) _dragStartPos = e.GetPosition(this); else try { DragMove(); } catch { } } }
    }

    private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (DataContext is MainViewModel vm && SidebarColumn != null && SidebarColumn.Width.IsAbsolute) vm.SidebarWidth = SidebarColumn.Width.Value;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var key = e.Key;
        if (key == Key.D && modifiers == ModifierKeys.Control) { ((MainViewModel)DataContext).ToggleSidebarCommand.Execute(null); e.Handled = true; }
        else if (key == Key.F5 || (key == Key.R && modifiers == ModifierKeys.Control)) { _webViewService.ActiveWebView?.Reload(); e.Handled = true; }
        else if (key == Key.F11) { SetFullscreen(!_isFullscreen); e.Handled = true; }
        else if (key == Key.T && modifiers == ModifierKeys.Control) { ((MainViewModel)DataContext).AddNewTabCommand.Execute(null); e.Handled = true; }
        else if (key == Key.W && modifiers == ModifierKeys.Control) { if (DataContext is MainViewModel vm && vm.SelectedTab != null) { vm.CloseTabCommand.Execute(vm.SelectedTab); e.Handled = true; } }
        else if ((key == Key.L && modifiers == ModifierKeys.Control) || (key == Key.D && modifiers == ModifierKeys.Alt)) { TopBarControl.AddressBar.Focus(); TopBarControl.AddressBar.SelectAll(); e.Handled = true; }
        else if ((key == Key.Left && modifiers == ModifierKeys.Alt) || key == Key.Back) { if (key == Key.Back && e.OriginalSource is System.Windows.Controls.TextBox) return; if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoBack) { _webViewService.ActiveWebView.GoBack(); e.Handled = true; } }
        else if (key == Key.Right && modifiers == ModifierKeys.Alt) { if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoForward) { _webViewService.ActiveWebView.GoForward(); e.Handled = true; } }
        else if (key == Key.Escape && ExtensionPopup.IsOpen) { CloseExtensionPopup(); e.Handled = true; }
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
            _webViewService.ActiveWebView.ZoomFactor = newZoom;
        }
    }

    private void ResetZoom()
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab != null && _webViewService.ActiveWebView != null)
        {
            vm.SelectedTab.ZoomFactor = 1.0;
            _webViewService.ActiveWebView.ZoomFactor = 1.0;
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ExtensionPopup.IsOpen) return;

        if (e.OriginalSource is DependencyObject d && FindAncestor<System.Windows.Controls.Button>(d) is System.Windows.Controls.Button btn && btn.Tag is string)
            return;

        var popupChild = ExtensionPopup.Child as FrameworkElement;
        if (popupChild == null) { CloseExtensionPopup(); return; }

        var popupSource = PresentationSource.FromVisual(popupChild) as System.Windows.Interop.HwndSource;
        if (popupSource == null) { CloseExtensionPopup(); return; }

        var screenPos = PointToScreen(e.GetPosition(this));

        System.Windows.Rect popupRect;
        NativeMethods.RECT rect;
        if (NativeMethods.GetWindowRect(popupSource.Handle, out rect))
        {
            popupRect = new System.Windows.Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
        }
        else
        {
            CloseExtensionPopup();
            return;
        }

        if (!popupRect.Contains(screenPos))
        {
            CloseExtensionPopup();
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isFullscreen) return;
        if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Maximized && _dragStartPos.HasValue)
        {
            System.Windows.Point currentPos = e.GetPosition(this);
            if (Math.Abs(currentPos.X - _dragStartPos.Value.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(currentPos.Y - _dragStartPos.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var mousePosOnScreen = PointToScreen(currentPos);
                double xRatio = currentPos.X / ActualWidth;
                _dragStartPos = null;
                WindowState = WindowState.Normal;
                Left = mousePosOnScreen.X - (ActualWidth * xRatio);
                Top = mousePosOnScreen.Y - 15;
                try { DragMove(); } catch { }
            }
        }
        else if (e.LeftButton != MouseButtonState.Pressed) _dragStartPos = null;
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
        if (!this.IsActive) return;
        if (DataContext is MainViewModel vm && !vm.IsSidebarVisible && !_isSidebarFloating) ShowFloatingSidebar();
    }
}