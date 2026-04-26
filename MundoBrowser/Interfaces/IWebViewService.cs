using System;
using System.Threading.Tasks;
using System.Windows.Controls;
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
        /// Removes and disposes the WebView2 instance for the specified tab.
        /// </summary>
        void RemoveTab(TabViewModel tab);

        /// <summary>
        /// Gets the WebView2 instance for a tab if it exists.
        /// </summary>
        WebView2? GetWebViewForTab(TabViewModel tab);
    }
}
