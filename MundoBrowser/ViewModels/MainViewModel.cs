using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;

namespace MundoBrowser.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAppSettingsService _appSettingsService;
        private readonly IAdBlockerService _adBlockerService;
        private readonly IUpdateService _updateService;
        private readonly IWebViewService _webViewService;

        public IAppSettingsService AppSettingsService => _appSettingsService;
        internal IAdBlockerService AdBlockerService => _adBlockerService;
        public IUpdateService UpdateService => _updateService;
        public IWebViewService WebViewService => _webViewService;
        public IHistoryManager HistoryManager { get; }
        public ISessionManager SessionManager { get; }
        public IFaviconService FaviconService { get; }

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
        private bool _isTopBarVisible = true;

        [ObservableProperty]
        private double _sidebarWidth = 250;

        partial void OnIsSidebarVisibleChanged(bool value)
        {
            _appSettingsService.Update(settings => settings.IsSidebarVisible = value);
        }

        partial void OnIsTopBarVisibleChanged(bool value)
        {
            _appSettingsService.Update(settings => settings.IsTopBarVisible = value);
        }

        [ObservableProperty]
        private ObservableCollection<HistoryEntry> _suggestions = new();

        [ObservableProperty]
        private bool _isPendingNewTab;

        [ObservableProperty]
        private bool _isDraggingTab;

        [ObservableProperty]
        private string _addressBarText = "";

        [ObservableProperty]
        private ObservableCollection<ExtensionInfo> _installedExtensions = new();

        [ObservableProperty]
        private bool _hasActiveDownloads;

        [ObservableProperty]
        private int _activeDownloadCount;

        public event EventHandler? TabDragCompleted;
        public event EventHandler? NewTabRequested;

        public void NotifyTabDragCompleted()
        {
            TabDragCompleted?.Invoke(this, EventArgs.Empty);
        }

        public MainViewModel(
            IAppSettingsService appSettingsService,
            IHistoryManager historyManager,
            ISessionManager sessionManager,
            IFaviconService faviconService,
            IAdBlockerService adBlockerService,
            IUpdateService updateService,
            IWebViewService webViewService)
        {
            _appSettingsService = appSettingsService;
            _adBlockerService = adBlockerService;
            _updateService = updateService;
            _webViewService = webViewService;
            HistoryManager = historyManager;
            SessionManager = sessionManager;
            FaviconService = faviconService;

            _webViewService.ActiveDownloadsChanged += OnActiveDownloadsChanged;
            _updateService.UpdateStatusChanged += OnUpdateStatusChanged;
            _appSettingsService.SettingsChanged += OnAppSettingsChanged;
            Tabs.CollectionChanged += (_, _) => UpdateSplitTabFlags();

            IsSidebarVisible = _appSettingsService.Current.IsSidebarVisible;
            IsTopBarVisible = _appSettingsService.Current.IsTopBarVisible;
            SidebarWidth = _appSettingsService.Current.SidebarWidth;

            for (int i = 0; i < 6; i++) PinnedTabs.Add(new PinnedTab(i));

            var session = SessionManager.LoadSession();
            if (session != null)
            {
                RestoreSession(session);
            }
            else
            {
                CreateDefaultTab();
            }

            if (SelectedTab == null) SelectedTab = Tabs.FirstOrDefault() ?? PinnedTabs.FirstOrDefault(p => !p.IsEmpty)?.Tab;
            if (SelectedTab != null) AddressBarText = SelectedTab.AddressUrl;

            InitializeSessionTracking();
        }

        private void OnAppSettingsChanged(AppSettings settings)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (IsSidebarVisible != settings.IsSidebarVisible)
                    IsSidebarVisible = settings.IsSidebarVisible;
                if (IsTopBarVisible != settings.IsTopBarVisible)
                    IsTopBarVisible = settings.IsTopBarVisible;
                if (Math.Abs(SidebarWidth - settings.SidebarWidth) > 0.1)
                    SidebarWidth = settings.SidebarWidth;
            });
        }

        partial void OnSelectedTabChanged(TabViewModel? value)
        {
            if (value != null)
            {
                foreach (var p in PinnedTabs) p.IsSelected = (p.Tab == value);
                IsPendingNewTab = false;
                AddressBarText = value.AddressUrl;

                if (PrimarySplitTab != null && SecondarySplitTab != null)
                {
                    if (value == PrimarySplitTab)
                    {
                        _focusedSplitPane = 0;
                        OnPropertyChanged(nameof(FocusedSplitPane));
                        IsSplitViewActive = true;
                    }
                    else if (value == SecondarySplitTab)
                    {
                        _focusedSplitPane = 1;
                        OnPropertyChanged(nameof(FocusedSplitPane));
                        IsSplitViewActive = true;
                    }
                    else
                    {
                        IsSplitViewActive = false;
                    }
                }
                else if (IsSplitViewActive)
                {
                    IsSplitViewActive = false;
                }
            }
            else
            {
                foreach (var p in PinnedTabs) p.IsSelected = false;
            }

            SelectedListTab = (value != null && Tabs.Contains(value))
                ? (value == SecondarySplitTab ? PrimarySplitTab : value)
                : null;

            RequestSessionSave();
        }

        partial void OnSelectedListTabChanged(TabViewModel? value)
        {
            if (value != null) SelectedTab = value;
        }

        private void CreateDefaultTab()
        {
            var startPage = _appSettingsService.Current.StartPage;
            var newTab = new TabViewModel { Title = "New Tab", Url = startPage, AddressUrl = startPage, IsDiscarded = false };
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        [RelayCommand]
        public void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

        [RelayCommand]
        public void ToggleTopBar() => IsTopBarVisible = !IsTopBarVisible;

        public void SetSidebarWidth(double width)
        {
            SidebarWidth = Math.Clamp(width, 200, 400);
            _appSettingsService.Update(settings => settings.SidebarWidth = SidebarWidth);
        }

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

        [RelayCommand]
        public void OpenDownloads() => _webViewService.OpenDownloadDialog();

        private void OnActiveDownloadsChanged()
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                HasActiveDownloads = _webViewService.HasActiveDownloads;
                ActiveDownloadCount = _webViewService.ActiveDownloadCount;
            });
        }

        [RelayCommand]
        public void AddNewTab()
        {
            IsPendingNewTab = true;
            AddressBarText = string.Empty;
            Suggestions.Clear();
            NewTabRequested?.Invoke(this, EventArgs.Empty);
        }

        public TabViewModel AddTabWithUrl(string url, TabViewModel? openedByTab = null, bool isFromNewWindow = false)
        {
            var newTab = new TabViewModel
            {
                Title = "Loading...",
                Url = url,
                AddressUrl = url,
                IsDiscarded = false,
                IsCreatedFromNewWindow = isFromNewWindow,
                OpenedByTab = openedByTab
            };
            Tabs.Add(newTab);
            SelectedTab = newTab;
            return newTab;
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
                var targetSlot = PinnedTabs[slotIndex];
                var previousSlot = PinnedTabs.FirstOrDefault(pinned => pinned != targetSlot && pinned.Tab == tab);
                var oldTab = targetSlot.Tab;
                if (oldTab != null && oldTab != tab && !Tabs.Contains(oldTab))
                    Tabs.Add(oldTab);

                targetSlot.Tab = tab;
                if (previousSlot != null)
                    previousSlot.Tab = null;

                if (Tabs.Contains(tab))
                    Tabs.Remove(tab);

                if (SelectedTab == tab) foreach (var p in PinnedTabs) p.IsSelected = (p.Tab == tab);
            }
        }

        [RelayCommand]
        public async Task CloseTab(TabViewModel tab)
        {
            if (tab == null) return;
            if (tab.IsClosing) return;

            if ((tab == PrimarySplitTab || tab == SecondarySplitTab) && IsSplitViewActive)
            {
                await CloseSplitCombination(tab);
                return;
            }

            bool wasSelected = (SelectedTab == tab);
            bool removed = false;

            if (Tabs.Contains(tab))
            {
                tab.IsClosing = true;
                removed = true;

                if (wasSelected)
                {
                    SelectNextTabAfterClose(tab);
                }

                await Task.Delay(150);

                if (Tabs.Contains(tab))
                {
                    Tabs.Remove(tab);
                }
            }
            else
            {
                foreach (var p in PinnedTabs)
                {
                    if (p.Tab == tab)
                    {
                        p.Tab = null;
                        removed = true;
                        break;
                    }
                }

                if (wasSelected)
                {
                    SelectNextTabAfterClose(tab);
                }
            }

            if (removed && ActiveMediaTab == tab) ActiveMediaTab = null;
        }

        private void SelectNextTabAfterClose(TabViewModel tabBeingClosed)
        {
            var remainingTabs = Tabs.Where(t => t != tabBeingClosed && !t.IsClosing).ToList();
            if (remainingTabs.Count > 0)
            {
                int currentIndex = Tabs.IndexOf(tabBeingClosed);
                if (currentIndex >= 0 && currentIndex < remainingTabs.Count)
                {
                    SelectedTab = remainingTabs[currentIndex];
                }
                else
                {
                    SelectedTab = remainingTabs[^1];
                }
            }
            else
            {
                var firstPinned = PinnedTabs.FirstOrDefault(p => !p.IsEmpty);
                if (firstPinned != null)
                {
                    SelectedTab = firstPinned.Tab;
                }
                else
                {
                    CreateDefaultTab();
                }
            }
        }

        [RelayCommand]
        public async Task CloseOtherTabs()
        {
            var toRemove = Tabs.Where(t => t != SelectedTab && !t.IsClosing).ToList();
            if (toRemove.Count == 0) return;

            foreach (var tab in toRemove) tab.IsClosing = true;

            await Task.Delay(150);

            foreach (var tab in toRemove) Tabs.Remove(tab);
        }
    }
}
