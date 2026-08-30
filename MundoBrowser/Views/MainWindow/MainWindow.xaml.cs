using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IWebViewService _webViewService;
    private readonly IExtensionService _extensionService;
    private readonly IAppSettingsService _settingsService;
    private readonly IUpdateService _updateService;
    private readonly HashSet<TabViewModel> _trackedTabs = [];
    private MainViewModel? _viewModel;
    private int _tabSwitchVersion;
    private bool _contentInitialized;
    private bool _isFullscreen;
    private bool _isApplyingFullscreenBounds;
    private bool _isRestoringFromFullscreen;
    private bool _fullscreenHidesUi;
    private bool _resizeOverlaysOpen;
    private bool _isSidebarFloating;
    private bool _isTopBarFloating;
    private bool _isClosingSafe;
    private bool _isSavingSession;
    private WindowState _windowStateBeforeTray = WindowState.Normal;
    private string? _currentExtensionId;
    private ExtensionPopupWindow? _extensionPopupWindow;
    private (WindowState State, WindowStyle Style, ResizeMode Resize, Wpf.Ui.Controls.WindowBackdropType Backdrop, Wpf.Ui.Controls.WindowCornerPreference Corners, bool Topmost, double Left, double Top, double Width, double Height) _prevWindowState;

    private readonly System.Windows.Threading.DispatcherTimer _globalMediaTimer;
    private int _mediaUpdateRunning;
    private DateTime _lastBackgroundMediaUpdate = DateTime.MinValue;
    private string[]? _startArgs;
    internal static readonly System.Windows.Media.SolidColorBrush _floatingTitleBarButtonBrush = CreateFrozenBrush(0xF2, 0x1E, 0x1E, 0x20);
    internal static readonly System.Windows.Media.SolidColorBrush _floatingTitleBarButtonHoverBrush = CreateFrozenBrush(0xFF, 0x3D, 0x3D, 0x3D);
    internal static readonly System.Windows.Media.SolidColorBrush _floatingTitleBarButtonPressedBrush = CreateFrozenBrush(0xFF, 0x50, 0x50, 0x50);
    internal static readonly System.Windows.Media.SolidColorBrush _floatingTitleBarCloseHoverBrush = CreateFrozenBrush(0xFF, 0xC4, 0x2B, 0x1C);
    internal static readonly System.Windows.Media.SolidColorBrush _floatingTitleBarClosePressedBrush = CreateFrozenBrush(0xFF, 0xD3, 0x2F, 0x1F);

    private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public bool IsFullscreen => _isFullscreen;

    public void SetStartupArgs(string[]? args) => _startArgs = args;

    public MainWindow(
        MainViewModel viewModel,
        IWebViewService webViewService,
        IExtensionService extensionService,
        IAppSettingsService settingsService,
        IUpdateService updateService,
        string[]? args = null)
    {
        _webViewService = webViewService;
        _extensionService = extensionService;
        _settingsService = settingsService;
        _updateService = updateService;
        _startArgs = args;
        InitializeComponent();
        DataContext = viewModel;

        Title = AppRuntime.DisplayName;

        InitializeTrayIcon();
        InitializeWindow();
        InitializeEvents(viewModel);

        // Single global timer for media updates to save resources (started on-demand)
        _globalMediaTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _globalMediaTimer.Tick += UpdateActiveMediaInfo;

        // Hook for taskbar respect
        SourceInitialized += (s, e) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowAppId(handle, NativeMethods.AppUserModelId);
            UpdateWindowFrameVisuals();
            
            System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
    }

    private void InitializeWindow()
    {
        StateChanged += (_, _) => {
            OnWindowStateChanged(forceResizeOverlayReopen: true);
            UpdateEdgeTriggerState(forceReopen: true);
            UpdateTopEdgeTriggerState(forceReopen: true);
        };
        LocationChanged += (_, _) => {
            KeepFullscreenBounds();
            RepositionEdgeTriggerPopup();
            RepositionTopEdgeTriggerPopup();
        };
        SizeChanged += (_, _) => {
            KeepFullscreenBounds();
            UpdateResizeOverlayState();
            UpdateEdgeTriggerState(forceReopen: true);
            UpdateTopEdgeTriggerState(forceReopen: true);
        };
        Activated += (_, _) => {
            UpdateEdgeTriggerState(forceReopen: true);
            UpdateTopEdgeTriggerState(forceReopen: true);
        };
        Deactivated += (_, _) => {
            if (!NativeMethods.IsCurrentProcessForeground())
            {
                HideFloatingSidebar(animate: false);
                HideFloatingTopBar(animate: false);
            }
        };
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);

        // Fix WPF bug: ElementName bindings on Popups often get lost after IsOpen toggles
        FloatingSidebarPopup.PlacementTarget = MainGrid;
        EdgeTriggerPopup.PlacementTarget = MainGrid;
        FloatingTopBarPopup.PlacementTarget = MainGrid;
        TopEdgeTriggerPopup.PlacementTarget = MainGrid;
        InitializeResizeOverlays();
        if (FindName("QuickUrlPopup") is System.Windows.Controls.Primitives.Popup quickPopup)
            quickPopup.PlacementTarget = MainGrid;

        ContentRendered += async (_, _) => {
            if (_contentInitialized)
                return;
            _contentInitialized = true;

            try
            {
                WindowTitleBar.PreviewMouseLeftButtonDown += TitleBar_PreviewMouseLeftButtonDown;
                WindowTitleBar.PreviewMouseMove += BlockFullscreenTitleBarDrag;
                WindowTitleBar.PreviewMouseRightButtonUp += TitleBar_PreviewMouseRightButtonUp;
                WindowTitleBar.MouseRightButtonUp += TitleBar_MouseRightButtonUp;
                DetachUnsafeTitleBarRightClick(WindowTitleBar);
                WindowTitleBar.Loaded += (s, e) =>
                {
                    DetachUnsafeTitleBarRightClick(WindowTitleBar);
                    foreach (var btn in FindVisualChildren<Wpf.Ui.Controls.TitleBarButton>(WindowTitleBar))
                    {
                        if (btn.ButtonType == Wpf.Ui.Controls.TitleBarButtonType.Help)
                            btn.Visibility = Visibility.Collapsed;
                    }
                };
                if (FloatingTitleBar != null)
                {
                    FloatingTitleBar.Loaded += (s, e) =>
                    {
                        UpdateFloatingTitleBarButtonsBackground();
                    };
                    AttachFloatingTitleBarToWindow();
                }
                UpdateResizeOverlayState(forceReopen: true);
                UpdateEdgeTriggerState(forceReopen: true);
                UpdateTopEdgeTriggerState(forceReopen: true);

                await _webViewService.InitializeAsync(WebViewsContainer);

                if (DataContext is MainViewModel vm)
                {
                    UpdateSidebarWidth(vm.IsSidebarVisible, animate: false);
                    UpdateTopBarHeight(vm.IsTopBarVisible, animate: false);

                    // Process startup arguments (URLs or files)
                    if (_startArgs != null && _startArgs.Length > 0)
                    {
                        string input = _startArgs[0];
                        // Handle local paths for PDF or HTML files
                        if (System.IO.File.Exists(input))
                        {
                            input = new Uri(System.IO.Path.GetFullPath(input)).AbsoluteUri;
                        }

                        vm.AddTabWithUrl(input);
                    }
                    else if (vm.SelectedTab != null)
                    {
                        await SwitchToTabAsync(vm.SelectedTab);
                    }
                    
                    await LoadExtensionsAsync();
                    _updateService.CheckForUpdatesInBackground(_startArgs);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize main window: {ex}");
            }
        };
    }

    private void RepositionEdgeTriggerPopup()
    {
        if (EdgeTriggerPopup != null && EdgeTriggerPopup.IsOpen)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (EdgeTriggerPopup != null && EdgeTriggerPopup.IsOpen)
                {
                    var offset = EdgeTriggerPopup.HorizontalOffset;
                    EdgeTriggerPopup.HorizontalOffset = offset + 0.1;
                    EdgeTriggerPopup.HorizontalOffset = offset;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void RepositionTopEdgeTriggerPopup()
    {
        if (TopEdgeTriggerPopup != null && TopEdgeTriggerPopup.IsOpen)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TopEdgeTriggerPopup != null && TopEdgeTriggerPopup.IsOpen)
                {
                    var offset = TopEdgeTriggerPopup.HorizontalOffset;
                    TopEdgeTriggerPopup.HorizontalOffset = offset + 0.1;
                    TopEdgeTriggerPopup.HorizontalOffset = offset;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    public Microsoft.Web.WebView2.Wpf.WebView2? GetActiveWebView() => _webViewService.ActiveWebView;

    public void HandleExternalArguments(string[] args)
    {
        if (args.Length > 0 && DataContext is MainViewModel vm)
        {
            string input = args[0];
            if (System.IO.File.Exists(input))
            {
                input = new Uri(System.IO.Path.GetFullPath(input)).AbsoluteUri;
            }
            
            vm.AddTabWithUrl(input);
            
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            
            ForceForeground(handle);
            
            Activate();
            Focus();
        }
    }

    private void ForceForeground(IntPtr hWnd)
    {
        if (WindowState == WindowState.Minimized)
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);

        IntPtr foregroundWnd = NativeMethods.GetForegroundWindow();
        uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundWnd, out _);
        uint currentThreadId = NativeMethods.GetCurrentThreadId();

        if (foregroundThreadId != currentThreadId)
        {
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
        else
        {
            NativeMethods.SetForegroundWindow(hWnd);
        }
    }

    private void AttachFloatingTitleBarToWindow()
    {
        if (FloatingTitleBar == null) return;

        DetachUnsafeTitleBarRightClick(FloatingTitleBar);

        try
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var fieldParent = typeof(Wpf.Ui.Controls.TitleBar).GetField("_parentWindow", flags);
            var fieldCurrent = typeof(Wpf.Ui.Controls.TitleBar).GetField("_currentWindow", flags);
            var methodContentRendered = typeof(Wpf.Ui.Controls.TitleBar).GetMethod("OnWindowContentRendered", flags);
            var methodHwndHook = typeof(Wpf.Ui.Controls.TitleBar).GetMethod("HwndSourceHook", flags);

            fieldParent?.SetValue(FloatingTitleBar, this);
            fieldCurrent?.SetValue(FloatingTitleBar, this);

            methodContentRendered?.Invoke(FloatingTitleBar, [this, EventArgs.Empty]);

            // Re-detach right click in case OnWindowContentRendered or Loaded re-attached it
            DetachUnsafeTitleBarRightClick(FloatingTitleBar);

            // Handle hover and pressed visual state for TitleBarButtons when in Popup (using solid opaque brushes)
            FloatingTitleBar.PreviewMouseMove += (s, e) =>
            {
                var targetBtn = e.OriginalSource is DependencyObject d ? FindAncestor<Wpf.Ui.Controls.TitleBarButton>(d) : null;
                
                foreach (var btn in FindVisualChildren<Wpf.Ui.Controls.TitleBarButton>(FloatingTitleBar))
                {
                    if (ReferenceEquals(btn, targetBtn))
                    {
                        if (e.LeftButton == MouseButtonState.Pressed)
                        {
                            btn.Background = btn.ButtonType == Wpf.Ui.Controls.TitleBarButtonType.Close
                                ? _floatingTitleBarClosePressedBrush
                                : _floatingTitleBarButtonPressedBrush;
                        }
                        else
                        {
                            btn.Background = btn.ButtonType == Wpf.Ui.Controls.TitleBarButtonType.Close
                                ? _floatingTitleBarCloseHoverBrush
                                : _floatingTitleBarButtonHoverBrush;
                        }
                        btn.Foreground = System.Windows.Media.Brushes.White;
                    }
                    else
                    {
                        btn.Background = _floatingTitleBarButtonBrush;
                        btn.Foreground = System.Windows.Media.Brushes.White;
                    }
                }
            };

            FloatingTitleBar.PreviewMouseDown += (s, e) =>
            {
                var targetBtn = e.OriginalSource is DependencyObject d ? FindAncestor<Wpf.Ui.Controls.TitleBarButton>(d) : null;
                if (targetBtn != null)
                {
                    targetBtn.Background = targetBtn.ButtonType == Wpf.Ui.Controls.TitleBarButtonType.Close
                        ? _floatingTitleBarClosePressedBrush
                        : _floatingTitleBarButtonPressedBrush;
                    targetBtn.Foreground = System.Windows.Media.Brushes.White;
                }
            };

            FloatingTitleBar.PreviewMouseUp += (s, e) =>
            {
                var targetBtn = e.OriginalSource is DependencyObject d ? FindAncestor<Wpf.Ui.Controls.TitleBarButton>(d) : null;
                if (targetBtn != null)
                {
                    targetBtn.Background = targetBtn.ButtonType == Wpf.Ui.Controls.TitleBarButtonType.Close
                        ? _floatingTitleBarCloseHoverBrush
                        : _floatingTitleBarButtonHoverBrush;
                    targetBtn.Foreground = System.Windows.Media.Brushes.White;
                }
            };

            FloatingTitleBar.MouseLeave += (s, e) =>
            {
                foreach (var btn in FindVisualChildren<Wpf.Ui.Controls.TitleBarButton>(FloatingTitleBar))
                {
                    btn.Background = _floatingTitleBarButtonBrush;
                    btn.Foreground = System.Windows.Media.Brushes.White;
                }
            };

            EventHandler popupOpened = (s, e) =>
            {
                if (PresentationSource.FromVisual(FloatingTopBarContent) is System.Windows.Interop.HwndSource popupHwndSource)
                {
                    NativeMethods.RemoveNoActivate(popupHwndSource.Handle);
                }
                UpdateFloatingTitleBarButtonsBackground();
                SetFloatingCaptionButtonsVisible(_currentFloatingZone == FloatingTopBarZone.Right || _currentFloatingZone == FloatingTopBarZone.All, animate: false);
            };

            FloatingTopBarPopup.Opened += popupOpened;
            if (FloatingTopBarPopup.IsOpen)
                popupOpened(FloatingTopBarPopup, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to attach floating titlebar to window: {ex}");
        }
    }

    internal void UpdateFloatingTitleBarButtonsBackground()
    {
        if (FloatingTitleBar == null) return;

        foreach (var btn in FindVisualChildren<Wpf.Ui.Controls.TitleBarButton>(FloatingTitleBar))
        {
            if (btn.ButtonType == Wpf.Ui.Controls.TitleBarButtonType.Help)
            {
                btn.Visibility = Visibility.Collapsed;
            }

            var parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && parent != FloatingTitleBar)
            {
                if (parent is FrameworkElement fe && fe.Name != "PART_MainGrid")
                {
                    if (parent is System.Windows.Controls.Panel panel)
                    {
                        panel.Background = _floatingTitleBarButtonBrush;
                        break;
                    }
                    if (parent is System.Windows.Controls.Border border)
                    {
                        border.Background = _floatingTitleBarButtonBrush;
                        break;
                    }
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
        }
    }

    internal static void DetachUnsafeTitleBarRightClick(Wpf.Ui.Controls.TitleBar? titleBar)
    {
        if (titleBar == null) return;

        try
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var methodRightButtonUp = typeof(Wpf.Ui.Controls.TitleBar).GetMethod("TitleBar_MouseRightButtonUp", flags);
            if (methodRightButtonUp != null)
            {
                var handler = (MouseButtonEventHandler)Delegate.CreateDelegate(typeof(MouseButtonEventHandler), titleBar, methodRightButtonUp);
                titleBar.MouseRightButtonUp -= handler;
            }
        }
        catch
        {
            // Ignore reflection issues
        }
    }
}
