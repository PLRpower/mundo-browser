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
        
        // Fullscreen never reserves layout space for the sidebar. It is shown temporarily as an overlay.
        if (_isFullscreen)
        {
            SidebarColumn.Width = new GridLength(0);
            SidebarColumn.MinWidth = 0;
            SplitterColumn.Width = new GridLength(0);
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;

            if (_fullscreenHidesUi)
                HideFloatingSidebar(animate: false);

            if (FloatingSidebarPopup != null)
                FloatingSidebarPopup.VerticalOffset = WindowTitleBar?.Visibility == Visibility.Visible ? 40 : 0;

            UpdateEdgeTriggerState();
            return;
        }

        if (FloatingSidebarPopup != null)
            FloatingSidebarPopup.VerticalOffset = 40;

        if (visible)
        {
            HideFloatingSidebar(animate: false);
            SidebarColumn.Width = new GridLength(vm.SidebarWidth);
            SidebarColumn.MinWidth = 200;
            SplitterColumn.Width = GridLength.Auto;
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarColumn.Width = new GridLength(0);
            SidebarColumn.MinWidth = 0;
            SplitterColumn.Width = new GridLength(0);
            if (SidebarSplitter != null) SidebarSplitter.Visibility = Visibility.Collapsed;
        }

        UpdateEdgeTriggerState();
    }

    private void ShowFloatingSidebar()
    {
        if (FloatingSidebarPopup == null || _fullscreenHidesUi) return;
        if (_isSidebarFloating && FloatingSidebarPopup.IsOpen) return;

        _isSidebarFloating = true;
        FloatingSidebarContent.DataContext = DataContext;
        FloatingSidebarPopup.IsOpen = true;
        var slideIn = new System.Windows.Media.Animation.DoubleAnimation { From = -250, To = 0, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
        FloatingSidebarContent.RenderTransform = new System.Windows.Media.TranslateTransform(-250, 0);
        FloatingSidebarContent.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);

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
                System.Windows.Media.TranslateTransform.XProperty,
                null);
            FloatingSidebarPopup.IsOpen = false;
            UpdateEdgeTriggerState();
            return;
        }

        var slideOut = new System.Windows.Media.Animation.DoubleAnimation { From = 0, To = -250, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
        slideOut.Completed += (_, _) =>
        {
            if (!_isSidebarFloating)
                FloatingSidebarPopup.IsOpen = false;
            UpdateEdgeTriggerState();
        };
        if (FloatingSidebarContent.RenderTransform is not System.Windows.Media.TranslateTransform) FloatingSidebarContent.RenderTransform = new System.Windows.Media.TranslateTransform(0, 0);
        FloatingSidebarContent.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOut);
    }

    private void FloatingSidebarContent_MouseLeave(object sender, MouseEventArgs e) => HideFloatingSidebar();

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

        if (shouldOpen && forceReopen && EdgeTriggerPopup.IsOpen)
            EdgeTriggerPopup.IsOpen = false;

        EdgeTriggerPopup.IsOpen = shouldOpen;

        if (shouldOpen)
            RepositionEdgeTriggerPopup();
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

    private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (DataContext is MainViewModel vm && SidebarColumn != null && SidebarColumn.Width.IsAbsolute) vm.SidebarWidth = SidebarColumn.Width.Value;
    }

    private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm && SidebarColumn != null && SidebarColumn.Width.IsAbsolute)
            vm.SetSidebarWidth(SidebarColumn.Width.Value);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var key = e.Key;
        if (key == Key.D && modifiers == ModifierKeys.Control) { ToggleSidebar(); e.Handled = true; }
        else if (key == Key.F5 || (key == Key.R && modifiers == ModifierKeys.Control)) { _webViewService.ActiveWebView?.Reload(); e.Handled = true; }
        else if (key == Key.F11) { SetFullscreen(!_isFullscreen); e.Handled = true; }
        else if (key == Key.T && modifiers == ModifierKeys.Control) { ((MainViewModel)DataContext).AddNewTabCommand.Execute(null); e.Handled = true; }
        else if (key == Key.W && modifiers == ModifierKeys.Control) { if (DataContext is MainViewModel vm && vm.SelectedTab != null) { vm.CloseTabCommand.Execute(vm.SelectedTab); e.Handled = true; } }
        else if ((key == Key.L && modifiers == ModifierKeys.Control) || (key == Key.D && modifiers == ModifierKeys.Alt)) { TopBarControl.AddressBar.Focus(); TopBarControl.AddressBar.SelectAll(); e.Handled = true; }
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
        // 1. Close extension popup if click is outside
        if (_extensionPopupWindow?.IsVisible == true)
        {
            if (!(e.OriginalSource is DependencyObject d && FindAncestor<System.Windows.Controls.Button>(d) is System.Windows.Controls.Button btn && btn.Tag is string))
                CloseExtensionPopup();
        }

        // 2. Clear address bar focus if click is outside in WPF
        if (TopBarControl != null && (TopBarControl.AddressBar.IsFocused || (DataContext is MainViewModel vm && vm.IsPendingNewTab)))
        {
            var clickedElement = e.OriginalSource as DependencyObject;
            bool clickedInsideAddressBar = clickedElement != null &&
                (FindAncestor<System.Windows.Controls.TextBox>(clickedElement) == TopBarControl.AddressBar ||
                 FindAncestor<System.Windows.Controls.Primitives.Popup>(clickedElement) == TopBarControl.SuggestionsPopupControl);

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
        if (!IsActive || _fullscreenHidesUi || _isSidebarFloating) return;
        if (_isFullscreen || DataContext is MainViewModel { IsSidebarVisible: false })
            ShowFloatingSidebar();
    }
}
