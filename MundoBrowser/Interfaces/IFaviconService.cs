using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Interfaces
{
    /// <summary>
    /// Handles fetching, caching, and providing favicons for websites.
    /// </summary>
    public interface IFaviconService
    {
        /// <summary>
        /// Gets the absolute path to a favicon from its relative path.
        /// </summary>
        string? GetAbsoluteFaviconPath(string relativePath);

        /// <summary>
        /// Resolves the favicon for a given tab and WebView2 instance.
        /// </summary>
        Task ResolveFaviconAsync(WebView2 wv, TabViewModel tab, bool forceReload = false);

        /// <summary>
        /// Cleans up favicons that are no longer in use.
        /// </summary>
        void CleanupStaleFavicons(HashSet<string> activeDomains);
    }
}
