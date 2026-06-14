using CommunityToolkit.Mvvm.ComponentModel;

namespace MundoBrowser.Models
{
    public partial class HistoryEntry : ObservableObject
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        [property: System.Text.Json.Serialization.JsonIgnore]
        [ObservableProperty]
        private string? _faviconUrl;
        public DateTime VisitedAt { get; set; }
        public int VisitCount { get; set; }
        
        public HistoryEntry()
        {
            VisitedAt = DateTime.Now;
            VisitCount = 1;
        }
    }
}
