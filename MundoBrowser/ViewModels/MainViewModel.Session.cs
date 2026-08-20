using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MundoBrowser.Models;
using Orientation = System.Windows.Controls.Orientation;

namespace MundoBrowser.ViewModels
{
    public partial class MainViewModel
    {
        // ════════════════════════════════════════════════════════════════
        // Session & Window State Persistence
        // ════════════════════════════════════════════════════════════════

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

        public SessionData CreateSessionDataSnapshot()
        {
            var sessionData = new SessionData
            {
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                WindowLeft = WindowLeft,
                WindowTop = WindowTop,
                WindowState = (int)WindowState,
                IsSplitViewActive = IsSplitViewActive,
                SplitOrientation = (int)SplitOrientation,
                FocusedSplitPane = FocusedSplitPane
            };

            // Save regular tabs
            foreach (var tab in Tabs)
            {
                sessionData.Tabs.Add(new TabSessionData
                {
                    Title = tab.Title,
                    Url = tab.Url,
                    FaviconRelativePath = tab.FaviconRelativePath,
                    FaviconUrl = tab.FaviconUrl,
                    ZoomFactor = tab.ZoomFactor
                });
            }

            // Save pinned tabs
            foreach (var pinned in PinnedTabs)
            {
                if (pinned.Tab != null)
                {
                    sessionData.PinnedTabs.Add(new TabSessionData
                    {
                        Title = pinned.Tab.Title,
                        Url = pinned.Tab.Url,
                        FaviconRelativePath = pinned.Tab.FaviconRelativePath,
                        FaviconUrl = pinned.Tab.FaviconUrl,
                        ZoomFactor = pinned.Tab.ZoomFactor,
                        SlotIndex = pinned.SlotIndex
                    });
                }
            }

            // Save selected tab
            var selectedTab = SelectedTab;
            if (selectedTab != null)
            {
                int index = Tabs.IndexOf(selectedTab);
                if (index >= 0)
                {
                    sessionData.SelectedTabIndex = index;
                    sessionData.IsSelectedTabPinned = false;
                }
                else
                {
                    var pinned = PinnedTabs.FirstOrDefault(p => p.Tab == selectedTab);
                    if (pinned != null)
                    {
                        sessionData.SelectedTabIndex = pinned.SlotIndex;
                        sessionData.IsSelectedTabPinned = true;
                    }
                }
            }

            // Save split view tab references
            if (PrimarySplitTab != null)
            {
                int index = Tabs.IndexOf(PrimarySplitTab);
                if (index >= 0)
                {
                    sessionData.PrimarySplitTabIndex = index;
                    sessionData.IsPrimarySplitTabPinned = false;
                }
                else
                {
                    var pinned = PinnedTabs.FirstOrDefault(p => p.Tab == PrimarySplitTab);
                    if (pinned != null)
                    {
                        sessionData.PrimarySplitTabIndex = pinned.SlotIndex;
                        sessionData.IsPrimarySplitTabPinned = true;
                    }
                }
            }
            else
            {
                sessionData.PrimarySplitTabIndex = -1;
            }

            if (SecondarySplitTab != null)
            {
                int index = Tabs.IndexOf(SecondarySplitTab);
                if (index >= 0)
                {
                    sessionData.SecondarySplitTabIndex = index;
                    sessionData.IsSecondarySplitTabPinned = false;
                }
                else
                {
                    var pinned = PinnedTabs.FirstOrDefault(p => p.Tab == SecondarySplitTab);
                    if (pinned != null)
                    {
                        sessionData.SecondarySplitTabIndex = pinned.SlotIndex;
                        sessionData.IsSecondarySplitTabPinned = true;
                    }
                }
            }
            else
            {
                sessionData.SecondarySplitTabIndex = -1;
            }

            return sessionData;
        }

