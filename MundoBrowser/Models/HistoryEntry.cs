using System;

namespace MundoBrowser.Models
{
    public class HistoryEntry
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime VisitedAt { get; set; }
        public int VisitCount { get; set; }
        
        public HistoryEntry()
        {
            VisitedAt = DateTime.Now;
            VisitCount = 1;
        }
    }
}
