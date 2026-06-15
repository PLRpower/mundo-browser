using System.IO;
using System.Text.Json;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;
using MundoBrowser.Services.Extensions;

namespace MundoBrowser.Services
{
    public class ExtensionService : IExtensionService
    {
        private const long MaxJsonFileBytes = 4 * 1024 * 1024;
        private readonly ExtensionDownloader _downloader;
        private readonly string _extensionsPath;

        public ExtensionService(ExtensionDownloader downloader)
        {
            _downloader = downloader;
            _extensionsPath = ExtensionRuntime.ExtensionsPath;
            Directory.CreateDirectory(_extensionsPath);
        }

        public async Task<List<ExtensionInfo>> LoadExtensionsAsync()
        {
            var loadedExtensions = new List<ExtensionInfo>();
            if (!Directory.Exists(_extensionsPath)) return loadedExtensions;

            foreach (var dir in ExtensionRuntime.GetInstalledDirectories())
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!IsReadableJsonFile(manifestPath))
                    continue;

                try
                {
                    loadedExtensions.Add(await CreateExtensionInfoAsync(dir));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Unable to load extension from '{dir}': {ex.Message}");
                }
            }

            return loadedExtensions;
        }

        public async Task<ExtensionInfo> InstallExtensionAsync(string extensionId)
        {
            var path = await _downloader.DownloadAndExtractExtension(extensionId);
            return await CreateExtensionInfoAsync(path);
        }

        private async Task<ExtensionInfo> CreateExtensionInfoAsync(string extensionDirectory)
        {
            var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
            if (!IsReadableJsonFile(manifestPath))
                throw new InvalidDataException($"Extension manifest is missing or invalid: '{manifestPath}'.");

            var json = await File.ReadAllTextAsync(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string sourceId = Path.GetFileName(extensionDirectory);
            string runtimeId = ExtensionRuntime.GetRuntimeId(extensionDirectory, root);
            var manifestName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
            var resolvedName = ResolveName(manifestName, extensionDirectory, root) ?? "Extension";
            var info = new ExtensionInfo(sourceId, resolvedName, true);
            ProcessManifest(root, extensionDirectory, runtimeId, info);
            return info;
        }

        private string? ResolveName(string? name, string extensionDir, JsonElement root)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!name.StartsWith("__MSG_") || !name.EndsWith("__")) return name;
            
            var key = name.Substring(6, name.Length - 8);
            var defaultLocale = root.TryGetProperty("default_locale", out var locale) ? (locale.GetString() ?? "en") : "en";
            var localesPath = ResolveExtensionPath(extensionDir, "_locales");
            
            if (localesPath == null || !Directory.Exists(localesPath)) return name;
            
            string[] searchLocales = { defaultLocale, "fr", "en_US" };
            foreach (var loc in searchLocales)
            {
                var msgPath = ResolveExtensionPath(extensionDir, Path.Combine("_locales", loc, "messages.json"));
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
                if (!IsReadableJsonFile(messagesPath))
                    return null;

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

        private static bool IsReadableJsonFile(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length <= MaxJsonFileBytes;
            }
            catch
            {
                return false;
            }
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
                var fullIconPath = ResolveExtensionPath(extensionDir, iconPath);
                if (fullIconPath != null && File.Exists(fullIconPath))
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

        private static string? ResolveExtensionPath(string extensionDir, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            var root = EnsureTrailingDirectorySeparator(Path.GetFullPath(extensionDir));
            var normalizedRelativePath = relativePath
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
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
