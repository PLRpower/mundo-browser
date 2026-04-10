using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MundoBrowser.Models;

namespace MundoBrowser.Services
{
    public class HistoryManager
    {
        private readonly string _historyFilePath;
        private readonly List<HistoryEntry> _history;
        private const int MaxHistoryEntries = 1000;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

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
            Task.Run(async () =>
            {
                await _saveLock.WaitAsync();
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(_history, options);
                    await File.WriteAllTextAsync(_historyFilePath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving history: {ex.Message}");
                }
                finally
                {
                    _saveLock.Release();
                }
            });
        }

        public void AddEntry(string url, string title = "")
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            // Normalize URL
            url = url.Trim();
            
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
            
            SaveHistory();
        }

        public List<HistoryEntry> SearchHistory(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<HistoryEntry>();

            query = query.ToLower();
            
            return _history
                .Where(h => 
                    h.Url.ToLower().Contains(query) || 
                    (!string.IsNullOrWhiteSpace(h.Title) && h.Title.ToLower().Contains(query))
                )
                .OrderByDescending(h => h.VisitCount)
                .ThenByDescending(h => h.VisitedAt)
                .Take(maxResults)
                .ToList();
        }

        public List<HistoryEntry> GetRecentHistory(int count = 20)
        {
            return _history
                .OrderByDescending(h => h.VisitedAt)
                .Take(count)
                .ToList();
        }

        public List<HistoryEntry> GetMostVisited(int count = 10)
        {
            return _history
                .OrderByDescending(h => h.VisitCount)
                .ThenByDescending(h => h.VisitedAt)
                .Take(count)
                .ToList();
        }

        public void ClearHistory()
        {
            _history.Clear();
            SaveHistory();
        }
    }
}
