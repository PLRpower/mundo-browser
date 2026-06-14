using System.Windows;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class TopBarView : System.Windows.Controls.UserControl
{
    private CancellationTokenSource? _zoomIndicatorCts;
    private CancellationTokenSource? _suggestionFaviconsCts;
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
        UpdateAddressDisplay();
    }

    public TopBarView()
    {
        InitializeComponent();
        DataContextChanged += TopBarView_DataContextChanged;
        Loaded += (_, _) =>
        {
            AttachZoomIndicatorObservers();
            UpdateAddressDisplay();
        };
        Unloaded += (_, _) =>
        {
            _suggestionFaviconsCts?.Cancel();
            DetachZoomIndicatorObservers();
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e) => GetWebView()?.GoBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => GetWebView()?.GoForward();
    private void Reload_Click(object sender, RoutedEventArgs e) => GetWebView()?.Reload();

    private void AdBlockerButton_Click(object sender, RoutedEventArgs e)
    {
        AdBlockerContextMenu.PlacementTarget = AdBlockerButton;
        AdBlockerContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AdBlockerContextMenu.IsOpen = true;
    }

    private void AdBlockerContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var service = (DataContext as MainViewModel)?.AdBlockerService;
        string? url = (DataContext as MainViewModel)?.SelectedTab?.AddressUrl;
        string? host = service?.GetSiteHost(url);

        CurrentSiteProtectionMenuItem.IsEnabled = host != null;
        CurrentSiteProtectionMenuItem.Header = host == null
            ? "Indisponible sur cette page"
            : service!.IsProtectionDisabledForSite(url)
                ? $"Réactiver sur {host}"
                : $"Désactiver sur {host}";
    }

    private void ToggleCurrentSiteProtection_Click(object sender, RoutedEventArgs e)
    {
        var service = (DataContext as MainViewModel)?.AdBlockerService;
        string? url = (DataContext as MainViewModel)?.SelectedTab?.AddressUrl;
        if (service == null || service.GetSiteHost(url) == null) return;

        bool disable = !service.IsProtectionDisabledForSite(url);
        if (service.SetProtectionDisabledForSite(url, disable))
            GetWebView()?.Reload();
    }

    private Microsoft.Web.WebView2.Wpf.WebView2? GetWebView()
    {
        var mw = Window.GetWindow(this) as MainWindow;
        return mw?.GetActiveWebView();
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
