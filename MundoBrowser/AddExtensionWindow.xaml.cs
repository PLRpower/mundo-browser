using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MundoBrowser.Services;

namespace MundoBrowser
{
    public partial class AddExtensionWindow : Window
    {
        private string? _extensionId;
        private readonly ExtensionDownloader _downloader;

        public string? ExtensionPath { get; private set; }

        public AddExtensionWindow()
        {
            InitializeComponent();
            _downloader = new ExtensionDownloader();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ExtensionUrlTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var input = ExtensionUrlTextBox.Text?.Trim();
            
            if (string.IsNullOrWhiteSpace(input))
            {
                _extensionId = null;
                InstallButton.IsEnabled = false;
                HideStatus();
                return;
            }

            // Try to extract extension ID from URL
            string? extractedId = null;
            
            if (input.Contains("chrome.google.com") || input.Contains("chromewebstore.google.com"))
            {
                extractedId = ExtensionDownloader.ExtractExtensionIdFromUrl(input);
            }
            else if (input.Length == 32 && input.All(c => c >= 'a' && c <= 'p'))
            {
                // Looks like a direct extension ID
                extractedId = input;
            }

            _extensionId = extractedId;
            InstallButton.IsEnabled = _extensionId != null;

            if (_extensionId != null)
            {
                ShowStatus($"Extension ID detected: {_extensionId}", false);
            }
            else
            {
                ShowStatus("Invalid URL or extension ID", false, true);
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_extensionId))
            {
                return;
            }

            try
            {
                // Disable UI
                InstallButton.IsEnabled = false;
                ExtensionUrlTextBox.IsEnabled = false;
                ShowStatus("Downloading extension...", true);

                // Download and extract
                ExtensionPath = await _downloader.DownloadAndExtractExtension(_extensionId);

                ShowStatus("Extension downloaded successfully!", false);

                // Wait a moment to show the success message
                await System.Threading.Tasks.Task.Delay(500);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.Message}", false, true);
                InstallButton.IsEnabled = true;
                ExtensionUrlTextBox.IsEnabled = true;
            }
        }

        private void ShowStatus(string message, bool showProgress, bool isError = false)
        {
            StatusBorder.Visibility = Visibility.Visible;
            StatusText.Text = message;
            StatusText.Foreground = isError ? 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 17, 35)) : 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            ProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HideStatus()
        {
            StatusBorder.Visibility = Visibility.Collapsed;
        }

        private void ExampleUrl_Click(object sender, RoutedEventArgs e)
        {
            ExtensionUrlTextBox.Text = "nngceckbapebfimnlniiiahkandclblb";
        }
    }
}
