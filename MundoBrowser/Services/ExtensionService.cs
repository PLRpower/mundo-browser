using System.IO;
using System.Text.Json;
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
            var loadedExtensions = new List<ExtensionInfo>();
            if (!Directory.Exists(_extensionsPath)) return loadedExtensions;

            var exts = await profile.GetBrowserExtensionsAsync();
            
            foreach (var ext in exts)
            {
                var extName = ext.Name ?? "Extension";
                if (!ext.IsEnabled || extName.Contains("Microsoft")) continue;

                var info = new ExtensionInfo(ext.Id, extName, true);

                if (Directory.Exists(_extensionsPath))
                {
                    foreach (var dir in Directory.GetDirectories(_extensionsPath))
                    {
                        var manifestPath = Path.Combine(dir, "manifest.json");
                        if (!File.Exists(manifestPath)) continue;

                        try
                        {
                            var json = await File.ReadAllTextAsync(manifestPath);
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;
                            
                            var manifestName = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var shortName = root.TryGetProperty("short_name", out var sn) ? sn.GetString() : null;
                            var resolvedName = ResolveName(manifestName, dir, root);
                            var resolvedShortName = ResolveName(shortName, dir, root);
                            
                            bool isMatch = false;
                            if (resolvedName != null && ext.Name != null && (ext.Name.Contains(resolvedName) || resolvedName.Contains(ext.Name))) isMatch = true;
                            else if (resolvedShortName != null && ext.Name != null && ext.Name.Contains(resolvedShortName)) isMatch = true;
                            else if (ext.Id.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)) isMatch = true;
                            
                            if (isMatch)
                            {
                                ProcessManifest(root, dir, ext.Id, info);
                                break;
                            }
                        }
                        catch
                        {
                            // Ignorer les erreurs de parsing pour une extension
                        }
                    }
                }
                loadedExtensions.Add(info);
            }
            return loadedExtensions;
        }

        public async Task<ExtensionInfo> InstallExtensionAsync(string extensionId, CoreWebView2Profile profile)
        {
            var path = await _downloader.DownloadAndExtractExtension(extensionId);
            var extension = await profile.AddBrowserExtensionAsync(path);
            
            if (extension == null) throw new Exception("Failed to load extension after download.");

            var info = new ExtensionInfo(extension.Id, extension.Name, true);
            var manifestPath = Path.Combine(path, "manifest.json");
            
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath);
                    using var doc = JsonDocument.Parse(json);
                    ProcessManifest(doc.RootElement, path, extension.Id, info);
                }
                catch { }
            }

            return info;
        }

        private string? ResolveName(string? name, string extensionDir, JsonElement root)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!name.StartsWith("__MSG_") || !name.EndsWith("__")) return name;
            
            var key = name.Substring(6, name.Length - 8);
            var defaultLocale = root.TryGetProperty("default_locale", out var locale) ? (locale.GetString() ?? "en") : "en";
            var localesPath = Path.Combine(extensionDir, "_locales");
            
            if (!Directory.Exists(localesPath)) return name;
            
            string[] searchLocales = { defaultLocale, "fr", "en_US" };
            foreach (var loc in searchLocales)
            {
                var msgPath = Path.Combine(localesPath, loc, "messages.json");
                if (File.Exists(msgPath))
                {
                    var val = GetMessageValue(msgPath, key);
                    if (val != null) return val;
                }
            }
            return name;
        }

        private string? GetMessageValue(string messagesPath, string key)
        {
            try
            {
                var json = File.ReadAllText(messagesPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var msgObj) && msgObj.TryGetProperty("message", out var msg))
                {
                    return msg.GetString();
                }
            }
            catch { }
            return null;
        }

        private void ProcessManifest(JsonElement root, string extensionDir, string extensionId, ExtensionInfo info)
        {
            string? popupPath = null;
            if (root.TryGetProperty("action", out var action) && action.TryGetProperty("default_popup", out var dp1)) popupPath = dp1.GetString();
            else if (root.TryGetProperty("browser_action", out var bAction) && bAction.TryGetProperty("default_popup", out var dp2)) popupPath = dp2.GetString();
            
            if (!string.IsNullOrEmpty(popupPath))
                info.PopupUrl = $"chrome-extension://{extensionId}/{popupPath.TrimStart('/')}";
                
            string? iconPath = null;
            if (root.TryGetProperty("icons", out var icons))
                iconPath = GetBestIconPath(icons);
                
            if (!string.IsNullOrEmpty(iconPath))
            {
                var fullIconPath = Path.Combine(extensionDir, iconPath.TrimStart('/'));
                if (File.Exists(fullIconPath))
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(fullIconPath);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Very important for performance and to allow crossing threads
                        info.IconSource = bitmap;
                    }
                    catch { }
                }
            }
        }

        private string? GetBestIconPath(JsonElement icons)
        {
            if (icons.ValueKind == JsonValueKind.String) return icons.GetString();
            if (icons.ValueKind == JsonValueKind.Object)
            {
                string[] sizes = { "128", "48", "32", "16" };
                foreach (var size in sizes)
                {
                    if (icons.TryGetProperty(size, out var path))
                        return path.GetString();
                }
                return icons.EnumerateObject().FirstOrDefault().Value.GetString();
            }
            return null;
        }
    }
}
