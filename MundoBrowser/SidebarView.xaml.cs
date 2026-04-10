using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;

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
}