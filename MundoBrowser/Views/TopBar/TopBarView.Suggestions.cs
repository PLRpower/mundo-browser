using System.Windows;
using System.Windows.Input;
using MundoBrowser.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MundoBrowser;

public partial class TopBarView
{
    private void PopulateSuggestions(
        string input,
        IReadOnlyList<Models.HistoryEntry> results,
        MainViewModel vm)
    {
        _suggestionFaviconsCts?.Cancel();
        _suggestionFaviconsCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _suggestionFaviconsCts.Token;

        string trimmedInput = input.Trim();
        vm.Suggestions.Clear();

        string? directUrl = null;
        string? directDisplayText = null;
        if (_inlineCompletionUrl != null && _inlineCompletionText != null)
        {
            directUrl = _inlineCompletionUrl;
            directDisplayText = _inlineCompletionText;
        }
        else if (TryGetDirectNavigationUrl(trimmedInput, out var typedUrl, out var typedDisplayText))
        {
            directUrl = typedUrl;
            directDisplayText = typedDisplayText;
        }

        if (directUrl != null && directDisplayText != null)
        {
            vm.Suggestions.Add(new Models.HistoryEntry
            {
                Title = directDisplayText,
                Url = directUrl,
                FaviconUrl = vm.FaviconService.GetCachedFaviconUrlForPage(directUrl),
                VisitCount = -2
            });
        }

        vm.Suggestions.Add(new Models.HistoryEntry
        {
            Title = trimmedInput,
            Url = trimmedInput,
            VisitCount = -1
        });

        foreach (var result in results)
        {
            if (directUrl != null && UrlsMatch(result.Url, directUrl))
                continue;

            vm.Suggestions.Add(new Models.HistoryEntry
            {
                Title = result.Title,
                Url = result.Url,
                FaviconUrl = vm.FaviconService.GetCachedFaviconUrlForPage(result.Url),
                VisitedAt = result.VisitedAt,
                VisitCount = result.VisitCount
            });
            if (vm.Suggestions.Count >= 8)
                break;
        }

        _ = LoadMissingSuggestionFaviconsAsync(vm, vm.Suggestions.ToList(), cancellationToken);
    }

    private static async Task LoadMissingSuggestionFaviconsAsync(
        MainViewModel vm,
        IReadOnlyList<Models.HistoryEntry> suggestions,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(180, cancellationToken);

            foreach (var suggestion in suggestions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (suggestion.VisitCount == -1 || suggestion.FaviconUrl != null)
                    continue;

                suggestion.FaviconUrl = vm.FaviconService.GetFaviconUrlForPage(suggestion.Url);
                await Task.Delay(25, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string GetSuggestionNavigationUrl(MainViewModel vm, Models.HistoryEntry entry)
        => entry.VisitCount == -1 ? BuildSearchUrl(vm, entry.Url) : entry.Url;

    private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var clickedElement = e.OriginalSource as DependencyObject;
        var clickedItem = clickedElement == null
            ? null
            : System.Windows.Controls.ItemsControl.ContainerFromElement(SuggestionsListBox, clickedElement)
                as System.Windows.Controls.ListBoxItem;

        if (clickedItem?.DataContext is Models.HistoryEntry entry && DataContext is MainViewModel vm)
        {
            SuggestionsListBox.SelectedItem = entry;
            NavigateToAddress(vm, GetSuggestionNavigationUrl(vm, entry));
            IsSuggestionsOpen = false;
            GetWebView()?.Focus();
            e.Handled = true;
        }
    }

    private void NavigateSuggestion(int direction)
    {
        if (!IsSuggestionsOpen || SuggestionsListBox.Items.Count == 0)
            return;

        int currentIndex = SuggestionsListBox.SelectedIndex;
        int nextIndex;

        if (direction > 0)
        {
            if (currentIndex < 0)
                nextIndex = 0;
            else
                nextIndex = Math.Min(currentIndex + 1, SuggestionsListBox.Items.Count - 1);
        }
        else
        {
            nextIndex = currentIndex - 1;
        }

        if (nextIndex < 0)
        {
            SuggestionsListBox.SelectedIndex = -1;
            if (_userTypedText != null)
            {
                ApplySuggestionTextToAddressBar(_userTypedText);
            }
            return;
        }

        SuggestionsListBox.SelectedIndex = nextIndex;
        SuggestionsListBox.ScrollIntoView(SuggestionsListBox.SelectedItem);

        if (SuggestionsListBox.SelectedItem is Models.HistoryEntry entry)
        {
            string displayText = GetSuggestionDisplayText(entry);
            ApplySuggestionTextToAddressBar(displayText);
        }
    }

    private string GetSuggestionDisplayText(Models.HistoryEntry entry)
    {
        if (entry.VisitCount == -1 || entry.VisitCount == -2)
            return entry.Title;

        return FormatUrlForDisplay(entry.Url, _userTypedText);
    }

    private static string FormatUrlForDisplay(string url, string? typedInput)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        string displayText = url.Trim();
        bool inputContainsScheme = typedInput != null &&
            (typedInput.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             typedInput.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

        if (!inputContainsScheme)
        {
            if (displayText.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                displayText = displayText[8..];
            else if (displayText.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                displayText = displayText[7..];

            bool inputStartsWithWww = typedInput != null && typedInput.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
            if (!inputStartsWithWww && displayText.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                displayText = displayText[4..];
        }

        if (displayText.EndsWith('/') && (typedInput == null || !typedInput.EndsWith('/')))
            displayText = displayText.TrimEnd('/');

        return displayText;
    }

    private void ApplySuggestionTextToAddressBar(string text)
    {
        ClearInlineCompletion();
        ClearAcceptedCompletion();
        _suppressedCompletionText = null;
        _suppressInlineCompletionUntilInsertion = true;

        _isUpdatingAddressBar = true;
        try
        {
            if (DataContext is MainViewModel vm)
                vm.AddressBarText = text;

            AddressTextBox.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, text);
            AddressTextBox.CaretIndex = text.Length;
            AddressTextBox.SelectionLength = 0;
            var scrollViewer = GetDescendantByType<System.Windows.Controls.ScrollViewer>(AddressTextBox);
            scrollViewer?.ScrollToRightEnd();
        }
        finally
        {
            _isUpdatingAddressBar = false;
        }

        UpdateAddressDisplay();
    }

    private void SuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseAddressBar();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter
                 && SuggestionsListBox.SelectedItem is Models.HistoryEntry entry
                 && DataContext is MainViewModel vm)
        {
            NavigateToAddress(vm, GetSuggestionNavigationUrl(vm, entry));
            IsSuggestionsOpen = false;
            GetWebView()?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            NavigateSuggestion(1);
            AddressTextBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            NavigateSuggestion(-1);
            AddressTextBox.Focus();
            e.Handled = true;
        }
    }
}
