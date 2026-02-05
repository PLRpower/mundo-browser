using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace MundoBrowser
{
    public partial class ExtensionPopupWindow : Window
    {
        private readonly string _extensionUrl;
        private readonly CoreWebView2Environment _environment;

        public ExtensionPopupWindow(string extensionUrl, string extensionName, CoreWebView2Environment environment)
        {
            InitializeComponent();
            _extensionUrl = extensionUrl;
            _environment = environment;
            TitleText.Text = extensionName;
            
            Loaded += async (s, e) =>
            {
                try
                {
                    // Initialize with the same environment to share extension context
                    await ExtensionWebView.EnsureCoreWebView2Async(_environment);
                    
                    // Navigate to extension popup
                    ExtensionWebView.CoreWebView2.Navigate(_extensionUrl);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Extension popup error: {ex.Message}");
                    System.Windows.MessageBox.Show($"Could not load extension: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
