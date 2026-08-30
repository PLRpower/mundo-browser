using System.Windows;
using System.Windows.Input;
using MundoBrowser.Models;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public enum FloatingTopBarZone
{
    None,
    Left,
    Center,
    Right,
    All
}

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

    public static readonly DependencyProperty IsFloatingModeProperty =
        DependencyProperty.Register(nameof(IsFloatingMode), typeof(bool), typeof(TopBarView), new PropertyMetadata(false));

    public bool IsFloatingMode
    {
        get => (bool)GetValue(IsFloatingModeProperty);
        set => SetValue(IsFloatingModeProperty, value);
    }

    internal static void AnimateElementVisibility(FrameworkElement? element, bool show, bool animated = true, bool useHidden = false)
    {
        if (element == null) return;

        var invisibleState = useHidden ? Visibility.Hidden : Visibility.Collapsed;

        if (!animated)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = show ? 1.0 : 0.0;
            element.Visibility = show ? Visibility.Visible : invisibleState;
            element.IsHitTestVisible = show;
            return;
        }

        if (show)
        {
            element.Visibility = Visibility.Visible;
            element.IsHitTestVisible = true;

            var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };

            element.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }
        else
        {
            element.IsHitTestVisible = false;

            var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };

            fadeAnim.Completed += (s, e) =>
            {
                if (element.Opacity <= 0.05 && !element.IsHitTestVisible)
                {
                    element.Visibility = invisibleState;
                }
            };

            element.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }
    }

    public void SetVisibleSection(FloatingTopBarZone zone, bool animate = true)
    {
        bool showLeft = zone == FloatingTopBarZone.Left || zone == FloatingTopBarZone.All;
        bool showCenter = zone == FloatingTopBarZone.Center || zone == FloatingTopBarZone.All;
        bool showRight = zone == FloatingTopBarZone.Right || zone == FloatingTopBarZone.All;

        if (!IsFloatingMode)
        {
            AnimateElementVisibility(NavButtonsContainer, true, animated: false);
            AnimateElementVisibility(UrlBarBorder, true, animated: false);
            AnimateElementVisibility(ActionsContainer, true, animated: false);
            return;
        }

        if (GetMainWindow() is MainWindow mw && mw.DataContext is MainViewModel vm && vm.IsSidebarVisible && !mw.IsFullscreen)
        {
            showLeft = false;
        }

        AnimateElementVisibility(NavButtonsContainer, showLeft, animated: animate);
        AnimateElementVisibility(UrlBarBorder, showCenter, animated: animate);
        AnimateElementVisibility(ActionsContainer, showRight, animated: animate);
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

        if (service == null || host == null)
        {
            CurrentSiteHeaderMenuItem.Header = "Protections indisponibles";
            CurrentSiteAdBlockerMenuItem.IsEnabled = false;
            CurrentSiteCookieBlockerMenuItem.IsEnabled = false;
            CurrentSiteProtectionMenuItem.IsEnabled = false;
            CurrentSiteProtectionMenuItem.Header = "Indisponible sur cette page";
            return;
        }

        CurrentSiteHeaderMenuItem.Header = $"Protections pour {host}";

        bool adBlockEnabled = service.IsAdBlockerEnabledForSite(url);
        bool cookieBlockEnabled = service.IsCookieBlockerEnabledForSite(url);

        CurrentSiteAdBlockerMenuItem.IsEnabled = true;
        CurrentSiteAdBlockerMenuItem.IsChecked = adBlockEnabled;

        CurrentSiteCookieBlockerMenuItem.IsEnabled = true;
        CurrentSiteCookieBlockerMenuItem.IsChecked = cookieBlockEnabled;

        bool allDisabled = !adBlockEnabled && !cookieBlockEnabled;
        CurrentSiteProtectionMenuItem.IsEnabled = true;
        CurrentSiteProtectionMenuItem.Header = allDisabled
            ? $"Réactiver toutes les protections sur {host}"
            : $"Désactiver toutes les protections sur {host}";
    }

    private void ToggleCurrentSiteAdBlocker_Click(object sender, RoutedEventArgs e)
    {
        var service = (DataContext as MainViewModel)?.AdBlockerService;
        string? url = (DataContext as MainViewModel)?.SelectedTab?.AddressUrl;
        if (service == null || service.GetSiteHost(url) == null) return;

        bool currentStatus = service.IsAdBlockerEnabledForSite(url);
        if (service.SetAdBlockerEnabledForSite(url, !currentStatus))
        {
            GetWebView()?.Reload();
        }
    }

    private void ToggleCurrentSiteCookieBlocker_Click(object sender, RoutedEventArgs e)
    {
        var service = (DataContext as MainViewModel)?.AdBlockerService;
        string? url = (DataContext as MainViewModel)?.SelectedTab?.AddressUrl;
        if (service == null || service.GetSiteHost(url) == null) return;

        bool currentStatus = service.IsCookieBlockerEnabledForSite(url);
        if (service.SetCookieBlockerEnabledForSite(url, !currentStatus))
        {
            GetWebView()?.Reload();
        }
    }

    private void ToggleCurrentSiteProtection_Click(object sender, RoutedEventArgs e)
    {
        var service = (DataContext as MainViewModel)?.AdBlockerService;
        string? url = (DataContext as MainViewModel)?.SelectedTab?.AddressUrl;
        if (service == null || service.GetSiteHost(url) == null) return;

        bool allDisabled = !service.IsAdBlockerEnabledForSite(url) && !service.IsCookieBlockerEnabledForSite(url);
        bool newStatus = allDisabled; // If all disabled, re-enable all (true). Otherwise disable all (false).

        service.SetAdBlockerEnabledForSite(url, newStatus);
        service.SetCookieBlockerEnabledForSite(url, newStatus);
        GetWebView()?.Reload();
    }

    private int _openExtensionContextMenuCount;

    private void ExtensionContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _openExtensionContextMenuCount++;
    }

    private void ExtensionContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _openExtensionContextMenuCount = Math.Max(0, _openExtensionContextMenuCount - 1);
    }

    private void ExtensionButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
        {
            e.Handled = true;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    public bool IsAnyMenuOrPopupOpen =>
        IsSuggestionsOpen ||
        _openExtensionContextMenuCount > 0 ||
        (AdBlockerContextMenu != null && AdBlockerContextMenu.IsOpen) ||
        (SiteDataContextMenu != null && SiteDataContextMenu.IsOpen) ||
        (UpdateContextMenu != null && UpdateContextMenu.IsOpen);

    private MainWindow? GetMainWindow()
    {
        return Window.GetWindow(this) as MainWindow ?? System.Windows.Application.Current.MainWindow as MainWindow;
    }

    private Microsoft.Web.WebView2.Wpf.WebView2? GetWebView()
    {
        return GetMainWindow()?.GetActiveWebView();
    }

    private void ExtensionIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string id && GetMainWindow() is MainWindow mw)
            mw.ShowExtensionPopup(id, btn);
    }

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        string? id = null;
        if (sender is System.Windows.Controls.MenuItem mi)
        {
            if (mi.Tag is string tagId && !string.IsNullOrEmpty(tagId))
                id = tagId;
            else if (mi.DataContext is ExtensionInfo extInfo)
                id = extInfo.Id;
            else if (mi.Parent is System.Windows.Controls.ContextMenu cm && cm.PlacementTarget is System.Windows.Controls.Button btn)
            {
                if (btn.Tag is string btnTag && !string.IsNullOrEmpty(btnTag))
                    id = btnTag;
                else if (btn.DataContext is ExtensionInfo btnExtInfo)
                    id = btnExtInfo.Id;
            }
        }

        if (string.IsNullOrEmpty(id)) return;

        if (GetMainWindow() is MainWindow mw)
        {
            await mw.UninstallExtensionAsync(id);
        }
        else if (DataContext is MainViewModel vm)
        {
            var ext = vm.InstalledExtensions.FirstOrDefault(x => x.Id == id || (!string.IsNullOrEmpty(x.StoreId) && x.StoreId == id));
            if (ext != null) vm.InstalledExtensions.Remove(ext);
        }
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateContextMenu.PlacementTarget = UpdateButton;
        UpdateContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        UpdateContextMenu.IsOpen = true;
    }
}
