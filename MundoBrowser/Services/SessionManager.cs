using System.IO;
using System.Text.Json;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;

namespace MundoBrowser.Services;

/// <summary>
/// Default implementation of ISessionManager for browser session persistence.
/// </summary>
public class SessionManager : ISessionManager
{
    private const long MaxSessionFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private readonly string _sessionFilePath;
    private readonly string _sessionBackupPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public SessionManager()
    {
        var appFolder = AppRuntime.LocalDataDirectory;
        Directory.CreateDirectory(appFolder);
        _sessionFilePath = Path.Combine(appFolder, "last_session.json");
        _sessionBackupPath = Path.Combine(appFolder, "last_session.json.bak");
        
        var faviconsPath = Path.Combine(appFolder, "Favicons");
        Directory.CreateDirectory(faviconsPath);
    }

    /// <inheritdoc/>
    public async Task SaveSessionAsync(SessionData sessionData)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await AtomicFileHelper.WriteJsonAtomicAsync(_sessionFilePath, sessionData, IndentedJsonOptions).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public SessionData? LoadSession()
    {
        return TryLoadSession(_sessionFilePath) ?? TryLoadSession(_sessionBackupPath);
    }

    private static SessionData? TryLoadSession(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            if (stream.Length > MaxSessionFileBytes)
                return null;

            return JsonSerializer.Deserialize<SessionData>(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load session from {path}: {ex.Message}");
        }

        return null;
    }
}
