using System.Windows;
using System.Windows.Input;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class TopBarView : System.Windows.Controls.UserControl
{
    private CancellationTokenSource? _suggestionCts;
    private bool _isUpdatingAddressBar;

    public System.Windows.Controls.TextBox AddressBar => AddressTextBox;

    public void SetAddressBarText(string text)
    {
        _isUpdatingAddressBar = true;
        AddressTextBox.Text = text;
        _isUpdatingAddressBar = false;
    }

    public TopBarView()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            var win = Window.GetWindow(this);
            if (win != null && win.WindowState != WindowState.Maximized) win.DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this).WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) 
    {
        var win = Window.GetWindow(this);
        win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this).Close();

    private void Back_Click(object sender, RoutedEventArgs e) => GetWebView()?.GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => GetWebView()?.GoForward();
    private void Reload_Click(object sender, RoutedEventArgs e) => GetWebView()?.Reload();

    private Microsoft.Web.WebView2.Wpf.WebView2? GetWebView()
    {
        var mw = Window.GetWindow(this) as MainWindow;
        return mw?.GetActiveWebView();
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedTab != null)
        {
            vm.SelectedTab.ZoomFactor = 1.0;
            var wv = GetWebView();
            if (wv != null) wv.ZoomFactor = 1.0;
        }
    }

    // Address Bar Logic
    private void AddressBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            var input = AddressTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(input)) return;

            string url = input;
            if (!url.StartsWith("http") && !url.Contains("://") && !url.StartsWith("about:"))
            {
                if (url.Contains(".") && !url.Contains(" ")) url = "https://" + url;
                else url = $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";
            }

            if (vm.IsPendingNewTab)
            {
                vm.AddTabWithUrl(url);
            }
            else if (vm.SelectedTab != null)
            {
                vm.SelectedTab.Url = url;
            }
            SuggestionsPopup.IsOpen = false;
            GetWebView()?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SuggestionsPopup.IsOpen)
        {
            SuggestionsListBox.Focus();
            if (SuggestionsListBox.Items.Count > 0)
            {
                var firstItem = SuggestionsListBox.ItemContainerGenerator.ContainerFromIndex(0) as System.Windows.Controls.ListBoxItem;
                firstItem?.Focus();
                SuggestionsListBox.SelectedIndex = 0;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (SuggestionsPopup.IsOpen)
            {
                SuggestionsPopup.IsOpen = false;
            }
            else
            {
                if (DataContext is MainViewModel vmEsc && vmEsc.IsPendingNewTab)
                {
                    vmEsc.IsPendingNewTab = false;
                    if (vmEsc.SelectedTab != null) vmEsc.AddressBarText = vmEsc.SelectedTab.AddressUrl;
                }
                
                AddressTextBox.SelectionLength = 0;
                var wv = GetWebView();
                if (wv != null) wv.Focus();
                else Keyboard.ClearFocus();
            }
            e.Handled = true;
        }
    }

    private void AddressTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isUpdatingAddressBar || DataContext is not MainViewModel vm) return;
        
        string text = AddressTextBox.Text;
        
        if (!AddressTextBox.IsFocused || string.IsNullOrWhiteSpace(text))
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        _suggestionCts?.Cancel();
        _suggestionCts = new CancellationTokenSource();
        var token = _suggestionCts.Token;

        Task.Run(async () => {
            try {
                await Task.Delay(200, token);
                if (token.IsCancellationRequested) return;
                
                var results = vm.HistoryManager.SearchHistory(text);
                
                Dispatcher.Invoke(() => {
                    if (token.IsCancellationRequested || !AddressTextBox.IsFocused) return;
                    
                    vm.Suggestions.Clear();
                    // Add Google Search as the first suggestion if it's not a URL
                    bool isUrl = text.StartsWith("http") || text.Contains("://") || (text.Contains(".") && !text.Contains(" "));
                    if (!isUrl)
                    {
                        vm.Suggestions.Add(new Models.HistoryEntry { Title = text, Url = text, VisitCount = -1 });
                    }
                    
                    foreach (var res in results.Take(7)) vm.Suggestions.Add(res);
                    SuggestionsPopup.IsOpen = vm.Suggestions.Any();
                });
            } catch (OperationCanceledException) { }
        }, token);
    }

    private void AddressTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        AddressTextBox.SelectAll();
        if (!string.IsNullOrWhiteSpace(AddressTextBox.Text))
        {
            // Simulate text changed to show suggestions when clicking back into the address bar
            AddressTextBox_TextChanged(sender, null!);
        }
    }

    private void AddressTextBox_LostFocus(object sender, RoutedEventArgs e) 
    {
        Task.Delay(200).ContinueWith(_ => Dispatcher.Invoke(() => {
            if (!AddressTextBox.IsFocused && !SuggestionsListBox.IsKeyboardFocusWithin)
            {
                SuggestionsPopup.IsOpen = false;
            }
        }));
    }

    private void AddressTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressTextBox.IsFocused)
        {
            AddressTextBox.Focus();
            e.Handled = true;
        }
    }

    private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsListBox.SelectedItem is Models.HistoryEntry entry && DataContext is MainViewModel vm)
        {
            if (vm.IsPendingNewTab)
            {
                vm.AddTabWithUrl(entry.Url);
            }
            else if (vm.SelectedTab != null)
            {
                vm.SelectedTab.Url = entry.Url;
            }
            SuggestionsPopup.IsOpen = false;
        }
    }

    private void SuggestionsList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SuggestionsPopup.IsOpen = false;
            AddressTextBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (SuggestionsListBox.SelectedItem is Models.HistoryEntry entry && DataContext is MainViewModel vm)
            {
                if (vm.IsPendingNewTab)
                {
                    vm.AddTabWithUrl(entry.Url);
                }
                else if (vm.SelectedTab != null)
                {
                    vm.SelectedTab.Url = entry.Url;
                }
                SuggestionsPopup.IsOpen = false;
                GetWebView()?.Focus();
                e.Handled = true;
            }
        }
    }

    private void ExtensionIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string id && Window.GetWindow(this) is MainWindow mw)
            mw.ShowExtensionPopup(id, btn);
    }

    private void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is string id && DataContext is MainViewModel vm)
        {
            var ext = vm.InstalledExtensions.FirstOrDefault(x => x.Id == id);
            if (ext != null) vm.InstalledExtensions.Remove(ext);
        }
    }
}
