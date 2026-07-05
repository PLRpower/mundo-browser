using System.IO;
using System.Text.Json;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;
using MundoBrowser.Models;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services
{
    /// <summary>
    /// Default implementation of ISessionManager for browser session persistence.
    /// </summary>
    public class SessionManager : ISessionManager
    {
        private const long MaxSessionFileBytes = 8 * 1024 * 1024;
        private readonly string _sessionFilePath;
        private readonly string _sessionBackupPath;
        private readonly string _faviconsPath;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        public SessionManager()
        {
            var appFolder = AppRuntime.LocalDataDirectory;
            Directory.CreateDirectory(appFolder);
            _sessionFilePath = Path.Combine(appFolder, "last_session.json");
            _sessionBackupPath = Path.Combine(appFolder, "last_session.json.bak");
            
            _faviconsPath = Path.Combine(appFolder, "Favicons");
            Directory.CreateDirectory(_faviconsPath);
        }

        /// <inheritdoc/>
        public async Task SaveSessionAsync(MainViewModel vm)
        {
            await _saveLock.WaitAsync();
            var temporaryPath = _sessionFilePath + ".tmp";
            try
            {
                var sessionData = new SessionData();
                
                // Save window state
                sessionData.WindowWidth = vm.WindowWidth;
                sessionData.WindowHeight = vm.WindowHeight;
                sessionData.WindowLeft = vm.WindowLeft;
                sessionData.WindowTop = vm.WindowTop;
                sessionData.WindowState = (int)vm.WindowState;

                // Save regular tabs
                foreach (var tab in vm.Tabs)
                {
                    sessionData.Tabs.Add(new TabSessionData
                    {
                        Title = tab.Title,
                        Url = tab.Url,
                        FaviconRelativePath = tab.FaviconRelativePath,
                        FaviconUrl = tab.FaviconUrl,
                        ZoomFactor = tab.ZoomFactor
                    });
                }

                // Save pinned tabs
                foreach (var pinned in vm.PinnedTabs)
                {
                    if (pinned.Tab != null)
                    {
                        sessionData.PinnedTabs.Add(new TabSessionData
                        {
                            Title = pinned.Tab.Title,
                            Url = pinned.Tab.Url,
                            FaviconRelativePath = pinned.Tab.FaviconRelativePath,
                            FaviconUrl = pinned.Tab.FaviconUrl,
                            ZoomFactor = pinned.Tab.ZoomFactor,
                            SlotIndex = pinned.SlotIndex
                        });
                    }
                }
                
                // Save selection
                var selectedTab = vm.SelectedTab;
                if (selectedTab != null)
                {
                    int index = vm.Tabs.IndexOf(selectedTab);
                    if (index >= 0)
                    {
                        sessionData.SelectedTabIndex = index;
                        sessionData.IsSelectedTabPinned = false;
                    }
                    else
                    {
                        var pinned = vm.PinnedTabs.FirstOrDefault(p => p.Tab == selectedTab);
                        if (pinned != null)
                        {
                            sessionData.SelectedTabIndex = pinned.SlotIndex;
                            sessionData.IsSelectedTabPinned = true;
                        }
                    }
                }

                var json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);

                if (File.Exists(_sessionFilePath))
                {
                    try
                    {
                        File.Replace(temporaryPath, _sessionFilePath, _sessionBackupPath, ignoreMetadataErrors: true);
                    }
                    catch
                    {
                        if (File.Exists(_sessionBackupPath))
                            File.Delete(_sessionBackupPath);
                        File.Move(_sessionFilePath, _sessionBackupPath);
                        File.Move(temporaryPath, _sessionFilePath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, _sessionFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <inheritdoc/>
        public SessionData? LoadSession()
        {
            return TryLoadSession(_sessionFilePath) ?? TryLoadSession(_sessionBackupPath);
        }

        private static SessionData? TryLoadSession(string path)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length <= MaxSessionFileBytes)
                    return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load session from {path}: {ex.Message}");
            }

            return null;
        }
    }
}

