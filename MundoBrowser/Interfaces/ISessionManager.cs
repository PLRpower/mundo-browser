using MundoBrowser.Models;

namespace MundoBrowser.Interfaces
{
    /// <summary>
    /// Manages browser session persistence, including tabs and window state.
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// Saves the current session state.
        /// </summary>
        Task SaveSessionAsync(SessionData sessionData);

        /// <summary>
        /// Loads the previously saved session data.
        /// </summary>
        SessionData? LoadSession();
    }
}
