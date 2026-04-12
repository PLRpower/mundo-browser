using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MundoBrowser.Services;
using MundoBrowser.Models;

namespace MundoBrowser.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
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

        [ObservableProperty]
        private ObservableCollection<HistoryEntry> _suggestions = new();

        [ObservableProperty]
        private bool _isPendingNewTab;

        [ObservableProperty]
        private string _addressBarText = "";

        [ObservableProperty]
        private ObservableCollection<ExtensionInfo> _installedExtensions = new();

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

        public MainViewModel()
        {
            HistoryManager = new HistoryManager();
            SessionManager = new SessionManager();
            
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
                        Tabs.Add(new TabViewModel { Title = tabData.Title, Url = tabData.Url, AddressUrl = tabData.Url, FaviconUrl = tabData.FaviconUrl });
                    }

                    foreach (var pinnedData in session.PinnedTabs)
                    {
                        if (pinnedData.SlotIndex >= 0 && pinnedData.SlotIndex < PinnedTabs.Count)
                        {
                            PinnedTabs[pinnedData.SlotIndex].Tab = new TabViewModel { Title = pinnedData.Title, Url = pinnedData.Url, AddressUrl = pinnedData.Url, FaviconUrl = pinnedData.FaviconUrl };
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
            var newTab = new TabViewModel { Title = "New Tab", Url = "https://www.google.com" };
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }
        
        public void SaveCurrentSession()
        {
            SessionManager.SaveSession(this);
        }

        public event EventHandler? NewTabRequested;

        [RelayCommand]
        public void AddNewTab()
        {
            IsPendingNewTab = true;
            AddressBarText = "";
            NewTabRequested?.Invoke(this, EventArgs.Empty);
        }

        public void AddTabWithUrl(string url)
        {
            var newTab = new TabViewModel { Title = "Loading...", Url = url, AddressUrl = url };
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
