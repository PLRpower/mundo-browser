using System.Collections.ObjectModel;
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
        private bool _isSidebarVisible = true;

        [ObservableProperty]
        private ObservableCollection<HistoryEntry> _suggestions = new();

        [ObservableProperty]
        private ObservableCollection<ExtensionInfo> _installedExtensions = new();

        public HistoryManager HistoryManager { get; }
        public SessionManager SessionManager { get; }

        [RelayCommand]
        public void ToggleSidebar()
        {
            IsSidebarVisible = !IsSidebarVisible;
        }

        public MainViewModel()
        {
            HistoryManager = new HistoryManager();
            SessionManager = new SessionManager();
            
            // Try to load previous session
            var session = SessionManager.LoadSession();
            if (session != null && session.Tabs.Count > 0)
            {
                foreach (var tabData in session.Tabs)
                {
                    Tabs.Add(new TabViewModel 
                    { 
                        Title = tabData.Title, 
                        Url = tabData.Url,
                        AddressUrl = tabData.Url // Ensure address bar shows the URL
                    });
                }

                if (session.SelectedTabIndex >= 0 && session.SelectedTabIndex < Tabs.Count)
                {
                    SelectedTab = Tabs[session.SelectedTabIndex];
                }
                else
                {
                    SelectedTab = Tabs.FirstOrDefault();
                }
            }
            else
            {
                // Add a default tab if no session restored
                CreateDefaultTab();
            }
        }

        private void CreateDefaultTab()
        {
            var newTab = new TabViewModel { Title = "New Tab", Url = "https://www.google.com" };
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }
        
        public void SaveCurrentSession()
        {
            SessionManager.SaveSession(Tabs, SelectedTab);
        }

        public event EventHandler? NewTabRequested;

        [RelayCommand]
        public void AddNewTab()
        {
            NewTabRequested?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        public void CloseTab(TabViewModel tab)
        {
            if (Tabs.Contains(tab))
            {
                // Capture if the tab to be closed is the currently selected one
                // WPF ListBox sets SelectedItem to null immediately when the item is removed,
                // so we must check this BEFORE removing from the collection.
                bool wasSelected = (SelectedTab == tab);

                Tabs.Remove(tab);
                
                // If we closed the active tab (or if SelectedTab matches passed tab), select another one
                if (wasSelected)
                {
                    if (Tabs.Count > 0)
                    {
                        SelectedTab = Tabs[^1];
                    }
                    else
                    {
                        // Option: Close app or add new tab
                        CreateDefaultTab();
                    }
                }
            }
        }

        // Event to notify when extension loading is requested
        public event EventHandler? LoadExtensionRequested;

        [RelayCommand]
        public void LoadExtension()
        {
            LoadExtensionRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
