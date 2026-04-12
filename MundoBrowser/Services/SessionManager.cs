using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using MundoBrowser.ViewModels;
using MundoBrowser.Models;

namespace MundoBrowser.Services
{
    public class TabSessionData
    {
        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "https://www.google.com";
        public string? FaviconUrl { get; set; }
        public int SlotIndex { get; set; } = -1; // -1 for regular tabs, 0+ for pinned slots
    }

    public class SessionData
    {
        public List<TabSessionData> Tabs { get; set; } = new();
        public List<TabSessionData> PinnedTabs { get; set; } = new();
        public int SelectedTabIndex { get; set; } = 0;
        public bool IsSelectedTabPinned { get; set; } = false;
        
        // Window State
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 800;
        public double WindowLeft { get; set; } = 100;
        public double WindowTop { get; set; } = 100;
        public int WindowState { get; set; } = 0; // 0: Normal, 1: Minimized, 2: Maximized
    }

    public class SessionManager
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

        public async void SaveSession(MainViewModel vm)
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

        public async Task<string?> SaveFaviconLocally(Stream stream, string url)
        {
            if (stream == null || string.IsNullOrEmpty(url)) return null;

            try
            {
                string hash = GetStringHash(url);
                string fileName = $"{hash}.png";
                string fullPath = Path.Combine(_faviconsPath, fileName);

                using (var fileStream = File.Create(fullPath))
                {
                    await stream.CopyToAsync(fileStream);
                }

                return new Uri(fullPath).AbsoluteUri;
            }
            catch { return null; }
        }

        private string GetStringHash(string text)
        {
            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(text);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return Convert.ToHexString(hashBytes);
            }
        }
    }
}
