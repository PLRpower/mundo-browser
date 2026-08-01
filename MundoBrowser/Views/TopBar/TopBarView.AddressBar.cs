using System.Windows;
using System.Windows.Input;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;

namespace MundoBrowser;

public partial class TopBarView
{
    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && HasActiveInlineCompletion())
        {
            AcceptInlineCompletion();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && HasActiveInlineCompletion())
        {
            RemoveInlineCompletion();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            var input = AddressTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(input))
                return;

            string url = GetNavigableInlineCompletionUrl()
                         ?? (TryGetDirectNavigationUrl(input, out var directUrl, out _)
                             ? directUrl
                             : BuildSearchUrl(vm, input));

            NavigateToAddress(vm, url);
            ClearInlineCompletion();
            ClearAcceptedCompletion();
            IsSuggestionsOpen = false;
            GetWebView()?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && IsSuggestionsOpen)
        {
            SuggestionsListBox.Focus();
            if (SuggestionsListBox.Items.Count > 0)
            {
                int nextIndex = Math.Min(
                    Math.Max(SuggestionsListBox.SelectedIndex + 1, 0),
                    SuggestionsListBox.Items.Count - 1);
                SuggestionsListBox.SelectedIndex = nextIndex;
                var nextItem = SuggestionsListBox.ItemContainerGenerator.ContainerFromIndex(nextIndex)
                    as System.Windows.Controls.ListBoxItem;
                nextItem?.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (IsSuggestionsOpen)
            {
                IsSuggestionsOpen = false;
            }
            else
            {
                if (DataContext is MainViewModel vmEsc && vmEsc.IsPendingNewTab)
                {
                    vmEsc.IsPendingNewTab = false;
                    if (vmEsc.SelectedTab != null)
                        vmEsc.AddressBarText = vmEsc.SelectedTab.AddressUrl;
                }

                AddressTextBox.SelectionLength = 0;
                var webView = GetWebView();
                if (webView != null)
                    webView.Focus();
                else
                    Keyboard.ClearFocus();
            }
            e.Handled = true;
        }
    }

    private void AddressTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateAddressDisplay();
        _suggestionFaviconsCts?.Cancel();
        if (_isUpdatingAddressBar || _isApplyingInlineCompletion || DataContext is not MainViewModel vm)
            return;

        if (e != null)
        {
            bool insertedText = e.Changes.Any(change => change.AddedLength > 0);
            bool removedText = e.Changes.Any(change => change.RemovedLength > 0);
            if (insertedText)
                _suppressInlineCompletionUntilInsertion = false;
            else if (removedText)
                _suppressInlineCompletionUntilInsertion = true;
        }

        string text = AddressTextBox.Text;
        if (!string.Equals(text, _acceptedCompletionText, StringComparison.Ordinal))
            ClearAcceptedCompletion();

        if (!string.Equals(text, _inlineCompletionText, StringComparison.Ordinal))
            ClearInlineCompletion();

        if (!string.Equals(text, _suppressedCompletionText, StringComparison.OrdinalIgnoreCase))
            _suppressedCompletionText = null;

        if (!AddressTextBox.IsKeyboardFocused || string.IsNullOrWhiteSpace(text))
        {
            ClearInlineCompletion();
            IsSuggestionsOpen = false;
            return;
        }

        var results = vm.HistoryManager.SearchHistory(text, maxResults: 20);
        TryApplyInlineCompletion(text, results, vm);
        PopulateSuggestions(text, results, vm);
        IsSuggestionsOpen = vm.Suggestions.Any();
        SuggestionsListBox.SelectedIndex = vm.Suggestions.Count > 0 ? 0 : -1;
    }

    private void AddressTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateAddressDisplay();
        
        _suppressInlineCompletionUntilInsertion = true;
            
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AddressTextBox.SelectAll();
            var scrollViewer = GetDescendantByType<System.Windows.Controls.ScrollViewer>(AddressTextBox);
            if (scrollViewer != null)
                scrollViewer.ScrollToLeftEnd();
            else
                AddressTextBox.ScrollToHome();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static T? GetDescendantByType<T>(System.Windows.DependencyObject depObj) where T : System.Windows.DependencyObject
    {
        if (depObj == null) return null;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            if (child is T t)
                return t;
            
            var result = GetDescendantByType<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private async void AddressTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await Task.Delay(200);
        if (!IsLoaded)
            return;

        if (!AddressTextBox.IsKeyboardFocused && !SuggestionsListBox.IsKeyboardFocusWithin)
        {
            ClearInlineCompletion();
            ClearAcceptedCompletion();
            _suppressedCompletionText = null;
            IsSuggestionsOpen = false;
            if (DataContext is MainViewModel vm && vm.IsPendingNewTab)
            {
                vm.IsPendingNewTab = false;
                if (vm.SelectedTab != null)
                    vm.AddressBarText = vm.SelectedTab.AddressUrl;
            }
        }

        UpdateAddressDisplay();
    }

    private void UpdateAddressDisplay()
    {
        if (!IsInitialized || AddressDisplayTextBlock == null || SuggestionsListBox == null)
            return;

        string text = AddressTextBox.Text ?? "";
        bool shouldShow = !AddressTextBox.IsKeyboardFocused
                          && !IsSuggestionsOpen
                          && !SuggestionsListBox.IsKeyboardFocusWithin
                          && !string.IsNullOrWhiteSpace(text)
                          && !(DataContext is MainViewModel vm && vm.IsPendingNewTab);

        AddressDisplayTextBlock.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        AddressTextBox.Foreground = shouldShow ? Brushes.Transparent : Brushes.White;

        if (!shouldShow)
            return;

        SplitAddressForDisplay(text, out string prefix, out string domain, out string suffix);
        AddressPrefixRun.Text = prefix;
        AddressDomainRun.Text = domain;
        AddressSuffixRun.Text = suffix;
    }

    private static void SplitAddressForDisplay(
        string address,
        out string prefix,
        out string domain,
        out string suffix)
    {
        prefix = address;
        domain = "";
        suffix = "";

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            return;

        int hostStart = address.IndexOf(uri.Host, StringComparison.OrdinalIgnoreCase);
        if (hostStart < 0)
            return;

        int grayHostPrefixLength = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? 4 : 0;
        int domainStart = hostStart + grayHostPrefixLength;
        int domainLength = uri.Host.Length - grayHostPrefixLength;

        prefix = address[..domainStart];
        domain = address.Substring(domainStart, domainLength);
        suffix = address[(domainStart + domainLength)..];
    }

    private void UrlBarBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddressTextBox.IsKeyboardFocused)
        {
            AddressTextBox.Focus();
            e.Handled = true;
        }
    }

    private void TryApplyInlineCompletion(
        string input,
        IReadOnlyList<Models.HistoryEntry> results,
        MainViewModel vm)
    {
        string trimmedInput = input.Trim();
        if (_suppressInlineCompletionUntilInsertion
            || trimmedInput.Length < 2
            || input.Contains(' ')
            || string.Equals(trimmedInput, _suppressedCompletionText, StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var result in results)
        {
            string displayText = GetCompletionDisplayText(result.Url, trimmedInput, out string navigationUrl);
            if (displayText.Length <= trimmedInput.Length
                || !displayText.StartsWith(trimmedInput, StringComparison.OrdinalIgnoreCase))
                continue;

            _inlineCompletionPrefix = trimmedInput;
            _inlineCompletionText = displayText;
            _inlineCompletionUrl = navigationUrl;

            _isApplyingInlineCompletion = true;
            try
            {
                vm.AddressBarText = displayText;
                AddressTextBox.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, displayText);
                AddressTextBox.Select(trimmedInput.Length, displayText.Length - trimmedInput.Length);
            }
            finally
            {
                _isApplyingInlineCompletion = false;
            }

            return;
        }
    }

    private static string GetCompletionDisplayText(string url, string input, out string navigationUrl)
    {
        navigationUrl = url;
        string displayText = url.Trim();
        string schemePrefix = "";
        string inputWithoutScheme = input;
        if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            schemePrefix = "https://";
            inputWithoutScheme = input[8..];
        }
        else if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            schemePrefix = "http://";
            inputWithoutScheme = input[7..];
        }

        bool inputContainsScheme = schemePrefix.Length > 0;
        bool inputTargetsHostOnly = !inputWithoutScheme.Contains('/')
                                    && !inputWithoutScheme.Contains('?')
                                    && !inputWithoutScheme.Contains('#');

        if (inputTargetsHostOnly
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            string host = uri.Authority;
            if (!inputWithoutScheme.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                && host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host[4..];

            string hostCompletion = schemePrefix + host;
            if (hostCompletion.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                navigationUrl = uri.GetLeftPart(UriPartial.Authority);
                return hostCompletion;
            }
        }

        if (!inputContainsScheme)
        {
            if (displayText.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                displayText = displayText[8..];
            else if (displayText.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                displayText = displayText[7..];

            if (!input.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                && displayText.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                displayText = displayText[4..];
        }

        if (displayText.EndsWith("/", StringComparison.Ordinal) && !input.EndsWith("/", StringComparison.Ordinal))
            displayText = displayText.TrimEnd('/');

        return displayText;
    }

    private bool HasActiveInlineCompletion()
    {
        return _inlineCompletionPrefix != null
               && _inlineCompletionText != null
               && _inlineCompletionUrl != null
               && string.Equals(AddressTextBox.Text, _inlineCompletionText, StringComparison.Ordinal)
               && AddressTextBox.SelectionStart == _inlineCompletionPrefix.Length
               && AddressTextBox.SelectionLength == _inlineCompletionText.Length - _inlineCompletionPrefix.Length;
    }

    private void AcceptInlineCompletion()
    {
        if (!HasActiveInlineCompletion())
            return;

        _acceptedCompletionText = _inlineCompletionText;
        _acceptedCompletionUrl = _inlineCompletionUrl;
        AddressTextBox.CaretIndex = AddressTextBox.Text.Length;
        AddressTextBox.SelectionLength = 0;
        ClearInlineCompletion();
        IsSuggestionsOpen = false;
    }

    private void RemoveInlineCompletion()
    {
        if (!HasActiveInlineCompletion() || _inlineCompletionPrefix == null)
            return;

        string prefix = _inlineCompletionPrefix;
        _suppressedCompletionText = prefix;
        _suppressInlineCompletionUntilInsertion = true;
        ClearInlineCompletion();
        ClearAcceptedCompletion();

        _isApplyingInlineCompletion = true;
        try
        {
            if (DataContext is MainViewModel vm)
                vm.AddressBarText = prefix;

            AddressTextBox.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, prefix);
            AddressTextBox.CaretIndex = prefix.Length;
            AddressTextBox.SelectionLength = 0;
        }
        finally
        {
            _isApplyingInlineCompletion = false;
        }

        AddressTextBox_TextChanged(AddressTextBox, null!);
    }

    private string? GetNavigableInlineCompletionUrl()
    {
        if (HasActiveInlineCompletion())
            return _inlineCompletionUrl;

        return string.Equals(AddressTextBox.Text, _acceptedCompletionText, StringComparison.Ordinal)
            ? _acceptedCompletionUrl
            : null;
    }

    private static bool TryGetDirectNavigationUrl(
        string input,
        out string navigationUrl,
        out string displayText)
    {
        navigationUrl = input;
        displayText = input;

        if (input.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || input.Contains("://", StringComparison.Ordinal))
            return true;

        if (input.Contains('.') && !input.Contains(' '))
        {
            navigationUrl = "https://" + input;
            return true;
        }

        return false;
    }

    internal static string BuildSearchUrl(MainViewModel vm, string query)
        => SearchEngineHelper.BuildSearchUrl(query, vm.AppSettingsService.Current.SearchEngine, vm.AppSettingsService.Current.CustomSearchUrl);

    private static bool UrlsMatch(string first, string second)
        => string.Equals(
            first.TrimEnd('/'),
            second.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static void NavigateToAddress(MainViewModel vm, string url)
    {
        if (vm.IsPendingNewTab)
            vm.AddTabWithUrl(url);
        else if (vm.SelectedTab != null)
            vm.SelectedTab.Url = url;
    }

    private void ClearInlineCompletion()
    {
        _inlineCompletionPrefix = null;
        _inlineCompletionText = null;
        _inlineCompletionUrl = null;
    }

    private void ClearAcceptedCompletion()
    {
        _acceptedCompletionText = null;
        _acceptedCompletionUrl = null;
    }
}
