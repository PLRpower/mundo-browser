using System.Collections.Generic;
using MundoBrowser.Models;

namespace MundoBrowser.Interfaces
{
    /// <summary>
    /// Manages the browser history, including adding, searching, and clearing entries.
    /// </summary>
    public interface IHistoryManager
    {
        /// <summary>
        /// Adds a new entry to the history or updates an existing one.
        /// </summary>
        void AddEntry(string url, string title = "");

        /// <summary>
        /// Searches the history for entries matching the query.
        /// </summary>
        List<HistoryEntry> SearchHistory(string query, int maxResults = 10);

        /// <summary>
        /// Gets the most recent history entries.
        /// </summary>
        List<HistoryEntry> GetRecentHistory(int count = 20);

        /// <summary>
        /// Gets the most visited history entries.
        /// </summary>
        List<HistoryEntry> GetMostVisited(int count = 10);

        /// <summary>
        /// Clears all history entries.
        /// </summary>
        void ClearHistory();
    }
}
