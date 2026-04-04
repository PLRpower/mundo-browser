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
    private readonly TabReorderHelper _tabReorderHelper;
    private CancellationTokenSource? _suggestionCts;
    private bool _isUpdatingAddressBar;
    private bool _isFullscreen;
    private (WindowState State, WindowStyle Style, ResizeMode Resize) _prevWindowState;

    public MainWindow()
    {
        InitializeComponent();
        
        var vm = (MainViewModel)DataContext;
        _webViewService = new WebViewService(WebViewsContainer);
        _tabReorderHelper = new TabReorderHelper(TabsListBox, vm);

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
            NativeMethods.WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void InitializeWindow()
    {
        NativeMethods.SetWindowCorners(this, NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
        StateChanged += (_, _) => OnWindowStateChanged();
        Closing += (_, _) => ((MainViewModel)DataContext).SaveCurrentSession();
        
        Loaded += async (_, _) => {
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
                UpdateSidebarWidth(vm.IsSidebarVisible);
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

        _isUpdatingAddressBar = true;
        AddressTextBox.Text = tab.AddressUrl;
        _isUpdatingAddressBar = false;
    }

    private void SetupWebViewEvents(WebView2 wv, TabViewModel tab)
    {
        wv.CoreWebView2.NavigationCompleted += (_, args) => {
            if (args.IsSuccess && ((MainViewModel)DataContext).SelectedTab == tab) {
                tab.Url = tab.AddressUrl = wv.CoreWebView2.Source;
                UpdateTitle();
                ((MainViewModel)DataContext).HistoryManager.AddEntry(tab.Url, wv.CoreWebView2.DocumentTitle);
            }
        };

        wv.CoreWebView2.SourceChanged += (_, _) => {
            tab.AddressUrl = wv.CoreWebView2.Source;
            if (((MainViewModel)DataContext).SelectedTab == tab) {
                _isUpdatingAddressBar = true;
                AddressTextBox.Text = tab.AddressUrl;
                _isUpdatingAddressBar = false;
            }
        };

        wv.CoreWebView2.DocumentTitleChanged += (_, _) => {
            if (((MainViewModel)DataContext).SelectedTab == tab) UpdateTitle();
        };

        wv.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => 
            SetFullscreen(wv.CoreWebView2.ContainsFullScreenElement);
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
        
        if (DataContext is MainViewModel vm && vm.SelectedTab != null) {
            vm.SelectedTab.Url = vm.SelectedTab.AddressUrl = url;
            _webViewService.ActiveWebView?.CoreWebView2?.Navigate(url);
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
    
    private void SetFullscreen(bool enable)
    {
        if (enable == _isFullscreen) return;
        _isFullscreen = enable;

        if (enable) {
            _prevWindowState = (WindowState, WindowStyle, ResizeMode);
            TopBar.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        } else {
            TopBar.Visibility = Visibility.Visible;
            WindowStyle = _prevWindowState.Style;
            ResizeMode = _prevWindowState.Resize;
            WindowState = _prevWindowState.State;
        }
    }

    private void UpdateSidebarWidth(bool visible)
    {
        if (SidebarColumn == null || SplitterColumn == null) return;

        if (visible)
        {
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
            var topPad = 0; // No padding needed with correct work area
            
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
            }
            else
            {
                if (WindowState == WindowState.Maximized)
                {
                    // Allow dragging to restore a maximized window
                    var mousePosOnScreen = PointToScreen(e.GetPosition(this));
                    double xRatio = e.GetPosition(this).X / ActualWidth;

                    WindowState = WindowState.Normal;

                    // Reposition based on new size to keep cursor at the same horizontal ratio
                    Left = mousePosOnScreen.X - (ActualWidth * xRatio);
                    Top = mousePosOnScreen.Y - 15;
                }
                
                try { DragMove(); } catch { }
            }
        }
    }

    private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // Logic for sidebar resizing if needed, but GridSplitter handles it mostly
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ToggleSidebarCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Custom window move logic if needed
    }

    private void Edge_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsSidebarVisible)
            vm.IsSidebarVisible = true;
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

    // --- DRAG & DROP TABS ---
    private void TabItem_PreviewMouseLeftButtonDown(object s, MouseButtonEventArgs e) => _tabReorderHelper.HandlePreviewMouseDown(e);
    private void TabItem_PreviewMouseMove(object s, System.Windows.Input.MouseEventArgs e) => _tabReorderHelper.HandlePreviewMouseMove(s, e);
    private void TabsList_DragOver(object s, System.Windows.DragEventArgs e) => _tabReorderHelper.HandleDragOver(e);
    private void TabsList_Drop(object s, System.Windows.DragEventArgs e) => _tabReorderHelper.HandleDrop(e);
    private void TabsList_DragLeave(object s, System.Windows.DragEventArgs e) => _tabReorderHelper.ClearIndicators();

    // --- WINDOW CONTROLS ---
    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
