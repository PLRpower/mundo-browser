using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CefSharp;
using CefSharp.Wpf.HwndHost;
using MundoBrowser.Helpers;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;

namespace MundoBrowser;

public partial class ExtensionPopupWindow : Window
{
    private readonly FrameworkElement _placementTarget;
    private readonly ChromiumWebBrowser _popupBrowser;

    public ExtensionPopupWindow(MainWindow owner, FrameworkElement placementTarget)
    {
        InitializeComponent();

        Owner = owner;
        _placementTarget = placementTarget;
        _popupBrowser = new ChromiumWebBrowser();
        PopupHost.Child = _popupBrowser;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowAppId(handle, NativeMethods.AppUserModelId);
            NativeMethods.SetWindowCorners(
                handle,
                NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
        };

        owner.LocationChanged += OwnerBoundsChanged;
        owner.SizeChanged += OwnerBoundsChanged;
        Deactivated += Window_Deactivated;
        Closed += (_, _) =>
        {
            owner.LocationChanged -= OwnerBoundsChanged;
            owner.SizeChanged -= OwnerBoundsChanged;
            _popupBrowser.Dispose();
        };
    }

    public async Task InitializeAsync(string popupUrl)
    {
        _popupBrowser.Load(popupUrl);
        await _popupBrowser.WaitForInitialLoadAsync();
        if (IsVisible && NativeMethods.IsCurrentProcessForeground())
            _popupBrowser.Focus();
    }

    public void PositionNextToTarget()
    {
        if (!_placementTarget.IsVisible) return;

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_placementTarget);
        Point targetBottomRight = _placementTarget.PointToScreen(
            new Point(_placementTarget.ActualWidth, _placementTarget.ActualHeight));

        Left = targetBottomRight.X / dpi.DpiScaleX - Width;
        Top = targetBottomRight.Y / dpi.DpiScaleY + 5;
    }

    private void OwnerBoundsChanged(object? sender, EventArgs e)
    {
        PositionNextToTarget();
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        await Task.Delay(100);
        if (IsVisible && !NativeMethods.IsCurrentProcessForeground())
            Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        Close();
        e.Handled = true;
    }
}
