using System.IO;
using System.Text.Json;
using MundoBrowser.Helpers;
using MundoBrowser.Models;
using MundoBrowser.Interfaces;

namespace MundoBrowser.Services
{
    public class HistoryManager : IHistoryManager
    {
        private readonly string _historyFilePath;
        private readonly string _historyBackupPath;
        private readonly List<HistoryEntry> _history;
        private const int MaxHistoryEntries = 1000;
        private const long MaxHistoryFileBytes = 16 * 1024 * 1024;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly object _historyLock = new();
        private CancellationTokenSource? _saveDebounceCts;
        private static readonly JsonSerializerOptions JsonOptions = new();

        public HistoryManager()
        {
            var appDataPath = AppRuntime.RoamingDataDirectory;
            
            Directory.CreateDirectory(appDataPath);
            _historyFilePath = Path.Combine(appDataPath, "history.json");
            _historyBackupPath = _historyFilePath + ".bak";
            _history = LoadHistory();
        }

        private List<HistoryEntry> LoadHistory()
        {
            var history = TryLoadHistory(_historyFilePath)
                          ?? TryLoadHistory(_historyBackupPath)
                          ?? [];
            return history.Take(MaxHistoryEntries).ToList();
        }

        private static List<HistoryEntry>? TryLoadHistory(string path)
        {
            try
            {
                return IsReadableHistoryFile(path)
                    ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path))
                    : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading history from {path}: {ex.Message}");
                return null;
            }
        }

        private static bool IsReadableHistoryFile(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length <= MaxHistoryFileBytes;
            }
            catch
            {
                return false;
            }
        }

        private void SaveHistory()
        {
            var cts = new CancellationTokenSource();
            var previousCts = Interlocked.Exchange(ref _saveDebounceCts, cts);
            TryCancel(previousCts);
            _ = SaveHistoryAsync(cts);
        }

        private async Task SaveHistoryAsync(CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(300, cts.Token).ConfigureAwait(false);
                await PersistHistoryAsync(cts.Token).ConfigureAwait(false);
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
                Interlocked.CompareExchange(ref _saveDebounceCts, null, cts);
                cts.Dispose();
            }
        }

        private async Task PersistHistoryAsync(CancellationToken cancellationToken)
        {
            await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var temporaryPath = _historyFilePath + ".tmp";
            try
            {
                List<HistoryEntry> snapshot;
                lock (_historyLock)
                {
                    snapshot = _history.ToList();
                }

                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(_historyFilePath))
                    File.Replace(temporaryPath, _historyFilePath, _historyBackupPath, ignoreMetadataErrors: true);
                else
                    File.Move(temporaryPath, _historyFilePath);
            }
            catch
            {
                TryDeleteFile(temporaryPath);
                throw;
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async Task FlushAsync()
        {
            var pendingSave = Interlocked.Exchange(ref _saveDebounceCts, null);
            TryCancel(pendingSave);
            try
            {
                await PersistHistoryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error flushing history: {ex.Message}");
            }
        }

        private static void TryCancel(CancellationTokenSource? cts)
        {
            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The scheduled save completed between the exchange and cancellation.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup only.
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
