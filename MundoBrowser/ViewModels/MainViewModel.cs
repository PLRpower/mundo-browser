using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using MundoBrowser.Interfaces;
using MundoBrowser.Services;
using MundoBrowser.Models;

namespace MundoBrowser.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAppSettingsService _appSettingsService;

        [ObservableProperty]
        private ObservableCollection<TabViewModel> _tabs = new();

        [ObservableProperty]
        private TabViewModel? _selectedTab;

        [ObservableProperty]
        private TabViewModel? _selectedListTab;

        [ObservableProperty]
        private ObservableCollection<PinnedTab> _pinnedTabs = new();

        [ObservableProperty]
        private bool _isSidebarVisible = true;

        [ObservableProperty]
        private double _sidebarWidth = 250;

        partial void OnIsSidebarVisibleChanged(bool value)
        {
            _appSettingsService.Update(settings => settings.IsSidebarVisible = value);
        }

        [ObservableProperty]
        private ObservableCollection<HistoryEntry> _suggestions = new();

        [ObservableProperty]
        private bool _isPendingNewTab;

        [ObservableProperty]
        private string _addressBarText = "";

        [ObservableProperty]
        private ObservableCollection<ExtensionInfo> _installedExtensions = new();

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
            get => CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Interfaces.IAdBlockerService>()?.IsAdBlockerEnabled ?? true;
            set
            {
                var service = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Interfaces.IAdBlockerService>();
                if (service != null)
                {
                    service.IsAdBlockerEnabled = value;
                    OnPropertyChanged(nameof(IsAdBlockerEnabled));
                }
            }
        }

        public bool IsCookieBlockerEnabled
        {
            get => CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Interfaces.IAdBlockerService>()?.IsCookieBlockerEnabled ?? true;
            set
            {
                var service = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<Interfaces.IAdBlockerService>();
                if (service != null)
                {
                    service.IsCookieBlockerEnabled = value;
                    OnPropertyChanged(nameof(IsCookieBlockerEnabled));
                }
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

        // Window state properties
        [ObservableProperty]
        private double _windowWidth = 1280;

        [ObservableProperty]
        private double _windowHeight = 840;

        [ObservableProperty]
        private double _windowLeft = 100;

        [ObservableProperty]
        private double _windowTop = 100;

        [ObservableProperty]
        private WindowState _windowState = WindowState.Normal;

        public HistoryManager HistoryManager { get; }
        public SessionManager SessionManager { get; }
        public FaviconService FaviconService { get; }

        partial void OnSelectedTabChanged(TabViewModel? value)
        {
            SelectedListTab = (value != null && Tabs.Contains(value)) ? value : null;

            if (value != null)
            {
                foreach (var p in PinnedTabs) p.IsSelected = (p.Tab == value);
                IsPendingNewTab = false;
                AddressBarText = value.AddressUrl;
            }
            else
            {
                foreach (var p in PinnedTabs) p.IsSelected = false;
            }
        }

        partial void OnSelectedListTabChanged(TabViewModel? value)
        {
            if (value != null) SelectedTab = value;
        }

        [RelayCommand]
        public void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

        [RelayCommand]
        public void OpenSettings()
        {
            var existingSettingsTab = Tabs
                .Concat(PinnedTabs.Where(pinned => pinned.Tab != null).Select(pinned => pinned.Tab!))
                .FirstOrDefault(IsSettingsTab);

            if (existingSettingsTab != null)
            {
                IsPendingNewTab = false;
                SelectedTab = existingSettingsTab;
                AddressBarText = existingSettingsTab.AddressUrl;
                return;
            }

            AddTabWithUrl("about:preferences");
        }

        private static bool IsSettingsTab(TabViewModel tab)
        {
            return tab.AddressUrl.StartsWith("about:preferences", StringComparison.OrdinalIgnoreCase)
                   || tab.Url.StartsWith("about:preferences", StringComparison.OrdinalIgnoreCase)
                   || tab.Url.StartsWith(
                       "https://internals.mundobrowser/settings.html",
                       StringComparison.OrdinalIgnoreCase);
        }

        public MainViewModel()
        {
            _appSettingsService = Ioc.Default.GetService<IAppSettingsService>()
                ?? throw new InvalidOperationException("App settings service is not configured.");

            HistoryManager = new HistoryManager();
            SessionManager = new SessionManager();
            FaviconService = new FaviconService();

            IsSidebarVisible = _appSettingsService.Current.IsSidebarVisible;
            SidebarWidth = _appSettingsService.Current.SidebarWidth;
            
            for (int i = 0; i < 6; i++) PinnedTabs.Add(new PinnedTab(i));

            var session = SessionManager.LoadSession();
            if (session != null)
            {
                // Restore Window State
                WindowWidth = session.WindowWidth;
                WindowHeight = session.WindowHeight;
                WindowLeft = session.WindowLeft;
                WindowTop = session.WindowTop;
                WindowState = (WindowState)session.WindowState;

                if (session.Tabs.Count > 0 || session.PinnedTabs.Count > 0)
                {
                    foreach (var tabData in session.Tabs)
                    {
                        Tabs.Add(new TabViewModel { 
                            Title = tabData.Title ?? "New Tab", 
                            Url = tabData.Url ?? _appSettingsService.Current.StartPage,
                            AddressUrl = tabData.Url ?? _appSettingsService.Current.StartPage,
                            FaviconUrl = tabData.FaviconUrl,
                            ZoomFactor = tabData.ZoomFactor > 0 ? tabData.ZoomFactor : 1.0
                        });
                    }

                    foreach (var pinnedData in session.PinnedTabs)
                    {
                        if (pinnedData.SlotIndex >= 0 && pinnedData.SlotIndex < PinnedTabs.Count)
                        {
                            PinnedTabs[pinnedData.SlotIndex].Tab = new TabViewModel { 
                                Title = pinnedData.Title ?? "New Tab", 
                                Url = pinnedData.Url ?? _appSettingsService.Current.StartPage,
                                AddressUrl = pinnedData.Url ?? _appSettingsService.Current.StartPage,
                                FaviconUrl = pinnedData.FaviconUrl,
                                ZoomFactor = pinnedData.ZoomFactor > 0 ? pinnedData.ZoomFactor : 1.0
                            };
                        }
                    }

                    if (session.IsSelectedTabPinned)
                    {
                        if (session.SelectedTabIndex >= 0 && session.SelectedTabIndex < PinnedTabs.Count)
                            SelectedTab = PinnedTabs[session.SelectedTabIndex].Tab;
                    }
                    else
                    {
                        if (session.SelectedTabIndex >= 0 && session.SelectedTabIndex < Tabs.Count)
                            SelectedTab = Tabs[session.SelectedTabIndex];
                    }
                }
                else CreateDefaultTab();
            }
            else CreateDefaultTab();

            if (SelectedTab == null) SelectedTab = Tabs.FirstOrDefault() ?? PinnedTabs.FirstOrDefault(p => !p.IsEmpty)?.Tab;
            if (SelectedTab != null) AddressBarText = SelectedTab.AddressUrl;
        }

        private void CreateDefaultTab()
        {
            var startPage = _appSettingsService.Current.StartPage;
            var newTab = new TabViewModel { Title = "New Tab", Url = startPage, AddressUrl = startPage, IsDiscarded = false };
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        public async Task SaveCurrentSessionAsync()
        {
            await SessionManager.SaveSessionAsync(this);
        }

        public event EventHandler? NewTabRequested;

        [RelayCommand]
        public void AddNewTab()
        {
            IsPendingNewTab = true;
            AddressBarText = string.Empty;
            Suggestions.Clear();
            NewTabRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetSidebarWidth(double width)
        {
            SidebarWidth = Math.Clamp(width, 200, 400);
            _appSettingsService.Update(settings => settings.SidebarWidth = SidebarWidth);
        }

        public void AddTabWithUrl(string url)
        {
            var newTab = new TabViewModel { Title = "Loading...", Url = url, AddressUrl = url, IsDiscarded = false };
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        [RelayCommand]
        public void OpenPinnedTab(PinnedTab pinned)
        {
            if (pinned != null && !pinned.IsEmpty) SelectedTab = pinned.Tab;
        }

        public void PinTab(TabViewModel tab, int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < PinnedTabs.Count && tab != null)
            {
                if (Tabs.Contains(tab)) Tabs.Remove(tab);
                var oldTab = PinnedTabs[slotIndex].Tab;
                if (oldTab != null && !Tabs.Contains(oldTab)) Tabs.Add(oldTab);
                PinnedTabs[slotIndex].Tab = tab;
                if (SelectedTab == tab) foreach (var p in PinnedTabs) p.IsSelected = (p.Tab == tab);
            }
        }

        [RelayCommand]
        public void CloseTab(TabViewModel tab)
        {
            bool wasSelected = (SelectedTab == tab);
            bool removed = false;

            if (Tabs.Contains(tab)) { Tabs.Remove(tab); removed = true; }
            else
            {
                foreach (var p in PinnedTabs)
                {
                    if (p.Tab == tab) { p.Tab = null; removed = true; break; }
                }
            }
            
            if (removed && ActiveMediaTab == tab) ActiveMediaTab = null;
            
            if (removed && wasSelected)
            {
                if (Tabs.Count > 0) SelectedTab = Tabs[^1];
                else
                {
                    var firstPinned = PinnedTabs.FirstOrDefault(p => !p.IsEmpty);
                    if (firstPinned != null) SelectedTab = firstPinned.Tab;
                    else CreateDefaultTab();
                }
            }
        }

        [RelayCommand]
        public void CloseOtherTabs()
        {
            // We only clean the "regular" tabs list. Pinned tabs (the grid) are kept.
            var toRemove = Tabs.Where(t => t != SelectedTab).ToList();
            foreach (var tab in toRemove) Tabs.Remove(tab);
        }
    }
}
