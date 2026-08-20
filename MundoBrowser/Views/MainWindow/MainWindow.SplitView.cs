using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Mouse = System.Windows.Input.Mouse;
using Cursors = System.Windows.Input.Cursors;

namespace MundoBrowser;

public partial class MainWindow
{
    private System.Windows.Threading.DispatcherTimer? _splitViewHoverTimer;

    private void InitializeSplitViewEvents(MainViewModel vm)
    {
        vm.SplitViewLayoutChanged += MainViewModel_SplitViewLayoutChanged;

        _splitViewHoverTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _splitViewHoverTimer.Tick += SplitViewHoverTimer_Tick;
        if (vm.IsSplitViewActive)
            _splitViewHoverTimer.Start();
    }

    private void SplitViewHoverTimer_Tick(object? sender, EventArgs e)
    {
        if (_viewModel == null || !_viewModel.IsSplitViewActive || !IsActive || WindowState == WindowState.Minimized)
        {
            _splitViewHoverTimer?.Stop();
            if (PrimaryPaneToolbarPopup != null && PrimaryPaneToolbarPopup.IsOpen) PrimaryPaneToolbarPopup.IsOpen = false;
            if (SecondaryPaneToolbarPopup != null && SecondaryPaneToolbarPopup.IsOpen) SecondaryPaneToolbarPopup.IsOpen = false;
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT mousePoint))
            return;

