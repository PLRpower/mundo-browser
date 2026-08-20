using System.IO;
using System.Text.Json;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;

namespace MundoBrowser.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private const long MaxSettingsFileBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _sync = new();
    private readonly string _settingsFilePath;
    private readonly string _backupFilePath;

    public AppSettings Current { get; }
    public event Action<AppSettings>? SettingsChanged;

    public AppSettingsService()
    {
        var appFolder = AppRuntime.LocalDataDirectory;
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
        SettingsChanged?.Invoke(Current);
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
            if (!File.Exists(path) || new FileInfo(path).Length > MaxSettingsFileBytes)
                return null;

            string content = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings from {path}: {ex.Message}");
            return null;
        }
    }

    private void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            string tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    File.Replace(tempPath, _settingsFilePath, _backupFilePath, ignoreMetadataErrors: true);
                }
                catch
                {
                    if (File.Exists(_backupFilePath))
                        File.Delete(_backupFilePath);
                    File.Move(_settingsFilePath, _backupFilePath);
                    File.Move(tempPath, _settingsFilePath);
                }
            }
            else
            {
                File.Move(tempPath, _settingsFilePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.SearchEngine = SearchEngineHelper.NormalizeSearchEngine(settings.SearchEngine);
        settings.CustomSearchUrl = settings.CustomSearchUrl?.Trim() ?? string.Empty;

        if (settings.UseSearchEngineAsStartPage)
        {
            settings.StartPage = SearchEngineHelper.GetEngineHomeUrl(settings.SearchEngine, settings.CustomSearchUrl);
        }
        else
        {
            settings.StartPage = NormalizeStartPage(settings.StartPage);
        }

        settings.EcoModeMinutes = Math.Clamp(settings.EcoModeMinutes, 1, 1440);
        settings.SidebarWidth = Math.Clamp(settings.SidebarWidth, 200, 400);
        settings.AdBlockDisabledSites = (settings.AdBlockDisabledSites ?? [])
            .Select(site => site.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(site => !string.IsNullOrWhiteSpace(site))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(site => site, StringComparer.OrdinalIgnoreCase)
            .ToList();

        settings.CookieBlockDisabledSites = (settings.CookieBlockDisabledSites ?? [])
            .Select(site => site.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(site => !string.IsNullOrWhiteSpace(site))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(site => site, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.ProtectionDisabledSites != null && settings.ProtectionDisabledSites.Count > 0)
        {
            foreach (var legacySite in settings.ProtectionDisabledSites)
            {
                var cleanSite = legacySite.Trim().TrimEnd('.').ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(cleanSite))
                {
                    if (!settings.AdBlockDisabledSites.Contains(cleanSite, StringComparer.OrdinalIgnoreCase))
                        settings.AdBlockDisabledSites.Add(cleanSite);
                    if (!settings.CookieBlockDisabledSites.Contains(cleanSite, StringComparer.OrdinalIgnoreCase))
                        settings.CookieBlockDisabledSites.Add(cleanSite);
                }
            }
            settings.ProtectionDisabledSites.Clear();
        }
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
