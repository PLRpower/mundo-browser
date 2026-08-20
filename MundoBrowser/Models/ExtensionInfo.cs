using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

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
        private string? _storeId;

        [ObservableProperty]
        private string? _folderPath;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string? _iconPath;

        [ObservableProperty]
        private ImageSource? _iconSource;

        [ObservableProperty]
        private string? _popupUrl;

        public ExtensionInfo(string id, string name, bool isEnabled, string? storeId = null, string? folderPath = null)
        {
            Id = id;
            Name = name;
            IsEnabled = isEnabled;
            StoreId = storeId;
            FolderPath = folderPath;
        }
    }
}
