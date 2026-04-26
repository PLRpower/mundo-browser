using System.Threading.Tasks;
using MundoBrowser.Services;
using MundoBrowser.ViewModels;
using MundoBrowser.Models;

namespace MundoBrowser.Interfaces
{
    /// <summary>
    /// Manages browser session persistence, including tabs and window state.
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// Saves the current session state based on the provided MainViewModel.
        /// </summary>
        Task SaveSessionAsync(MainViewModel vm);

        /// <summary>
        /// Loads the previously saved session data.
        /// </summary>
        SessionData? LoadSession();
    }
}
