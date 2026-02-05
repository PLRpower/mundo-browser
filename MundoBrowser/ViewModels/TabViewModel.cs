using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MundoBrowser.ViewModels
{
    public partial class TabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "New Tab";

        // The active URL of the WebView
        [ObservableProperty]
        private string _url = "https://www.google.com";

        // Called automatically when Url changes - sync AddressUrl
        partial void OnUrlChanged(string value)
        {
            // Update the address bar to show the current URL
            AddressUrl = value;
        }

        // The text in the address bar
        [ObservableProperty]
        private string _addressUrl = "https://www.google.com";
        
        [ObservableProperty]
        private bool _canGoBack;
        
        [ObservableProperty]
        private bool _canGoForward;
        
        [ObservableProperty]
        private bool _isLoading;

        [RelayCommand]
        public void Navigate()
        {
            // Trigger navigation by updating the active Url
            if (!string.IsNullOrWhiteSpace(AddressUrl))
            {
                // Simple check to add https if missing
                if (!AddressUrl.StartsWith("http") && !AddressUrl.Contains("://"))
                {
                    Url = "https://" + AddressUrl;
                }
                else
                {
                    Url = AddressUrl;
                }
            }
        }
    }
}
