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

    private static string GetSuggestionNavigationUrl(Models.HistoryEntry entry)
        => entry.VisitCount == -1 ? BuildGoogleSearchUrl(entry.Url) : entry.Url;

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
            NavigateToAddress(vm, GetSuggestionNavigationUrl(entry));
            IsSuggestionsOpen = false;
            GetWebView()?.Focus();
            e.Handled = true;
        }
    }

    private void SuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsSuggestionsOpen = false;
            AddressTextBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter
                 && SuggestionsListBox.SelectedItem is Models.HistoryEntry entry
                 && DataContext is MainViewModel vm)
        {
            NavigateToAddress(vm, GetSuggestionNavigationUrl(entry));
            IsSuggestionsOpen = false;
            GetWebView()?.Focus();
            e.Handled = true;
        }
    }
}
