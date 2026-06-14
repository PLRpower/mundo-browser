using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class SidebarMediaBarView : System.Windows.Controls.UserControl
{
    public SidebarMediaBarView()
    {
        InitializeComponent();
    }

    private void OnMediaSliderDragStarted(
        object sender,
        System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (DataContext is MainViewModel { ActiveMediaTab: { } activeMediaTab })
            activeMediaTab.IsSeeking = true;
    }

    private void OnMediaSliderDragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (sender is System.Windows.Controls.Slider slider
            && DataContext is MainViewModel vm
            && vm.ActiveMediaTab is { } activeMediaTab)
        {
            vm.MediaSeekCommand.Execute(slider.Value);
            activeMediaTab.IsSeeking = false;
        }
    }
}
