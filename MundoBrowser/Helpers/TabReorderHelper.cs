using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Helpers;

public class TabReorderHelper
{
    private System.Windows.Point _dragStartPoint;
    private ListBoxItem? _lastIndicatorItem;
    private string? _lastTag;
    private readonly System.Windows.Controls.ListBox _listBox;
    private readonly MainViewModel _viewModel;

    public TabReorderHelper(System.Windows.Controls.ListBox listBox, MainViewModel viewModel)
    {
        _listBox = listBox;
        _viewModel = viewModel;
    }

    public void HandlePreviewMouseDown(MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    public void HandlePreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is ListBoxItem item)
        {
            Vector diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || 
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                item.GiveFeedback += (s, a) => { 
                    a.UseDefaultCursors = false; 
                    System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
                    a.Handled = true; 
                };

                DragDrop.DoDragDrop(item, item.DataContext, System.Windows.DragDropEffects.Move);
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }
    }

    public void HandleDragOver(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TabViewModel)))
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            var res = VisualTreeHelper.HitTest(_listBox, e.GetPosition(_listBox));
            var target = res != null ? FindParent<ListBoxItem>(res.VisualHit) : null;
            
            ListBoxItem? ni = null; 
            string? nt = null;

            if (target != null)
            {
                var rel = e.GetPosition(target);
                int idx = _listBox.ItemContainerGenerator.IndexFromContainer(target);
                if (rel.Y < target.ActualHeight / 2) { ni = target; nt = "DropTop"; }
                else if (idx < _listBox.Items.Count - 1) { ni = _listBox.ItemContainerGenerator.ContainerFromItem(_listBox.Items[idx + 1]) as ListBoxItem; nt = "DropTop"; }
                else { ni = target; nt = "DropBottom"; }
            }

            if (ni != _lastIndicatorItem || nt != _lastTag)
            {
                if (_lastIndicatorItem != null) _lastIndicatorItem.Tag = null;
                if (ni != null) ni.Tag = nt;
                _lastIndicatorItem = ni; _lastTag = nt;
            }
        }
    }

    public void HandleDrop(System.Windows.DragEventArgs e)
    {
        ClearIndicators();
        if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel src)
        {
            var res = VisualTreeHelper.HitTest(_listBox, e.GetPosition(_listBox));
            var targetItem = res != null ? FindParent<ListBoxItem>(res.VisualHit) : null;
            
            if (targetItem != null && targetItem.DataContext is TabViewModel target)
            {
                int oldIdx = _viewModel.Tabs.IndexOf(src);
                int targetIdx = _viewModel.Tabs.IndexOf(target);
                
                if (oldIdx != -1 && targetIdx != -1)
                {
                    int newIdx = targetIdx;
                    if (e.GetPosition(targetItem).Y >= targetItem.ActualHeight / 2) newIdx++;
                    if (oldIdx < newIdx) newIdx--;
                    
                    if (oldIdx != newIdx && newIdx >= 0 && newIdx < _viewModel.Tabs.Count)
                        _viewModel.Tabs.Move(oldIdx, newIdx);
                }
            }
        }
    }

    public void ClearIndicators()
    {
        if (_lastIndicatorItem != null)
        {
            _lastIndicatorItem.Tag = null;
            _lastIndicatorItem = null;
            _lastTag = null;
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject p = VisualTreeHelper.GetParent(child);
        if (p == null) return null;
        return p is T parent ? parent : FindParent<T>(p);
    }
}
