using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        if (e.LeftButton == MouseButtonState.Pressed && sender is ListBoxItem item && item.DataContext is TabViewModel)
        {
            Vector diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || 
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _viewModel.IsDraggingTab = true;

                System.Windows.GiveFeedbackEventHandler feedbackHandler = (s, a) =>
                {
                    a.UseDefaultCursors = false;
                    System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
                    a.Handled = true;
                };

                try
                {
                    item.GiveFeedback += feedbackHandler;
                    DragDrop.DoDragDrop(item, item.DataContext, System.Windows.DragDropEffects.Move);
                }
                finally
                {
                    item.GiveFeedback -= feedbackHandler;
                    System.Windows.Input.Mouse.OverrideCursor = null;
                    ClearIndicators();
                    _viewModel.IsDraggingTab = false;
                    _viewModel.NotifyTabDragCompleted();
                }
            }
        }
    }

    public void HandleDragOver(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel src)
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            
            System.Windows.Point pos = e.GetPosition(_listBox);
            int slot = GetTargetSlot(pos);
            int oldIdx = _viewModel.Tabs.IndexOf(src);

            ListBoxItem? ni = null;
            string? nt = null;

            if (oldIdx == -1 || (slot != oldIdx && slot != oldIdx + 1))
            {
                GetIndicatorForItemSlot(slot, out ni, out nt);
            }

            if (ni != _lastIndicatorItem || nt != _lastTag)
            {
                if (_lastIndicatorItem != null && _lastIndicatorItem != ni)
                {
                    _lastIndicatorItem.Tag = null;
                }
                if (ni != null)
                {
                    ni.Tag = nt;
                }
                _lastIndicatorItem = ni;
                _lastTag = nt;
            }
        }
    }

    public void HandleDragLeave(System.Windows.DragEventArgs e)
    {
        System.Windows.Point pos = e.GetPosition(_listBox);
        if (pos.X < 0 || pos.Y < 0 || pos.X >= _listBox.ActualWidth || pos.Y >= _listBox.ActualHeight)
        {
            ClearIndicators();
        }
    }

    public void HandleDrop(System.Windows.DragEventArgs e)
    {
        ClearIndicators();
        if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel src)
        {
            System.Windows.Point pos = e.GetPosition(_listBox);
            int targetSlot = GetTargetSlot(pos);
            
            int oldIdx = _viewModel.Tabs.IndexOf(src);
            if (oldIdx != -1)
            {
                int newIdx = targetSlot;
                if (oldIdx < newIdx) newIdx--;
                
                if (oldIdx != newIdx && newIdx >= 0 && newIdx < _viewModel.Tabs.Count)
                {
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

    private int GetTargetSlot(System.Windows.Point pos)
    {
        int count = _listBox.Items.Count;
        if (count == 0) return 0;

        int bestSlot = 0;
        double minDistance = double.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (_listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container && container.IsVisible)
            {
                System.Windows.Point itemPos = container.TranslatePoint(new System.Windows.Point(0, 0), _listBox);
                double itemTop = itemPos.Y;
                double itemHeight = container.ActualHeight;
                double itemBottom = itemTop + itemHeight;
                double itemCenter = itemTop + (itemHeight / 2.0);

                if (pos.Y >= itemTop && pos.Y <= itemBottom)
                {
                    return pos.Y < itemCenter ? i : i + 1;
                }

                double distToTop = Math.Abs(pos.Y - itemTop);
                if (distToTop < minDistance)
                {
                    minDistance = distToTop;
                    bestSlot = i;
                }

                double distToBottom = Math.Abs(pos.Y - itemBottom);
                if (distToBottom < minDistance)
                {
                    minDistance = distToBottom;
                    bestSlot = i + 1;
                }
            }
        }

        return bestSlot;
    }

    private void GetIndicatorForItemSlot(int slot, out ListBoxItem? ni, out string? nt)
    {
        ni = null;
        nt = null;
        int count = _listBox.Items.Count;
        if (count == 0) return;

        if (slot < count)
        {
            ni = _listBox.ItemContainerGenerator.ContainerFromIndex(slot) as ListBoxItem;
            if (ni != null)
            {
                nt = "DropTop";
                return;
            }
        }

        if (slot > 0)
        {
            ni = _listBox.ItemContainerGenerator.ContainerFromIndex(slot - 1) as ListBoxItem;
            if (ni != null)
            {
                nt = "DropBottom";
                return;
            }
        }

        ni = _listBox.ItemContainerGenerator.ContainerFromIndex(count - 1) as ListBoxItem;
        if (ni != null)
        {
            nt = "DropBottom";
        }
    }
}


