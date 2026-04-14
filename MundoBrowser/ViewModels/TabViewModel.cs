using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MundoBrowser.ViewModels
{
    public partial class TabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "New Tab";

        [ObservableProperty]
        private DateTime _lastAccessed = DateTime.Now;

        [ObservableProperty]
        private bool _isDiscarded;

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

        [ObservableProperty]
        private string? _faviconUrl;

        [ObservableProperty]
        private string? _faviconRelativePath;

        [ObservableProperty]
        private bool _isExtensionStorePage;

        [ObservableProperty]
        private bool _isPlayingAudio;
        
        [ObservableProperty]
        private bool _isMediaPaused;

        [ObservableProperty]
        private string? _mediaTitle;

        [ObservableProperty]
        private string? _mediaArtist;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MediaPositionText))]
        [NotifyPropertyChangedFor(nameof(MediaProgressRatio))]
        private double _mediaPosition;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MediaDurationText))]
        [NotifyPropertyChangedFor(nameof(MediaProgressRatio))]
        private double _mediaDuration;

        public double MediaProgressRatio => MediaDuration > 0 ? (MediaPosition / MediaDuration) * 100 : 0;

        [ObservableProperty]
        private bool _isSeeking;

        [ObservableProperty]
        private bool _isMediaMuted;

        public string MediaPositionText => FormatTime(MediaPosition);
        public string MediaDurationText => FormatTime(MediaDuration);

        private string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "0:00";
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 
                ? time.ToString(@"h\:mm\:ss") 
                : time.ToString(@"m\:ss");
        }

        [ObservableProperty]
        private string? _installableExtensionId;

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