        public async Task SaveCurrentSessionAsync()
        {
            await HistoryManager.FlushAsync();
            var snapshot = CreateSessionDataSnapshot();
            await SessionManager.SaveSessionAsync(snapshot);
        }

        private void RestoreSession(SessionData session)
        {
            WindowWidth = session.WindowWidth;
            WindowHeight = session.WindowHeight;
            WindowLeft = session.WindowLeft;
            WindowTop = session.WindowTop;
            WindowState = (WindowState)session.WindowState;

            if (session.Tabs.Count > 0 || session.PinnedTabs.Count > 0)
            {
                foreach (var tabData in session.Tabs)
                {
                    Tabs.Add(new TabViewModel
                    {
                        Title = tabData.Title ?? "New Tab",
                        Url = tabData.Url ?? _appSettingsService.Current.StartPage,
                        AddressUrl = tabData.Url ?? _appSettingsService.Current.StartPage,
                        FaviconUrl = ResolveStoredFavicon(tabData),
                        FaviconRelativePath = tabData.FaviconRelativePath,
                        ZoomFactor = tabData.ZoomFactor > 0 ? tabData.ZoomFactor : 1.0
                    });
                }

                foreach (var pinnedData in session.PinnedTabs)
                {
                    if (pinnedData.SlotIndex >= 0 && pinnedData.SlotIndex < PinnedTabs.Count)
                    {
                        PinnedTabs[pinnedData.SlotIndex].Tab = new TabViewModel
                        {
                            Title = pinnedData.Title ?? "New Tab",
                            Url = pinnedData.Url ?? _appSettingsService.Current.StartPage,
                            AddressUrl = pinnedData.Url ?? _appSettingsService.Current.StartPage,
                            FaviconUrl = ResolveStoredFavicon(pinnedData),
                            FaviconRelativePath = pinnedData.FaviconRelativePath,
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

                // Restore Split View State
                if (session.PrimarySplitTabIndex >= 0 && session.SecondarySplitTabIndex >= 0)
                {
                    TabViewModel? primaryTab = null;
                    if (session.IsPrimarySplitTabPinned)
                    {
                        if (session.PrimarySplitTabIndex < PinnedTabs.Count)
                            primaryTab = PinnedTabs[session.PrimarySplitTabIndex].Tab;
                    }
                    else
                    {
                        if (session.PrimarySplitTabIndex < Tabs.Count)
                            primaryTab = Tabs[session.PrimarySplitTabIndex];
                    }

                    TabViewModel? secondaryTab = null;
                    if (session.IsSecondarySplitTabPinned)
                    {
                        if (session.SecondarySplitTabIndex < PinnedTabs.Count)
                            secondaryTab = PinnedTabs[session.SecondarySplitTabIndex].Tab;
                    }
                    else
                    {
                        if (session.SecondarySplitTabIndex < Tabs.Count)
                            secondaryTab = Tabs[session.SecondarySplitTabIndex];
                    }

                    if (primaryTab != null && secondaryTab != null && primaryTab != secondaryTab)
                    {
                        PrimarySplitTab = primaryTab;
                        SecondarySplitTab = secondaryTab;
                        SplitOrientation = (Orientation)session.SplitOrientation;
                        FocusedSplitPane = session.FocusedSplitPane;
                        IsSplitViewActive = session.IsSplitViewActive;
                        if (session.IsSplitViewActive)
                        {
                            SelectedTab = session.FocusedSplitPane == 0 ? primaryTab : secondaryTab;
                        }
                        UpdateSplitTabFlags();
                    }
                }
            }
            else
            {
                CreateDefaultTab();
            }
        }

        private string? ResolveStoredFavicon(TabSessionData tabData)
        {
            return !string.IsNullOrWhiteSpace(tabData.FaviconRelativePath)
                ? FaviconService.GetAbsoluteFaviconPath(tabData.FaviconRelativePath) ?? tabData.FaviconUrl
                : tabData.FaviconUrl;
        }
    }
}
