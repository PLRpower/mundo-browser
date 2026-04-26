using System.Collections.Generic;

namespace MundoBrowser.Models
{
    public class TabSessionData
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? FaviconRelativePath { get; set; }
        public string? FaviconUrl { get; set; }
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
    }
}
