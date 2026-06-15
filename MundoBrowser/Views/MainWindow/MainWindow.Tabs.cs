using System.Collections.Specialized;
using CefSharp;
using CefSharp.Wpf.HwndHost;
using MundoBrowser.Services.Browser;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private void InitializeEvents(MainViewModel vm)
    {
        _viewModel = vm;
        vm.PropertyChanged += MainViewModel_PropertyChanged;
        vm.Tabs.CollectionChanged += Tabs_CollectionChanged;
        foreach (var pinnedTab in vm.PinnedTabs)
            pinnedTab.PropertyChanged += PinnedTab_PropertyChanged;

        SynchronizeTrackedTabs();
        vm.NewTabRequested += MainViewModel_NewTabRequested;
        vm.MediaActionRequested += OnMediaActionRequested;
    }

    private async Task SwitchToTabAsync(TabViewModel tab)
    {
        if (!_trackedTabs.Contains(tab))
            return;

        int switchVersion = Interlocked.Increment(ref _tabSwitchVersion);
        var browser = await _browserService.GetOrCreateBrowserAsync(
            tab,
            instance => SetupBrowserEvents(instance, tab));

        if (switchVersion != Volatile.Read(ref _tabSwitchVersion)
            || _viewModel?.SelectedTab != tab
            || !_trackedTabs.Contains(tab))
            return;

        await _browserService.SwitchToTabAsync(tab, browser);
        if (_viewModel is { } vm)
        {
            TopBarControl?.SetAddressBarText(tab.AddressUrl);
            vm.AddressBarText = tab.AddressUrl;
        }
    }

    private async void OnTabPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TabViewModel.Url) || sender is not TabViewModel tab)
            return;

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnTabPropertyChanged(sender, e));
            return;
        }

        if (!_trackedTabs.Contains(tab))
            return;

        try
        {
            var browser = await _browserService.GetOrCreateBrowserAsync(
                tab,
                instance => SetupBrowserEvents(instance, tab));
            if (_trackedTabs.Contains(tab)
                && !string.Equals(browser.Address, tab.Url, StringComparison.OrdinalIgnoreCase))
                browser.Load(tab.Url);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async void MainViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is not { } vm)
            return;

        if (e.PropertyName == nameof(MainViewModel.SelectedTab) && vm.SelectedTab != null)
        {
            try
            {
                await SwitchToTabAsync(vm.SelectedTab);
            }
            catch (ObjectDisposedException)
            {
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible))
        {
            UpdateSidebarWidth(vm.IsSidebarVisible);
        }
        else if (e.PropertyName == nameof(MainViewModel.SidebarWidth) && vm.IsSidebarVisible)
        {
            UpdateSidebarWidth(visible: true);
        }
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        SynchronizeTrackedTabs();

    private void PinnedTab_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.PinnedTab.Tab))
            SynchronizeTrackedTabs();
    }

    private void SynchronizeTrackedTabs()
    {
        if (_viewModel == null)
            return;

        var currentTabs = _viewModel.Tabs
            .Concat(_viewModel.PinnedTabs.Where(pinned => pinned.Tab != null).Select(pinned => pinned.Tab!))
            .ToHashSet();

        foreach (var removedTab in _trackedTabs.Except(currentTabs).ToList())
        {
            removedTab.PropertyChanged -= OnTabPropertyChanged;
            _browserService.RemoveTab(removedTab);
            _trackedTabs.Remove(removedTab);
        }

        foreach (var addedTab in currentTabs.Except(_trackedTabs))
        {
            addedTab.PropertyChanged += OnTabPropertyChanged;
            _trackedTabs.Add(addedTab);
        }
    }

    private void MainViewModel_NewTabRequested(object? sender, EventArgs e)
    {
        TopBarControl.AddressBar.Focus();
        TopBarControl.AddressBar.SelectAll();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _globalMediaTimer.Stop();
        _globalMediaTimer.Tick -= UpdateActiveMediaInfo;
        CloseExtensionPopup();
        DisposeTrayIcon();

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= MainViewModel_PropertyChanged;
            _viewModel.Tabs.CollectionChanged -= Tabs_CollectionChanged;
            _viewModel.NewTabRequested -= MainViewModel_NewTabRequested;
            _viewModel.MediaActionRequested -= OnMediaActionRequested;
            foreach (var pinnedTab in _viewModel.PinnedTabs)
                pinnedTab.PropertyChanged -= PinnedTab_PropertyChanged;
        }

        foreach (var tab in _trackedTabs)
            tab.PropertyChanged -= OnTabPropertyChanged;
        _trackedTabs.Clear();

        if (_browserService is IDisposable disposable)
            disposable.Dispose();

        if (_restartRequested)
            App.RestartAfterCurrentProcessExits();
    }

    private void SetupBrowserEvents(ChromiumWebBrowser browser, TabViewModel tab)
    {
        browser.DisplayHandler = new BrowserDisplayHandler(
            _ => Dispatcher.BeginInvoke(async () =>
            {
                if (_viewModel is { } vm)
                    await vm.FaviconService.ResolveFaviconAsync(browser, tab, forceReload: true);
            }),
            fullscreen => Dispatcher.BeginInvoke(() => SetFullscreen(fullscreen, true)));

        browser.LifeSpanHandler = new BrowserLifeSpanHandler(
            url => Dispatcher.BeginInvoke(() => _viewModel?.AddTabWithUrl(url)),
            () => Dispatcher.BeginInvoke(() => _viewModel?.CloseTab(tab)));

        browser.JavascriptMessageReceived += (_, args) =>
            Dispatcher.BeginInvoke(async () =>
                await HandleExtensionStoreMessageAsync(browser, args.Message));

        browser.LoadingStateChanged += (_, args) =>
            Dispatcher.BeginInvoke(() =>
            {
                tab.CanGoBack = args.CanGoBack;
                tab.CanGoForward = args.CanGoForward;
                tab.IsLoading = args.IsLoading;
                if (!args.IsLoading)
                    OnBrowserLoadCompleted(browser, tab);
            });

        browser.FrameLoadStart += (_, args) =>
        {
            if (args.Frame.IsMain)
                Dispatcher.BeginInvoke(async () =>
                    await OnBrowserAddressChanged(browser, tab, args.Url));
        };

        browser.TitleChanged += (_, args) =>
            Dispatcher.BeginInvoke(() =>
            {
                string? title = args.NewValue as string;
                tab.Title = string.IsNullOrWhiteSpace(title) ? tab.Url : title;
                if (_viewModel?.SelectedTab == tab)
                    UpdateTitle();
            });
    }

    private void OnBrowserLoadCompleted(ChromiumWebBrowser browser, TabViewModel tab)
    {
        if (DataContext is not MainViewModel vm)
            return;

        string source = browser.Address;
        if (source.Contains("internals.mundobrowser", StringComparison.OrdinalIgnoreCase))
        {
            if (source.Contains("settings.html", StringComparison.OrdinalIgnoreCase))
            {
                string version = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString(3) ?? "1.0.0";
                browser.ExecuteScriptAsync(
                    $"if(document.getElementById('app-version')) document.getElementById('app-version').innerText = 'Version {version} (Build stable)';");
            }
        }
        else if (!string.IsNullOrWhiteSpace(source))
        {
            tab.Url = source;
        }

        vm.HistoryManager.AddEntry(tab.Url, browser.Title);
        UpdateTitle();
    }

    private async Task OnBrowserAddressChanged(
        ChromiumWebBrowser browser,
        TabViewModel tab,
        string source)
    {
        if (source.Contains("internals.mundobrowser", StringComparison.OrdinalIgnoreCase))
        {
            string hash = source.Contains('#') ? source[source.IndexOf('#')..] : "#general";
            tab.AddressUrl = "about:preferences" + hash;
        }
        else if (!string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            tab.Url = tab.AddressUrl = source;
        }

        if (_viewModel is { } vm && vm.SelectedTab == tab)
        {
            if (TopBarControl?.AddressBar.IsFocused == false)
            {
                TopBarControl.SetAddressBarText(tab.AddressUrl);
                vm.AddressBarText = tab.AddressUrl;
            }
        }

        if (_viewModel is { } mainVm)
            await mainVm.FaviconService.ResolveFaviconAsync(browser, tab);
    }

    private void UpdateTitle()
    {
        if (_browserService.ActiveBrowser == null || _viewModel?.SelectedTab is not { } selectedTab)
            return;

        string title = _browserService.ActiveBrowser.Title;
        selectedTab.Title = string.IsNullOrWhiteSpace(title) ? selectedTab.Url : title;
    }
}
