using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MundoBrowser.Helpers;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Services;

public partial class WebViewService
{
    private void ApplySettingChange(string? key, JsonElement value)
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext as MainViewModel;

        switch (key)
        {
            case "startPage":
                _settingsService.Update(settings =>
                {
                    settings.StartPage = value.GetString() ?? "";
                    if (settings.UseSearchEngineAsStartPage &&
                        !string.Equals(settings.StartPage, SearchEngineHelper.GetEngineHomeUrl(settings.SearchEngine, settings.CustomSearchUrl), StringComparison.OrdinalIgnoreCase))
                    {
                        settings.UseSearchEngineAsStartPage = false;
                    }
                });
                BroadcastSettingsToPages();
                break;

            case "searchEngine":
                var searchEngine = SearchEngineHelper.NormalizeSearchEngine(value.GetString());
                _settingsService.Update(settings =>
                {
                    settings.SearchEngine = searchEngine;
                    if (settings.UseSearchEngineAsStartPage)
                    {
                        settings.StartPage = SearchEngineHelper.GetEngineHomeUrl(searchEngine, settings.CustomSearchUrl);
                    }
                });
                BroadcastSettingsToPages();
                break;

            case "customSearchUrl":
                var customUrl = value.GetString() ?? "";
                _settingsService.Update(settings =>
                {
                    settings.CustomSearchUrl = customUrl;
                    if (settings.SearchEngine == "custom" && settings.UseSearchEngineAsStartPage)
                    {
                        settings.StartPage = SearchEngineHelper.GetEngineHomeUrl("custom", customUrl);
                    }
                });
                BroadcastSettingsToPages();
                break;

            case "useSearchEngineAsStartPage":
                var useAsStartPage = value.GetBoolean();
                _settingsService.Update(settings =>
                {
                    settings.UseSearchEngineAsStartPage = useAsStartPage;
                    if (useAsStartPage)
                    {
                        settings.StartPage = SearchEngineHelper.GetEngineHomeUrl(settings.SearchEngine, settings.CustomSearchUrl);
                    }
                });
                BroadcastSettingsToPages();
                break;

            case "ecoModeEnabled":
                EcoModeEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.EcoModeEnabled = EcoModeEnabled);
                break;

            case "ecoModeDuration":
                EcoModeMinutes = ReadInt(value, 10);
                _settingsService.Update(settings => settings.EcoModeMinutes = EcoModeMinutes);
                EcoModeMinutes = _settingsService.Current.EcoModeMinutes;
                break;

            case "minimizeToTrayOnClose":
                _settingsService.Update(settings => settings.MinimizeToTrayOnClose = value.GetBoolean());
                break;

            case "sidebarVisible":
                if (vm != null)
                    vm.IsSidebarVisible = value.GetBoolean();
                else
                    _settingsService.Update(settings => settings.IsSidebarVisible = value.GetBoolean());
                break;

            case "sidebarWidth":
                var width = ReadDouble(value, 250);
                if (vm != null)
                    vm.SetSidebarWidth(width);
                else
                    _settingsService.Update(settings => settings.SidebarWidth = width);
                break;

            case "adBlockerEnabled":
                if (vm != null)
                    vm.IsAdBlockerEnabled = value.GetBoolean();
                else
                    _adBlockerService.IsAdBlockerEnabled = value.GetBoolean();
                break;

            case "cookieBlockerEnabled":
                if (vm != null)
                    vm.IsCookieBlockerEnabled = value.GetBoolean();
                else
                    _adBlockerService.IsCookieBlockerEnabled = value.GetBoolean();
                break;

            case "trackingPreventionEnabled":
                var enabled = value.GetBoolean();
                _settingsService.Update(settings => settings.IsTrackingPreventionEnabled = enabled);
                foreach (var webView in _webViews.Values)
                    ApplyTrackingPrevention(webView);
                break;

            case "passwordAutosaveEnabled":
                var passwordEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.IsPasswordAutosaveEnabled = passwordEnabled);
                foreach (var webView in _webViews.Values)
                    ApplyAutofillSettings(webView);
                break;

            case "generalAutofillEnabled":
                var generalEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.IsGeneralAutofillEnabled = generalEnabled);
                foreach (var webView in _webViews.Values)
                    ApplyAutofillSettings(webView);
                break;

            case "betaChannelEnabled":
                var betaEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.IsBetaChannelEnabled = betaEnabled);
                break;
        }
    }

    private void ApplyAutofillSettings(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2?.Settings == null)
                return;

            webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled =
                _settingsService.Current.IsPasswordAutosaveEnabled;
            webView.CoreWebView2.Settings.IsGeneralAutofillEnabled =
                _settingsService.Current.IsGeneralAutofillEnabled;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply autofill settings: {ex.Message}");
        }
    }

    private void ApplyTrackingPrevention(WebView2 webView)
    {
        try
        {
            webView.CoreWebView2.Profile.PreferredTrackingPreventionLevel =
                _settingsService.Current.IsTrackingPreventionEnabled
                    ? CoreWebView2TrackingPreventionLevel.Balanced
                    : CoreWebView2TrackingPreventionLevel.None;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply tracking prevention setting: {ex.Message}");
        }
    }

    private void BroadcastSettingsToPages()
    {
        foreach (var webView in _webViews.Values)
            PostSettingsToPage(webView);
    }

    private void PostSettingsToPage(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2 == null
                || !webView.CoreWebView2.Source.StartsWith(
                    "https://internals.mundobrowser/settings.html",
                    StringComparison.OrdinalIgnoreCase))
                return;

            var settings = _settingsService.Current;
            var message = JsonSerializer.Serialize(new
            {
                type = "initSettings",
                startPage = settings.StartPage,
                searchEngine = settings.SearchEngine,
                customSearchUrl = settings.CustomSearchUrl,
                useSearchEngineAsStartPage = settings.UseSearchEngineAsStartPage,
                ecoModeEnabled = settings.EcoModeEnabled,
                ecoModeDuration = settings.EcoModeMinutes,
                minimizeToTrayOnClose = settings.MinimizeToTrayOnClose,
                sidebarVisible = settings.IsSidebarVisible,
                sidebarWidth = settings.SidebarWidth,
                adBlockerEnabled = settings.IsAdBlockerEnabled,
                cookieBlockerEnabled = settings.IsCookieBlockerEnabled,
                trackingPreventionEnabled = settings.IsTrackingPreventionEnabled,
                passwordAutosaveEnabled = settings.IsPasswordAutosaveEnabled,
                generalAutofillEnabled = settings.IsGeneralAutofillEnabled,
                betaChannelEnabled = settings.IsBetaChannelEnabled
            });

            webView.CoreWebView2.PostWebMessageAsJson(message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to synchronize settings page: {ex.Message}");
        }
    }

    private static int ReadInt(JsonElement value, int fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        return int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    private static double ReadDouble(JsonElement value, double fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        return double.TryParse(
            value.GetString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number)
            ? number
            : fallback;
    }
}
