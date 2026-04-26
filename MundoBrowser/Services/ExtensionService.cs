using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;

namespace MundoBrowser.Services
{
    public class ExtensionService : IExtensionService
    {
        private readonly ExtensionDownloader _downloader;
        private readonly string _extensionsPath;

        public ExtensionService()
        {
            _downloader = new ExtensionDownloader();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _extensionsPath = Path.Combine(appData, "MundoBrowser", "Extensions");
            Directory.CreateDirectory(_extensionsPath);
        }

        public async Task<List<ExtensionInfo>> LoadExtensionsAsync(CoreWebView2Profile profile)
        {
            var extensions = new List<ExtensionInfo>();
            if (!Directory.Exists(_extensionsPath)) return extensions;

            foreach (var dir in Directory.GetDirectories(_extensionsPath))
            {
                try
                {
                    var extension = await profile.AddBrowserExtensionAsync(dir);
                    if (extension != null)
                    {
                        extensions.Add(new ExtensionInfo(extension.Id, extension.Name, true));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load extension from {dir}: {ex.Message}");
                }
            }
            return extensions;
        }

        public async Task<ExtensionInfo> InstallExtensionAsync(string extensionId, CoreWebView2Profile profile)
        {
            var path = await _downloader.DownloadAndExtractExtension(extensionId);
            var extension = await profile.AddBrowserExtensionAsync(path);
            
            if (extension == null) throw new Exception("Failed to load extension after download.");

            return new ExtensionInfo(extension.Id, extension.Name, true);
        }
    }
}
