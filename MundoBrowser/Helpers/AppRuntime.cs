using System;
using System.IO;
using System.Reflection;

namespace MundoBrowser.Helpers;

public static class AppRuntime
{
#if DEBUG
    public const bool IsDevelopment = true;
    public const string AppUserModelId = "MundoBrowser.Development";
    public const string UniqueInstanceName = "MundoBrowser-Development-SingleInstance-Guid-v1";
    public const string DisplayName = "Mundo Browser (Development)";
    private const string DataDirectoryName = "MundoBrowser.Development";
#else
    public const bool IsDevelopment = false;
    public const string AppUserModelId = "MundoBrowser.App";
    public const string UniqueInstanceName = "MundoBrowser-SingleInstance-Guid-v2";
    public const string DisplayName = "Mundo Browser";
    private const string DataDirectoryName = "MundoBrowser";
#endif

    public static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataDirectoryName);

    public static string RoamingDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        DataDirectoryName);

    private static string? _version;
    private static string? _channel;
    private static string? _versionBadgeText;

    public static string Version => _version ??= ResolveVersion();
    public static string Channel => _channel ??= ResolveChannel();
    public static bool IsBeta => string.Equals(Channel, "beta", StringComparison.OrdinalIgnoreCase);
    public static string VersionBadgeText => _versionBadgeText ??= $"Version {Version} (Build {Channel})";

    private static string ResolveVersion()
    {
        try
        {
            if (Velopack.Locators.VelopackLocator.Current?.CurrentlyInstalledVersion is { } semVer)
            {
                return semVer.ToFullString();
            }
        }
        catch { }

        string? baseVer = null;
        string? commitHash = null;

        try
        {
            var infoVer = typeof(AppRuntime).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                var plusIdx = infoVer.IndexOf('+');
                if (plusIdx >= 0)
                {
                    baseVer = infoVer[..plusIdx].Trim();
                    var rawHash = infoVer[(plusIdx + 1)..].Trim();
                    if (!string.IsNullOrEmpty(rawHash))
                    {
                        commitHash = rawHash.Length > 7 ? rawHash[..7] : rawHash;
                    }
                }
                else
                {
                    baseVer = infoVer.Trim();
                }
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(baseVer))
        {
            var ver = typeof(AppRuntime).Assembly.GetName().Version;
            baseVer = ver != null ? (ver.Build > 0 ? ver.ToString(3) : $"{ver.Major}.{ver.Minor}.{ver.Build}") : "1.0.0";
        }

#if DEBUG
        if (!baseVer.Contains("-dev", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(commitHash) ? $"{baseVer}-dev+{commitHash}" : $"{baseVer}-dev";
        }
        if (!string.IsNullOrEmpty(commitHash) && !baseVer.Contains('+'))
        {
            return $"{baseVer}+{commitHash}";
        }
        return baseVer;
#else
        return baseVer;
#endif
    }

    private static string ResolveChannel()
    {
        try
        {
            var velopackChannel = Velopack.Locators.VelopackLocator.Current?.Channel;
            if (!string.IsNullOrWhiteSpace(velopackChannel))
            {
                var norm = velopackChannel.Trim().ToLowerInvariant();
                return norm == "release" ? "stable" : norm;
            }
        }
        catch { }

        var ver = Version;
        if (ver.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            return "beta";
        }
        if (ver.Contains("alpha", StringComparison.OrdinalIgnoreCase))
        {
            return "alpha";
        }
        if (ver.Contains("rc", StringComparison.OrdinalIgnoreCase))
        {
            return "rc";
        }

#if DEBUG
        return "dev";
#else
        return "stable";
#endif
    }
}
