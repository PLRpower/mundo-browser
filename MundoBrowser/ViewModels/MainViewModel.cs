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

        public HistoryManager HistoryManager { get; }

        [RelayCommand]
        public void ToggleSidebar()
        {
            IsSidebarVisible = !IsSidebarVisible;
        }

        public MainViewModel()
        {
            HistoryManager = new HistoryManager();
            // Add a default tab
            AddNewTab();
        }

        [RelayCommand]
        public void AddNewTab()
        {
            var newTab = new TabViewModel { Title = "New Tab", Url = "https://www.google.com" };
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        [RelayCommand]
        public void CloseTab(TabViewModel tab)
        {
            if (Tabs.Contains(tab))
            {
                Tabs.Remove(tab);
                if (SelectedTab == tab && Tabs.Count > 0)
                {
                    SelectedTab = Tabs[^1];
                }
                else if (Tabs.Count == 0)
                {
                    // Option: Close app or add new tab
                    AddNewTab();
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
