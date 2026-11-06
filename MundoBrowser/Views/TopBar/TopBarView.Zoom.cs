using System.Windows;
using System.Windows.Media.Animation;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class TopBarView
{
    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab != null)
        {
            vm.SelectedTab.ZoomFactor = 1.0;
            var webView = GetWebView();
            if (webView != null)
            {
                try { webView.ZoomFactor = 1.0; } catch { }
            }
        }
    }

    private void TopBarView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachZoomIndicatorObservers();
        AttachZoomIndicatorObservers();
    }

    private void AttachZoomIndicatorObservers()
    {
        if (_mainViewModel != null)
            return;

        _mainViewModel = DataContext as MainViewModel;
        if (_mainViewModel == null)
            return;

        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        ObserveSelectedTab(showTransientAtDefaultZoom: false);
    }

    private void MainViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            ObserveSelectedTab(showTransientAtDefaultZoom: false);
    }

    private void ObserveSelectedTab(bool showTransientAtDefaultZoom)
    {
        if (_observedZoomTab != null)
            _observedZoomTab.PropertyChanged -= ObservedZoomTab_PropertyChanged;

        _observedZoomTab = _mainViewModel?.SelectedTab;
        if (_observedZoomTab != null)
            _observedZoomTab.PropertyChanged += ObservedZoomTab_PropertyChanged;

        UpdateZoomIndicator(showTransientAtDefaultZoom);
    }

    private void ObservedZoomTab_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.ZoomFactor))
            UpdateZoomIndicator(showTransientAtDefaultZoom: true);
    }

    private void UpdateZoomIndicator(bool showTransientAtDefaultZoom)
    {
        _zoomIndicatorCts?.Cancel();
        _zoomIndicatorCts = null;
        ZoomIndicatorButton.BeginAnimation(OpacityProperty, null);
        ZoomIndicatorButton.Opacity = 1;

        if (_observedZoomTab == null)
        {
            ZoomIndicatorButton.Visibility = Visibility.Collapsed;
            return;
        }

        bool isZoomedOut = _observedZoomTab.ZoomFactor < 1.0 - 0.001;
        bool isDefaultZoom = Math.Abs(_observedZoomTab.ZoomFactor - 1.0) <= 0.001;
        ZoomOutIcon.Visibility = isZoomedOut ? Visibility.Visible : Visibility.Collapsed;
        ZoomInIcon.Visibility = isZoomedOut ? Visibility.Collapsed : Visibility.Visible;

        if (!isDefaultZoom)
        {
            ZoomIndicatorButton.Visibility = Visibility.Visible;
            return;
        }

        if (!showTransientAtDefaultZoom)
        {
            ZoomIndicatorButton.Visibility = Visibility.Collapsed;
            return;
        }

        ZoomIndicatorButton.Visibility = Visibility.Visible;
        var cts = new CancellationTokenSource();
        _zoomIndicatorCts = cts;
        _ = FadeOutZoomIndicatorAsync(cts);
    }

    private async Task FadeOutZoomIndicatorAsync(CancellationTokenSource cts)
    {
        var cancellationToken = cts.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fadeOut.Completed += (_, _) =>
            {
                if (ReferenceEquals(_zoomIndicatorCts, cts))
                    ZoomIndicatorButton.Visibility = Visibility.Collapsed;
            };
            ZoomIndicatorButton.BeginAnimation(OpacityProperty, fadeOut);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void DetachZoomIndicatorObservers()
    {
        _zoomIndicatorCts?.Cancel();
        _zoomIndicatorCts = null;
        if (_observedZoomTab != null)
            _observedZoomTab.PropertyChanged -= ObservedZoomTab_PropertyChanged;
        if (_mainViewModel != null)
            _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;

        _observedZoomTab = null;
        _mainViewModel = null;
    }
}
