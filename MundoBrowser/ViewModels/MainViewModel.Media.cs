using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MundoBrowser.ViewModels
{
    public partial class MainViewModel
    {
        // ════════════════════════════════════════════════════════════════
        // Media Control State & Management
        // ════════════════════════════════════════════════════════════════

        [ObservableProperty]
        private TabViewModel? _activeMediaTab;

        partial void OnActiveMediaTabChanged(TabViewModel? value)
        {
            if (value != null) IsMediaBarVisible = true;
        }

        [ObservableProperty]
        private bool _isMediaBarVisible = true;

        public bool IsAdBlockerEnabled
        {
            get => _adBlockerService.IsAdBlockerEnabled;
            set
            {
                _adBlockerService.IsAdBlockerEnabled = value;
                OnPropertyChanged(nameof(IsAdBlockerEnabled));
            }
        }

        public bool IsCookieBlockerEnabled
        {
            get => _adBlockerService.IsCookieBlockerEnabled;
            set
            {
                _adBlockerService.IsCookieBlockerEnabled = value;
                OnPropertyChanged(nameof(IsCookieBlockerEnabled));
            }
        }

        [RelayCommand]
        private void CloseMediaBar() => IsMediaBarVisible = false;

        [RelayCommand]
        private void MediaPlayPause()
        {
            if (ActiveMediaTab != null) ActiveMediaTab.IsMediaPaused = !ActiveMediaTab.IsMediaPaused;
            RequestMediaAction("playPause");
        }

        [RelayCommand]
        private void MediaNext() => RequestMediaAction("next");

        [RelayCommand]
        private void MediaPrevious() => RequestMediaAction("previous");

        [RelayCommand]
        private void MediaVolume()
        {
            if (ActiveMediaTab != null) ActiveMediaTab.IsMediaMuted = !ActiveMediaTab.IsMediaMuted;
            RequestMediaAction("volume");
        }

        [RelayCommand]
        private void MediaSeek(double percent) => RequestMediaAction($"seek:{percent.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        public event EventHandler<string>? MediaActionRequested;
        private void RequestMediaAction(string action) => MediaActionRequested?.Invoke(this, action);
    }
}
