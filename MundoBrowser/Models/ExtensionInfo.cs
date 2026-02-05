using CommunityToolkit.Mvvm.ComponentModel;

namespace MundoBrowser.Models
{
    /// <summary>
    /// Represents an installed browser extension
    /// </summary>
    public partial class ExtensionInfo : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string? _iconPath;

        public ExtensionInfo(string id, string name, bool isEnabled)
        {
            Id = id;
            Name = name;
            IsEnabled = isEnabled;
        }
    }
}
