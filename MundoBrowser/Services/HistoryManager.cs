using System.IO;
using System.Text.Json;
using MundoBrowser.Models;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services
{
    public class HistoryManager : IHistoryManager
    {
        private readonly string _historyFilePath;
        private readonly List<HistoryEntry> _history;
        private const int MaxHistoryEntries = 1000;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly object _historyLock = new();
        private CancellationTokenSource? _saveDebounceCts;
        private static readonly JsonSerializerOptions JsonOptions = new();

        public HistoryManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MundoBrowser"
            );
            
            Directory.CreateDirectory(appDataPath);
            _historyFilePath = Path.Combine(appDataPath, "history.json");
            _history = LoadHistory();
        }

        private List<HistoryEntry> LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    return JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading history: {ex.Message}");
            }
            
            return new List<HistoryEntry>();
        }

        private void SaveHistory()
        {
            _saveDebounceCts?.Cancel();

            var cts = new CancellationTokenSource();
            _saveDebounceCts = cts;
            _ = SaveHistoryAsync(cts.Token);
        }

        private async Task SaveHistoryAsync(CancellationToken cancellationToken)
        {
            bool lockTaken = false;

            try
            {
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                lockTaken = true;

                List<HistoryEntry> snapshot;
                lock (_historyLock)
                {
                    snapshot = _history.ToList();
                }

                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                await File.WriteAllTextAsync(_historyFilePath, json, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // A newer save is already scheduled.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving history: {ex.Message}");
            }
            finally
            {
                if (lockTaken)
                    _saveLock.Release();
            }
        }

        public void AddEntry(string url, string title = "")
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            // Normalize URL
            url = url.Trim();

            lock (_historyLock)
            {
                // Check if URL already exists
                var existing = _history.FirstOrDefault(h => h.Url.Equals(url, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Update existing entry
                    existing.VisitCount++;
                    existing.VisitedAt = DateTime.Now;
                    if (!string.IsNullOrWhiteSpace(title))
                        existing.Title = title;
                }
                else
                {
                    // Add new entry
                    _history.Insert(0, new HistoryEntry
                    {
                        Url = url,
                        Title = title,
                        VisitedAt = DateTime.Now,
                        VisitCount = 1
                    });

                    // Limit history size
                    if (_history.Count > MaxHistoryEntries)
                    {
                        _history.RemoveRange(MaxHistoryEntries, _history.Count - MaxHistoryEntries);
                    }
                }
            }
            
            SaveHistory();
        }

        public List<HistoryEntry> SearchHistory(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<HistoryEntry>();

            lock (_historyLock)
            {
                return _history
                    .Where(h =>
                        h.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(h.Title) && h.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                    )
                    .OrderByDescending(h => h.VisitCount)
                    .ThenByDescending(h => h.VisitedAt)
                    .Take(maxResults)
                    .ToList();
            }
        }

        public List<HistoryEntry> GetRecentHistory(int count = 20)
        {
            lock (_historyLock)
            {
                return _history
                    .OrderByDescending(h => h.VisitedAt)
                    .Take(count)
                    .ToList();
            }
        }

        public List<HistoryEntry> GetMostVisited(int count = 10)
        {
            lock (_historyLock)
            {
                return _history
                    .OrderByDescending(h => h.VisitCount)
                    .ThenByDescending(h => h.VisitedAt)
                    .Take(count)
                    .ToList();
            }
        }

        public void ClearHistory()
        {
            lock (_historyLock)
            {
                _history.Clear();
            }

            SaveHistory();
        }
    }
}
