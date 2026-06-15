using System.IO;
using System.Text.Json;
using System.Windows;
using CefSharp;
using CefSharp.SchemeHandler;
using CefSharp.Wpf.HwndHost;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Services.Browser;

public partial class BrowserService : IBrowserService, IDisposable
{
    private const string InternalSettingsUrl = "https://internals.mundobrowser/settings.html";

    private readonly Dictionary<TabViewModel, ChromiumWebBrowser> _browsers = [];
    private readonly Dictionary<TabViewModel, Task<ChromiumWebBrowser>> _initializationTasks = [];
    private readonly HashSet<TabViewModel> _removedTabs = [];
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly System.Timers.Timer _memoryTimer;
    private readonly IAppSettingsService _settingsService;
    private readonly IAdBlockerService _adBlockerService;

    private System.Windows.Controls.Panel? _container;
    private ChromiumWebBrowser? _activeBrowser;
    private IRequestContext? _requestContext;
    private int _memoryOptimizationRunning;
    private bool _disposed;

    public bool EcoModeEnabled { get; set; } = true;
    public int EcoModeMinutes { get; set; } = 10;
    public ChromiumWebBrowser? ActiveBrowser => _activeBrowser;
    public IRequestContext? RequestContext => _requestContext;

    public BrowserService(IAppSettingsService settingsService, IAdBlockerService adBlockerService)
    {
        _settingsService = settingsService;
        _adBlockerService = adBlockerService;
        EcoModeEnabled = settingsService.Current.EcoModeEnabled;
        EcoModeMinutes = settingsService.Current.EcoModeMinutes;

        _memoryTimer = new System.Timers.Timer(60000);
        _memoryTimer.Elapsed += CheckMemoryOptimization;
        _memoryTimer.Start();
    }

    public ChromiumWebBrowser? GetBrowserForTab(TabViewModel tab) =>
        _browsers.TryGetValue(tab, out var browser) ? browser : null;

    public Task InitializeAsync(System.Windows.Controls.Panel container)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _container = container;
        _requestContext = Cef.GetGlobalRequestContext()
            ?? throw new InvalidOperationException("The global CEF request context is unavailable.");

        string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Pages");
        if (!Directory.Exists(assetsPath))
            assetsPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Pages");

        _requestContext.RegisterSchemeHandlerFactory(
            "https",
            "internals.mundobrowser",
            new FolderSchemeHandlerFactory(
                assetsPath,
                "https",
                "internals.mundobrowser",
                "settings.html",
                FileShare.Read));

