using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using MundoBrowser.Models;

namespace MundoBrowser;

public partial class SidebarView : System.Windows.Controls.UserControl
{
    private TabReorderHelper? _tabReorderHelper;

    public SidebarView()
    {
        InitializeComponent();
        this.DataContextChanged += SidebarView_DataContextChanged;
    }

    private void SidebarView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            _tabReorderHelper = new TabReorderHelper(TabsListBox, vm);
        }
    }

    private void TabItem_PreviewMouseLeftButtonDown(object s, MouseButtonEventArgs e) => _tabReorderHelper?.HandlePreviewMouseDown(e);
    private void TabItem_PreviewMouseMove(object s, System.Windows.Input.MouseEventArgs e) => _tabReorderHelper?.HandlePreviewMouseMove(s, e);
    private void TabsList_DragOver(object s, System.Windows.DragEventArgs e) => _tabReorderHelper?.HandleDragOver(e);
    private void TabsList_Drop(object s, System.Windows.DragEventArgs e) => _tabReorderHelper?.HandleDrop(e);
    private void TabsList_DragLeave(object s, System.Windows.DragEventArgs e) => _tabReorderHelper?.ClearIndicators();

    private void PinnedSlot_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TabViewModel)))
        {
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void PinnedSlot_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TabViewModel)) && 
            sender is System.Windows.Controls.Button btn && btn.DataContext is PinnedTab slot)
        {
            slot.IsDraggingOver = true;
        }
    }

    private void PinnedSlot_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is PinnedTab slot)
        {
            slot.IsDraggingOver = false;
        }
    }

    private void PinnedSlot_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is PinnedTab slot)
        {
            slot.IsDraggingOver = false;
            
            if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel tab)
            {
                if (DataContext is MainViewModel vm)
                {
                    int index = vm.PinnedTabs.IndexOf(slot);
                    vm.PinTab(tab, index);
                    
                    // Jouer une animation de rebond lors du drop
                    if (btn.Template.FindName("btnScale", btn) is System.Windows.Media.ScaleTransform scale)
                    {
                        var sb = new System.Windows.Media.Animation.Storyboard();
                        var animX = new System.Windows.Media.Animation.DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(500))
                        {
                            EasingFunction = new System.Windows.Media.Animation.ElasticEase { Oscillations = 2, Springiness = 15 }
                        };
                        var animY = animX.Clone();

                        System.Windows.Media.Animation.Storyboard.SetTarget(animX, scale);
                        System.Windows.Media.Animation.Storyboard.SetTargetProperty(animX, new PropertyPath(System.Windows.Media.ScaleTransform.ScaleXProperty));
                        
                        System.Windows.Media.Animation.Storyboard.SetTarget(animY, scale);
                        System.Windows.Media.Animation.Storyboard.SetTargetProperty(animY, new PropertyPath(System.Windows.Media.ScaleTransform.ScaleYProperty));

                        sb.Children.Add(animX);
                        sb.Children.Add(animY);
                        sb.Begin();
                    }
                }
            }
        }
    }
}