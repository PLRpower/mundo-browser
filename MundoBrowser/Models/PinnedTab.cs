using CommunityToolkit.Mvvm.ComponentModel;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Models
{
    public partial class PinnedTab : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEmpty))]
        private TabViewModel? _tab;

        partial void OnTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
        {
            if (oldValue != null) oldValue.IsPinned = false;
            if (newValue != null) newValue.IsPinned = true;
        }

        [ObservableProperty]
        private bool _isSelected = false;

        [ObservableProperty]
        private int _slotIndex;

        [ObservableProperty]
        private bool _isDraggingOver = false;

        public bool IsEmpty => Tab == null;

        public PinnedTab(int slotIndex) 
        {
            SlotIndex = slotIndex;
        }

    }
}
