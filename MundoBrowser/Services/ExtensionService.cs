using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;

namespace MundoBrowser.Services;

public class ExtensionService : IExtensionService
{
    private const long MaxJsonFileBytes = 4 * 1024 * 1024;
    private readonly ExtensionDownloader _downloader;
    private readonly string _extensionsPath;

    public ExtensionService(ExtensionDownloader downloader)
    {
        _downloader = downloader;
        _extensionsPath = Path.Combine(AppRuntime.LocalDataDirectory, "Extensions");
        Directory.CreateDirectory(_extensionsPath);
    }

    public async Task<List<ExtensionInfo>> LoadExtensionsAsync(CoreWebView2Profile profile)
    {
        var loadedExtensions = new List<ExtensionInfo>();
        if (!Directory.Exists(_extensionsPath)) return loadedExtensions;

        CleanupDeletedDirectories();

        var exts = await profile.GetBrowserExtensionsAsync();
        
        foreach (var ext in exts)
        {
            var extName = ext.Name ?? "Extension";
            if (!ext.IsEnabled || extName.Contains("Microsoft")) continue;

            string? matchedDir = null;
            string? matchedStoreId = null;
            JsonElement? matchedRoot = null;
            JsonDocument? matchedDoc = null;

            if (Directory.Exists(_extensionsPath))
            {
                var directExtensionDirectory = Path.Combine(_extensionsPath, ext.Id);
                IEnumerable<string> candidateDirectories = Directory.Exists(directExtensionDirectory)
                    ? [directExtensionDirectory]
                    : Directory.EnumerateDirectories(_extensionsPath);

                foreach (var dir in candidateDirectories)
                {
                    if (IsTemporaryOrDeletedDirectory(dir))
                        continue;

                    var manifestPath = Path.Combine(dir, "manifest.json");
                    if (!IsReadableJsonFile(manifestPath)) continue;

                    try
                    {
                        var json = await File.ReadAllTextAsync(manifestPath);
                        var doc = JsonDocument.Parse(json);
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
                            matchedDir = dir;
                            matchedStoreId = Path.GetFileName(dir);
                            matchedDoc = doc;
                            matchedRoot = root;
                            break;
                        }
                        else
                        {
                            doc.Dispose();
                        }
                    }
                    catch
                    {
                        // Ignore individual manifest parsing errors
                    }
                }
            }

            // If the folder on disk was deleted or uninstalled, purge orphaned extension from profile
            if (matchedDir == null || !Directory.Exists(matchedDir))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtensionService] Purging orphaned extension {ext.Id} ({ext.Name}) from profile.");
                    await ext.RemoveAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtensionService] Failed to purge orphaned extension {ext.Id}: {ex.Message}");
                }
                continue;
            }

            var info = new ExtensionInfo(ext.Id, extName, true, matchedStoreId, matchedDir);

            if (matchedRoot.HasValue)
            {
                try
                {
                    ProcessManifest(matchedRoot.Value, matchedDir, ext.Id, info);
                }
                finally
                {
                    matchedDoc?.Dispose();
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
        
        if (extension == null) throw new InvalidOperationException("Failed to load extension into profile after download.");

        var info = new ExtensionInfo(extension.Id, extension.Name, true, extensionId, path);
        var manifestPath = Path.Combine(path, "manifest.json");
        
        if (IsReadableJsonFile(manifestPath))
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

    public async Task RemoveExtensionAsync(ExtensionInfo info, CoreWebView2Profile profile)
    {
        await RemoveExtensionInternalAsync(info.Id, info.StoreId, info.FolderPath, profile);
    }

    public async Task RemoveExtensionAsync(string extensionId, CoreWebView2Profile profile)
    {
        await RemoveExtensionInternalAsync(extensionId, null, null, profile);
    }

    private async Task RemoveExtensionInternalAsync(string? extensionId, string? storeId, string? folderPath, CoreWebView2Profile profile)
    {
        if (string.IsNullOrWhiteSpace(extensionId) && string.IsNullOrWhiteSpace(storeId) && string.IsNullOrWhiteSpace(folderPath))
            return;

        // 1. Remove from WebView2 profile
        try
        {
            var exts = await profile.GetBrowserExtensionsAsync();
            foreach (var ext in exts)
            {
                bool shouldRemove = false;

                if (!string.IsNullOrEmpty(extensionId) && ext.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase))
                    shouldRemove = true;
                else if (!string.IsNullOrEmpty(storeId) && ext.Id.Equals(storeId, StringComparison.OrdinalIgnoreCase))
                    shouldRemove = true;
                else if (!string.IsNullOrEmpty(storeId) && Directory.Exists(_extensionsPath))
                {
                    var candidateDir = Path.Combine(_extensionsPath, storeId);
                    if (Directory.Exists(candidateDir) && MatchesExtensionDirectory(candidateDir, ext))
                    {
                        shouldRemove = true;
                    }
                }
                else if (!string.IsNullOrEmpty(extensionId) && Directory.Exists(_extensionsPath))
                {
                    var candidateDir = Path.Combine(_extensionsPath, extensionId);
                    if (Directory.Exists(candidateDir) && MatchesExtensionDirectory(candidateDir, ext))
                    {
                        shouldRemove = true;
                    }
                }

                if (shouldRemove)
                {
                    try
                    {
                        await ext.RemoveAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExtensionService] Failed to remove browser extension from profile: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtensionService] Failed to get extensions from profile during remove: {ex.Message}");
        }

        // 2. Identify and delete directory from disk
        var directoriesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
        {
            directoriesToDelete.Add(folderPath);
        }

        if (!string.IsNullOrEmpty(storeId))
        {
            var storeDir = Path.Combine(_extensionsPath, storeId);
            if (Directory.Exists(storeDir))
                directoriesToDelete.Add(storeDir);
        }

        if (!string.IsNullOrEmpty(extensionId))
        {
            var idDir = Path.Combine(_extensionsPath, extensionId);
            if (Directory.Exists(idDir))
                directoriesToDelete.Add(idDir);
        }

        if (Directory.Exists(_extensionsPath))
        {
            foreach (var dir in Directory.EnumerateDirectories(_extensionsPath))
            {
                if (IsTemporaryOrDeletedDirectory(dir))
                    continue;

                var dirName = Path.GetFileName(dir);
                if (dirName.Equals(extensionId, StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals(storeId, StringComparison.OrdinalIgnoreCase))
                {
                    directoriesToDelete.Add(dir);
                }
            }
        }

        foreach (var dir in directoriesToDelete)
        {
            DeleteDirectorySafely(dir);
        }
    }

    private static bool MatchesExtensionDirectory(string dir, CoreWebView2BrowserExtension ext)
    {
        var manifestPath = Path.Combine(dir, "manifest.json");
        if (!IsReadableJsonFile(manifestPath)) return false;

        try
        {
            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var manifestName = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var shortName = root.TryGetProperty("short_name", out var sn) ? sn.GetString() : null;
            var resolvedName = ResolveName(manifestName, dir, root);
            var resolvedShortName = ResolveName(shortName, dir, root);

            if (resolvedName != null && ext.Name != null && (ext.Name.Contains(resolvedName) || resolvedName.Contains(ext.Name))) return true;
            if (resolvedShortName != null && ext.Name != null && ext.Name.Contains(resolvedShortName)) return true;
            if (ext.Id.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }

        return false;
    }

    private static void DeleteDirectorySafely(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtensionService] Direct delete failed for {dir}: {ex.Message}, attempting rename delete.");
            try
            {
                var tempDeleted = dir + ".deleted_" + Guid.NewGuid().ToString("N");
                Directory.Move(dir, tempDeleted);
                try { Directory.Delete(tempDeleted, recursive: true); } catch { }
            }
            catch (Exception moveEx)
            {
                System.Diagnostics.Debug.WriteLine($"[ExtensionService] Rename delete failed for {dir}: {moveEx.Message}");
            }
        }
    }

    private void CleanupDeletedDirectories()
    {
        try
        {
            if (!Directory.Exists(_extensionsPath)) return;
            foreach (var dir in Directory.EnumerateDirectories(_extensionsPath))
            {
                if (IsTemporaryOrDeletedDirectory(dir))
                {
                    try { Directory.Delete(dir, recursive: true); } catch { }
                }
            }
        }
        catch { }
    }

    private static bool IsTemporaryOrDeletedDirectory(string dir)
    {
        return dir.Contains(".deleted_") || dir.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || dir.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveName(string? name, string extensionDir, JsonElement root)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!name.StartsWith("__MSG_", StringComparison.Ordinal) || !name.EndsWith("__", StringComparison.Ordinal)) return name;
        
        var key = name[6..^2];
        var defaultLocale = root.TryGetProperty("default_locale", out var locale) ? (locale.GetString() ?? "en") : "en";
        var localesPath = ResolveExtensionPath(extensionDir, "_locales");
        
        if (localesPath == null || !Directory.Exists(localesPath)) return name;
        
        string[] searchLocales = [defaultLocale, "fr", "en_US", "en"];
        foreach (var loc in searchLocales)
        {
            var msgPath = ResolveExtensionPath(extensionDir, Path.Combine("_locales", loc, "messages.json"));
            if (msgPath != null && File.Exists(msgPath))
            {
                var val = GetMessageValue(msgPath, key);
                if (val != null) return val;
            }
        }
        return name;
    }

    private static string? GetMessageValue(string messagesPath, string key)
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

    private static void ProcessManifest(JsonElement root, string extensionDir, string extensionId, ExtensionInfo info)
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
                    bitmap.Freeze();
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

    private static string? GetBestIconPath(JsonElement icons)
    {
        if (icons.ValueKind == JsonValueKind.String) return icons.GetString();
        if (icons.ValueKind == JsonValueKind.Object)
        {
            string[] sizes = ["128", "48", "32", "16"];
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
