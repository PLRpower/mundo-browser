using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MundoBrowser;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, args) => SetRoundedCorners();
        
        // Handle window state changes to fix maximized mode overflow
        StateChanged += OnWindowStateChanged;
        
        // Initialize WebView2 when the window loads
        Loaded += async (s, args) =>
        {
            try
            {
                // Configure WebView2 environment to disable ALL download verification and SmartScreen
                var options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions()
                {
                    // Enable browser extensions support
                    AreBrowserExtensionsEnabled = true,
                    AdditionalBrowserArguments = 
                        "--disable-features=DownloadBubble,DownloadBubbleV2 " +
                        "--safebrowsing-disable-download-protection " +
                        "--safebrowsing-disable-extension-blacklist " +
                        "--disable-client-side-phishing-detection " +
                        "--disable-popup-blocking"
                };
                
                // Create a persistent user data folder for WebView2 (required for extensions)
                var userDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MundoBrowser", "WebView2Data");
                System.IO.Directory.CreateDirectory(userDataFolder);
                
                var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    null, userDataFolder, options);
                
                await BrowserView.EnsureCoreWebView2Async(environment);
                
                // Disable download verification by handling the DownloadStarting event
                BrowserView.CoreWebView2.DownloadStarting += (sender, args) =>
                {
                    // Disable SmartScreen check for this download
                    args.Handled = false;  // Let the download proceed
                    
                    // Get the default download path
                    var downloadPath = args.ResultFilePath;
                    if (string.IsNullOrEmpty(downloadPath))
                    {
                        // Set default download path to Downloads folder
                        var downloadsFolder = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                            "Downloads");
                        var fileName = System.IO.Path.GetFileName(new Uri(args.DownloadOperation.Uri).LocalPath);
                        if (string.IsNullOrEmpty(fileName))
                        {
                            fileName = "download";
                        }
                        downloadPath = System.IO.Path.Combine(downloadsFolder, fileName);
                        args.ResultFilePath = downloadPath;
                    }
                    
                    // CRITICAL: This prevents the Mark-of-the-Web and SmartScreen verification
                    args.DownloadOperation.StateChanged += (s, e) =>
                    {
                        if (args.DownloadOperation.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed)
                        {
                            // Remove the Mark-of-the-Web (Zone.Identifier) that triggers SmartScreen
                            try
                            {
                                var zoneIdentifierPath = downloadPath + ":Zone.Identifier";
                                if (System.IO.File.Exists(zoneIdentifierPath))
                                {
                                    System.IO.File.Delete(zoneIdentifierPath);
                                }
                            }
                            catch
                            {
                                // Ignore errors when removing Zone.Identifier
                            }
                        }
                    };
                };
                
                // Enable extensions support
                await InitializeExtensionsSupport();
                
                // Subscribe to navigation events to update tab titles and URL
                BrowserView.CoreWebView2.NavigationCompleted += (sender, args) =>
                {
                    UpdateCurrentTabTitle();
                    
                    // Update the address bar with the current URL
                    if (DataContext is ViewModels.MainViewModel viewModel && viewModel.SelectedTab != null && args.IsSuccess)
                    {
                        var url = BrowserView.CoreWebView2.Source;
                        var title = BrowserView.CoreWebView2.DocumentTitle;
                        
                        // Update both URL properties
                        viewModel.SelectedTab.Url = url;
                        viewModel.SelectedTab.AddressUrl = url;
                        
                        // Add to history
                        viewModel.HistoryManager.AddEntry(url, title);
                    }
                };
                
                // Subscribe to document title changes
                BrowserView.CoreWebView2.DocumentTitleChanged += (sender, args) =>
                {
                    UpdateCurrentTabTitle();
                };
                
                // Subscribe to source changes (URL changes) - this fires immediately when URL changes
                BrowserView.CoreWebView2.SourceChanged += (sender, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"SourceChanged fired! New URL: {BrowserView.CoreWebView2.Source}");
                    
                    // Use Dispatcher to ensure UI updates happen on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        var newUrl = BrowserView.CoreWebView2.Source;
                        
                        System.Diagnostics.Debug.WriteLine($"Updating AddressTextBox to: {newUrl}");
                        
                        // Update the address bar directly
                        _isUpdatingText = true;
                        AddressTextBox.Text = newUrl;
                        _isUpdatingText = false;
                        
                        // Also update ViewModel for consistency
                        if (DataContext is ViewModels.MainViewModel viewModel && viewModel.SelectedTab != null)
                        {
                            viewModel.SelectedTab.AddressUrl = newUrl;
                        }
                    });
                };
                
                // Navigate to initial URL after environment is fully configured
                if (DataContext is ViewModels.MainViewModel vm2 && vm2.SelectedTab != null && !string.IsNullOrEmpty(vm2.SelectedTab.Url))
                {
                    BrowserView.CoreWebView2.Navigate(vm2.SelectedTab.Url);
                }
                
                // Load installed extensions to display in the toolbar
                await LoadInstalledExtensions();
            }
            catch (Exception ex)
            {
                // WebView2 runtime might not be installed
                System.Diagnostics.Debug.WriteLine($"WebView2 init error: {ex.Message}");
            }
        };
        
        // Subscribe to LoadExtension event
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.LoadExtensionRequested += OnLoadExtensionRequested;
            
            // Subscribe to property changes
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.IsSidebarVisible))
                {
                    UpdateSidebarColumnWidth(vm.IsSidebarVisible);
                }
                else if (e.PropertyName == nameof(vm.SelectedTab))
                {
                    // When the selected tab changes, navigate to its URL
                    UpdateCurrentTabTitle();
                    
                    // Navigate to the tab's URL
                    if (vm.SelectedTab != null && !string.IsNullOrEmpty(vm.SelectedTab.Url) && BrowserView.CoreWebView2 != null)
                    {
                        BrowserView.CoreWebView2.Navigate(vm.SelectedTab.Url);
                    }
                }
            };
        }
    }

    private void UpdateSidebarColumnWidth(bool isVisible)
    {
        var mainGrid = (Grid)Content;
        var sidebarColumn = mainGrid.ColumnDefinitions[0];
        
        if (isVisible)
        {
            // Restore to default width with constraints
            sidebarColumn.Width = new GridLength(250);
            sidebarColumn.MinWidth = 150;
            sidebarColumn.MaxWidth = 400;
        }
        else
        {
            // Collapse the column
            sidebarColumn.Width = new GridLength(0);
            sidebarColumn.MinWidth = 0;
            sidebarColumn.MaxWidth = 0;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // When maximized, add border thickness to account for the resize border
        // and negative margin on MainGrid to pull content up to screen edge
        if (WindowState == WindowState.Maximized)
        {
            // The resize border thickness is 5, plus extra padding for Windows
            BorderThickness = new Thickness(8, 8, 8, 8);
            // Pull the content up to compensate for the border
            MainGrid.Margin = new Thickness(0, -1, -6, 0);
        }
        else
        {
            BorderThickness = new Thickness(0);
            MainGrid.Margin = new Thickness(0);
        }
    }

    private async Task InitializeExtensionsSupport()
    {
        // Extensions are supported - we can load them programmatically
        System.Diagnostics.Debug.WriteLine("WebView2 ready for extension support");
    }

    private void UpdateCurrentTabTitle()
    {
        try
        {
            if (DataContext is ViewModels.MainViewModel vm && vm.SelectedTab != null)
            {
                // Get the page title from WebView2
                var title = BrowserView.CoreWebView2?.DocumentTitle;
                
                // Update the tab title. If title is empty, show URL or "New Tab"
                if (!string.IsNullOrWhiteSpace(title))
                {
                    vm.SelectedTab.Title = title;
                }
                else if (!string.IsNullOrWhiteSpace(vm.SelectedTab.Url))
                {
                    // Show a portion of the URL if title is not available
                    vm.SelectedTab.Title = vm.SelectedTab.Url;
                }
                else
                {
                    vm.SelectedTab.Title = "New Tab";
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating tab title: {ex.Message}");
        }
    }

    private async void OnLoadExtensionRequested(object? sender, EventArgs e)
    {
        try
        {
            // Show the modern Add Extension dialog
            var dialog = new AddExtensionWindow
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.ExtensionPath))
            {
                await LoadExtensionFromFolder(dialog.ExtensionPath);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Error loading extension: {ex.Message}", "Extension Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadExtensionFromFolder(string path)
    {
        try
        {
            // Log the path for debugging
            System.Diagnostics.Debug.WriteLine($"Attempting to load extension from: {path}");
            
            // Check if manifest.json exists
            var manifestPath = System.IO.Path.Combine(path, "manifest.json");
            if (!System.IO.File.Exists(manifestPath))
            {
                System.Windows.MessageBox.Show($"No manifest.json found in:\n{path}", 
                    "Extension Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Read and log manifest info
            var manifestContent = await System.IO.File.ReadAllTextAsync(manifestPath);
            System.Diagnostics.Debug.WriteLine($"Manifest content: {manifestContent.Substring(0, Math.Min(500, manifestContent.Length))}...");
            
            if (BrowserView.CoreWebView2 != null)
            {
                var profile = BrowserView.CoreWebView2.Profile;
                
                System.Diagnostics.Debug.WriteLine($"Profile path: {profile.ProfilePath}");
                System.Diagnostics.Debug.WriteLine($"AreBrowserExtensionsEnabled should be true");
                
                // Add extension to the browser profile
                var extension = await profile.AddBrowserExtensionAsync(path);
                
                System.Windows.MessageBox.Show($"Extension '{extension.Name}' loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Diagnostics.Debug.WriteLine($"Extension loaded: {extension.Name} (ID: {extension.Id})");
            }
            else
            {
                System.Windows.MessageBox.Show("WebView2 is not initialized yet.", 
                    "Extension Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (System.Runtime.InteropServices.COMException comEx)
        {
            System.Windows.MessageBox.Show($"COM Error loading extension:\nHResult: 0x{comEx.HResult:X8}\nMessage: {comEx.Message}\n\nPath: {path}", 
                "Extension Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"COM Exception: HResult=0x{comEx.HResult:X8}, {comEx.Message}");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to load extension: {ex.Message}\n\nPath: {path}\n\nMake sure the folder contains a valid manifest.json file.", 
                "Extension Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Debug.WriteLine($"Extension load error: {ex.GetType().Name}: {ex.Message}");
        }
        
        // Refresh the extensions list after loading
        await LoadInstalledExtensions();
    }

    private async Task LoadInstalledExtensions()
    {
        try
        {
            if (BrowserView.CoreWebView2 == null || DataContext is not ViewModels.MainViewModel vm)
                return;

            var profile = BrowserView.CoreWebView2.Profile;
            var extensions = await profile.GetBrowserExtensionsAsync();

            vm.InstalledExtensions.Clear();

            foreach (var ext in extensions)
            {
                if (ext.IsEnabled)
                {
                    vm.InstalledExtensions.Add(new Models.ExtensionInfo(ext.Id, ext.Name, ext.IsEnabled));
                    System.Diagnostics.Debug.WriteLine($"Found extension: {ext.Name} (ID: {ext.Id})");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading extensions list: {ex.Message}");
        }
    }

    private async void ExtensionIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string extensionId)
        {
            try
            {
                if (BrowserView.CoreWebView2 == null) return;
                
                // Get extension info from WebView2
                var profile = BrowserView.CoreWebView2.Profile;
                var extensions = await profile.GetBrowserExtensionsAsync();
                
                string? extensionName = null;
                foreach (var ext in extensions)
                {
                    if (ext.Id == extensionId)
                    {
                        extensionName = ext.Name;
                        break;
                    }
                }
                
                string popupPath = "popup/index.html"; // Default - common pattern
                
                // Search in our downloaded extensions folder
                var ourExtensionsPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MundoBrowser", "Extensions");
                
                System.Diagnostics.Debug.WriteLine($"Searching for manifest in: {ourExtensionsPath}");
                
                if (System.IO.Directory.Exists(ourExtensionsPath))
                {
                    // Check each extension folder for a matching manifest
                    foreach (var extFolder in System.IO.Directory.GetDirectories(ourExtensionsPath))
                    {
                        var manifestPath = System.IO.Path.Combine(extFolder, "manifest.json");
                        if (System.IO.File.Exists(manifestPath))
                        {
                            var manifestJson = await System.IO.File.ReadAllTextAsync(manifestPath);
                            
                            // Check if this extension name matches (since IDs differ)
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(manifestJson, @"""name""\s*:\s*""([^""]+)""");
                            if (nameMatch.Success)
                            {
                                var manifestName = nameMatch.Groups[1].Value;
                                // Handle localized names like "__MSG_extName__"
                                if (manifestName.StartsWith("__MSG_") || 
                                    (extensionName != null && manifestName.Contains(extensionName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    System.Diagnostics.Debug.WriteLine($"Found matching extension folder: {extFolder}");
                                }
                            }
                            
                            // Try to find popup path
                            var popupMatch = System.Text.RegularExpressions.Regex.Match(
                                manifestJson, @"""default_popup""\s*:\s*""([^""]+)""");
                            if (popupMatch.Success)
                            {
                                popupPath = popupMatch.Groups[1].Value;
                                System.Diagnostics.Debug.WriteLine($"Found popup path: {popupPath}");
                                break;
                            }
                        }
                    }
                }
                
                var extensionUrl = $"chrome-extension://{extensionId}/{popupPath}";
                System.Diagnostics.Debug.WriteLine($"Opening extension popup: {extensionUrl}");
                
                // Open in a popup window
                var popupWindow = new ExtensionPopupWindow(
                    extensionUrl, 
                    extensionName ?? "Extension",
                    BrowserView.CoreWebView2.Environment);
                popupWindow.Owner = this;
                popupWindow.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening extension: {ex.Message}");
                System.Windows.MessageBox.Show($"Could not open extension: {ex.Message}", 
                    "Extension Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SetRoundedCorners()
    {
        // Try to enable rounded corners on Windows 11
        var hWnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        DwmSetWindowAttribute(hWnd, attribute, ref preference, sizeof(uint));
    }

    // P/Invoke definitions for DWM
    public enum DWMWINDOWATTRIBUTE
    {
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
    }

    public enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = false)]
    public static extern void DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute, uint cbAttribute);

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Edge_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Try to show sidebar if it's hidden
        if (DataContext is ViewModels.MainViewModel vm && !vm.IsSidebarVisible)
        {
            vm.IsSidebarVisible = true;
        }
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // Optional: Manual detection if the invisible border doesn't catch quickly enough
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserView.CanGoBack)
        {
            BrowserView.GoBack();
        }
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserView.CanGoForward)
        {
            BrowserView.GoForward();
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        BrowserView.Reload();
    }

    private void SelectAllUrl_Click(object sender, RoutedEventArgs e)
    {
        // Select all text in the address bar
        AddressTextBox.SelectAll();
        AddressTextBox.Focus();
    }

    private void AddressBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                var input = textBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(input)) return;

                string finalUrl;
                
                // Detect if it's a URL or a search query
                if (IsUrl(input))
                {
                    // It looks like a URL
                    if (!input.StartsWith("http://") && !input.StartsWith("https://"))
                    {
                        finalUrl = "https://" + input;
                    }
                    else
                    {
                        finalUrl = input;
                    }
                }
                else
                {
                    // It's a search query - use Google search
                    finalUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(input)}";
                }
                
                // Update ViewModel (so tab title/url data is consistent)
                if (DataContext is ViewModels.MainViewModel vm && vm.SelectedTab != null)
                {
                    vm.SelectedTab.Url = finalUrl;
                    vm.SelectedTab.AddressUrl = finalUrl;
                }
                
                // Navigate
                try
                {
                    BrowserView.Source = new Uri(finalUrl);
                    // Title will be updated automatically via NavigationCompleted event
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
                }
            }
        }
    }

    private bool IsUrl(string text)
    {
        // If it contains spaces, it's definitely a search query
        if (text.Contains(" ")) return false;
        
        // If it starts with a protocol, it's a URL
        if (text.StartsWith("http://") || text.StartsWith("https://") || text.StartsWith("file://"))
            return true;
        
        // If it looks like localhost or IP address
        if (text.StartsWith("localhost") || text.StartsWith("127.0.0.1"))
            return true;
        
        // Check if it looks like a domain (contains a dot and has a valid TLD pattern)
        if (text.Contains("."))
        {
            var parts = text.Split('.');
            // Must have at least domain.tld (2 parts)
            if (parts.Length >= 2)
            {
                var lastPart = parts[^1].Split('/')[0]; // Get TLD before any path
                // TLD should be 2+ characters and not contain special chars
                if (lastPart.Length >= 2 && lastPart.All(c => char.IsLetterOrDigit(c)))
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var mainGrid = (Grid)Content;
        var sidebarColumn = mainGrid.ColumnDefinitions[0];
        
        var currentWidth = sidebarColumn.Width.Value;
        var newWidth = currentWidth + e.HorizontalChange;
        
        // Respect min/max constraints
        if (newWidth >= 150 && newWidth <= 400)
        {
            sidebarColumn.Width = new GridLength(newWidth);
        }
    }

    // Autocomplete handlers
    private bool _isUpdatingText = false;
    
    private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm || SuggestionsPopup == null || _isUpdatingText)
            return;

        var query = AddressTextBox.Text;
        
        // Don't show suggestions if text is empty or very short
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            SuggestionsPopup.IsOpen = false;
            vm.Suggestions.Clear();
            return;
        }

        // Search history for matching entries
        var suggestions = vm.HistoryManager.SearchHistory(query, 5);
        
        vm.Suggestions.Clear();
        foreach (var suggestion in suggestions)
        {
            vm.Suggestions.Add(suggestion);
        }

        // Inline autocomplete with first suggestion
        if (vm.Suggestions.Count > 0)
        {
            var firstSuggestion = vm.Suggestions[0];
            var urlToComplete = firstSuggestion.Url;
            
            // Try to find the best completion match
            string completionText = null;
            int matchPosition = -1;
            
            // 1. Check if URL starts with query (best match)
            if (urlToComplete.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                completionText = urlToComplete;
                matchPosition = 0;
            }
            // 2. Check if URL starts with query after removing protocol
            else
            {
                var urlWithoutProtocol = urlToComplete.Replace("https://", "").Replace("http://", "").Replace("www.", "");
                if (urlWithoutProtocol.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    completionText = urlWithoutProtocol;
                    matchPosition = 0;
                }
                // 3. Check if domain contains the query (e.g., "yout" matches "youtube.com")
                else
                {
                    var domainPart = urlWithoutProtocol.Split('/')[0]; // Get just the domain
                    var index = domainPart.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        // Found in domain - complete from that position
                        completionText = domainPart;
                        matchPosition = index;
                    }
                }
            }
            
            // Apply inline completion if we found a match
            if (completionText != null && matchPosition >= 0)
            {
                _isUpdatingText = true;
                
                var originalLength = query.Length;
                
                // If match is at beginning, use the full completion
                if (matchPosition == 0)
                {
                    AddressTextBox.Text = completionText;
                    AddressTextBox.Select(originalLength, completionText.Length - originalLength);
                }
                else
                {
                    // Match is in the middle - reconstruct intelligently
                    // Complete from the match position
                    var completion = completionText.Substring(matchPosition);
                    if (completion.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    {
                        AddressTextBox.Text = completion;
                        AddressTextBox.Select(originalLength, completion.Length - originalLength);
                    }
                }
                
                _isUpdatingText = false;
            }
        }

        // Show popup if we have suggestions
        SuggestionsPopup.IsOpen = vm.Suggestions.Count > 0;
    }

    private void AddressTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SuggestionsPopup == null)
            return;
            
        // Could show recent history when focusing without text
        if (DataContext is ViewModels.MainViewModel vm && string.IsNullOrWhiteSpace(AddressTextBox.Text))
        {
            var recentHistory = vm.HistoryManager.GetMostVisited(5);
            vm.Suggestions.Clear();
            foreach (var entry in recentHistory)
            {
                vm.Suggestions.Add(entry);
            }
            
            if (vm.Suggestions.Count > 0)
            {
                SuggestionsPopup.IsOpen = true;
            }
        }
    }

    private void AddressTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SuggestionsPopup == null || SuggestionsListBox == null)
            return;
            
        // Delay closing to allow click on suggestion
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SuggestionsListBox != null && !SuggestionsListBox.IsMouseOver)
            {
                SuggestionsPopup.IsOpen = false;
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox && listBox.SelectedItem is Models.HistoryEntry selectedEntry)
        {
            if (DataContext is ViewModels.MainViewModel vm && vm.SelectedTab != null)
            {
                vm.SelectedTab.AddressUrl = selectedEntry.Url;
                // Navigate to the selected URL
                if (BrowserView?.CoreWebView2 != null)
                {
                    BrowserView.CoreWebView2.Navigate(selectedEntry.Url);
                }
            }
            
            SuggestionsPopup.IsOpen = false;
        }
    }
}
