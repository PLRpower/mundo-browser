using System.Collections.ObjectModel;
using System.Linq;
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

        /// <summary>
        /// Propriété helper pour la ListBox des onglets normaux.
        /// On la sépare de SelectedTab pour éviter que la ListBox ne "force" la sélection à null
        /// lorsqu'un onglet épinglé (qui n'est pas dans la liste des onglets normaux) est sélectionné.
        /// </summary>
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

        public HistoryManager HistoryManager { get; }
        public SessionManager SessionManager { get; }

        partial void OnSelectedTabChanged(TabViewModel? value)
        {
            // Synchroniser avec la ListBox : si le nouvel onglet est dans la liste normale, on le sélectionne.
            // Sinon (onglet épinglé), on désélectionne la ListBox visuellement.
            SelectedListTab = (value != null && Tabs.Contains(value)) ? value : null;

            if (value != null)
            {
                // Sync pinned selection state: highlight slot if its tab is the selected one
                foreach (var p in PinnedTabs) 
                {
                    p.IsSelected = (p.Tab == value);
                }

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
            // Quand l'utilisateur clique dans la liste des onglets normaux, on met à jour l'onglet actif global.
            if (value != null)
            {
                SelectedTab = value;
            }
        }

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

            if (SelectedTab != null) AddressBarText = SelectedTab.AddressUrl;

            // Initialize 6 empty pinned slots
            for (int i = 0; i < 6; i++)
            {
                PinnedTabs.Add(new PinnedTab(i));
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
            if (pinned == null || pinned.IsEmpty)
                return;

            // Selecting a pinned tab makes it the active one
            SelectedTab = pinned.Tab;
        }

        public void PinTab(TabViewModel tab, int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < PinnedTabs.Count && tab != null)
            {
                // Remove tab from regular list if it's there
                if (Tabs.Contains(tab))
                {
                    Tabs.Remove(tab);
                }
                
                // If the slot already had a tab, move it back to regular list
                var oldTab = PinnedTabs[slotIndex].Tab;
                if (oldTab != null && !Tabs.Contains(oldTab))
                {
                    Tabs.Add(oldTab);
                }

                // Place tab in slot
                PinnedTabs[slotIndex].Tab = tab;
                
                // Ensure selection state is updated
                if (SelectedTab == tab)
                {
                    foreach (var p in PinnedTabs) p.IsSelected = (p.Tab == tab);
                }
            }
        }

        [RelayCommand]
        public void CloseTab(TabViewModel tab)
        {
            bool wasSelected = (SelectedTab == tab);
            bool removed = false;

            if (Tabs.Contains(tab))
            {
                Tabs.Remove(tab);
                removed = true;
            }
            else
            {
                // Check pinned tabs
                foreach (var p in PinnedTabs)
                {
                    if (p.Tab == tab)
                    {
                        p.Tab = null;
                        removed = true;
                        break;
                    }
                }
            }
            
            if (removed && wasSelected)
            {
                // If we closed the active tab, find another one
                if (Tabs.Count > 0)
                {
                    SelectedTab = Tabs[^1];
                }
                else
                {
                    // Check if any other pinned tab can be selected
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
        }

        [RelayCommand]
        public void CloseOtherTabs()
        {
            if (SelectedTab == null) return;
            
            // Remove all regular tabs except selected
            var toRemove = Tabs.Where(t => t != SelectedTab).ToList();
            foreach (var tab in toRemove) Tabs.Remove(tab);
            
            // Also clear all pinned slots except if selected
            foreach (var p in PinnedTabs)
            {
                if (p.Tab != null && p.Tab != SelectedTab)
                {
                    p.Tab = null;
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
