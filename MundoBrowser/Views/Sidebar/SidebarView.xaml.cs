using System.Windows;
using System.Windows.Input;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using MundoBrowser.Models;

namespace MundoBrowser;

public partial class SidebarView : System.Windows.Controls.UserControl
{
    private TabReorderHelper? _tabReorderHelper;

    public static readonly DependencyProperty ShowNavButtonsProperty =
        DependencyProperty.Register(
            nameof(ShowNavButtons),
            typeof(bool),
            typeof(SidebarView),
            new PropertyMetadata(true));

    public bool ShowNavButtons
    {
        get => (bool)GetValue(ShowNavButtonsProperty);
        set => SetValue(ShowNavButtonsProperty, value);
    }

    public SidebarView()
    {
        InitializeComponent();
        this.DataContextChanged += SidebarView_DataContextChanged;
    }

    private MainWindow? GetMainWindow()
    {
        return Window.GetWindow(this) as MainWindow ?? System.Windows.Application.Current.MainWindow as MainWindow;
    }

    private Microsoft.Web.WebView2.Wpf.WebView2? GetWebView()
    {
        return GetMainWindow()?.GetActiveWebView();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => GetWebView()?.GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => GetWebView()?.GoForward();
    private void Reload_Click(object sender, RoutedEventArgs e) => GetWebView()?.Reload();

    private void TopNavHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d)
        {
            while (d != null && d != sender)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase)
                    return;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
        }

        if (e.ClickCount == 2)
        {
            var win = GetMainWindow();
            if (win != null && win.ResizeMode != ResizeMode.NoResize)
            {
                win.WindowState = win.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                e.Handled = true;
                return;
            }
        }

        if (GetMainWindow() is MainWindow mw)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(mw).Handle;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(2), IntPtr.Zero);
            e.Handled = true;
        }
    }

    private void SidebarView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            _tabReorderHelper = new TabReorderHelper(TabsListBox, vm);
        }
    }

    private void TabItem_PreviewMouseLeftButtonDown(object s, MouseButtonEventArgs e) => _tabReorderHelper?.HandlePreviewMouseDown(e);
    private void TabItem_PreviewMouseLeftButtonUp(object s, MouseButtonEventArgs e) => _tabReorderHelper?.HandlePreviewMouseUp(e);
    private void TabItem_PreviewMouseMove(object s, System.Windows.Input.MouseEventArgs e) => _tabReorderHelper?.HandlePreviewMouseMove(s, e);
    private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem item && item.DataContext is TabViewModel tab)
        {
            if (DataContext is MainViewModel vm && vm.SelectedTab != tab)
            {
                vm.SelectedTab = tab;
            }
        }
    }
    private void TabItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is System.Windows.Controls.ListBoxItem item && item.DataContext is TabViewModel tab)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.CloseTabCommand.Execute(tab);
                e.Handled = true;
            }
        }
    }

    private void TabsList_DragOver(object s, System.Windows.DragEventArgs e) => _tabReorderHelper?.HandleDragOver(e);
    private void TabsList_Drop(object s, System.Windows.DragEventArgs e) => _tabReorderHelper?.HandleDrop(e);
    private void TabsList_DragLeave(object s, System.Windows.DragEventArgs e) => _tabReorderHelper?.HandleDragLeave(e);

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
