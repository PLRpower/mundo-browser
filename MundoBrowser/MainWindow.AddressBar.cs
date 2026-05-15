using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MundoBrowser.Models;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Escape)
        {
            vm.IsPendingNewTab = false;
            if (vm.SelectedTab != null)
            {
                _isUpdatingAddressBar = true;
                vm.AddressBarText = vm.SelectedTab.AddressUrl;
                _isUpdatingAddressBar = false;
            }
            if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false;
            _webViewService.ActiveWebView?.Focus();
            e.Handled = true;
            return;
        }

        // Suggestions navigation with Arrows
        if (SuggestionsPopup != null && SuggestionsPopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                if (SuggestionsListBox.SelectedIndex < SuggestionsListBox.Items.Count - 1)
                {
                    SuggestionsListBox.SelectedIndex++;
                    SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
                }
                else if (SuggestionsListBox.Items.Count > 0)
                {
                    SuggestionsListBox.SelectedIndex = 0;
                    SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                if (SuggestionsListBox.SelectedIndex > 0)
                {
                    SuggestionsListBox.SelectedIndex--;
                    SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);
                }
                else
                {
                    SuggestionsListBox.SelectedIndex = -1;
                }
                e.Handled = true;
                return;
            }
        }

        if (e.Key != Key.Enter) return;

        string url;
        var input = AddressTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(input)) return;
        
        // If an item is selected in suggestions, use it
        if (SuggestionsPopup != null && SuggestionsPopup.IsOpen && SuggestionsListBox.SelectedItem is HistoryEntry selectedEntry)
        {
            // If it's a search suggestion (VisitCount == -1) but the input looks like a URL,
            // we prioritize direct navigation. This allows typing IPs or domains without
            // being forced into a Google search.
            if (selectedEntry.VisitCount == -1 && IsUrl(input))
            {
                url = (input.Contains("://") || input.StartsWith("about:")) ? input : "https://" + input;
            }
            else
            {
                url = selectedEntry.Url;
            }
        }
        else
        {
            if (IsUrl(input))
            {
                url = (input.Contains("://") || input.StartsWith("about:")) ? input : "https://" + input;
            }
            else
            {
                url = $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";
            }
        }

        if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false;

        if (vm.IsPendingNewTab) {
            vm.IsPendingNewTab = false;
            vm.AddTabWithUrl(url);
        } else if (vm.SelectedTab != null) {
            vm.SelectedTab.Url = vm.SelectedTab.AddressUrl = url;
            _webViewService.ActiveWebView?.CoreWebView2?.Navigate(url);
        }
        
        _webViewService.ActiveWebView?.Focus();
    }

    private bool IsUrl(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return false;
        t = t.Trim();
        if (t.Contains(" ")) return false;
        
        // Protocol or special URLs
        if (t.Contains("://") || t.StartsWith("about:") || t.StartsWith("file:") || t.StartsWith("data:")) return true;
        
        // Common hostnames
        if (t == "localhost") return true;
        
        // IP Address (v4 or v6)
        if (t.Contains(".") && System.Net.IPAddress.TryParse(t, out var addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return true;
        if (t.StartsWith("[") && t.Contains(":") && t.EndsWith("]")) return true; // Simple IPv6 check

        // Heuristic for domains: contains a dot and doesn't end with one
        if (t.Contains(".") && !t.EndsWith(".") && t.Split('.').Last().Length >= 2) return true;

        return false;
    }

    private async void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingAddressBar || DataContext is not MainViewModel vm) return;
        if (AddressTextBox == null || SuggestionsPopup == null) return;
        _suggestionCts?.Cancel();
        _suggestionCts = new CancellationTokenSource();
        var query = AddressTextBox.Text;
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2 || !AddressTextBox.IsFocused) {
            SuggestionsPopup.IsOpen = false;
            return;
        }
        try {
            await Task.Delay(150, _suggestionCts.Token);
            if (SuggestionsPopup == null || vm.HistoryManager == null) return;
            var results = vm.HistoryManager.SearchHistory(query, 5);
            vm.Suggestions.Clear();
            foreach (var r in results) vm.Suggestions.Add(r);
            
            // ALWAYS add Google Search suggestion as the last item
            vm.Suggestions.Add(new HistoryEntry 
            { 
                Title = $"Rechercher \"{query}\" avec Google", 
                Url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}",
                VisitCount = -1 // Special flag to identify search suggestions
            });

            if (vm.Suggestions.Count > 0)
            {
                SuggestionsListBox.SelectedIndex = 0; // Pre-select first result
                SuggestionsPopup.IsOpen = true;
            }
            else
            {
                SuggestionsPopup.IsOpen = false;
            }
        } catch (TaskCanceledException) { }
    }

    private void AddressTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        AddressTextBox.SelectAll();
        if (DataContext is MainViewModel vm && vm.Suggestions.Count > 0 && SuggestionsPopup != null)
        {
            SuggestionsListBox.SelectedIndex = -1;
            SuggestionsPopup.IsOpen = true;
        }
    }

    private void AddressTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressTextBox.IsFocused) { AddressTextBox.Focus(); e.Handled = true; }
    }

    private void AddressTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsPendingNewTab = false;
            if (vm.SelectedTab != null && AddressTextBox.Text != vm.SelectedTab.AddressUrl)
            {
                // Only restore URL if we are not clicking on a suggestion
                // We use a small delay to let the suggestion click process
                Dispatcher.BeginInvoke(new Action(() => {
                    if (!AddressTextBox.IsFocused && (SuggestionsPopup == null || !SuggestionsPopup.IsOpen))
                    {
                        _isUpdatingAddressBar = true;
                        vm.AddressBarText = vm.SelectedTab.AddressUrl;
                        _isUpdatingAddressBar = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        Task.Delay(200).ContinueWith(_ => Dispatcher.Invoke(() => { if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false; }));
    }

    private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Find the actual item clicked
        var item = ItemsControl.ContainerFromElement(SuggestionsListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item != null && item.DataContext is HistoryEntry entry && DataContext is MainViewModel vm)
        {
            if (vm.IsPendingNewTab)
            {
                vm.IsPendingNewTab = false;
                vm.AddTabWithUrl(entry.Url);
            }
            else if (vm.SelectedTab != null)
            {
                vm.SelectedTab.Url = vm.SelectedTab.AddressUrl = entry.Url;
                _webViewService.ActiveWebView?.CoreWebView2?.Navigate(entry.Url);
            }
            if (SuggestionsPopup != null) SuggestionsPopup.IsOpen = false;
            _webViewService.ActiveWebView?.Focus();
            e.Handled = true;
        }
    }
}