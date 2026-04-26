using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
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
        private readonly string _sessionFilePath;
        private readonly string _faviconsPath;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        public SessionManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "MundoBrowser");
            Directory.CreateDirectory(appFolder);
            _sessionFilePath = Path.Combine(appFolder, "last_session.json");
            
            _faviconsPath = Path.Combine(appFolder, "Favicons");
            Directory.CreateDirectory(_faviconsPath);
        }

        /// <inheritdoc/>
        public async Task SaveSessionAsync(MainViewModel vm)
        {
            await _saveLock.WaitAsync();
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
                        FaviconUrl = tab.FaviconUrl
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
                await File.WriteAllTextAsync(_sessionFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <inheritdoc/>
        public SessionData? LoadSession()
        {
            try
            {
                if (File.Exists(_sessionFilePath))
                {
                    var json = File.ReadAllText(_sessionFilePath);
                    return JsonSerializer.Deserialize<SessionData>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load session: {ex.Message}");
            }
            return null;
        }
    }
}

