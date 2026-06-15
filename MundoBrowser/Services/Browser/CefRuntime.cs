using System.IO;
using CefSharp;
using CefSharp.Wpf.HwndHost;
using MundoBrowser.Helpers;
using MundoBrowser.Services.Extensions;

namespace MundoBrowser.Services.Browser;

internal static class CefRuntime
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        var dataPath = Path.Combine(AppRuntime.LocalDataDirectory, "Chromium");
        Directory.CreateDirectory(dataPath);

        // HwndHost uses native GPU rendering and follows the monitor refresh rate.
        // Enabling windowless rendering would force the WPF OSR path, capped at 60 FPS by CEF.
        using var settings = new CefSettings
        {
            CachePath = dataPath,
            RootCachePath = dataPath,
            PersistSessionCookies = true
        };

        settings.CefCommandLineArgs["disable-features"] = "DownloadBubble,DownloadBubbleV2";
        // Improve precision-touchpad gesture pacing and preserve smooth inertial flings.
        settings.CefCommandLineArgs["enable-features"] = string.Join(',',
            "ResamplingScrollEvents",
            "ResamplingScrollEventsExperimentalPrediction",
            "ResampleScrollEventsForFling");
        settings.CefCommandLineArgs["enable-smooth-scrolling"] = "1";

        var extensionDirectories = ExtensionRuntime.GetInstalledDirectories();
        if (extensionDirectories.Count > 0)
            settings.CefCommandLineArgs["load-extension"] = string.Join(',', extensionDirectories);

        if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
            throw new InvalidOperationException("CEF could not be initialized.");

        _initialized = true;
    }

    public static void Shutdown()
    {
        if (!_initialized)
            return;

        Cef.Shutdown();
        _initialized = false;
    }
}
