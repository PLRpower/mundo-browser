using System.IO;
using System.Text.Json;
using System.Threading;
using MundoBrowser.ViewModels;
using System.Collections.ObjectModel;

namespace MundoBrowser.Services
{
    public class SessionData
    {
        public List<TabSessionData> Tabs { get; set; } = new();
        public int SelectedTabIndex { get; set; } = 0;
    }

    public class TabSessionData
    {
        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "https://www.google.com";
    }

    public class SessionManager
    {
        private readonly string _sessionFilePath;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        public SessionManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "MundoBrowser");
            Directory.CreateDirectory(appFolder);
            _sessionFilePath = Path.Combine(appFolder, "last_session.json");
        }

        public async void SaveSession(ObservableCollection<TabViewModel> tabs, TabViewModel? selectedTab)
        {
            await _saveLock.WaitAsync();
            try
            {
                var sessionData = new SessionData();
                
                // Save tabs
                foreach (var tab in tabs)
                {
                    sessionData.Tabs.Add(new TabSessionData
                    {
                        Title = tab.Title,
                        Url = tab.Url
                    });
                }
                
                // Save selected index
                if (selectedTab != null)
                {
                    sessionData.SelectedTabIndex = tabs.IndexOf(selectedTab);
                }

                var json = JsonSerializer.Serialize(sessionData);
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
    }
}