        ApplyChromiumPreferences();
        return Task.CompletedTask;
    }

    public async Task<ChromiumWebBrowser> GetOrCreateBrowserAsync(
        TabViewModel tab,
        Action<ChromiumWebBrowser> setupEvents)
    {
        Task<ChromiumWebBrowser> initTask;
        lock (_initializationTasks)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _removedTabs.Remove(tab);

            if (_browsers.TryGetValue(tab, out var existing))
                return existing;

            if (!_initializationTasks.TryGetValue(tab, out initTask!))
            {
                initTask = InitializeBrowserInternal(tab, setupEvents);
                _initializationTasks[tab] = initTask;
            }
        }

        try
        {
            return await initTask;
        }
        finally
        {
            lock (_initializationTasks)
            {
                if (_initializationTasks.TryGetValue(tab, out var activeTask)
                    && ReferenceEquals(activeTask, initTask))
                    _initializationTasks.Remove(tab);
            }
        }
    }

    private async Task<ChromiumWebBrowser> InitializeBrowserInternal(
        TabViewModel tab,
        Action<ChromiumWebBrowser> setupEvents)
    {
        if (_container == null || _requestContext == null)
            throw new InvalidOperationException("BrowserService must be initialized before use.");

        await _initSemaphore.WaitAsync();
        ChromiumWebBrowser? browser = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            browser = new ChromiumWebBrowser
            {
                RequestContext = _requestContext,
                Visibility = Visibility.Collapsed
            };

            browser.DownloadHandler = new BrowserDownloadHandler();
            browser.RequestHandler = new BrowserRequestHandler(
                _adBlockerService,
                tab.Url,
                uri => browser.Dispatcher.BeginInvoke(() => NavigateToSettings(browser, tab, uri)),
                () => browser.Dispatcher.BeginInvoke(() => browser.Reload()));

            browser.FrameLoadEnd += (_, args) =>
            {
                if (!args.Frame.IsMain)
                    return;

                string script = BuildDocumentEnhancementsScript(_adBlockerService, args.Url);
                if (!string.IsNullOrWhiteSpace(script))
                    args.Frame.ExecuteJavaScriptAsync(script);

                browser.Dispatcher.BeginInvoke(() => PostSettingsToPage(browser));
            };
            browser.JavascriptMessageReceived += (_, args) =>
                browser.Dispatcher.BeginInvoke(() => ProcessJavascriptMessage(args.Message, tab));

            setupEvents(browser);
            _container.Children.Add(browser);

            string initialUrl = NormalizeInitialUrl(tab);
            if (!string.IsNullOrWhiteSpace(initialUrl))
                browser.Load(initialUrl);

            lock (_initializationTasks)
            {
                if (_disposed || _removedTabs.Contains(tab))
                    throw new ObjectDisposedException(nameof(BrowserService));
            }

            _browsers[tab] = browser;
            return browser;
        }
        catch
        {
            if (browser != null)
            {
                _container?.Children.Remove(browser);
                browser.Dispose();
            }
            throw;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private void ProcessJavascriptMessage(object? message, TabViewModel tab)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(message));
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type))
                return;

            if (type.GetString() == "mediaPlaybackChanged")
            {
                bool playing = root.TryGetProperty("playing", out var playingProperty)
                               && playingProperty.GetBoolean();
                tab.IsPlayingAudio = playing;
                if (playing
                    && System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel mediaVm)
                {
                    mediaVm.ActiveMediaTab = tab;
                    mediaVm.IsMediaBarVisible = true;
                }
                return;
            }

            if (type.GetString() != "settingChanged")
                return;

            string? key = root.GetProperty("key").GetString();
            var value = root.GetProperty("value");
            if (key == "makeDefault")
            {
                WindowsBrowserRegistration.RegisterInstalledBrowser();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "ms-settings:defaultapps") { UseShellExecute = true });
            }
            else if (key == "subPage")
            {
                tab.AddressUrl = $"about:preferences#{value.GetString()}";
                if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel vm
                    && vm.SelectedTab == tab)
                    vm.AddressBarText = tab.AddressUrl;
            }
            else
            {
                ApplySettingChange(key, value);
                BroadcastSettingsToPages();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to process browser message: {ex.Message}");
        }
    }

    private static string NormalizeInitialUrl(TabViewModel tab)
    {
        if (!IsSettingsUrl(tab.Url))
            return tab.Url;

        string hash = GetSettingsHash(tab.Url);
        tab.AddressUrl = "about:preferences" + hash;
        return InternalSettingsUrl + hash;
    }

    private static void NavigateToSettings(ChromiumWebBrowser browser, TabViewModel tab, string uri)
    {
        string hash = GetSettingsHash(uri);
        tab.AddressUrl = "about:preferences" + hash;
        browser.Load(InternalSettingsUrl + hash);
    }

    internal static bool IsSettingsUrl(string? uri) =>
        !string.IsNullOrWhiteSpace(uri)
        && (uri.StartsWith("about:preferences", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("edge://preferences", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("chrome://settings", StringComparison.OrdinalIgnoreCase));

    private static string GetSettingsHash(string uri) =>
        uri.Contains('#') ? uri[uri.IndexOf('#')..] : "#general";

    private static string BuildDocumentEnhancementsScript(
        IAdBlockerService adBlocker,
        string? pageUrl)
    {
        string css = adBlocker.IsProtectionDisabledForSite(pageUrl)
            ? ""
            : adBlocker.GetCosmeticCss() + adBlocker.GetCookieCosmeticCss();
        string cookieScript = adBlocker.IsProtectionDisabledForSite(pageUrl)
            ? ""
            : adBlocker.GetCookieRemovalScript();
        string serializedCss = JsonSerializer.Serialize(css);
        string extensionStoreScript = BuildExtensionStoreIntegrationScript(pageUrl);

        return $$"""
            (() => {
                if (!window.__mundoMediaEventsInstalled) {
                    window.__mundoMediaEventsInstalled = true;
                    const reportPlayback = () => {
                        const playing = Array.from(document.querySelectorAll('video, audio'))
                            .some(media => !media.paused && !media.ended);
                        if (window.CefSharp?.PostMessage) {
                            window.CefSharp.PostMessage({
                                type: 'mediaPlaybackChanged',
                                playing
                            });
                        }
                    };
                    document.addEventListener('play', reportPlayback, true);
                    document.addEventListener('pause', reportPlayback, true);
                    document.addEventListener('ended', reportPlayback, true);
                    reportPlayback();
                }

                const adblockCss = {{serializedCss}};
                if (adblockCss && !document.getElementById('mundo-adblock-css')) {
                    const style = document.createElement('style');
                    style.id = 'mundo-adblock-css';
                    style.textContent = adblockCss;
                    (document.head || document.documentElement).appendChild(style);
                }
                {{cookieScript}}
                {{extensionStoreScript}}
            })();
            """;
    }

    public Task SwitchToTabAsync(TabViewModel tab, ChromiumWebBrowser browser)
    {
        if (_activeBrowser != null && _activeBrowser != browser)
            _activeBrowser.Visibility = Visibility.Collapsed;

        _activeBrowser = browser;
        browser.Visibility = Visibility.Visible;
        browser.ZoomLevel = ZoomFactorToLevel(tab.ZoomFactor);
        tab.LastAccessed = DateTime.Now;
        tab.IsDiscarded = false;
        return Task.CompletedTask;
    }

    public void RemoveTab(TabViewModel tab)
    {
        lock (_initializationTasks)
            _removedTabs.Add(tab);

        if (!_browsers.Remove(tab, out var browser))
            return;

        if (_activeBrowser == browser)
            _activeBrowser = null;
        _container?.Children.Remove(browser);
        browser.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _memoryTimer.Stop();
        _memoryTimer.Elapsed -= CheckMemoryOptimization;
        _memoryTimer.Dispose();

        var browsersToDispose = _browsers.Values
            .Concat(_container?.Children.OfType<ChromiumWebBrowser>() ?? [])
            .Distinct()
            .ToList();

        foreach (var browser in browsersToDispose)
        {
            _container?.Children.Remove(browser);
            browser.Dispose();
        }

        _browsers.Clear();
        _initializationTasks.Clear();
        _removedTabs.Clear();
        _activeBrowser = null;
        _requestContext = null;
        _container = null;
        GC.SuppressFinalize(this);
    }

    internal static double ZoomFactorToLevel(double factor) =>
        Math.Log(Math.Max(0.25, factor)) / Math.Log(1.2);
}
