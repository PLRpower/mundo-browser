namespace MundoBrowser.Models
{
    public class TabSessionData
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? FaviconRelativePath { get; set; }
        public string? FaviconUrl { get; set; }
        public double ZoomFactor { get; set; } = 1.0;
        public int SlotIndex { get; set; }
    }

    public class SessionData
    {
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public int WindowState { get; set; }
        public List<TabSessionData> Tabs { get; set; } = new();
        public List<TabSessionData> PinnedTabs { get; set; } = new();
        public int SelectedTabIndex { get; set; }
        public bool IsSelectedTabPinned { get; set; }

        public bool IsSplitViewActive { get; set; }
        public int SplitOrientation { get; set; }
        public int PrimarySplitTabIndex { get; set; } = -1;
        public bool IsPrimarySplitTabPinned { get; set; }
        public int SecondarySplitTabIndex { get; set; } = -1;
        public bool IsSecondarySplitTabPinned { get; set; }
        public int FocusedSplitPane { get; set; }
    }
}
