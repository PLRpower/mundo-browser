using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orientation = System.Windows.Controls.Orientation;

namespace MundoBrowser.ViewModels
{
    public partial class MainViewModel
    {
        // ════════════════════════════════════════════════════════════════
        // Split-View State & Management
        // ════════════════════════════════════════════════════════════════

        public static bool IsSplitViewFeatureEnabled { get; set; } = false;

        [ObservableProperty]
        private bool _isSplitViewActive;

        [ObservableProperty]
        private Orientation _splitOrientation = Orientation.Horizontal;

        [ObservableProperty]
        private TabViewModel? _primarySplitTab;

        [ObservableProperty]
        private TabViewModel? _secondarySplitTab;

        [ObservableProperty]
        private int _focusedSplitPane; // 0 = Primary, 1 = Secondary

        public TabViewModel? ActiveSplitTab => FocusedSplitPane == 0 ? PrimarySplitTab : SecondarySplitTab;

        private ICollectionView? _sidebarTabsView;

        public ICollectionView SidebarTabs
        {
            get
            {
                if (_sidebarTabsView == null)
                {
                    _sidebarTabsView = CollectionViewSource.GetDefaultView(Tabs);
                    _sidebarTabsView.Filter = FilterSidebarTab;
                }
                return _sidebarTabsView;
            }
        }

        private bool FilterSidebarTab(object item)
        {
            if (!IsSplitViewFeatureEnabled) return true;
            if (item is not TabViewModel tab) return false;
            if (PrimarySplitTab != null && SecondarySplitTab != null && Tabs.Contains(PrimarySplitTab))
            {
                return tab != SecondarySplitTab;
            }
            return true;
        }

        public void UpdateSplitTabFlags(bool refreshView = true)
        {
            if (!IsSplitViewFeatureEnabled)
            {
                foreach (var tab in Tabs) tab.IsPrimarySplitTab = false;
                foreach (var p in PinnedTabs) if (p.Tab != null) p.Tab.IsPrimarySplitTab = false;
                if (refreshView) _sidebarTabsView?.Refresh();
                return;
            }

            foreach (var tab in Tabs)
            {
                tab.IsPrimarySplitTab = (PrimarySplitTab != null && SecondarySplitTab != null && tab == PrimarySplitTab);
            }
            foreach (var p in PinnedTabs)
            {
                if (p.Tab != null)
                    p.Tab.IsPrimarySplitTab = (PrimarySplitTab != null && SecondarySplitTab != null && p.Tab == PrimarySplitTab);
            }
            if (refreshView)
            {
                _sidebarTabsView?.Refresh();
            }
        }

        public event EventHandler? SplitViewLayoutChanged;

        public void NotifySplitViewLayoutChanged()
        {
            UpdateSplitTabFlags();
            SplitViewLayoutChanged?.Invoke(this, EventArgs.Empty);
            RequestSessionSave();
        }

        partial void OnIsSplitViewActiveChanged(bool value)
        {
            if (!IsSplitViewFeatureEnabled)
            {
                if (value)
                {
                    _isSplitViewActive = false;
                    OnPropertyChanged(nameof(IsSplitViewActive));
                }
                UpdateSplitTabFlags();
                return;
            }

            if (value)
            {
                if (PrimarySplitTab == null) PrimarySplitTab = SelectedTab;
                if (SecondarySplitTab == null || SecondarySplitTab == PrimarySplitTab)
                {
                    SecondarySplitTab = Tabs.FirstOrDefault(t => t != PrimarySplitTab)
                                        ?? PinnedTabs.FirstOrDefault(p => !p.IsEmpty && p.Tab != PrimarySplitTab)?.Tab;
                    if (SecondarySplitTab == null)
                    {
                        SecondarySplitTab = AddTabWithUrl(_appSettingsService.Current.StartPage);
                    }
                }
                FocusedSplitPane = 0;
                SelectedTab = PrimarySplitTab;
            }
            else
            {
                if (SelectedTab == PrimarySplitTab || SelectedTab == SecondarySplitTab)
                {
                    if (ActiveSplitTab != null) SelectedTab = ActiveSplitTab;
                }
            }
            NotifySplitViewLayoutChanged();
        }

        partial void OnPrimarySplitTabChanged(TabViewModel? value)
        {
            if (IsSplitViewActive && FocusedSplitPane == 0 && value != null)
            {
                SelectedTab = value;
            }
            NotifySplitViewLayoutChanged();
        }

        partial void OnSecondarySplitTabChanged(TabViewModel? value)
        {
            if (IsSplitViewActive && FocusedSplitPane == 1 && value != null)
            {
                SelectedTab = value;
            }
            NotifySplitViewLayoutChanged();
        }

