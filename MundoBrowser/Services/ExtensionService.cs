using System.Globalization;
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
    public const string WebView2ExtensionShimMarker = "/* MUNDO_WV2_EXTENSION_SHIM_V7 */";
    private const string LegacyShimMarkerV1 = "/* MUNDO_WV2_EXTENSION_SHIM_V1 */";
    private const string WebView2ExtensionShimEndMarker = "/* MUNDO_WV2_EXTENSION_SHIM_END */";

    public const string WebView2ExtensionShimScript = """
        /* MUNDO_WV2_EXTENSION_SHIM_V7 */
        (function() {
          if (typeof chrome === "undefined") return;
          if (chrome.scripting) {
            if (chrome.scripting.registerContentScripts && chrome.scripting.unregisterContentScripts && !chrome.scripting.registerContentScripts.__mundoPatched) {
              const origRegister = chrome.scripting.registerContentScripts.bind(chrome.scripting);
              const origUnregister = chrome.scripting.unregisterContentScripts.bind(chrome.scripting);
              const patchedRegister = function(scripts, callback) {
                const list = Array.isArray(scripts) ? scripts : [];
                const ids = list.map(s => s && s.id).filter(Boolean);
                const run = async () => {
                  if (ids.length > 0) {
                    try { await origUnregister({ ids }); } catch (e) {}
                  }
                  return await origRegister(scripts);
                };
                const p = run();
                if (typeof callback === "function") {
                  p.then(() => callback(), () => callback());
                  return;
                }
                return p;
              };
              patchedRegister.__mundoPatched = true;
              chrome.scripting.registerContentScripts = patchedRegister;
            }
            if (chrome.scripting.executeScript && !chrome.scripting.executeScript.__mundoPatched) {
              const origExec = chrome.scripting.executeScript.bind(chrome.scripting);
              const recentExecs = new Map();
              const patchedExec = function(injection, callback) {
                const tabId = injection && injection.target && injection.target.tabId;
                const files = injection && Array.isArray(injection.files) ? injection.files.join(",") : "";
                if (tabId != null && files && files.includes("fido2")) {
                  const key = tabId + ":" + files + ":" + ((injection && injection.world) || "");
                  const now = Date.now();
                  const prev = recentExecs.get(key);
                  if (prev && (now - prev < 2000)) {
                    const emptyRes = [];
                    if (typeof callback === "function") {
                      setTimeout(() => callback(emptyRes), 0);
                      return;
                    }
                    return Promise.resolve(emptyRes);
                  }
                  recentExecs.set(key, now);
                }
                return origExec.apply(this, arguments);
              };
              patchedExec.__mundoPatched = true;
              chrome.scripting.executeScript = patchedExec;
            }
          }
          const urlsMatch = (u1, u2) => {
            if (!u1 || !u2) return false;
            if (u1 === u2) return true;
            try {
              const m1 = u1.match(/[?&]singleActionPopout=([^&#]+)/);
              const m2 = u2.match(/[?&]singleActionPopout=([^&#]+)/);
              if (m1 && m2 && m1[1] === m2[1]) return true;
              const s1 = u1.match(/[?&]sessionId=([^&#]+)/);
              const s2 = u2.match(/[?&]sessionId=([^&#]+)/);
              if (s1 && s2 && s1[1] === s2[1]) return true;
              const b1 = u1.split("#")[0];
              const b2 = u2.split("#")[0];
              if (b1 && b2 && b1 === b2 && b1.includes("popup/index.html")) return true;
            } catch (e) {}
            return false;
          };
          if (chrome.windows) {
            if (chrome.windows.create && !chrome.windows.create.__mundoPatched) {
              const origWindowCreate = chrome.windows.create.bind(chrome.windows);
              let createQueue = Promise.resolve();
              const resolveCreatedWindow = async (createData, beforeIds) => {
                const rawUrl = typeof createData?.url === "string"
                  ? createData.url
                  : (Array.isArray(createData?.url) ? createData.url[0] : null);
                const targetUrl = rawUrl && !rawUrl.includes("://") && chrome.runtime && chrome.runtime.getURL
                  ? chrome.runtime.getURL(rawUrl)
                  : rawUrl;
                for (let attempt = 0; attempt < 15; attempt++) {
                  const wins = await new Promise(r => chrome.windows.getAll({ populate: true }, w => r(w || [])));
                  if (targetUrl && chrome.tabs && chrome.tabs.query) {
                    const tabs = await new Promise(r => chrome.tabs.query({}, t => r(t || [])));
                    const matchTab = tabs.find(t => urlsMatch(t.url, targetUrl) || urlsMatch(t.pendingUrl, targetUrl));
                    if (matchTab && matchTab.windowId != null) {
                      const w = wins.find(x => x.id === matchTab.windowId);
                      if (w) return w;
                      return {
                        id: matchTab.windowId,
                        focused: true,
                        alwaysOnTop: false,
                        incognito: false,
                        type: createData?.type || "popup",
                        state: "normal",
                        tabs: [matchTab]
                      };
                    }
                  }
                  const newWin = wins.find(w => !beforeIds.has(w.id));
                  if (newWin) return newWin;
                  await new Promise(r => setTimeout(r, 30));
                }
                return {
                  id: -1,
                  focused: true,
                  alwaysOnTop: false,
                  incognito: false,
                  type: createData?.type || "popup",
                  state: "normal",
                  tabs: []
                };
              };
              const patchedWindowCreate = function(createData, callback) {
                if (typeof createData === "function") {
                  callback = createData;
                  createData = {};
                }
                const run = async () => {
                  const beforeWins = await new Promise(r => chrome.windows.getAll({}, w => r(w || [])));
                  const beforeIds = new Set(beforeWins.map(w => w.id));
                  const safeData = Object.assign({}, createData || {});
                  delete safeData.left;
                  delete safeData.top;
                  delete safeData.width;
                  delete safeData.height;
                  const rawResult = await new Promise(r => {
                    try {
                      origWindowCreate(safeData, w => r(w));
                    } catch (e) {
                      r(undefined);
                    }
                  });
                  if (rawResult && typeof rawResult.id === "number") return rawResult;
                  return await resolveCreatedWindow(createData, beforeIds);
                };
                const promise = createQueue.then(run, run);
                createQueue = promise.catch(() => {});
                if (typeof callback === "function") {
                  promise.then(w => callback(w));
                  return;
                }
                return promise;
              };
              patchedWindowCreate.__mundoPatched = true;
              chrome.windows.create = patchedWindowCreate;
            }
            if (chrome.windows.update && !chrome.windows.update.__mundoPatched) {
              const origWindowUpdate = chrome.windows.update.bind(chrome.windows);
              const patchedWindowUpdate = function(winId, updateInfo, callback) {
                let called = false;
                const safeCb = (w) => {
                  if (!called) {
                    called = true;
                    if (typeof callback === "function") {
                      try { callback(w || { id: winId }); } catch (e) {}
                    }
                  }
                };
                setTimeout(() => safeCb({ id: winId }), 150);
                try {
                  const res = origWindowUpdate(winId, updateInfo, safeCb);
                  if (res && typeof res.then === "function") {
                    return Promise.race([res, new Promise(r => setTimeout(() => r({ id: winId }), 150))]);
                  }
                } catch (e) {
                  safeCb({ id: winId });
                }
              };
              patchedWindowUpdate.__mundoPatched = true;
              chrome.windows.update = patchedWindowUpdate;
            }
            if (chrome.windows.remove && !chrome.windows.remove.__mundoPatched) {
              const origWindowRemove = chrome.windows.remove.bind(chrome.windows);
              const patchedWindowRemove = function(winId, callback) {
                let called = false;
                const safeCb = () => {
                  if (!called) {
                    called = true;
                    if (typeof callback === "function") {
                      try { callback(); } catch (e) {}
                    }
                  }
                };
                setTimeout(safeCb, 150);
                try {
                  const res = origWindowRemove(winId, safeCb);
                  if (res && typeof res.then === "function") {
                    return Promise.race([res, new Promise(r => setTimeout(r, 150))]);
                  }
                } catch (e) {
                  safeCb();
                }
              };
              patchedWindowRemove.__mundoPatched = true;
              chrome.windows.remove = patchedWindowRemove;
            }
          }
          if (chrome.tabs && chrome.tabs.create && !chrome.tabs.create.__mundoPatched) {
            const origTabCreate = chrome.tabs.create.bind(chrome.tabs);
            const resolveCreatedTab = async (createProperties, beforeTabIds) => {
              const rawUrl = createProperties?.url;
              const targetUrl = rawUrl && !rawUrl.includes("://") && chrome.runtime && chrome.runtime.getURL
                ? chrome.runtime.getURL(rawUrl)
                : rawUrl;
              for (let attempt = 0; attempt < 15; attempt++) {
                const tabs = await new Promise(r => chrome.tabs.query({}, t => r(t || [])));
                if (targetUrl) {
                  const matchTab = tabs.find(t => urlsMatch(t.url, targetUrl) || urlsMatch(t.pendingUrl, targetUrl));
                  if (matchTab) return matchTab;
                }
                const newTab = tabs.find(t => t.id != null && !beforeTabIds.has(t.id));
                if (newTab) return newTab;
                await new Promise(r => setTimeout(r, 30));
              }
              return { id: -1, windowId: -1, index: 0, active: true, highlighted: true, pinned: false, incognito: false, url: targetUrl || "about:blank" };
            };
            const patchedTabCreate = function(createProperties, callback) {
              if (typeof createProperties === "function") {
                callback = createProperties;
                createProperties = {};
              }
              const run = async () => {
                const beforeTabs = await new Promise(r => chrome.tabs.query({}, t => r(t || [])));
                const beforeIds = new Set(beforeTabs.map(t => t.id).filter(id => id != null));
                const rawResult = await new Promise(r => {
                  try {
                    origTabCreate(createProperties || {}, t => r(t));
                  } catch (e) {
                    r(undefined);
                  }
                });
                if (rawResult && typeof rawResult.id === "number") return rawResult;
                return await resolveCreatedTab(createProperties, beforeIds);
              };
              const promise = run();
              if (typeof callback === "function") {
                promise.then(t => callback(t));
                return;
              }
              return promise;
            };
            patchedTabCreate.__mundoPatched = true;
            chrome.tabs.create = patchedTabCreate;
          }
        })();
        /* MUNDO_WV2_EXTENSION_SHIM_END */
        """;

    private readonly ExtensionDownloader _downloader;
    private readonly string _extensionsPath;

    public ExtensionService(ExtensionDownloader downloader)
    {
        _downloader = downloader;
        _extensionsPath = Path.Combine(AppRuntime.LocalDataDirectory, "Extensions");
        Directory.CreateDirectory(_extensionsPath);
    }

    public static void EnsureAllInstalledExtensionsPatched(string extensionsRootPath)
    {
        try
        {
            if (!Directory.Exists(extensionsRootPath))
                return;

            foreach (var dir in Directory.GetDirectories(extensionsRootPath))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!IsReadableJsonFile(manifestPath))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    EnsureExtensionCompatibilityShim(dir, doc.RootElement);
                }
                catch
                {
                    // Ignore malformed extension manifests
                }
            }
        }
        catch
        {
            // Ignore directory enumeration errors
        }
    }

    public static bool IsExtensionPopupUri(string? uri, IEnumerable<ExtensionInfo>? installedExtensions = null)
    {
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
            return false;

        if (uri.Contains("uilocation=popout", StringComparison.OrdinalIgnoreCase)
            || uri.Contains("uilocation=popup", StringComparison.OrdinalIgnoreCase)
            || uri.Contains("singleActionPopout=", StringComparison.OrdinalIgnoreCase)
            || uri.Contains("#/fido2", StringComparison.OrdinalIgnoreCase)
            || uri.Contains("/popup/", StringComparison.OrdinalIgnoreCase)
            || uri.Contains("/popup.html", StringComparison.OrdinalIgnoreCase)
            || uri.Contains("/notification/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (installedExtensions != null)
        {
            string baseUri = StripQueryAndHash(uri);
            foreach (var ext in installedExtensions)
            {
                if (string.IsNullOrWhiteSpace(ext.PopupUrl))
                    continue;

                if (baseUri.Equals(StripQueryAndHash(ext.PopupUrl), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static string StripQueryAndHash(string url)
    {
        int qIdx = url.IndexOfAny(['?', '#']);
        return qIdx >= 0 ? url[..qIdx] : url;
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

                        if (MatchesManifest(dir, root, ext.Id, ext.Name))
                        {
                            matchedDir = dir;
                            matchedStoreId = Path.GetFileName(dir);
                            matchedDoc = doc;
                            matchedRoot = root;
                            break;
                        }

                        doc.Dispose();
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
                bool wasPatched = false;
                try
                {
                    wasPatched = EnsureExtensionCompatibilityShim(matchedDir, matchedRoot.Value);
                    ProcessManifest(matchedRoot.Value, matchedDir, ext.Id, info);
                }
                finally
                {
                    matchedDoc?.Dispose();
                }

                if (wasPatched)
                {
                    try
                    {
                        await profile.AddBrowserExtensionAsync(matchedDir);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExtensionService] Failed to reload patched extension {ext.Id}: {ex.Message}");
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
        var manifestPath = Path.Combine(path, "manifest.json");
        JsonDocument? manifestDoc = null;

        if (IsReadableJsonFile(manifestPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                manifestDoc = JsonDocument.Parse(json);
                EnsureExtensionCompatibilityShim(path, manifestDoc.RootElement);
            }
            catch { }
        }

        try
        {
            var extension = await profile.AddBrowserExtensionAsync(path);
            if (extension == null) throw new InvalidOperationException("Failed to load extension into profile after download.");

            var info = new ExtensionInfo(extension.Id, extension.Name, true, extensionId, path);
            if (manifestDoc != null)
            {
                try
                {
                    ProcessManifest(manifestDoc.RootElement, path, extension.Id, info);
                }
                catch { }
            }

            return info;
        }
        finally
        {
            manifestDoc?.Dispose();
        }
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
            return MatchesManifest(dir, doc.RootElement, ext.Id, ext.Name);
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

    public static bool MatchesManifest(string extensionDir, JsonElement root, string extensionId, string? extensionName)
    {
        if (extensionId.Equals(Path.GetFileName(extensionDir), StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(extensionName))
            return false;

        var manifestName = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        foreach (var candidate in ResolveLocalizedNames(manifestName, extensionDir, root))
        {
            if (extensionName.Contains(candidate, StringComparison.OrdinalIgnoreCase)
                || candidate.Contains(extensionName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var shortName = root.TryGetProperty("short_name", out var sn) ? sn.GetString() : null;
        foreach (var candidate in ResolveLocalizedNames(shortName, extensionDir, root))
        {
            if (extensionName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> ResolveLocalizedNames(string? name, string extensionDir, JsonElement root)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        if (!name.StartsWith("__MSG_", StringComparison.Ordinal) || !name.EndsWith("__", StringComparison.Ordinal))
        {
            yield return name;
            yield break;
        }

        var key = name[6..^2];
        var localesPath = ResolveExtensionPath(extensionDir, "_locales");
        if (localesPath == null || !Directory.Exists(localesPath))
        {
            yield return name;
            yield break;
        }

        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultLocale = root.TryGetProperty("default_locale", out var locale) ? (locale.GetString() ?? "en") : "en";
        var seenLocales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var yieldedAny = false;

        string[] searchLocales =
        [
            uiCulture.Name.Replace('-', '_'),
            uiCulture.TwoLetterISOLanguageName,
            defaultLocale,
            "fr",
            "en_US",
            "en"
        ];

        foreach (var loc in searchLocales)
        {
            if (string.IsNullOrWhiteSpace(loc) || !seenLocales.Add(loc))
                continue;

            var msgPath = ResolveExtensionPath(extensionDir, Path.Combine("_locales", loc, "messages.json"));
            if (msgPath == null || !File.Exists(msgPath))
                continue;

            var val = GetMessageValue(msgPath, key);
            if (!string.IsNullOrWhiteSpace(val))
            {
                yieldedAny = true;
                yield return val;
            }
        }

        if (!yieldedAny)
            yield return name;
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
                return msg.GetString();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Object
                    && prop.Value.TryGetProperty("message", out var ciMsg))
                {
                    return ciMsg.GetString();
                }
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
        else if (root.TryGetProperty("action", out var actionForIcon) && actionForIcon.TryGetProperty("default_icon", out var actionIcons))
            iconPath = GetBestIconPath(actionIcons);
        else if (root.TryGetProperty("browser_action", out var browserActionForIcon) && browserActionForIcon.TryGetProperty("default_icon", out var browserActionIcons))
            iconPath = GetBestIconPath(browserActionIcons);
            
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

    public static bool EnsureExtensionCompatibilityShim(string extensionDir, JsonElement manifestRoot)
    {
        bool patchedAny = false;

        try
        {
            if (!manifestRoot.TryGetProperty("background", out var bg) || bg.ValueKind != JsonValueKind.Object)
                return false;

            if (bg.TryGetProperty("service_worker", out var swProp) && swProp.ValueKind == JsonValueKind.String)
            {
                var swPath = ResolveExtensionPath(extensionDir, swProp.GetString() ?? string.Empty);
                if (EnsureExtensionScriptPatched(swPath))
                    patchedAny = true;
            }

            if (bg.TryGetProperty("scripts", out var scriptsProp) && scriptsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var scriptItem in scriptsProp.EnumerateArray())
                {
                    if (scriptItem.ValueKind != JsonValueKind.String)
                        continue;

                    var scriptPath = ResolveExtensionPath(extensionDir, scriptItem.GetString() ?? string.Empty);
                    if (EnsureExtensionScriptPatched(scriptPath))
                        patchedAny = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtensionService] Failed to apply compatibility shim in {extensionDir}: {ex.Message}");
        }

        return patchedAny;
    }

    private static bool EnsureExtensionScriptPatched(string? scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            return false;

        try
        {
            var content = File.ReadAllText(scriptPath);
            if (content.Contains(WebView2ExtensionShimMarker, StringComparison.Ordinal))
                return false;

            content = StripLegacyExtensionShim(content);
            File.WriteAllText(scriptPath, WebView2ExtensionShimScript + "\n" + content);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtensionService] Failed to patch extension script {scriptPath}: {ex.Message}");
            return false;
        }
    }

    private static string StripLegacyExtensionShim(string content)
    {
        int endMarkerIdx = content.IndexOf(WebView2ExtensionShimEndMarker, StringComparison.Ordinal);
        if (endMarkerIdx >= 0)
        {
            return content[(endMarkerIdx + WebView2ExtensionShimEndMarker.Length)..].TrimStart('\r', '\n');
        }

        int v1Idx = content.IndexOf(LegacyShimMarkerV1, StringComparison.Ordinal);
        if (v1Idx >= 0)
        {
            int patchTabIdx = content.IndexOf("chrome.tabs.create = patchedTabCreate;", v1Idx, StringComparison.Ordinal);
            if (patchTabIdx >= 0)
            {
                int iifeEnd = content.IndexOf("})();", patchTabIdx, StringComparison.Ordinal);
                if (iifeEnd >= 0)
                {
                    return content[(iifeEnd + "})();".Length)..].TrimStart('\r', '\n');
                }
            }
        }

        return content;
    }
}

