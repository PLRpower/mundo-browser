using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Interfaces
{
    /// <summary>
    /// Manages WebView2 instances and their lifecycle.
    /// </summary>
    public interface IWebViewService
    {
        /// <summary>
        /// The currently active WebView2 instance.
        /// </summary>
        WebView2? ActiveWebView { get; }

        /// <summary>
        /// The shared WebView2 environment.
        /// </summary>
        CoreWebView2Environment? WebViewEnvironment { get; }

        /// <summary>
        /// Whether eco mode is enabled to save resources.
        /// </summary>
        bool EcoModeEnabled { get; set; }

        /// <summary>
        /// Minutes of inactivity before a tab is discarded in eco mode.
        /// </summary>
        int EcoModeMinutes { get; set; }

        /// <summary>
        /// Initializes the WebView2 environment.
        /// </summary>
        Task InitializeAsync(System.Windows.Controls.Panel container);

        /// <summary>
        /// Gets or creates a WebView2 instance for the specified tab.
        /// </summary>
        Task<WebView2> GetOrCreateWebViewAsync(TabViewModel tab, Action<WebView2> setupEvents);

        /// <summary>
        /// Switches the active view to the specified tab.
        /// </summary>
        Task SwitchToTabAsync(TabViewModel tab, WebView2 webView);

        /// <summary>
        /// Updates the layout of tab WebViews for Split-View or single view mode.
        /// </summary>
        Task UpdateSplitViewLayoutAsync(
            bool isSplit,
            TabViewModel? primaryTab,
            TabViewModel? secondaryTab,
            TabViewModel? activeTab,
            System.Windows.Controls.Panel? primaryHost,
            System.Windows.Controls.Panel? secondaryHost);

        /// <summary>
        /// Removes and disposes the WebView2 instance for the specified tab.
        /// </summary>
        void RemoveTab(TabViewModel tab);

        /// <summary>
        /// Gets the WebView2 instance for a tab if it exists.
        /// </summary>
        WebView2? GetWebViewForTab(TabViewModel tab);

        /// <summary>
        /// Gets the active WebView2 instance, or any available initialized WebView2 instance.
        /// </summary>
        WebView2? GetAnyActiveWebView();

        /// <summary>
        /// Gets all currently tracked WebView2 instances.
        /// </summary>
        IReadOnlyList<WebView2> GetAllWebViews();

        /// <summary>
        /// Registers an active download for a WebView2 instance to delay disposal until download finishes.
        /// </summary>
        void RegisterActiveDownload(WebView2 webView, CoreWebView2DownloadOperation download);

        /// <summary>
        /// Whether any WebView has active downloads in progress.
        /// </summary>
        bool HasActiveDownloads { get; }

        /// <summary>
        /// Total number of active downloads across all WebViews.
        /// </summary>
        int ActiveDownloadCount { get; }

        /// <summary>
        /// Event fired when the active download list changes.
        /// </summary>
        event Action? ActiveDownloadsChanged;

        /// <summary>
        /// Event fired when the underlying WebView2 browser process exits/crashes.
        /// </summary>
        event Action? BrowserProcessExited;

        /// <summary>
        /// Opens the default download dialog if available.
        /// </summary>
        void OpenDownloadDialog();

        /// <summary>
        /// Opens DevTools for the specified tab.
        /// </summary>
        Task<WebView2?> OpenDevToolsForTabAsync(TabViewModel tab);

        /// <summary>
        /// Toggles the DevTools state for the specified tab.
        /// </summary>
        Task ToggleDevToolsForTabAsync(TabViewModel tab);
    }
}