        partial void OnFocusedSplitPaneChanged(int value)
        {
            if (IsSplitViewActive)
            {
                var targetTab = value == 0 ? PrimarySplitTab : SecondarySplitTab;
                if (targetTab != null) SelectedTab = targetTab;
            }
            NotifySplitViewLayoutChanged();
        }

        partial void OnSplitOrientationChanged(Orientation value)
        {
            NotifySplitViewLayoutChanged();
        }

        [RelayCommand]
        public void ToggleSplitView()
        {
            if (!IsSplitViewFeatureEnabled) return;
            IsSplitViewActive = !IsSplitViewActive;
        }

        [RelayCommand]
        public void EnableSplitView(TabViewModel? secondaryTab = null)
        {
            if (!IsSplitViewFeatureEnabled) return;
            PrimarySplitTab = SelectedTab;
            if (secondaryTab != null && secondaryTab != PrimarySplitTab)
            {
                SecondarySplitTab = secondaryTab;
            }
            IsSplitViewActive = true;
        }

        [RelayCommand]
        public void EnableSplitViewWithOrientation(string orientation)
        {
            if (!IsSplitViewFeatureEnabled) return;
            SplitOrientation = orientation == "Vertical"
                ? Orientation.Vertical
                : Orientation.Horizontal;
            EnableSplitView();
        }

        [RelayCommand]
        public void DisableSplitView()
        {
            IsSplitViewActive = false;
            PrimarySplitTab = null;
            SecondarySplitTab = null;
            NotifySplitViewLayoutChanged();
        }

        [RelayCommand]
        public void SwapSplitPanes()
        {
            if (!IsSplitViewActive) return;
            (PrimarySplitTab, SecondarySplitTab) = (SecondarySplitTab, PrimarySplitTab);
            FocusedSplitPane = FocusedSplitPane == 0 ? 1 : 0;
        }

        [RelayCommand]
        public void ToggleSplitOrientation()
        {
            SplitOrientation = SplitOrientation == Orientation.Horizontal
                ? Orientation.Vertical
                : Orientation.Horizontal;
        }

        [RelayCommand]
        public void CloseSingleSplitPaneTab(TabViewModel? tabToClose)
        {
            if (tabToClose == null) return;

            TabViewModel? remainingTab = (tabToClose == PrimarySplitTab) ? SecondarySplitTab : PrimarySplitTab;

            IsSplitViewActive = false;
            PrimarySplitTab = null;
            SecondarySplitTab = null;

            if (remainingTab != null)
            {
                SelectedTab = remainingTab;
            }

            CloseTab(tabToClose);
            NotifySplitViewLayoutChanged();
        }

        [RelayCommand]
        public void MaximizeSplitPaneTab(TabViewModel? tabToMaximize)
        {
            if (tabToMaximize == null) return;

            IsSplitViewActive = false;
            PrimarySplitTab = null;
            SecondarySplitTab = null;
            SelectedTab = tabToMaximize;
            NotifySplitViewLayoutChanged();
        }

        [RelayCommand]
        public void SetFocusedPane(object? parameter)
        {
            int index = -1;
            if (parameter is int idx) index = idx;
            else if (parameter is string str && int.TryParse(str, out int parsed)) index = parsed;

            if (index == 0 || index == 1)
            {
                FocusedSplitPane = index;
                if (PrimarySplitTab != null && SecondarySplitTab != null)
                {
                    IsSplitViewActive = true;
                    SelectedTab = index == 0 ? PrimarySplitTab : SecondarySplitTab;
                }
            }
        }

        [RelayCommand]
        public void SetPaneTab(TabViewModel tab)
        {
            if (FocusedSplitPane == 0) PrimarySplitTab = tab;
            else SecondarySplitTab = tab;
        }

        [RelayCommand]
        public void CloseSplitCombination(TabViewModel? tab = null)
        {
            TabViewModel? primary = PrimarySplitTab;
            TabViewModel? secondary = SecondarySplitTab;

            if (primary == null && secondary == null) return;

            IsSplitViewActive = false;
            PrimarySplitTab = null;
            SecondarySplitTab = null;

            if (secondary != null)
            {
                if (Tabs.Contains(secondary)) Tabs.Remove(secondary);
                else foreach (var p in PinnedTabs) { if (p.Tab == secondary) p.Tab = null; }
                if (ActiveMediaTab == secondary) ActiveMediaTab = null;
            }

            if (primary != null)
            {
                bool wasSelected = (SelectedTab == primary || SelectedTab == secondary);
                if (wasSelected)
                {
                    SelectNextTabAfterClose(primary);
                }

                if (Tabs.Contains(primary)) Tabs.Remove(primary);
                else foreach (var p in PinnedTabs) { if (p.Tab == primary) p.Tab = null; }
                if (ActiveMediaTab == primary) ActiveMediaTab = null;
            }
        }
    }
}
