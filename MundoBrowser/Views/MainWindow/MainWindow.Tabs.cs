using System.Collections.Specialized;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private readonly HashSet<TabViewModel> _syncingUrlTabs = [];

    private void InitializeEvents(MainViewModel vm)
    {
        _viewModel = vm;
        vm.PropertyChanged += MainViewModel_PropertyChanged;
        vm.Tabs.CollectionChanged += Tabs_CollectionChanged;
        foreach (var pinnedTab in vm.PinnedTabs)
            pinnedTab.PropertyChanged += PinnedTab_PropertyChanged;

        _webViewService.BrowserProcessExited += OnBrowserProcessExited;

        SynchronizeTrackedTabs();

        vm.NewTabRequested += MainViewModel_NewTabRequested;
        vm.MediaActionRequested += OnMediaActionRequested;
        vm.TabDragCompleted += MainViewModel_TabDragCompleted;
        InitializeSplitViewEvents(vm);
    }

    private void OnBrowserProcessExited()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(OnBrowserProcessExited);
            return;
        }

        if (_viewModel?.SelectedTab is { } currentTab)
        {
            _ = SwitchToTabAsync(currentTab);
        }
    }

    private async Task SwitchToTabAsync(TabViewModel tab)
    {
        if (!_trackedTabs.Contains(tab))
            return;

        int switchVersion = Interlocked.Increment(ref _tabSwitchVersion);

        try
        {
            var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv => SetupWebViewEvents(wv, tab));
            if ((switchVersion != Volatile.Read(ref _tabSwitchVersion) && _viewModel?.SelectedTab != tab)
                || _viewModel?.SelectedTab != tab
                || !_trackedTabs.Contains(tab))
                return;

            if (_viewModel?.IsSplitViewActive == true)
            {
                await UpdateSplitViewWebViewsAsync();
            }
            else
            {
                await _webViewService.SwitchToTabAsync(tab, webView);
            }

            if (_viewModel is { } vm)
            {
                TopBarControl?.SetAddressBarText(tab.AddressUrl);
                FloatingTopBarControl?.SetAddressBarText(tab.AddressUrl);
                vm.AddressBarText = tab.AddressUrl;
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // Tab switch cancelled or WebView recovering from crash
        }
    }

    private async void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TabViewModel.Url) || sender is not TabViewModel tab)
            return;

        if (_syncingUrlTabs.Contains(tab))
            return;

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnTabPropertyChanged(sender, e));
            return;
        }

        if (_syncingUrlTabs.Contains(tab) || !_trackedTabs.Contains(tab))
            return;

        try
        {
            bool createdNewWebView = false;
            var webView = await _webViewService.GetOrCreateWebViewAsync(tab, wv =>
            {
                createdNewWebView = true;
                SetupWebViewEvents(wv, tab);
            });
            if (!createdNewWebView && _trackedTabs.Contains(tab))
            {
                try
                {
                    if (webView.CoreWebView2 != null && webView.CoreWebView2.Source != tab.Url)
                        webView.CoreWebView2.Navigate(tab.Url);
                }
                catch { }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // The tab was closed or webview crashed while its WebView was being initialized.
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
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                // The selected tab changed again or crashed while initializing.
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible))
        {
            UpdateSidebarWidth(vm.IsSidebarVisible);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsTopBarVisible))
        {
            UpdateTopBarHeight(vm.IsTopBarVisible);
        }
        else if (e.PropertyName == nameof(MainViewModel.SidebarWidth) && vm.IsSidebarVisible)
        {
            UpdateSidebarWidth(true);
            UpdateTopEdgeTriggerState();
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
        FocusAddressBar();
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
            _viewModel.SplitViewLayoutChanged -= MainViewModel_SplitViewLayoutChanged;
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
        webView.WebMessageReceived += async (_, e) =>
        {
            try
            {
                var json = e.WebMessageAsJson;
                if (TryHandleMediaWebMessage(tab, json))
                    return;

                await HandleExtensionWebMessageAsync(webView, json);
            }
            catch
            {
                // Ignored
            }
        };

        webView.CoreWebView2.IsDocumentPlayingAudioChanged += (_, _) =>
        {
            tab.IsPlayingAudio = webView.CoreWebView2.IsDocumentPlayingAudio;
            tab.IsMediaPaused = !tab.IsPlayingAudio;
            if (tab.IsPlayingAudio) tab.LastAccessed = DateTime.Now;
            if (DataContext is MainViewModel vm)
            {
                if (tab.IsPlayingAudio)
                {
                    if (vm.ActiveMediaTab == tab)
                    {
                        vm.KeepActiveMediaTabWhenPaused = false;
                        vm.IsMediaBarVisible = true;
                    }
                    else if (vm.ActiveMediaTab == null || !vm.IsMediaBarVisible || (!vm.ActiveMediaTab.IsPlayingAudio && !vm.KeepActiveMediaTabWhenPaused))
                    {
                        vm.ActiveMediaTab = tab;
                        vm.IsMediaBarVisible = true;
                    }
                    EnsureMediaObserverInstalled(webView);
                }
                else if (vm.ActiveMediaTab == tab)
                {
                    if (vm.FindPlayingAudioTab(tab) != null)
                        vm.KeepActiveMediaTabWhenPaused = true;

                    EnsureMediaObserverInstalled(webView);
                    if (tab.IsMediaPaused)
                    {
                        _globalMediaTimer.Stop();
                    }
                }
            }
        };

        bool hasLoadedDocument = false;
        ulong activeNavigationId = 0;

        webView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            activeNavigationId = args.NavigationId;
            tab.LastAccessed = DateTime.Now;
            tab.IsLoading = true;
        };

        webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            tab.LastAccessed = DateTime.Now;
            if (activeNavigationId == 0 || args.NavigationId == activeNavigationId)
                tab.IsLoading = false;

            if (!args.IsSuccess || DataContext is not MainViewModel vm)
                return;

            var source = webView.CoreWebView2.Source;
            if (string.IsNullOrEmpty(source) || source.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                return;

            hasLoadedDocument = true;

            if (source.Contains("internals.mundobrowser", StringComparison.OrdinalIgnoreCase))
            {
                if (source.Contains("settings.html", StringComparison.OrdinalIgnoreCase))
                {
                    string versionBadge = AppRuntime.VersionBadgeText;
                    _ = webView.CoreWebView2.ExecuteScriptAsync(
                        $"if(document.getElementById('app-version')) document.getElementById('app-version').innerText = '{versionBadge}';");
                }

                if (string.IsNullOrEmpty(tab.AddressUrl)
                    || !tab.AddressUrl.StartsWith("about:preferences", StringComparison.OrdinalIgnoreCase))
                {
                    int hashIdx = source.IndexOf('#');
                    string hash = hashIdx >= 0 ? source[hashIdx..] : "#general";
                    tab.AddressUrl = "about:preferences" + hash;
                }
            }
            else
            {
                _syncingUrlTabs.Add(tab);
                try
                {
                    tab.Url = tab.AddressUrl = source;
                }
                finally
                {
                    _syncingUrlTabs.Remove(tab);
                }
            }

            var docTitle = webView.CoreWebView2.DocumentTitle;
            if (!string.IsNullOrWhiteSpace(docTitle))
            {
                tab.Title = docTitle;
            }

            vm.HistoryManager.AddEntry(tab.Url, webView.CoreWebView2.DocumentTitle);

            if (vm.SelectedTab == tab)
            {
                UpdateTitle();

                if (TopBarControl?.AddressBar.IsFocused == false && FloatingTopBarControl?.AddressBar.IsFocused == false)
                {
                    TopBarControl.SetAddressBarText(tab.AddressUrl);
                    FloatingTopBarControl.SetAddressBarText(tab.AddressUrl);
                    vm.AddressBarText = tab.AddressUrl;
                }

                CheckForExtensionStorePage(tab, tab.Url);
                NotifyExtensionStatusToWebView(webView, tab.Url);
            }
        };

        webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            var docTitle = webView.CoreWebView2.DocumentTitle;
            if (!string.IsNullOrWhiteSpace(docTitle))
            {
                tab.Title = docTitle;
            }

            if (_viewModel?.SelectedTab == tab)
                UpdateTitle();
        };

        webView.CoreWebView2.SourceChanged += async (_, _) =>
        {
            try
            {
                if (webView.CoreWebView2 == null) return;
                var source = webView.CoreWebView2.Source;
                if (webView.CoreWebView2.DocumentTitle == "about:preferences"
                    || source.Contains("internals.mundobrowser", StringComparison.OrdinalIgnoreCase))
                {
                    if (source.Contains("settings.html", StringComparison.OrdinalIgnoreCase))
                    {
                        int hashIdx = source.IndexOf('#');
                        string hash = hashIdx >= 0 ? source[hashIdx..] : "#general";
                        tab.AddressUrl = "about:preferences" + hash;
                    }
                    else if (string.IsNullOrEmpty(tab.AddressUrl)
                             || !tab.AddressUrl.StartsWith("about:preferences", StringComparison.OrdinalIgnoreCase))
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
                    if (TopBarControl?.AddressBar.IsFocused == false && FloatingTopBarControl?.AddressBar.IsFocused == false)
                    {
                        TopBarControl.SetAddressBarText(tab.AddressUrl);
                        FloatingTopBarControl?.SetAddressBarText(tab.AddressUrl);
                        vm.AddressBarText = tab.AddressUrl;
                    }

                    CheckForExtensionStorePage(tab, tab.AddressUrl);
                    NotifyExtensionStatusToWebView(webView, tab.AddressUrl);
                }

                if (_viewModel is { } mainVm)
                    await mainVm.FaviconService.ResolveFaviconAsync(webView, tab);
            }
            catch
            {
            }
        };

        webView.CoreWebView2.FaviconChanged += async (_, _) =>
        {
            try
            {
                if (_viewModel is not { } vm)
                    return;

                bool shouldRefreshImmediately = vm.SelectedTab == tab || string.IsNullOrEmpty(tab.FaviconUrl);
                if (shouldRefreshImmediately)
                    await vm.FaviconService.ResolveFaviconAsync(webView, tab, forceReload: true);
            }
            catch
            {
            }
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

        webView.CoreWebView2.NewWindowRequested += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Handled = true;

            try
            {
                bool isExtensionPopup = Services.ExtensionService.IsExtensionPopupUri(args.Uri, _viewModel?.InstalledExtensions);
                bool isPopupWindow = args.WindowFeatures.HasSize
                    || args.WindowFeatures.HasPosition
                    || !args.WindowFeatures.ShouldDisplayToolbar
                    || isExtensionPopup;

                if (isPopupWindow)
                {
                    if (isExtensionPopup)
                    {
                        CloseExtensionPopup();
                        if (_activeExtensionPopoutWindow?.IsVisible == true)
                        {
                            try { _activeExtensionPopoutWindow.Close(); } catch { }
                        }
                        _activeExtensionPopoutWindow = null;
                    }

                    double defaultWidth = isExtensionPopup ? 480 : 800;
                    double defaultHeight = 600;

                    var popupWindow = new System.Windows.Window
                    {
                        Title = isExtensionPopup ? "Extension" : "Mundo Browser",
                        Width = args.WindowFeatures.HasSize && args.WindowFeatures.Width > 0 ? args.WindowFeatures.Width : defaultWidth,
                        Height = args.WindowFeatures.HasSize && args.WindowFeatures.Height > 0 ? args.WindowFeatures.Height : defaultHeight,
                        WindowStartupLocation = !isExtensionPopup && args.WindowFeatures.HasPosition
                            ? System.Windows.WindowStartupLocation.Manual
                            : System.Windows.WindowStartupLocation.CenterOwner,
                        Owner = this
                    };

                    if (!isExtensionPopup && args.WindowFeatures.HasPosition)
                    {
                        popupWindow.Left = args.WindowFeatures.Left;
                        popupWindow.Top = args.WindowFeatures.Top;
                    }

                    if (isExtensionPopup)
                    {
                        _activeExtensionPopoutWindow = popupWindow;
                    }

                    popupWindow.SourceInitialized += (_, _) => Helpers.NativeMethods.ApplyDarkMode(popupWindow);

                    var popupWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
                    popupWindow.Content = popupWebView;

                    popupWindow.Closed += (_, _) =>
                    {
                        if (ReferenceEquals(_activeExtensionPopoutWindow, popupWindow))
                            _activeExtensionPopoutWindow = null;
                        popupWindow.Content = null;
                        try { popupWebView.Dispose(); } catch { }
                    };

                    popupWindow.Show();
                    popupWindow.Activate();

                    await popupWebView.EnsureCoreWebView2Async(webView.CoreWebView2.Environment);
                    args.NewWindow = popupWebView.CoreWebView2;

                    popupWebView.CoreWebView2.WindowCloseRequested += (_, _) =>
                    {
                        popupWindow.Close();
                    };
                    popupWebView.CoreWebView2.ProcessFailed += (_, _) =>
                    {
                        popupWindow.Close();
                    };

                    if (isExtensionPopup)
                    {
                        try
                        {
                            await popupWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                                (function() {
                                    if (typeof chrome === 'undefined' || !chrome.runtime || !chrome.runtime.sendMessage) return;
                                    var origSend = chrome.runtime.sendMessage.bind(chrome.runtime);
                                    chrome.runtime.sendMessage = function(msg) {
                                        if (msg && (msg.type === 'PickCredentialResponse' || msg.type === 'ConfirmNewCredentialResponse')) {
                                            setTimeout(function() {
                                                try { window.close(); } catch (e) {}
                                            }, 100);
                                        }
                                        return origSend.apply(this, arguments);
                                    };
                                })();
                            """);
                        }
                        catch { }
                    }

                    await _webViewService.ConfigurePopupWebViewAsync(popupWebView, args.Uri);
                    popupWebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                    {
                        if (!string.IsNullOrWhiteSpace(popupWebView.CoreWebView2.DocumentTitle))
                            popupWindow.Title = popupWebView.CoreWebView2.DocumentTitle;
                    };
                    popupWebView.CoreWebView2.NewWindowRequested += (_, popupArgs) =>
                    {
                        if (_viewModel == null || string.IsNullOrWhiteSpace(popupArgs.Uri))
                            return;

                        popupArgs.Handled = true;
                        _viewModel.AddTabWithUrl(popupArgs.Uri, _viewModel.SelectedTab ?? tab);
                    };
                }
                else if (_viewModel != null)
                {
                    string targetUrl = string.IsNullOrWhiteSpace(args.Uri) ? "about:blank" : args.Uri;
                    var newTab = _viewModel.AddTabWithUrl(targetUrl, tab, isFromNewWindow: true);

                    var newWebView = await _webViewService.GetOrCreateWebViewAsync(newTab, wv => SetupWebViewEvents(wv, newTab));
                    if (newWebView.CoreWebView2 != null)
                    {
                        args.NewWindow = newWebView.CoreWebView2;
                    }

                    newTab.IsCreatedFromNewWindow = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NewWindowRequested handling failed: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        };

        webView.CoreWebView2.DownloadStarting += (sender, args) =>
        {
            var downloadOp = args.DownloadOperation;
            if (!hasLoadedDocument && downloadOp.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
            }
            else
            {
                _webViewService.RegisterActiveDownload(webView, downloadOp);
            }

            bool isBlankDownloadTab = !hasLoadedDocument
                && !webView.CoreWebView2.CanGoBack
                && !tab.IsPinned;

            if (isBlankDownloadTab)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_viewModel == null || !_trackedTabs.Contains(tab))
                        return;

                    if (_viewModel.SelectedTab == tab)
                    {
                        var targetTab = (tab.OpenedByTab != null && _trackedTabs.Contains(tab.OpenedByTab))
                            ? tab.OpenedByTab
                            : _viewModel.Tabs.FirstOrDefault(t => t != tab)
                              ?? _viewModel.PinnedTabs.FirstOrDefault(p => !p.IsEmpty && p.Tab != tab)?.Tab;

                        if (targetTab != null)
                        {
                            _viewModel.SelectedTab = targetTab;
                        }
                    }

                    _viewModel.CloseTab(tab);
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
