using System.Text.Json;
using CefSharp;
using CefSharp.Wpf.HwndHost;
using MundoBrowser.ViewModels;

namespace MundoBrowser.Services.Browser;

public partial class BrowserService
{
    private void ApplySettingChange(string? key, JsonElement value)
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext as MainViewModel;

        switch (key)
        {
            case "startPage":
                _settingsService.Update(settings => settings.StartPage = value.GetString() ?? "");
                break;
            case "ecoModeEnabled":
                EcoModeEnabled = value.GetBoolean();
                _settingsService.Update(settings => settings.EcoModeEnabled = EcoModeEnabled);
                break;
            case "ecoModeDuration":
                EcoModeMinutes = ReadInt(value, 10);
                _settingsService.Update(settings => settings.EcoModeMinutes = EcoModeMinutes);
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
                double width = ReadDouble(value, 250);
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
                _settingsService.Update(settings =>
                    settings.IsTrackingPreventionEnabled = value.GetBoolean());
                ApplyChromiumPreferences();
                break;
            case "passwordAutosaveEnabled":
                _settingsService.Update(settings =>
                    settings.IsPasswordAutosaveEnabled = value.GetBoolean());
                ApplyChromiumPreferences();
                break;
            case "generalAutofillEnabled":
                _settingsService.Update(settings =>
                    settings.IsGeneralAutofillEnabled = value.GetBoolean());
                ApplyChromiumPreferences();
                break;
        }
    }

    private void ApplyChromiumPreferences()
    {
        if (_requestContext == null)
            return;

        var settings = _settingsService.Current;
        _ = Cef.UIThreadTaskFactory.StartNew(() =>
        {
            SetPreference("enable_do_not_track", settings.IsTrackingPreventionEnabled);
            SetPreference("credentials_enable_service", settings.IsPasswordAutosaveEnabled);
            SetPreference("profile.password_manager_enabled", settings.IsPasswordAutosaveEnabled);
            SetPreference("autofill.enabled", settings.IsGeneralAutofillEnabled);
            SetPreference("autofill.profile_enabled", settings.IsGeneralAutofillEnabled);
            SetPreference("autofill.credit_card_enabled", settings.IsGeneralAutofillEnabled);
        });
    }

    private void SetPreference(string key, object value)
    {
        if (_requestContext == null)
            return;

        if (!_requestContext.SetPreference(key, value, out string? error) && !string.IsNullOrWhiteSpace(error))
            System.Diagnostics.Debug.WriteLine($"CEF preference '{key}' was not applied: {error}");
    }

    private void BroadcastSettingsToPages()
    {
        foreach (var browser in _browsers.Values)
            PostSettingsToPage(browser);
    }

    private void PostSettingsToPage(ChromiumWebBrowser browser)
    {
        if (!browser.Address.StartsWith(InternalSettingsUrl, StringComparison.OrdinalIgnoreCase))
            return;

        var settings = _settingsService.Current;
        string message = JsonSerializer.Serialize(new
        {
            type = "initSettings",
            startPage = settings.StartPage,
            ecoModeEnabled = settings.EcoModeEnabled,
            ecoModeDuration = settings.EcoModeMinutes,
            minimizeToTrayOnClose = settings.MinimizeToTrayOnClose,
            sidebarVisible = settings.IsSidebarVisible,
            sidebarWidth = settings.SidebarWidth,
            adBlockerEnabled = settings.IsAdBlockerEnabled,
            cookieBlockerEnabled = settings.IsCookieBlockerEnabled,
            trackingPreventionEnabled = settings.IsTrackingPreventionEnabled,
            passwordAutosaveEnabled = settings.IsPasswordAutosaveEnabled,
            generalAutofillEnabled = settings.IsGeneralAutofillEnabled
        });

        browser.ExecuteScriptAsync(
            $"window.dispatchEvent(new MessageEvent('message', {{ data: {message} }}));");
    }

    private static int ReadInt(JsonElement value, int fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return number;
        return int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    private static double ReadDouble(JsonElement value, double fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
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
