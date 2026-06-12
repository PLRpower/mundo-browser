using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class TopBarView : System.Windows.Controls.UserControl
{
    private CancellationTokenSource? _zoomIndicatorCts;
    private bool _isUpdatingAddressBar;
    private bool _isApplyingInlineCompletion;
    private bool _suppressInlineCompletionUntilInsertion;
    private string? _inlineCompletionPrefix;
    private string? _inlineCompletionText;
    private string? _inlineCompletionUrl;
    private string? _acceptedCompletionText;
    private string? _acceptedCompletionUrl;
    private string? _suppressedCompletionText;
    private MainViewModel? _mainViewModel;
    private TabViewModel? _observedZoomTab;

    public System.Windows.Controls.TextBox AddressBar => AddressTextBox;

    public static readonly DependencyProperty IsSuggestionsOpenProperty =
        DependencyProperty.Register(nameof(IsSuggestionsOpen), typeof(bool), typeof(TopBarView), new PropertyMetadata(false));

    public bool IsSuggestionsOpen
    {
        get => (bool)GetValue(IsSuggestionsOpenProperty);
        set => SetValue(IsSuggestionsOpenProperty, value);
    }

    public System.Windows.Controls.Primitives.Popup SuggestionsPopupControl => SuggestionsPopup;

    public void SetAddressBarText(string text)
    {
        ClearInlineCompletion();
        ClearAcceptedCompletion();
        _suppressedCompletionText = null;
        _suppressInlineCompletionUntilInsertion = false;
        _isUpdatingAddressBar = true;
        AddressTextBox.Text = text;
        _isUpdatingAddressBar = false;
    }

    public TopBarView()
    {
        InitializeComponent();
        DataContextChanged += TopBarView_DataContextChanged;
        Loaded += (_, _) => AttachZoomIndicatorObservers();
        Unloaded += (_, _) => DetachZoomIndicatorObservers();
    }

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

    private void TopBarView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachZoomIndicatorObservers();
        AttachZoomIndicatorObservers();
    }

    private void AttachZoomIndicatorObservers()
    {
        if (_mainViewModel != null) return;

        _mainViewModel = DataContext as MainViewModel;
        if (_mainViewModel == null) return;

        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
        ObserveSelectedTab(showTransientAtDefaultZoom: false);
    }

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            ObserveSelectedTab(showTransientAtDefaultZoom: false);
    }

    private void ObserveSelectedTab(bool showTransientAtDefaultZoom)
    {
        if (_observedZoomTab != null)
            _observedZoomTab.PropertyChanged -= ObservedZoomTab_PropertyChanged;

        _observedZoomTab = _mainViewModel?.SelectedTab;
        if (_observedZoomTab != null)
            _observedZoomTab.PropertyChanged += ObservedZoomTab_PropertyChanged;

        UpdateZoomIndicator(showTransientAtDefaultZoom);
    }

    private void ObservedZoomTab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.ZoomFactor))
            UpdateZoomIndicator(showTransientAtDefaultZoom: true);
    }

    private void UpdateZoomIndicator(bool showTransientAtDefaultZoom)
    {
        _zoomIndicatorCts?.Cancel();
        ZoomIndicatorButton.BeginAnimation(OpacityProperty, null);
        ZoomIndicatorButton.Opacity = 1;

        if (_observedZoomTab == null)
        {
            ZoomIndicatorButton.Visibility = Visibility.Collapsed;
            return;
        }

        bool isZoomedOut = _observedZoomTab.ZoomFactor < 1.0 - 0.001;
        bool isDefaultZoom = Math.Abs(_observedZoomTab.ZoomFactor - 1.0) <= 0.001;
        ZoomOutIcon.Visibility = isZoomedOut ? Visibility.Visible : Visibility.Collapsed;
        ZoomInIcon.Visibility = isZoomedOut ? Visibility.Collapsed : Visibility.Visible;

        if (!isDefaultZoom)
        {
            ZoomIndicatorButton.Visibility = Visibility.Visible;
            return;
        }

        if (!showTransientAtDefaultZoom)
        {
            ZoomIndicatorButton.Visibility = Visibility.Collapsed;
            return;
        }

        ZoomIndicatorButton.Visibility = Visibility.Visible;
        _zoomIndicatorCts = new CancellationTokenSource();
        _ = FadeOutZoomIndicatorAsync(_zoomIndicatorCts.Token);
    }

    private async Task FadeOutZoomIndicatorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fadeOut.Completed += (_, _) =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    ZoomIndicatorButton.Visibility = Visibility.Collapsed;
            };
            ZoomIndicatorButton.BeginAnimation(OpacityProperty, fadeOut);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DetachZoomIndicatorObservers()
    {
        _zoomIndicatorCts?.Cancel();
        if (_observedZoomTab != null)
            _observedZoomTab.PropertyChanged -= ObservedZoomTab_PropertyChanged;
        if (_mainViewModel != null)
            _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;

        _observedZoomTab = null;
        _mainViewModel = null;
    }

    // Address Bar Logic
    private void AddressBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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
            if (string.IsNullOrWhiteSpace(input)) return;

            string url = GetNavigableInlineCompletionUrl()
                         ?? (TryGetDirectNavigationUrl(input, out var directUrl, out _)
                             ? directUrl
                             : BuildGoogleSearchUrl(input));

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
        if (_isUpdatingAddressBar || _isApplyingInlineCompletion || DataContext is not MainViewModel vm) return;

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
        
        if (!AddressTextBox.IsFocused || string.IsNullOrWhiteSpace(text))
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

    private void TryApplyInlineCompletion(
        string input,
        IReadOnlyList<Models.HistoryEntry> results,
        MainViewModel vm)
    {
        string trimmedInput = input.Trim();
        if (_suppressInlineCompletionUntilInsertion
            || trimmedInput.Length < 2
            || trimmedInput.Contains(' ')
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
        if (!HasActiveInlineCompletion()) return;

        _acceptedCompletionText = _inlineCompletionText;
        _acceptedCompletionUrl = _inlineCompletionUrl;
        AddressTextBox.CaretIndex = AddressTextBox.Text.Length;
        AddressTextBox.SelectionLength = 0;
        ClearInlineCompletion();
        IsSuggestionsOpen = false;
    }

    private void RemoveInlineCompletion()
    {
        if (!HasActiveInlineCompletion() || _inlineCompletionPrefix == null) return;

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

    private void PopulateSuggestions(
        string input,
        IReadOnlyList<Models.HistoryEntry> results,
        MainViewModel vm)
    {
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

            vm.Suggestions.Add(result);
            if (vm.Suggestions.Count >= 8)
                break;
        }
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

    private static string BuildGoogleSearchUrl(string query)
        => $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";

    private static bool UrlsMatch(string first, string second)
        => string.Equals(
            first.TrimEnd('/'),
            second.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static string GetSuggestionNavigationUrl(Models.HistoryEntry entry)
        => entry.VisitCount == -1 ? BuildGoogleSearchUrl(entry.Url) : entry.Url;

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

    private void SuggestionsList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsSuggestionsOpen = false;
            AddressTextBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (SuggestionsListBox.SelectedItem is Models.HistoryEntry entry && DataContext is MainViewModel vm)
            {
                NavigateToAddress(vm, GetSuggestionNavigationUrl(entry));
                IsSuggestionsOpen = false;
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
