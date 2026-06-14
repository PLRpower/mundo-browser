using System.IO;

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
}
