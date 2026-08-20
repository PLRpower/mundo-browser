using System.IO;
using System.Text.Json;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;

namespace MundoBrowser.Services;

public class HistoryManager : IHistoryManager
{
    private const int MaxHistoryEntries = 1000;
    private const long MaxHistoryFileBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly string _historyFilePath;
    private readonly string _historyBackupPath;
    private readonly List<HistoryEntry> _history;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Lock _historyLock = new();
    private CancellationTokenSource? _saveDebounceCts;

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
            if (!IsReadableHistoryFile(path))
                return null;

            string content = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(content);
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
        try
        {
            List<HistoryEntry> snapshot;
            lock (_historyLock)
            {
                snapshot = [.. _history];
            }

            await AtomicFileHelper.WriteJsonAtomicAsync(_historyFilePath, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
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

    public void AddEntry(string url, string title = "")
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        url = url.Trim();

        lock (_historyLock)
        {
            var existing = _history.FirstOrDefault(h => h.Url.Equals(url, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.VisitCount++;
                existing.VisitedAt = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(title))
                    existing.Title = title;
            }
            else
            {
                _history.Insert(0, new HistoryEntry
                {
                    Url = url,
                    Title = title,
                    VisitedAt = DateTime.Now,
                    VisitCount = 1
                });

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
            return [];

        query = query.Trim();

        lock (_historyLock)
        {
            var matches = new List<HistoryEntry>();
            for (int i = 0; i < _history.Count; i++)
            {
                var entry = _history[i];
                if (entry.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(entry.Title) && entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(entry);
                }
            }

            matches.Sort((a, b) =>
            {
                int cmp = b.VisitCount.CompareTo(a.VisitCount);
                return cmp != 0 ? cmp : b.VisitedAt.CompareTo(a.VisitedAt);
            });

            if (matches.Count <= maxResults)
                return matches;

            return matches.Take(maxResults).ToList();
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
