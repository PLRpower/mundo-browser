using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MundoBrowser.Services
{
    public static class ExtensionPatcher
    {
        private const string PolyfillFileName = "mundo_polyfill.js";
        private const string LocalApiUrl = "http://127.0.0.1:50000/*";

        private const string PolyfillCode = @"
// MundoBrowser Extension Polyfill
// Bridges the gap between WebView2 and Chrome Extension APIs
(function() {
    // Avoid double initialization
    if (globalThis._mundoPolyfillInjected) return;
    globalThis._mundoPolyfillInjected = true;

    globalThis.chrome = globalThis.chrome || {};
    globalThis.chrome.tabs = globalThis.chrome.tabs || {};
    globalThis.chrome.scripting = globalThis.chrome.scripting || {};

    // Falsifier la demande de l'onglet actif (Pour Bitwarden)
    const _originalQuery = globalThis.chrome.tabs.query;
    globalThis.chrome.tabs.query = async function(queryInfo, callback) {
        try {
            let res = await fetch('http://127.0.0.1:50000/api/tabs/active');
            let tab = await res.json();
            if (callback) callback([tab]);
            return [tab];
        } catch(e) {
            console.error('MundoBrowser Polyfill: /api/tabs/active failed', e);
            if (_originalQuery) return _originalQuery(queryInfo, callback);
        }
    };

    // Falsifier l'exécution de script (Pour Wappalyzer)
    const _originalExecuteScript = globalThis.chrome.scripting.executeScript;
    globalThis.chrome.scripting.executeScript = async function(details, callback) {
        try {
            let res = await fetch('http://127.0.0.1:50000/api/tabs/execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(details)
            });
            let result = await res.json();
            
            // Format to match Chrome Extension result
            let formattedResult = [{
                documentId: '1',
                frameId: 0,
                result: result.result !== 'null' ? JSON.parse(result.result) : null
            }];
            
            if (callback) callback(formattedResult);
            return formattedResult;
        } catch(e) {
            console.error('MundoBrowser Polyfill: /api/tabs/execute failed', e);
            if (_originalExecuteScript) return _originalExecuteScript(details, callback);
        }
    };
})();
";

        public static void PatchExtension(string extensionFolder)
        {
            try
            {
                var manifestPath = Path.Combine(extensionFolder, "manifest.json");
                if (!File.Exists(manifestPath)) return;

                // 1. Write the polyfill to the extension folder
                File.WriteAllText(Path.Combine(extensionFolder, PolyfillFileName), PolyfillCode);

                // 2. Read and modify manifest
                string manifestContent = File.ReadAllText(manifestPath);
                var manifestJson = JsonNode.Parse(manifestContent)?.AsObject();
                if (manifestJson == null) return;

                bool isManifestV3 = manifestJson.TryGetPropertyValue("manifest_version", out var versionNode) && 
                                   versionNode?.GetValue<int>() == 3;

                // Add permissions
                var permKey = isManifestV3 ? "host_permissions" : "permissions";
                if (!manifestJson.ContainsKey(permKey))
                {
                    manifestJson[permKey] = new JsonArray();
                }

                if (manifestJson[permKey] is JsonArray permArray)
                {
                    // Check if already present
                    bool hasPerm = false;
                    foreach (var p in permArray)
                    {
                        if (p?.GetValue<string>() == LocalApiUrl)
                        {
                            hasPerm = true;
                            break;
                        }
                    }

                    if (!hasPerm)
                    {
                        permArray.Add(LocalApiUrl);
                    }
                }

                File.WriteAllText(manifestPath, manifestJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                // 3. Inject polyfill into background scripts
                if (manifestJson.TryGetPropertyValue("background", out var bgNode) && bgNode is JsonObject bgObj)
                {
                    // MV3 Service Worker
                    if (bgObj.TryGetPropertyValue("service_worker", out var swNode))
                    {
                        var swPath = swNode?.GetValue<string>();
                        if (!string.IsNullOrEmpty(swPath))
                        {
                            InjectIntoJsFile(Path.Combine(extensionFolder, swPath), $"importScripts('/{PolyfillFileName}');\n");
                        }
                    }
                    
                    // MV2 Scripts
                    if (bgObj.TryGetPropertyValue("scripts", out var scriptsNode) && scriptsNode is JsonArray scriptsArray)
                    {
                        foreach (var scriptNode in scriptsArray)
                        {
                            var scriptPath = scriptNode?.GetValue<string>();
                            if (!string.IsNullOrEmpty(scriptPath))
                            {
                                // We inject the polyfill code directly since importScripts isn't for normal pages
                                InjectIntoJsFile(Path.Combine(extensionFolder, scriptPath), PolyfillCode + "\n");
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to patch extension {extensionFolder}: {ex.Message}");
            }
        }

        private static void InjectIntoJsFile(string filePath, string injection)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                var content = File.ReadAllText(filePath);
                if (content.Contains("mundo_polyfill.js") || content.Contains("_mundoPolyfillInjected")) return; // Already injected
                
                File.WriteAllText(filePath, injection + content);
            }
            catch { }
        }
    }
}
