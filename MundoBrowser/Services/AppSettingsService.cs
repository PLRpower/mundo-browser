using System.IO;
using System.Text.Json;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;

namespace MundoBrowser.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _sync = new();
    private readonly string _settingsFilePath;
    private readonly string _backupFilePath;

    public AppSettings Current { get; }

    public AppSettingsService()
    {
        var appFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MundoBrowser");

        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "settings.json");
        _backupFilePath = Path.Combine(appFolder, "settings.json.bak");
        Current = LoadSettings();
        Normalize(Current);
    }

    public void Update(Action<AppSettings> update)
    {
        lock (_sync)
        {
            update(Current);
            Normalize(Current);
            SaveSettings();
        }
    }

    private AppSettings LoadSettings()
    {
        return TryLoad(_settingsFilePath)
               ?? TryLoad(_backupFilePath)
               ?? new AppSettings();
    }

    private static AppSettings? TryLoad(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings from {path}: {ex.Message}");
            return null;
        }
    }

    private void SaveSettings()
    {
        var temporaryPath = _settingsFilePath + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, JsonOptions));

            if (File.Exists(_settingsFilePath))
                File.Replace(temporaryPath, _settingsFilePath, _backupFilePath, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, _settingsFilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");

            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.StartPage = NormalizeStartPage(settings.StartPage);
        settings.EcoModeMinutes = Math.Clamp(settings.EcoModeMinutes, 1, 1440);
        settings.SidebarWidth = Math.Clamp(settings.SidebarWidth, 200, 400);
    }

    private static string NormalizeStartPage(string? value)
    {
        var startPage = value?.Trim();
        if (string.IsNullOrWhiteSpace(startPage))
            return "https://www.google.com";

        if (startPage.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || startPage.Contains("://", StringComparison.Ordinal))
            return startPage;

        return "https://" + startPage;
    }
}