        try
        {
            if (PrimaryPaneHost != null && PrimaryPaneToolbarPopup != null)
            {
                Point primaryScreenPos = PrimaryPaneHost.PointToScreen(new Point(0, 0));
                double primaryWidth = PrimaryPaneHost.ActualWidth;
                double primaryHeight = PrimaryPaneHost.ActualHeight;

                if (primaryWidth > 50 && primaryHeight > 50)
                {
                    double minY = PrimaryPaneToolbarPopup.IsOpen ? primaryScreenPos.Y - 50 : primaryScreenPos.Y;
                    double maxY = PrimaryPaneToolbarPopup.IsOpen ? primaryScreenPos.Y + 80 : primaryScreenPos.Y + 55;

                    bool primaryHovered = (mousePoint.x >= primaryScreenPos.X && mousePoint.x <= primaryScreenPos.X + primaryWidth
                        && mousePoint.y >= minY && mousePoint.y <= maxY);

                    if (PrimaryPaneToolbarPopup.IsOpen != primaryHovered)
                    {
                        PrimaryPaneToolbarPopup.IsOpen = primaryHovered;
                    }
                    if (primaryHovered)
                    {
                        double primaryToolbarWidth = PrimaryPaneToolbarContent?.ActualWidth ?? 110;
                        PrimaryPaneToolbarPopup.HorizontalOffset = Math.Max(0, (primaryWidth - primaryToolbarWidth) / 2);
                    }
                }
                else PrimaryPaneToolbarPopup.IsOpen = false;
            }

            if (SecondaryPaneHost != null && SecondaryPaneToolbarPopup != null)
            {
                Point secondaryScreenPos = SecondaryPaneHost.PointToScreen(new Point(0, 0));
                double secondaryWidth = SecondaryPaneHost.ActualWidth;
                double secondaryHeight = SecondaryPaneHost.ActualHeight;

                if (secondaryWidth > 50 && secondaryHeight > 50)
                {
                    double minY = SecondaryPaneToolbarPopup.IsOpen ? secondaryScreenPos.Y - 50 : secondaryScreenPos.Y;
                    double maxY = SecondaryPaneToolbarPopup.IsOpen ? secondaryScreenPos.Y + 80 : secondaryScreenPos.Y + 55;

                    bool secondaryHovered = (mousePoint.x >= secondaryScreenPos.X && mousePoint.x <= secondaryScreenPos.X + secondaryWidth
                        && mousePoint.y >= minY && mousePoint.y <= maxY);

                    if (SecondaryPaneToolbarPopup.IsOpen != secondaryHovered)
                    {
                        SecondaryPaneToolbarPopup.IsOpen = secondaryHovered;
                    }
                    if (secondaryHovered)
                    {
                        double secondaryToolbarWidth = SecondaryPaneToolbarContent?.ActualWidth ?? 110;
                        SecondaryPaneToolbarPopup.HorizontalOffset = Math.Max(0, (secondaryWidth - secondaryToolbarWidth) / 2);
                    }
                }
                else SecondaryPaneToolbarPopup.IsOpen = false;
            }
        }
        catch
        {
            // Ignore during rapid window resize/reposition
        }
    }

    private void MainViewModel_SplitViewLayoutChanged(object? sender, EventArgs e)
    {
        if (_viewModel == null) return;

        if (_viewModel.IsSplitViewActive)
        {
            if (_splitViewHoverTimer?.IsEnabled == false)
                _splitViewHoverTimer.Start();
        }
        else
        {
            _splitViewHoverTimer?.Stop();
            if (PrimaryPaneToolbarPopup != null) PrimaryPaneToolbarPopup.IsOpen = false;
            if (SecondaryPaneToolbarPopup != null) SecondaryPaneToolbarPopup.IsOpen = false;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            UpdateSplitViewGridOrientation();
            await UpdateSplitViewWebViewsAsync();
        });
    }

    private void UpdateSplitViewGridOrientation()
    {
        if (_viewModel == null) return;

        if (_viewModel.SplitOrientation == Orientation.Horizontal)
        {
            PrimaryCol.Width = new GridLength(1, GridUnitType.Star);
            SplitterCol.Width = new GridLength(3);
            SecondaryCol.Width = new GridLength(1, GridUnitType.Star);

            PrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            SplitterRow.Height = new GridLength(0);
            SecondaryRow.Height = new GridLength(0);

            Grid.SetRow(PrimaryPaneBorder, 0);
            Grid.SetColumn(PrimaryPaneBorder, 0);
            Grid.SetRowSpan(PrimaryPaneBorder, 1);
            Grid.SetColumnSpan(PrimaryPaneBorder, 1);

            Grid.SetRow(PanesSplitter, 0);
            Grid.SetColumn(PanesSplitter, 1);
            Grid.SetRowSpan(PanesSplitter, 1);
            Grid.SetColumnSpan(PanesSplitter, 1);

            PanesSplitter.Width = 3;
            PanesSplitter.Height = double.NaN;
            PanesSplitter.ResizeDirection = GridResizeDirection.Columns;
            PanesSplitter.HorizontalAlignment = HorizontalAlignment.Center;
            PanesSplitter.VerticalAlignment = VerticalAlignment.Stretch;

            Grid.SetRow(SecondaryPaneBorder, 0);
            Grid.SetColumn(SecondaryPaneBorder, 2);
            Grid.SetRowSpan(SecondaryPaneBorder, 1);
            Grid.SetColumnSpan(SecondaryPaneBorder, 1);
        }
        else
        {
            PrimaryCol.Width = new GridLength(1, GridUnitType.Star);
            SplitterCol.Width = new GridLength(0);
            SecondaryCol.Width = new GridLength(0);

            PrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            SplitterRow.Height = new GridLength(3);
            SecondaryRow.Height = new GridLength(1, GridUnitType.Star);

            Grid.SetRow(PrimaryPaneBorder, 0);
            Grid.SetColumn(PrimaryPaneBorder, 0);
            Grid.SetRowSpan(PrimaryPaneBorder, 1);
            Grid.SetColumnSpan(PrimaryPaneBorder, 1);

            Grid.SetRow(PanesSplitter, 1);
            Grid.SetColumn(PanesSplitter, 0);
            Grid.SetRowSpan(PanesSplitter, 1);
            Grid.SetColumnSpan(PanesSplitter, 1);

            PanesSplitter.Width = double.NaN;
            PanesSplitter.Height = 3;
            PanesSplitter.ResizeDirection = GridResizeDirection.Rows;
            PanesSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            PanesSplitter.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetRow(SecondaryPaneBorder, 2);
            Grid.SetColumn(SecondaryPaneBorder, 0);
            Grid.SetRowSpan(SecondaryPaneBorder, 1);
            Grid.SetColumnSpan(SecondaryPaneBorder, 1);
        }
    }

    private async Task UpdateSplitViewWebViewsAsync()
    {
        if (_viewModel == null) return;

        UpdateSplitViewGridOrientation();

        if (_viewModel.IsSplitViewActive)
        {
            if (_viewModel.PrimarySplitTab != null)
                await _webViewService.GetOrCreateWebViewAsync(_viewModel.PrimarySplitTab, wv => SetupWebViewEvents(wv, _viewModel.PrimarySplitTab));

            if (_viewModel.SecondarySplitTab != null)
                await _webViewService.GetOrCreateWebViewAsync(_viewModel.SecondarySplitTab, wv => SetupWebViewEvents(wv, _viewModel.SecondarySplitTab));
        }

        await _webViewService.UpdateSplitViewLayoutAsync(
            _viewModel.IsSplitViewActive,
            _viewModel.PrimarySplitTab,
            _viewModel.SecondarySplitTab,
            _viewModel.SelectedTab,
            PrimaryPaneHost,
            SecondaryPaneHost);
    }

    private void PrimaryPane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SetFocusedPane(0);
        }
    }

    private void SecondaryPane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SetFocusedPane(1);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TabViewModel)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            if (sender is Border border)
            {
                border.Opacity = 0.9;
            }
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 1.0;
        }
    }

    private void HandleDropOnZone(DragEventArgs e, Action<TabViewModel> applyDropAction)
    {
        if (_viewModel == null) return;

        if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel droppedTab)
        {
            applyDropAction(droppedTab);
            e.Handled = true;
        }

        _viewModel.IsDraggingTab = false;
    }

    private void DropZoneLeft_Drop(object sender, DragEventArgs e)
    {
        HandleDropOnZone(e, tab =>
        {
            if (_viewModel == null) return;
            _viewModel.PrimarySplitTab = tab;
            _viewModel.SplitOrientation = Orientation.Horizontal;
            _viewModel.IsSplitViewActive = true;
        });
    }

    private void DropZoneRight_Drop(object sender, DragEventArgs e)
    {
        HandleDropOnZone(e, tab =>
        {
            if (_viewModel == null) return;
            _viewModel.SecondarySplitTab = tab;
            _viewModel.SplitOrientation = Orientation.Horizontal;
            _viewModel.IsSplitViewActive = true;
        });
    }

    private void DropZoneTop_Drop(object sender, DragEventArgs e)
    {
        HandleDropOnZone(e, tab =>
        {
            if (_viewModel == null) return;
            _viewModel.PrimarySplitTab = tab;
            _viewModel.SplitOrientation = Orientation.Vertical;
            _viewModel.IsSplitViewActive = true;
        });
    }

    private void DropZoneBottom_Drop(object sender, DragEventArgs e)
    {
        HandleDropOnZone(e, tab =>
        {
            if (_viewModel == null) return;
            _viewModel.SecondarySplitTab = tab;
            _viewModel.SplitOrientation = Orientation.Vertical;
            _viewModel.IsSplitViewActive = true;
        });
    }

    private void DropZoneCenter_Drop(object sender, DragEventArgs e)
    {
        HandleDropOnZone(e, tab =>
        {
            if (_viewModel == null) return;
            _viewModel.SelectedTab = tab;
            _viewModel.IsSplitViewActive = false;
        });
    }

    private Point _moveButtonDragStartPoint;

    private void SplitPaneMoveButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _moveButtonDragStartPoint = e.GetPosition(null);
    }

    private void SplitPaneMoveButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _viewModel == null)
            return;

        Vector diff = _moveButtonDragStartPoint - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is FrameworkElement element)
        {
            TabViewModel? tabToDrag = element.Name == "PrimaryPaneMoveButton" 
                ? _viewModel.PrimarySplitTab 
                : _viewModel.SecondarySplitTab;

            if (tabToDrag == null)
                return;

            _viewModel.IsDraggingTab = true;

            System.Windows.GiveFeedbackEventHandler feedbackHandler = (s, a) =>
            {
                a.UseDefaultCursors = false;
                Mouse.OverrideCursor = Cursors.Arrow;
                a.Handled = true;
            };

            try
            {
                element.GiveFeedback += feedbackHandler;
                DragDrop.DoDragDrop(element, tabToDrag, DragDropEffects.Move);
            }
            finally
            {
                element.GiveFeedback -= feedbackHandler;
                Mouse.OverrideCursor = null;
                _viewModel.IsDraggingTab = false;
                _viewModel.NotifyTabDragCompleted();
            }
        }
    }

    private void SplitViewDragOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.IsDraggingTab = false;
        }
    }
}
