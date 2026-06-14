using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using MundoBrowser.Helpers;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;

namespace MundoBrowser;

public partial class ExtensionPopupWindow : Window
{
    private readonly FrameworkElement _placementTarget;

    public ExtensionPopupWindow(MainWindow owner, FrameworkElement placementTarget)
    {
        InitializeComponent();

        Owner = owner;
        _placementTarget = placementTarget;

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
            PopupWebView.Dispose();
        };
    }

    public async Task InitializeAsync(CoreWebView2Environment environment, string popupUrl)
    {
        await PopupWebView.EnsureCoreWebView2Async(environment);
        if (!IsVisible) return;

        PopupWebView.CoreWebView2.Settings.IsScriptEnabled = true;
        PopupWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        PopupWebView.CoreWebView2.Navigate(popupUrl);

        await Task.Delay(100);
        if (IsVisible && NativeMethods.IsCurrentProcessForeground())
            PopupWebView.Focus();
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
