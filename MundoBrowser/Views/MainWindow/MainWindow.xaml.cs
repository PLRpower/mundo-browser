using System.Windows;
using System.Windows.Input;
using CefSharp.Wpf.HwndHost;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IBrowserService _browserService;
    private readonly IExtensionService _extensionService;
    private readonly IAppSettingsService _settingsService;
    private readonly HashSet<TabViewModel> _trackedTabs = [];
    private readonly HashSet<string> _installingExtensionIds = [];
    private MainViewModel? _viewModel;
    private int _tabSwitchVersion;
    private bool _contentInitialized;
    private bool _isFullscreen;
    private bool _isApplyingFullscreenBounds;
    private bool _isRestoringFromFullscreen;
    private bool _fullscreenHidesUi;
    private bool _resizeOverlaysOpen;
    private bool _isSidebarFloating;
    private bool _isClosingSafe;
    private bool _isSavingSession;
    private bool _restartRequested;
    private WindowState _windowStateBeforeTray = WindowState.Normal;
    private string? _currentExtensionId;
    private ExtensionPopupWindow? _extensionPopupWindow;
    private (WindowState State, WindowStyle Style, ResizeMode Resize, Wpf.Ui.Controls.WindowBackdropType Backdrop, Wpf.Ui.Controls.WindowCornerPreference Corners, bool Topmost, double Left, double Top, double Width, double Height) _prevWindowState;

    private readonly System.Windows.Threading.DispatcherTimer _globalMediaTimer;
    private int _mediaUpdateRunning;
    private DateTime _lastBackgroundMediaUpdate = DateTime.MinValue;
    private readonly string[]? _startArgs;

    public MainWindow(
        MainViewModel viewModel,
        IBrowserService browserService,
        IExtensionService extensionService,
        IAppSettingsService settingsService,
        string[]? args = null)
    {
        _browserService = browserService;
        _extensionService = extensionService;
        _settingsService = settingsService;
        _startArgs = args;
        InitializeComponent();
        DataContext = viewModel;

        Title = AppRuntime.DisplayName;

        InitializeTrayIcon();
        InitializeWindow();
        InitializeEvents(viewModel);

        // Single global timer for media updates to save resources
        _globalMediaTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _globalMediaTimer.Tick += UpdateActiveMediaInfo;
        _globalMediaTimer.Start();

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
        };
        LocationChanged += (_, _) => {
            KeepFullscreenBounds();
            RepositionEdgeTriggerPopup();
        };
        SizeChanged += (_, _) => {
            KeepFullscreenBounds();
            UpdateResizeOverlayState();
            UpdateEdgeTriggerState(forceReopen: true);
        };
        Activated += (_, _) => UpdateEdgeTriggerState(forceReopen: true);
        Deactivated += (_, _) => HideFloatingSidebar(animate: false);
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);

        // Fix WPF bug: ElementName bindings on Popups often get lost after IsOpen toggles
        FloatingSidebarPopup.PlacementTarget = MainGrid;
        EdgeTriggerPopup.PlacementTarget = MainGrid;
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
                UpdateResizeOverlayState(forceReopen: true);
                UpdateEdgeTriggerState(forceReopen: true);

                await _browserService.InitializeAsync(BrowsersContainer);

                if (DataContext is MainViewModel vm)
                {
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
                        UpdateSidebarWidth(vm.IsSidebarVisible);
                    }
                    
                    await LoadExtensionsAsync();
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

    public ChromiumWebBrowser? GetActiveBrowser() => _browserService.ActiveBrowser;

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
        uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundWnd, IntPtr.Zero);
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
}
