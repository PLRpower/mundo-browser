using CommunityToolkit.Mvvm.ComponentModel;

namespace MundoBrowser.ViewModels
{
    public partial class TabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "New Tab";

        [ObservableProperty]
        private DateTime _lastAccessed = DateTime.Now;

        [ObservableProperty]
        private bool _isClosing;

        [ObservableProperty]
        private bool _isPrimarySplitTab;

        [ObservableProperty]
        private bool _isDiscarded = true;

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

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return "0:00";
            int totalSeconds = (int)seconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;
            return hours > 0
                ? $"{hours}:{minutes:D2}:{secs:D2}"
                : $"{minutes}:{secs:D2}";
        }

        [ObservableProperty]
        private string? _installableExtensionId;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ZoomPercentage))]
        private double _zoomFactor = 1.0;

        public string ZoomPercentage => $"{(int)(Math.Round(ZoomFactor * 100))}%";

        [ObservableProperty]
        private bool _isCreatedFromNewWindow;

        public TabViewModel? OpenedByTab { get; set; }
    }
}
