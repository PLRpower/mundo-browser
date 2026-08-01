using System.Collections.Specialized;
using Microsoft.Web.WebView2.Wpf;
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
        vm.TabDragCompleted += MainViewModel_TabDragCompleted;
    }

    private async Task SwitchToTabAsync(TabViewModel tab)
    {
        if (!_trackedTabs.Contains(tab))
            return;

        int switchVersion = Interlocked.Increment(ref _tabSwitchVersion);

        var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
        if (switchVersion != Volatile.Read(ref _tabSwitchVersion)
            || _viewModel?.SelectedTab != tab
            || !_trackedTabs.Contains(tab))
            return;

        await _webViewService.SwitchToTabAsync(tab, webView);

        if (_viewModel is { } vm)
        {
            TopBarControl?.SetAddressBarText(tab.AddressUrl);
            vm.AddressBarText = tab.AddressUrl;
        }
    }

    private async void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
            var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
            if (_trackedTabs.Contains(tab) && webView.CoreWebView2.Source != tab.Url)
                webView.CoreWebView2.Navigate(tab.Url);
        }
        catch (ObjectDisposedException)
        {
            // The tab was closed while its WebView was being initialized.
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
                // The selected tab changed again while its WebView was initializing.
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

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => SynchronizeTrackedTabs();

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
            _webViewService.RemoveTab(removedTab);
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
            _viewModel.TabDragCompleted -= MainViewModel_TabDragCompleted;
            foreach (var pinnedTab in _viewModel.PinnedTabs)
                pinnedTab.PropertyChanged -= PinnedTab_PropertyChanged;
        }

        foreach (var tab in _trackedTabs)
            tab.PropertyChanged -= OnTabPropertyChanged;
        _trackedTabs.Clear();

        if (_webViewService is IDisposable disposableWebViewService)
            disposableWebViewService.Dispose();
    }

    private void SetupWebViewEvents(WebView2 webView, TabViewModel tab)
    {
        webView.CoreWebView2.IsDocumentPlayingAudioChanged += (_, _) =>
        {
            tab.IsPlayingAudio = webView.CoreWebView2.IsDocumentPlayingAudio;
            if (tab.IsPlayingAudio && DataContext is MainViewModel vm)
            {
                vm.ActiveMediaTab = tab;
                vm.IsMediaBarVisible = true;
            }
        };

        webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess || DataContext is not MainViewModel vm || vm.SelectedTab != tab)
                return;

            var source = webView.CoreWebView2.Source;
            if (source.Contains("internals.mundobrowser"))
            {
                if (source.Contains("settings.html"))
                {
                    string version = System.Reflection.Assembly.GetExecutingAssembly()
                        .GetName().Version?.ToString(3) ?? "1.0.0";
                    _ = webView.CoreWebView2.ExecuteScriptAsync(
                        $"if(document.getElementById('app-version')) document.getElementById('app-version').innerText = 'Version {version} (Build stable)';");
                }

                if (string.IsNullOrEmpty(tab.AddressUrl)
                    || !tab.AddressUrl.StartsWith("about:preferences"))
                {
                    string hash = source.Contains("#") ? source[source.IndexOf("#")..] : "#general";
                    tab.AddressUrl = "about:preferences" + hash;
                }
            }
            else
            {
                tab.Url = tab.AddressUrl = source;
            }

            UpdateTitle();
            vm.HistoryManager.AddEntry(tab.Url, webView.CoreWebView2.DocumentTitle);

            if (TopBarControl?.AddressBar.IsFocused == false)
            {
                TopBarControl.SetAddressBarText(tab.AddressUrl);
                vm.AddressBarText = tab.AddressUrl;
            }

            CheckForExtensionStorePage(tab, tab.Url);
        };

        webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            if (_viewModel?.SelectedTab == tab)
                UpdateTitle();
        };

        webView.CoreWebView2.SourceChanged += async (_, _) =>
        {
            var source = webView.CoreWebView2.Source;
            if (webView.CoreWebView2.DocumentTitle == "about:preferences"
                || source.Contains("internals.mundobrowser"))
            {
                if (source.Contains("settings.html"))
                {
                    string hash = source.Contains("#") ? source[source.IndexOf("#")..] : "#general";
                    tab.AddressUrl = "about:preferences" + hash;
                }
                else if (string.IsNullOrEmpty(tab.AddressUrl)
                         || !tab.AddressUrl.StartsWith("about:preferences"))
                {
                    tab.AddressUrl = "about:preferences#general";
                }
            }
            else if (source != "about:blank")
            {
                tab.AddressUrl = source;
            }

            if (_viewModel is { } vm && vm.SelectedTab == tab)
            {
                if (TopBarControl?.AddressBar.IsFocused == false)
                {
                    TopBarControl.SetAddressBarText(tab.AddressUrl);
                    vm.AddressBarText = tab.AddressUrl;
                }

                CheckForExtensionStorePage(tab, tab.AddressUrl);
            }

            if (_viewModel is { } mainVm)
                await mainVm.FaviconService.ResolveFaviconAsync(webView, tab);
        };

        webView.CoreWebView2.FaviconChanged += async (_, _) =>
        {
            if (_viewModel is not { } vm)
                return;

            bool shouldRefreshImmediately = vm.SelectedTab == tab || string.IsNullOrEmpty(tab.FaviconUrl);
            if (shouldRefreshImmediately)
                await vm.FaviconService.ResolveFaviconAsync(webView, tab, forceReload: true);
        };

        webView.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
            SetFullscreen(webView.CoreWebView2.ContainsFullScreenElement, true);

        webView.CoreWebView2.ContextMenuRequested += (_, args) =>
        {
            var menuItems = args.MenuItems;
            Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItem? inspectItem = null;

            foreach (var item in menuItems)
            {
                if (item.Name == "inspectElement" || item.Label.Contains("Inspect", StringComparison.OrdinalIgnoreCase))
                {
                    inspectItem = item;
                    break;
                }
            }

            if (inspectItem != null)
            {
                int index = menuItems.IndexOf(inspectItem);
                menuItems.RemoveAt(index);

                var customInspect = webView.CoreWebView2.Environment.CreateContextMenuItem(
                    "Inspecter", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);

                customInspect.CustomItemSelected += (_, _) =>
                {
                    Dispatcher.BeginInvoke(async () =>
                    {
                        await _webViewService.OpenDevToolsForTabAsync(tab);
                    });
                };

                menuItems.Insert(index, customInspect);
            }
        };

        webView.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;

            if (args.WindowFeatures.HasSize || args.WindowFeatures.HasPosition)
            {
                var deferral = args.GetDeferral();
                var popupWindow = new System.Windows.Window
                {
                    Title = "Mundo Browser",
                    Width = args.WindowFeatures.HasSize && args.WindowFeatures.Width > 0 ? args.WindowFeatures.Width : 800,
                    Height = args.WindowFeatures.HasSize && args.WindowFeatures.Height > 0 ? args.WindowFeatures.Height : 600,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                Helpers.NativeMethods.ApplyDarkMode(popupWindow);

                var popupWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
                popupWindow.Content = popupWebView;

                popupWebView.CoreWebView2InitializationCompleted += (s, ev) =>
                {
                    if (ev.IsSuccess)
                    {
                        args.NewWindow = popupWebView.CoreWebView2;
                        
                        popupWebView.CoreWebView2.WindowCloseRequested += (s2, e2) =>
                        {
                            popupWindow.Close();
                        };
                        popupWebView.CoreWebView2.DocumentTitleChanged += (s2, e2) =>
                        {
                            popupWindow.Title = popupWebView.CoreWebView2.DocumentTitle;
                        };
                    }
                    deferral.Complete();
                };

                _ = popupWebView.EnsureCoreWebView2Async(webView.CoreWebView2.Environment);
                popupWindow.Show();
            }
            else
            {
                _viewModel?.AddTabWithUrl(args.Uri, tab, isFromNewWindow: true);
            }
        };

        webView.CoreWebView2.DownloadStarting += (sender, args) =>
        {
            var downloadOp = args.DownloadOperation;
            _webViewService.RegisterActiveDownload(webView, downloadOp);

            bool isBlankDownloadTab = tab.IsCreatedFromNewWindow ||
                (!webView.CoreWebView2.CanGoBack &&
                 (string.IsNullOrEmpty(webView.CoreWebView2.DocumentTitle) 
                  || webView.CoreWebView2.DocumentTitle == "about:blank" 
                  || webView.CoreWebView2.Source == tab.Url));

            if (isBlankDownloadTab)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_viewModel != null)
                    {
                        if (_viewModel.SelectedTab == tab)
                        {
                            var targetTab = (tab.OpenedByTab != null && _trackedTabs.Contains(tab.OpenedByTab))
                                ? tab.OpenedByTab
                                : _viewModel.Tabs.FirstOrDefault(t => t != tab)
                                  ?? _viewModel.PinnedTabs.FirstOrDefault(p => !p.IsEmpty)?.Tab;

                            if (targetTab != null)
                            {
                                _viewModel.SelectedTab = targetTab;
                            }
                        }

                        _viewModel.CloseTab(tab);
                    }
                });
            }
        };

        webView.CoreWebView2.WindowCloseRequested += (_, _) => _viewModel?.CloseTab(tab);
    }

    private void UpdateTitle()
    {
        if (_webViewService.ActiveWebView?.CoreWebView2 == null
            || _viewModel?.SelectedTab is not { } selectedTab)
            return; 

        var title = _webViewService.ActiveWebView.CoreWebView2.DocumentTitle;
        selectedTab.Title = !string.IsNullOrWhiteSpace(title) ? title : selectedTab.Url;
    }
}
