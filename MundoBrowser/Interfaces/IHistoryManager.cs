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

        Task FlushAsync();

        /// <summary>
        /// Clears all history entries.
        /// </summary>
        void ClearHistory();
    }
}
