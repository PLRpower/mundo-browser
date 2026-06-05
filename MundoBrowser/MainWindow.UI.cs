using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
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

            if (_fullscreenHidesUi)
            {
                if (_isSidebarFloating)
                    HideFloatingSidebar();
                else if (FloatingSidebarPopup != null)
                    FloatingSidebarPopup.IsOpen = false;

                return;
            }

            if (FloatingSidebarPopup != null)
                FloatingSidebarPopup.VerticalOffset = WindowTitleBar?.Visibility == Visibility.Visible ? 40 : 0;

            // If the user toggles it visible in F11, we show the floating one
            if (visible)
            {
                if (!_isSidebarFloating) ShowFloatingSidebar();
            }
            else if (_isSidebarFloating)
            {
                HideFloatingSidebar();
            }
            else if (FloatingSidebarPopup != null)
            {
                FloatingSidebarPopup.IsOpen = false;
            }
            
            return;
        }

        if (FloatingSidebarPopup != null)
            FloatingSidebarPopup.VerticalOffset = 40;

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
        if (FloatingSidebarPopup == null) return;
        if (_isSidebarFloating && FloatingSidebarPopup.IsOpen) return;

        _isSidebarFloating = true;
        FloatingSidebarContent.DataContext = DataContext;
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
