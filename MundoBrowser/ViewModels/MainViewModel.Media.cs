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

        public bool KeepActiveMediaTabWhenPaused { get; set; }

        partial void OnActiveMediaTabChanged(TabViewModel? value)
        {
            KeepActiveMediaTabWhenPaused = false;
            if (value != null) IsMediaBarVisible = true;
        }

        [ObservableProperty]
        private bool _isMediaBarVisible = true;

        public TabViewModel? FindPlayingAudioTab(TabViewModel? excludeTab = null)
        {
            foreach (var tab in Tabs)
            {
                if (tab != excludeTab && tab.IsPlayingAudio)
                    return tab;
            }

            foreach (var pinned in PinnedTabs)
            {
                if (pinned.Tab is { IsPlayingAudio: true } pinnedTab && pinnedTab != excludeTab)
                    return pinnedTab;
            }

            return null;
        }

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
        private void CloseMediaBar()
        {
            IsMediaBarVisible = false;
            ActiveMediaTab = null;
        }

        [RelayCommand]
        private void MediaPlayPause()
        {
            if (ActiveMediaTab != null)
            {
                ActiveMediaTab.IsMediaPaused = !ActiveMediaTab.IsMediaPaused;
                KeepActiveMediaTabWhenPaused = ActiveMediaTab.IsMediaPaused;
            }
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
