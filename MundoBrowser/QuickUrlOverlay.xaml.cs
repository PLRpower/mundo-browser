using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MundoBrowser.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MundoBrowser
{
    public partial class QuickUrlOverlay : System.Windows.Controls.UserControl
    {
        public event EventHandler<string>? UrlSubmitted;
        public event EventHandler? CloseRequested;

        public QuickUrlOverlay()
        {
            InitializeComponent();
            
            // Auto-focus the textbox whenever it becomes visible
            IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    // Delay slightly to ensure visual tree is ready
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UrlTextBox.Focus();
                        Keyboard.Focus(UrlTextBox);
                        UrlTextBox.SelectAll();
                        UpdateSuggestions(); // Initial suggestions
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            };
        }

        private void UrlTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && SuggestionsList.Items.Count > 0)
            {
                // Move focus to list
                SuggestionsList.SelectedIndex = 0;
                var item = SuggestionsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                item?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                SubmitUrl(UrlTextBox.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSuggestions();
        }

        private void UpdateSuggestions()
        {
            if (DataContext is MainViewModel vm)
            {
                var query = UrlTextBox.Text;
                
                // Use the same HistoryManager logic as the main address bar
                // If query is empty, show recent history
                var suggestions = vm.HistoryManager.SearchHistory(query, 5);
                
                vm.Suggestions.Clear();
                foreach (var suggestion in suggestions)
                {
                    vm.Suggestions.Add(suggestion);
                }
            }
        }

        private void SuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (SuggestionsList.SelectedItem is Models.HistoryEntry entry)
                {
                    SubmitUrl(entry.Url);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Key == Key.Up && SuggestionsList.SelectedIndex == 0)
            {
                // Go back to TextBox
                SuggestionsList.SelectedIndex = -1;
                UrlTextBox.Focus();
                e.Handled = true;
            }
        }

        private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Handle click on item
            var item = ItemsControl.ContainerFromElement(SuggestionsList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null && item.DataContext is Models.HistoryEntry entry)
            {
                SubmitUrl(entry.Url);
                e.Handled = true;
            }
        }

        private void SubmitUrl(string? url)
        {
            var trimmedUrl = url?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedUrl))
            {
                UrlSubmitted?.Invoke(this, trimmedUrl);
                UrlTextBox.Clear(); // Clear for next time
            }
        }

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close overlay when clicking on the dark background
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            UrlTextBox.Clear();
            if (DataContext is MainViewModel vm)
            {
                vm.Suggestions.Clear();
            }
        }
    }
}
