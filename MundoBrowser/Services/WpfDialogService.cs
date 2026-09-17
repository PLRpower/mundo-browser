using System.Windows;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services;

public class WpfDialogService : IDialogService
{
    private readonly IDispatcherService _dispatcherService;

    public WpfDialogService(IDispatcherService dispatcherService)
    {
        _dispatcherService = dispatcherService;
    }

    public void ShowInformation(string message, string title)
    {
        _dispatcherService.Invoke(() =>
        {
            System.Windows.MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    public void ShowError(string message, string title)
    {
        _dispatcherService.Invoke(() =>
        {
            System.Windows.MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }
}
