using System.Collections.Specialized;
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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows.Media;

// Ambiguity Resolution Aliases
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;

namespace MundoBrowser;

public partial class MainWindow : Window
{
    private readonly WebViewService _webViewService;
    private CancellationTokenSource? _suggestionCts;
    private bool _isUpdatingAddressBar;
    private bool _isFullscreen;
    private bool _isSidebarFloating;
    private string? _currentExtensionId;
    private string? _lastClosedExtensionId;
    private DateTime _lastExtensionPopupClosed = DateTime.MinValue;
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
            handled = true;
            NativeMethods.WmGetMinMaxInfo(hwnd, lParam, _isFullscreen);
        }
        return IntPtr.Zero;
    }

    private void InitializeWindow()
    {
        NativeMethods.SetWindowCorners(this, NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
        StateChanged += (_, _) => OnWindowStateChanged();
        Closing += (_, _) => ((MainViewModel)DataContext).SaveCurrentSession();
        
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);

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
                if (!_isFullscreen)
                    UpdateSidebarWidth(vm.IsSidebarVisible);
            }
        };

        vm.Tabs.CollectionChanged += (_, e) => {
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TabViewModel tab in e.OldItems)
                {
                    tab.PropertyChanged -= OnTabPropertyChanged;
                    _webViewService.RemoveTab(tab);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (TabViewModel tab in e.NewItems)
                {
                    tab.PropertyChanged += OnTabPropertyChanged;
                }
            }
        };

        foreach (var tab in vm.Tabs)
        {
            tab.PropertyChanged += OnTabPropertyChanged;
        }

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

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.Url) && sender is TabViewModel tab)
        {
            Dispatcher.Invoke(async () => {
                var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
                if (webView != null && webView.CoreWebView2 != null)
                {
                    if (webView.CoreWebView2.Source != tab.Url)
                    {
                        webView.CoreWebView2.Navigate(tab.Url);
                    }
                }
            });
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

                CheckForExtensionStorePage(tab, tab.Url);
            }
        };

        wv.CoreWebView2.DocumentTitleChanged += (_, _) => {
            if (((MainViewModel)DataContext).SelectedTab == tab) UpdateTitle();
        };

        wv.CoreWebView2.SourceChanged += async (_, _) => {
            tab.AddressUrl = wv.CoreWebView2.Source;
            if (DataContext is MainViewModel vm && vm.SelectedTab == tab) {
                _isUpdatingAddressBar = true;
                vm.AddressBarText = tab.AddressUrl;
                _isUpdatingAddressBar = false;

                CheckForExtensionStorePage(tab, tab.AddressUrl);
            }
            await FetchHighResIconAsync(wv, tab);
        };

        wv.CoreWebView2.FaviconChanged += async (s, args) => {
            try 
            {
                using var stream = await wv.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if (stream != null && DataContext is MainViewModel vm)
                {
                    var localPath = await vm.SessionManager.SaveFaviconLocally(stream, wv.CoreWebView2.Source);
                    if (localPath != null) tab.FaviconUrl = localPath;
                }
            }
            catch { }
            
            if (string.IsNullOrEmpty(tab.FaviconUrl) || tab.FaviconUrl.StartsWith("http"))
            {
                try 
                {
                    var uri = new Uri(wv.CoreWebView2.Source);
                    tab.FaviconUrl = $"https://www.google.com/s2/favicons?sz=64&domain_url={uri.Host}";
                }
                catch { tab.FaviconUrl = wv.CoreWebView2.FaviconUri; }
            }
        };

        wv.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => 
            SetFullscreen(wv.CoreWebView2.ContainsFullScreenElement, true);

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

    private async Task FetchHighResIconAsync(WebView2 wv, TabViewModel tab)
    {
        try
        {
            string script = @"
                (function() {
                    let links = Array.from(document.querySelectorAll('link[rel*=""icon""], link[rel*=""apple-touch-icon""]'));
                    let best = null;
                    let maxSize = 0;
                    links.forEach(l => {
                        let size = 0;
                        if (l.sizes && l.sizes.value) {
                            size = parseInt(l.sizes.value.split('x')[0]);
                        } else if (l.rel.includes('apple-touch-icon')) {
                            size = 180;
                        }
                        if (size >= maxSize) {
                            maxSize = size;
                            best = l.href;
                        }
                    });
                    return best;
                })()";

            var iconUrl = await wv.CoreWebView2.ExecuteScriptAsync(script);
            iconUrl = iconUrl?.Trim('\"');

            if (!string.IsNullOrEmpty(iconUrl) && iconUrl != "null")
            {
                if (iconUrl.StartsWith("data:"))
                {
                    tab.FaviconUrl = iconUrl;
                    return;
                }

                if (DataContext is MainViewModel vm)
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, gridClick/537.36) Chrome/120.0.0.0 Safari/537.36");
                    
                    var bytes = await client.GetByteArrayAsync(iconUrl);
                    using var ms = new MemoryStream(bytes);
                    var localPath = await vm.SessionManager.SaveFaviconLocally(ms, wv.CoreWebView2.Source);
                    if (localPath != null) tab.FaviconUrl = localPath;
                }
            }
            else
            {
                var host = new Uri(wv.CoreWebView2.Source).Host;
                tab.FaviconUrl = $"https://www.google.com/s2/favicons?sz=128&domain_url={host}";
            }
        }
        catch { }
    }

    private void UpdateTitle()
    {
        if (_webViewService.ActiveWebView?.CoreWebView2 == null || DataContext is not MainViewModel vm || vm.SelectedTab == null) return;
        var title = _webViewService.ActiveWebView.CoreWebView2.DocumentTitle;
        vm.SelectedTab.Title = !string.IsNullOrWhiteSpace(title) ? title : (vm.SelectedTab.Url ?? "New Tab");
    }

    // --- BARRE D'ADRESSE ---
    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Escape)
        {
            vm.IsPendingNewTab = false;
            if (vm.SelectedTab != null)
            {
                _isUpdatingAddressBar = true;
                vm.AddressBarText = vm.SelectedTab.AddressUrl;
                _isUpdatingAddressBar = false;
            }
            if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false;
            _webViewService.ActiveWebView?.Focus();
            e.Handled = true;
            return;
        }

        // Suggestions navigation with Arrows
        if (SuggestionsPopup != null && SuggestionsPopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                if (SuggestionsListBox.SelectedIndex < SuggestionsListBox.Items.Count - 1)
                {
                    SuggestionsListBox.SelectedIndex++;
                    SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                if (SuggestionsListBox.SelectedIndex > 0)
                {
                    SuggestionsListBox.SelectedIndex--;
                    SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
                }
                e.Handled = true;
                return;
            }
        }

        if (e.Key != Key.Enter) return;

        string url;
        // If an item is selected in suggestions, use it
        if (SuggestionsPopup != null && SuggestionsPopup.IsOpen && SuggestionsListBox.SelectedItem is HistoryEntry selectedEntry)
        {
            url = selectedEntry.Url;
        }
        else
        {
            var input = AddressTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(input)) return;
            url = IsUrl(input) ? (input.Contains("://") ? input : "https://" + input) : $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";
        }

        if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false;

        if (vm.IsPendingNewTab) {
            vm.IsPendingNewTab = false;
            vm.AddTabWithUrl(url);
        } else if (vm.SelectedTab != null) {
            vm.SelectedTab.Url = vm.SelectedTab.AddressUrl = url;
            _webViewService.ActiveWebView?.CoreWebView2?.Navigate(url);
        }
        
        _webViewService.ActiveWebView?.Focus();
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
            
            if (vm.Suggestions.Count > 0)
            {
                SuggestionsListBox.SelectedIndex = -1; // Reset selection
                SuggestionsPopup.IsOpen = true;
            }
            else
            {
                SuggestionsPopup.IsOpen = false;
            }
        } catch (TaskCanceledException) { }
    }

    private void AddressTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        AddressTextBox.SelectAll();
        if (DataContext is MainViewModel vm && vm.Suggestions.Count > 0 && SuggestionsPopup != null)
        {
            SuggestionsListBox.SelectedIndex = -1;
            SuggestionsPopup.IsOpen = true;
        }
    }

    private void AddressTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressTextBox.IsFocused) { AddressTextBox.Focus(); e.Handled = true; }
    }

    private void AddressTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsPendingNewTab = false;
            if (vm.SelectedTab != null && AddressTextBox.Text != vm.SelectedTab.AddressUrl)
            {
                // Only restore URL if we are not clicking on a suggestion
                // We use a small delay to let the suggestion click process
                Dispatcher.BeginInvoke(new Action(() => {
                    if (!AddressTextBox.IsFocused && (SuggestionsPopup == null || !SuggestionsPopup.IsOpen))
                    {
                        _isUpdatingAddressBar = true;
                        vm.AddressBarText = vm.SelectedTab.AddressUrl;
                        _isUpdatingAddressBar = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        Task.Delay(200).ContinueWith(_ => Dispatcher.Invoke(() => { if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false; }));
    }

    private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Find the actual item clicked
        var item = ItemsControl.ContainerFromElement(SuggestionsListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item != null && item.DataContext is HistoryEntry entry && DataContext is MainViewModel vm)
        {
            if (vm.IsPendingNewTab)
            {
                vm.IsPendingNewTab = false;
                vm.AddTabWithUrl(entry.Url);
            }
            else if (vm.SelectedTab != null)
            {
                vm.SelectedTab.Url = vm.SelectedTab.AddressUrl = entry.Url;
                _webViewService.ActiveWebView?.CoreWebView2?.Navigate(entry.Url);
            }
            if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false;
            _webViewService.ActiveWebView?.Focus();
            e.Handled = true;
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
            WindowChrome.SetWindowChrome(this, null);
            if (hideUI) {
                if (TopBar != null) TopBar.Visibility = Visibility.Collapsed;
                if (EdgeTriggerPopup != null) EdgeTriggerPopup.Visibility = Visibility.Collapsed;
                UpdateSidebarWidth(false);
            }
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            if (this.WindowState == WindowState.Maximized) this.WindowState = WindowState.Normal;
            this.WindowState = WindowState.Maximized;
        } else {
            this.WindowState = _prevWindowState.State;
            this.WindowStyle = _prevWindowState.Style;
            this.ResizeMode = _prevWindowState.Resize;
            if (TopBar != null) TopBar.Visibility = Visibility.Visible;
            if (EdgeTriggerPopup != null) EdgeTriggerPopup.Visibility = Visibility.Visible;
            if (DataContext is MainViewModel vm) UpdateSidebarWidth(vm.IsSidebarVisible);
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });
            OnWindowStateChanged();
        }
    }

    private void UpdateSidebarWidth(bool visible)
    {
        if (SidebarColumn == null || SplitterColumn == null || DataContext is not MainViewModel vm) return;
        if (visible) {
            if (_isSidebarFloating) HideFloatingSidebar();
            SidebarColumn.Width = new GridLength(vm.SidebarWidth);
            SidebarColumn.MinWidth = 150;
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
        if (TopBar != null) { TopBar.Height = 40; WindowControlsStack.Margin = new Thickness(0); NavButtonsStack.Margin = new Thickness(0, 0, 15, 0); UrlBarBorder.Margin = new Thickness(0); ExtensionsControl.Margin = new Thickness(10, 0, 10, 0); }
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.ResizeBorderThickness = isMax ? new Thickness(0) : new Thickness(6);
        if (RightResizeBorder != null) RightResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (BottomResizeBorder != null) BottomResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (BottomRightResizeBorder != null) BottomRightResizeBorder.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) { if (e.OriginalSource != sender) return; if (e.ClickCount == 2) { Maximize_Click(sender, e); _dragStartPos = null; } else { if (WindowState == WindowState.Maximized) _dragStartPos = e.GetPosition(this); else try { DragMove(); } catch { } } }
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
        else if ((key == Key.L && modifiers == ModifierKeys.Control) || (key == Key.D && modifiers == ModifierKeys.Alt)) { AddressTextBox.Focus(); AddressTextBox.SelectAll(); e.Handled = true; }
        else if ((key == Key.Left && modifiers == ModifierKeys.Alt) || key == Key.Back) { if (key == Key.Back && e.OriginalSource is TextBox) return; if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoBack) { _webViewService.ActiveWebView.GoBack(); e.Handled = true; } }
        else if (key == Key.Right && modifiers == ModifierKeys.Alt) { if (_webViewService.ActiveWebView != null && _webViewService.ActiveWebView.CanGoForward) { _webViewService.ActiveWebView.GoForward(); e.Handled = true; } }
        else if (key == Key.Escape && ExtensionPopup.IsOpen) { CloseExtensionPopup(); e.Handled = true; }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ExtensionPopup.IsOpen) return;

        if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) is Button btn && btn.Tag is string)
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
        if (AddressTextBox.IsFocused)
        {
            MainGrid.Focus();
        }
    }

    private void Edge_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsSidebarVisible && !_isSidebarFloating && !_isFullscreen) ShowFloatingSidebar();
    }

    private void CheckForExtensionStorePage(TabViewModel tab, string url)
    {
        if (string.IsNullOrEmpty(url)) { tab.IsExtensionStorePage = false; tab.InstallableExtensionId = null; return; }
        var extensionId = ExtensionDownloader.ExtractExtensionIdFromUrl(url);
        if (DataContext is MainViewModel vm && extensionId != null && vm.InstalledExtensions.Any(e => e.Id == extensionId)) { tab.IsExtensionStorePage = false; tab.InstallableExtensionId = null; return; }
        tab.InstallableExtensionId = extensionId;
        tab.IsExtensionStorePage = !string.IsNullOrEmpty(extensionId);
    }

    private async Task LoadExtensionsAsync()
    {
        if (_webViewService.ActiveWebView?.CoreWebView2 == null || DataContext is not MainViewModel vm) return;
        var profile = _webViewService.ActiveWebView.CoreWebView2.Profile;
        var exts = await profile.GetBrowserExtensionsAsync();
        vm.InstalledExtensions.Clear();
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var extensionsPath = Path.Combine(appDataPath, "MundoBrowser", "Extensions");
        foreach (var ext in exts)
        {
            var extName = ext.Name ?? "Extension";
            if (!ext.IsEnabled || extName.Contains("Microsoft")) continue;
            var info = new Models.ExtensionInfo(ext.Id, extName, true);
            if (Directory.Exists(extensionsPath))
            {
                foreach (var dir in Directory.GetDirectories(extensionsPath))
                {
                    var manifestPath = Path.Combine(dir, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;
                    try
                    {
                        var json = File.ReadAllText(manifestPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var manifestName = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var shortName = root.TryGetProperty("short_name", out var sn) ? sn.GetString() : null;
                        var resolvedName = ResolveName(manifestName, dir, root);
                        var resolvedShortName = ResolveName(shortName, dir, root);
                        bool isMatch = false;
                        if (resolvedName != null && ext.Name != null && (ext.Name.Contains(resolvedName) || resolvedName.Contains(ext.Name))) isMatch = true;
                        else if (resolvedShortName != null && ext.Name != null && ext.Name.Contains(resolvedShortName)) isMatch = true;
                        else if (ext.Id.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)) isMatch = true;
                        if (isMatch) { ProcessManifest(root, dir, ext.Id, info); break; }
                    } catch { continue; }
                }
            }
            vm.InstalledExtensions.Add(info);
        }
    }

    private string? ResolveName(string? name, string extensionDir, System.Text.Json.JsonElement root)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!name.StartsWith("__MSG_") || !name.EndsWith("__")) return name;
        var key = name.Substring(6, name.Length - 8);
        var defaultLocale = root.TryGetProperty("default_locale", out var locale) ? (locale.GetString() ?? "en") : "en";
        var localesPath = Path.Combine(extensionDir, "_locales");
        if (!Directory.Exists(localesPath)) return name;
        string[] searchLocales = { defaultLocale, "fr", "en_US" };
        foreach (var loc in searchLocales) { var msgPath = Path.Combine(localesPath, loc, "messages.json"); if (File.Exists(msgPath)) { var val = GetMessageValue(msgPath, key); if (val != null) return val; } }
        return name;
    }

    private string? GetMessageValue(string messagesPath, string key)
    {
        try { var json = File.ReadAllText(messagesPath); using var doc = System.Text.Json.JsonDocument.Parse(json); if (doc.RootElement.TryGetProperty(key, out var msgObj) && msgObj.TryGetProperty("message", out var msg)) return msg.GetString(); }
        catch { }
        return null;
    }

    private void ProcessManifest(System.Text.Json.JsonElement root, string extensionDir, string extensionId, Models.ExtensionInfo info)
    {
        string? popupPath = null;
        if (root.TryGetProperty("action", out var action) && action.TryGetProperty("default_popup", out var dp1)) popupPath = dp1.GetString();
        else if (root.TryGetProperty("browser_action", out var bAction) && bAction.TryGetProperty("default_popup", out var dp2)) popupPath = dp2.GetString();
        if (!string.IsNullOrEmpty(popupPath)) info.PopupUrl = $"chrome-extension://{extensionId}/{popupPath.TrimStart('/')}";
        string? iconPath = null;
        if (root.TryGetProperty("icons", out var icons)) iconPath = GetBestIconPath(icons);
        if (!string.IsNullOrEmpty(iconPath))
        {
            var fullIconPath = Path.Combine(extensionDir, iconPath.TrimStart('/'));
            if (File.Exists(fullIconPath)) { try { var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = new Uri(fullIconPath); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.EndInit(); info.IconSource = bitmap; } catch { } }
        }
    }

    private string? GetBestIconPath(System.Text.Json.JsonElement icons)
    {
        if (icons.ValueKind == System.Text.Json.JsonValueKind.String) return icons.GetString();
        if (icons.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            string[] sizes = { "128", "48", "32", "16" };
            foreach (var size in sizes) if (icons.TryGetProperty(size, out var path)) return path.GetString();
            return icons.EnumerateObject().FirstOrDefault().Value.GetString();
        }
        return null;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void InstallExtensionFromBar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab?.InstallableExtensionId != null)
        {
            InstallProgressBar.Visibility = Visibility.Visible;
            InstallStatusText.Visibility = Visibility.Visible;
            InstallStatusText.Text = "Téléchargement...";
            try {
                var downloader = new ExtensionDownloader();
                var extPath = await downloader.DownloadAndExtractExtension(vm.SelectedTab.InstallableExtensionId);
                InstallStatusText.Text = "Installation...";
                
                // Get profile from active WebView to install extension
                if (_webViewService.ActiveWebView?.CoreWebView2?.Profile != null)
                {
                    await _webViewService.ActiveWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(extPath);
                    await LoadExtensionsAsync();
                    vm.SelectedTab.IsExtensionStorePage = false;
                }
            } catch (Exception ex) { MessageBox.Show("Erreur installation: " + ex.Message); }
            finally { InstallProgressBar.Visibility = Visibility.Collapsed; InstallStatusText.Visibility = Visibility.Collapsed; }
        }
    }

    private void CloseInstallBar_Click(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm && vm.SelectedTab != null) vm.SelectedTab.IsExtensionStorePage = false; }

    private async void ExtensionIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string extId && DataContext is MainViewModel vm)
        {
            // If the popup was closed very recently (by a MouseDown that triggered this click), 
            // and it was the SAME extension, don't reopen it immediately.
            if (DateTime.Now - _lastExtensionPopupClosed < TimeSpan.FromMilliseconds(200) && _lastClosedExtensionId == extId)
            {
                return;
            }

            // Toggle logic fallback
            if (ExtensionPopup.IsOpen && _currentExtensionId == extId)
            {
                CloseExtensionPopup();
                return;
            }

            var ext = vm.InstalledExtensions.FirstOrDefault(x => x.Id == extId);
            if (ext != null && !string.IsNullOrEmpty(ext.PopupUrl) && _webViewService.WebViewEnvironment != null)
            {
                _currentExtensionId = extId;
                ExtensionPopup.PlacementTarget = btn;
                ExtensionPopup.IsOpen = true;

                try
                {
                    await ExtensionPopupWebView.EnsureCoreWebView2Async(_webViewService.WebViewEnvironment);
                    
                    // Force interaction settings
                    ExtensionPopupWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                    ExtensionPopupWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

                    ExtensionPopupWebView.CoreWebView2.Navigate(ext.PopupUrl);
                    
                    // Delay to allow loading/rendering then force focus
                    await Task.Delay(200);
                    if (ExtensionPopup.IsOpen)
                    {
                        ExtensionPopupWebView.Focus();
                        System.Windows.Input.FocusManager.SetFocusedElement(ExtensionPopup, ExtensionPopupWebView);
                        ExtensionPopupWebView.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Extension popup error: {ex.Message}");
                    CloseExtensionPopup();
                }
            }
        }
    }

    private void ExtensionPopup_Opened(object sender, EventArgs e)
    {
        var child = ExtensionPopup.Child as FrameworkElement;
        if (child != null)
        {
            var source = PresentationSource.FromVisual(child) as System.Windows.Interop.HwndSource;
            if (source != null)
            {
                NativeMethods.SetWindowCorners(source.Handle, NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
            }
        }
    }

    private void ExtensionPopupWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (ExtensionPopupWebView.CoreWebView2 != null)
        {
            ExtensionPopupWebView.Focus();
        }
    }

    private void ExtensionPopup_Closed(object sender, EventArgs e)
    {
        _lastExtensionPopupClosed = DateTime.Now;
        _lastClosedExtensionId = _currentExtensionId;
        _currentExtensionId = null;
    }

    private void CloseExtensionPopup()
    {
        ExtensionPopup.IsOpen = false;
        _currentExtensionId = null;
    }

    private void CloseExtensionPopup_Click(object sender, RoutedEventArgs e)
    {
        ExtensionPopup.IsOpen = false;
        _currentExtensionId = null;
    }

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string extId)
        {
            if (_webViewService.ActiveWebView?.CoreWebView2?.Profile != null)
            {
                var profile = _webViewService.ActiveWebView.CoreWebView2.Profile;
                var exts = await profile.GetBrowserExtensionsAsync();
                var ext = exts.FirstOrDefault(x => x.Id == extId);
                if (ext != null) { await ext.RemoveAsync(); await LoadExtensionsAsync(); }
            }
        }
    }
}

