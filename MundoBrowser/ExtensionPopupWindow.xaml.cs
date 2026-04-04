using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace MundoBrowser
{
    public partial class ExtensionPopupWindow : Window
    {
        private readonly string _extensionUrl;
        private readonly CoreWebView2Environment _environment;

        public ExtensionPopupWindow(string extensionUrl, string extensionName, CoreWebView2Environment environment, System.Windows.Point? iconPosition = null)
        {
            InitializeComponent();
            _extensionUrl = extensionUrl;
            _environment = environment;
            TitleText.Text = extensionName;
            
            // Position the window below the icon if position is provided
            if (iconPosition.HasValue)
            {
                Left = iconPosition.Value.X;
                Top = iconPosition.Value.Y;
                
                // Make sure the window stays on screen
                var workArea = SystemParameters.WorkArea;
                if (Left + Width > workArea.Right)
                {
                    Left = workArea.Right - Width;
                }
                if (Top + Height > workArea.Bottom)
                {
                    Top = iconPosition.Value.Y - Height - 10; // Show above if no room below
                }
            }
            
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
