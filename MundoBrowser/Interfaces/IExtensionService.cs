using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using MundoBrowser.Models;

namespace MundoBrowser.Interfaces
{
    /// <summary>
    /// Manages browser extensions, including downloading and loading them into the WebView2 environment.
    /// </summary>
    public interface IExtensionService
    {
        /// <summary>
        /// Loads installed extensions into the specified WebView2 environment.
        /// </summary>
        Task<List<ExtensionInfo>> LoadExtensionsAsync(CoreWebView2Profile profile);

        /// <summary>
        /// Downloads and installs an extension from a Chrome Web Store ID.
        /// </summary>
        Task<ExtensionInfo> InstallExtensionAsync(string extensionId, CoreWebView2Profile profile);
    }
}
