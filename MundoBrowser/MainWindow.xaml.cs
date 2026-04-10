using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.Models;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow : Window
{
    private readonly WebViewService _webViewService;
    private CancellationTokenSource? _suggestionCts;
    private bool _isUpdatingAddressBar;
    private bool _isFullscreen;
    private bool _isSidebarFloating;
    private System.Windows.Point? _dragStartPos;
    private (WindowState State, WindowStyle Style, ResizeMode Resize) _prevWindowState;

    public MainWindow()
    {
        InitializeComponent();
        
        var vm = (MainViewModel)DataContext;
        _webViewService = new WebViewService(WebViewsContainer);

        InitializeWindow();
        InitializeEvents(vm);

        // Hook for taskbar respect
        SourceInitialized += (s, e) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024) // WM_GETMINMAXINFO
        {
            NativeMethods.WmGetMinMaxInfo(hwnd, lParam, _isFullscreen);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void InitializeWindow()
    {
        NativeMethods.SetWindowCorners(this, NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
        StateChanged += (_, _) => OnWindowStateChanged();
        Closing += (_, _) => ((MainViewModel)DataContext).SaveCurrentSession();
        
        // Fix WPF bug: ElementName bindings on Popups often get lost after IsOpen toggles
        FloatingSidebarPopup.PlacementTarget = MainGrid;
        EdgeTriggerPopup.PlacementTarget = MainGrid;
        if (FindName("QuickUrlPopup") is System.Windows.Controls.Primitives.Popup quickPopup)
            quickPopup.PlacementTarget = MainGrid;

        ContentRendered += async (_, _) => {
            await _webViewService.InitializeAsync();
            if (DataContext is MainViewModel vm && vm.SelectedTab != null)
            {
                await SwitchToTabAsync(vm.SelectedTab);
                UpdateSidebarWidth(vm.IsSidebarVisible);
            }
            await LoadExtensionsAsync();
        };
    }

    private void InitializeEvents(MainViewModel vm)
    {
        vm.PropertyChanged += async (s, e) => {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab) && vm.SelectedTab != null)
                await SwitchToTabAsync(vm.SelectedTab);
            else if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible))
            {
                // In fullscreen, we force sidebar hidden regardless of property
                if (!_isFullscreen)
                    UpdateSidebarWidth(vm.IsSidebarVisible);
            }
        };

        vm.Tabs.CollectionChanged += (_, e) => {
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
                foreach (TabViewModel tab in e.OldItems) _webViewService.RemoveTab(tab);
        };

        vm.LoadExtensionRequested += async (_, _) => await OnLoadExtensionRequested();
        vm.NewTabRequested += (_, _) => { AddressTextBox.Focus(); AddressTextBox.SelectAll(); };
    }

    private async Task SwitchToTabAsync(TabViewModel tab)
    {
        if (tab == null) return;

        var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
        _webViewService.SwitchToTab(tab, webView);

        if (DataContext is MainViewModel vm)
        {
            _isUpdatingAddressBar = true;
            vm.AddressBarText = tab.AddressUrl;
            _isUpdatingAddressBar = false;
        }
    }

    private void SetupWebViewEvents(WebView2 wv, TabViewModel tab)
    {
        wv.CoreWebView2.NavigationCompleted += (_, args) => {
            if (args.IsSuccess && DataContext is MainViewModel vm && vm.SelectedTab == tab) {
                tab.Url = tab.AddressUrl = wv.CoreWebView2.Source;
                UpdateTitle();
                vm.HistoryManager.AddEntry(tab.Url, wv.CoreWebView2.DocumentTitle);
                
                _isUpdatingAddressBar = true;
                vm.AddressBarText = tab.AddressUrl;
                _isUpdatingAddressBar = false;
            }
        };

        wv.CoreWebView2.SourceChanged += (_, _) => {
            tab.AddressUrl = wv.CoreWebView2.Source;
            if (DataContext is MainViewModel vm && vm.SelectedTab == tab) {
                _isUpdatingAddressBar = true;
                vm.AddressBarText = tab.AddressUrl;
                _isUpdatingAddressBar = false;
            }
        };

        wv.CoreWebView2.DocumentTitleChanged += (_, _) => {
            if (((MainViewModel)DataContext).SelectedTab == tab) UpdateTitle();
        };

        wv.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => 
            SetFullscreen(wv.CoreWebView2.ContainsFullScreenElement, true);

        wv.CoreWebView2.FaviconChanged += (s, args) => {
            tab.FaviconUrl = wv.CoreWebView2.FaviconUri;
        };

        wv.CoreWebView2.NewWindowRequested += (s, args) => {
            args.Handled = true;
            if (DataContext is MainViewModel vm)
            {
                vm.AddTabWithUrl(args.Uri);
            }
        };

        wv.CoreWebView2.WindowCloseRequested += (s, args) => {
            if (DataContext is MainViewModel vm)
            {
                vm.CloseTab(tab);
            }
        };
    }

    private void UpdateTitle()
    {
        if (_webViewService.ActiveWebView?.CoreWebView2 == null || DataContext is not MainViewModel vm || vm.SelectedTab == null) return;
        var title = _webViewService.ActiveWebView.CoreWebView2.DocumentTitle;
        vm.SelectedTab.Title = !string.IsNullOrWhiteSpace(title) ? title : (vm.SelectedTab.Url ?? "New Tab");
    }

    // --- BARRE D'ADRESSE ---
    private void AddressBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        
        var input = AddressTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        string url = IsUrl(input) ? (input.Contains("://") ? input : "https://" + input) : $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";
        
        if (DataContext is MainViewModel vm) {
            if (vm.IsPendingNewTab) {
                vm.IsPendingNewTab = false;
                vm.AddTabWithUrl(url);
            } else if (vm.SelectedTab != null) {
                vm.SelectedTab.Url = vm.SelectedTab.AddressUrl = url;
                _webViewService.ActiveWebView?.CoreWebView2?.Navigate(url);
            }
        }
    }

    private bool IsUrl(string t) => !t.Contains(" ") && (t.Contains(".") || t.Contains("://") || t == "localhost");

    private async void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingAddressBar || DataContext is not MainViewModel vm) return;
        if (AddressTextBox == null || SuggestionsPopup == null) return;
        
        _suggestionCts?.Cancel();
        _suggestionCts = new CancellationTokenSource();
        
        var query = AddressTextBox.Text;
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2 || !AddressTextBox.IsFocused) {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        try {
            await Task.Delay(150, _suggestionCts.Token);
            if (SuggestionsPopup == null || vm.HistoryManager == null) return;

            var results = vm.HistoryManager.SearchHistory(query, 5);
            vm.Suggestions.Clear();
            foreach (var r in results) vm.Suggestions.Add(r);
            SuggestionsPopup.IsOpen = vm.Suggestions.Count > 0;
        } catch (TaskCanceledException) { }
    }

    private void AddressTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Suggestions.Count > 0 && SuggestionsPopup != null)
            SuggestionsPopup.IsOpen = true;
    }

    private void AddressTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Delay closing to allow clicking on suggestions
        Task.Delay(200).ContinueWith(_ => Dispatcher.Invoke(() => SuggestionsPopup.IsOpen = false));
    }

    private void SelectAllUrl_Click(object sender, RoutedEventArgs e)
    {
        AddressTextBox.Focus();
        AddressTextBox.SelectAll();
    }

    private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox lb && lb.SelectedItem is HistoryEntry entry && DataContext is MainViewModel vm)
        {
            if (vm.SelectedTab != null)
            {
                vm.SelectedTab.Url = vm.SelectedTab.AddressUrl = entry.Url;
                _webViewService.ActiveWebView?.CoreWebView2?.Navigate(entry.Url);
            }
            SuggestionsPopup.IsOpen = false;
        }
    }

    // --- UI ACTIONS ---
    private void Back_Click(object sender, RoutedEventArgs e) => _webViewService.ActiveWebView?.GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => _webViewService.ActiveWebView?.GoForward();
    private void Reload_Click(object sender, RoutedEventArgs e) => _webViewService.ActiveWebView?.Reload();
    
    private void SetFullscreen(bool enable, bool hideUI = false)
    {
        if (enable == _isFullscreen) return;
        _isFullscreen = enable;

        if (enable) {
            _prevWindowState = (WindowState, WindowStyle, ResizeMode);

            // True Fullscreen: Remove WindowChrome to prevent taskbar layer interference
            WindowChrome.SetWindowChrome(this, null);

            if (hideUI) {
                if (TopBar != null) TopBar.Visibility = Visibility.Collapsed;
                if (EdgeTriggerPopup != null) EdgeTriggerPopup.Visibility = Visibility.Collapsed;
                UpdateSidebarWidth(false); // Hide sidebar
            }

            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            
            // Trigger a state refresh
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            
            this.WindowState = WindowState.Maximized;
        } else {
            // Restore Window State
            this.WindowState = _prevWindowState.State;
            this.WindowStyle = _prevWindowState.Style;
            this.ResizeMode = _prevWindowState.Resize;

            if (TopBar != null) TopBar.Visibility = Visibility.Visible;
            if (EdgeTriggerPopup != null) EdgeTriggerPopup.Visibility = Visibility.Visible;
            if (DataContext is MainViewModel vm)
                UpdateSidebarWidth(vm.IsSidebarVisible);

            // Re-apply WindowChrome for borderless custom window mechanics
            WindowChrome.SetWindowChrome(this, new WindowChrome { 
                CaptionHeight = 0, 
                ResizeBorderThickness = new Thickness(6), 
                GlassFrameThickness = new Thickness(0), 
                CornerRadius = new CornerRadius(0) 
            });
            
            // Trigger manual margin updates based on current state
            OnWindowStateChanged();
        }
    }

    private void UpdateSidebarWidth(bool visible)
    {
        if (SidebarColumn == null || SplitterColumn == null) return;

        if (visible)
        {
            if (_isSidebarFloating) HideFloatingSidebar();
            SidebarColumn.Width = new GridLength(250);
            SidebarColumn.MinWidth = 150;
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
    }

    private void ShowFloatingSidebar()
    {
        if (FloatingSidebarPopup == null || _isSidebarFloating) return;
        _isSidebarFloating = true;
        
        FloatingSidebarPopup.IsOpen = true;

        var slideIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = -250,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        FloatingSidebarContent.RenderTransform = new System.Windows.Media.TranslateTransform(-250, 0);
        FloatingSidebarContent.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);
    }

    private void HideFloatingSidebar()
    {
        if (FloatingSidebarPopup == null || !_isSidebarFloating) return;
        _isSidebarFloating = false;

        var slideOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = -250,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };
        
        slideOut.Completed += (s, e) => 
        {
            if (!_isSidebarFloating)
                FloatingSidebarPopup.IsOpen = false;
        };

        if (FloatingSidebarContent.RenderTransform is not System.Windows.Media.TranslateTransform)
            FloatingSidebarContent.RenderTransform = new System.Windows.Media.TranslateTransform(0, 0);

        FloatingSidebarContent.RenderTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOut);
    }

    private void FloatingSidebarContent_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isSidebarFloating)
        {
            HideFloatingSidebar();
        }
    }

    private void OnWindowStateChanged()
    {
        // When maximized, WindowChrome automatically adds margins. 
        // With our WM_GETMINMAXINFO hook, we just need to handle the hit-test areas.
        bool isMax = WindowState == WindowState.Maximized;
        MainGrid.Margin = isMax ? new Thickness(0) : new Thickness(0); // Handled by WM_GETMINMAXINFO

        // Adjust TitleBar elements when maximized.
        if (TopBar != null)
        {
            TopBar.Height = isMax ? 40 : 40; // Use same height, WM_GETMINMAXINFO handles the work area
            
            WindowControlsStack.Margin = new Thickness(0);
            
            MinimizeBtn.Padding = new Thickness(0);
            MaximizeBtn.Padding = new Thickness(0);
            CloseBtn.Padding = new Thickness(0);

            NavButtonsStack.Margin = new Thickness(0, 0, 15, 0);
            UrlBarBorder.Margin = new Thickness(0);
            ExtensionsControl.Margin = new Thickness(10, 0, 10, 0);
        }

        // Adjust WindowChrome properties based on state
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null)
        {
            chrome.ResizeBorderThickness = isMax ? new Thickness(0) : new Thickness(6);
        }

        // Hide manual resize borders when maximized
        if (RightResizeBorder != null) RightResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (BottomResizeBorder != null) BottomResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (BottomRightResizeBorder != null) BottomRightResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
                _dragStartPos = null;
            }
            else
            {
                if (WindowState == WindowState.Maximized)
                {
                    _dragStartPos = e.GetPosition(this);
                }
                else
                {
                    try { DragMove(); } catch { }
                }
            }
        }
    }

    private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // Logic for sidebar resizing if needed, but GridSplitter handles it mostly
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var key = e.Key;

        // Ctrl + D: Toggle Sidebar
        if (key == Key.D && modifiers == ModifierKeys.Control)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ToggleSidebarCommand.Execute(null);
                e.Handled = true;
            }
        }
        // F5 or Ctrl + R: Refresh
        else if (key == Key.F5 || (key == Key.R && modifiers == ModifierKeys.Control))
        {
            _webViewService.ActiveWebView?.Reload();
            e.Handled = true;
        }
        // F11: Fullscreen
        else if (key == Key.F11)
        {
            SetFullscreen(!_isFullscreen);
            e.Handled = true;
        }
        // Ctrl + T: New Tab
        else if (key == Key.T && modifiers == ModifierKeys.Control)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.AddNewTabCommand.Execute(null);
                e.Handled = true;
            }
        }
        // Ctrl + W: Close Tab
        else if (key == Key.W && modifiers == ModifierKeys.Control)
        {
            if (DataContext is MainViewModel vm && vm.SelectedTab != null)
            {
                vm.CloseTabCommand.Execute(vm.SelectedTab);
                e.Handled = true;
            }
        }
        // Ctrl + L or Alt + D: Focus Address Bar
        else if ((key == Key.L && modifiers == ModifierKeys.Control) || (key == Key.D && modifiers == ModifierKeys.Alt))
        {
            AddressTextBox.Focus();
            AddressTextBox.SelectAll();
            e.Handled = true;
        }
        // Alt + Left or Back: Go Back
        else if ((key == Key.Left && modifiers == ModifierKeys.Alt) || key == Key.Back)
        {
            // Only go back if not typing in a text field (Back key)
            if (key == Key.Back && e.OriginalSource is System.Windows.Controls.TextBox) return;
            
            if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoBack)
            {
                _webViewService.ActiveWebView.GoBack();
                e.Handled = true;
            }
        }
        // Alt + Right: Go Forward
        else if (key == Key.Right && modifiers == ModifierKeys.Alt)
        {
            if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoForward)
            {
                _webViewService.ActiveWebView.GoForward();
                e.Handled = true;
            }
        }
        // Ctrl + Tab: Next Tab
        else if (key == Key.Tab && modifiers == ModifierKeys.Control)
        {
            if (DataContext is MainViewModel vm && vm.SelectedTab != null && vm.Tabs.Count > 1)
            {
                int nextIndex = (vm.Tabs.IndexOf(vm.SelectedTab) + 1) % vm.Tabs.Count;
                vm.SelectedTab = vm.Tabs[nextIndex];
                e.Handled = true;
            }
        }
        // Ctrl + Shift + Tab: Previous Tab
        else if (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (DataContext is MainViewModel vm && vm.SelectedTab != null && vm.Tabs.Count > 1)
            {
                int prevIndex = (vm.Tabs.IndexOf(vm.SelectedTab) - 1 + vm.Tabs.Count) % vm.Tabs.Count;
                vm.SelectedTab = vm.Tabs[prevIndex];
                e.Handled = true;
            }
        }
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Maximized && _dragStartPos.HasValue)
        {
            System.Windows.Point currentPos = e.GetPosition(this);
            if (Math.Abs(currentPos.X - _dragStartPos.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPos.Y - _dragStartPos.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
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
        else if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStartPos = null;
        }
    }

    private void Edge_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsSidebarVisible && !_isSidebarFloating && !_isFullscreen)
        {
            ShowFloatingSidebar();
        }
    }

    // --- EXTENSIONS ---
    private async Task LoadExtensionsAsync()
    {
        if (_webViewService.ActiveWebView?.CoreWebView2 == null || DataContext is not MainViewModel vm) return;
        var exts = await _webViewService.ActiveWebView.CoreWebView2.Profile.GetBrowserExtensionsAsync();
        vm.InstalledExtensions.Clear();
        foreach (var ext in exts) 
            if (ext.IsEnabled && !ext.Name.Contains("Microsoft"))
                vm.InstalledExtensions.Add(new Models.ExtensionInfo(ext.Id, ext.Name, true));
    }

    private async Task OnLoadExtensionRequested()
    {
        var dialog = new AddExtensionWindow { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.ExtensionPath) && _webViewService.ActiveWebView != null) {
            await _webViewService.ActiveWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(dialog.ExtensionPath);
            await LoadExtensionsAsync();
        }
    }

    private void ExtensionIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string extensionId)
        {
            // Show extension popup logic
        }
    }

    // --- WINDOW CONTROLS ---
    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
