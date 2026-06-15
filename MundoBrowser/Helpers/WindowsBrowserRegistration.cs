using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MundoBrowser.Helpers;

public static class WindowsBrowserRegistration
{
    private const string ClientName = "MundoBrowser";
    private const string ProgId = "MundoBrowserHTML";
    private const string CapabilitiesPath = @"Software\Clients\StartMenuInternet\MundoBrowser\Capabilities";
    private const string ClientPath = @"Software\Clients\StartMenuInternet\MundoBrowser";
    private const string ProgIdPath = @"Software\Classes\MundoBrowserHTML";
    private const string RegisteredApplicationsPath = @"Software\RegisteredApplications";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfDword = 0x0003;
    private const uint ShcnfFlush = 0x1000;

    private static readonly string[] HtmlExtensions =
    [
        ".htm",
        ".html",
        ".shtml",
        ".xht",
        ".xhtml"
    ];

    public static void RegisterInstalledBrowser()
    {
        try
        {
            string? launcherPath = GetInstalledLauncherPath();
            if (launcherPath == null)
                return;

            string icon = $"{launcherPath},0";
            string openCommand = $"\"{launcherPath}\" \"%1\"";

            using (RegistryKey client = CreateKey(ClientPath))
            {
                client.SetValue(string.Empty, AppRuntime.DisplayName);
            }

            using (RegistryKey defaultIcon = CreateKey($@"{ClientPath}\DefaultIcon"))
            {
                defaultIcon.SetValue(string.Empty, icon);
            }

            using (RegistryKey open = CreateKey($@"{ClientPath}\shell\open\command"))
            {
                open.SetValue(string.Empty, $"\"{launcherPath}\"");
            }

            using (RegistryKey capabilities = CreateKey(CapabilitiesPath))
            {
                capabilities.SetValue("ApplicationDescription", "Navigateur web Mundo Browser.");
                capabilities.SetValue("ApplicationIcon", icon);
                capabilities.SetValue("ApplicationName", AppRuntime.DisplayName);
            }

            using (RegistryKey startMenu = CreateKey($@"{CapabilitiesPath}\StartMenu"))
            {
                startMenu.SetValue("StartMenuInternet", ClientName);
            }

            using (RegistryKey fileAssociations = CreateKey($@"{CapabilitiesPath}\FileAssociations"))
            {
                foreach (string extension in HtmlExtensions)
                    fileAssociations.SetValue(extension, ProgId);
            }

            using (RegistryKey urlAssociations = CreateKey($@"{CapabilitiesPath}\URLAssociations"))
            {
                urlAssociations.SetValue("http", ProgId);
                urlAssociations.SetValue("https", ProgId);
            }

            using (RegistryKey progId = CreateKey(ProgIdPath))
            {
                progId.SetValue(string.Empty, "Mundo Browser HTML Document");
                progId.SetValue("AppUserModelId", AppRuntime.AppUserModelId);
            }

            using (RegistryKey application = CreateKey($@"{ProgIdPath}\Application"))
            {
                application.SetValue("ApplicationDescription", "Navigateur web Mundo Browser.");
                application.SetValue("ApplicationIcon", icon);
                application.SetValue("ApplicationName", AppRuntime.DisplayName);
                application.SetValue("AppUserModelId", AppRuntime.AppUserModelId);
            }

            using (RegistryKey defaultIcon = CreateKey($@"{ProgIdPath}\DefaultIcon"))
            {
                defaultIcon.SetValue(string.Empty, icon);
            }

            using (RegistryKey open = CreateKey($@"{ProgIdPath}\shell\open\command"))
            {
                open.SetValue(string.Empty, openCommand);
            }

            using (RegistryKey registeredApplications = CreateKey(RegisteredApplicationsPath))
            {
                registeredApplications.SetValue(ClientName, CapabilitiesPath);
            }

            NotifyAssociationChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register Mundo Browser with Windows: {ex}");
        }
    }

    public static void UnregisterBrowser()
    {
        try
        {
            using (RegistryKey? registeredApplications = Registry.CurrentUser.OpenSubKey(
                       RegisteredApplicationsPath,
                       writable: true))
            {
                registeredApplications?.DeleteValue(ClientName, throwOnMissingValue: false);
            }

            Registry.CurrentUser.DeleteSubKeyTree(ClientPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(ProgIdPath, throwOnMissingSubKey: false);
            NotifyAssociationChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to unregister Mundo Browser from Windows: {ex}");
        }
    }

    private static RegistryKey CreateKey(string path)
        => Registry.CurrentUser.CreateSubKey(path, writable: true)
           ?? throw new InvalidOperationException($"Unable to create registry key HKCU\\{path}.");

    private static string? GetInstalledLauncherPath()
    {
        if (AppRuntime.IsDevelopment || Environment.ProcessPath is not { } processPath)
            return null;

        string? currentDirectory = Path.GetDirectoryName(processPath);
        string? rootDirectory = currentDirectory == null
            ? null
            : Directory.GetParent(currentDirectory)?.FullName;

        if (rootDirectory == null
            || !File.Exists(Path.Combine(currentDirectory!, "sq.version"))
            || !File.Exists(Path.Combine(rootDirectory, "Update.exe"))
            || File.Exists(Path.Combine(rootDirectory, ".portable")))
            return null;

        string launcherPath = Path.Combine(rootDirectory, Path.GetFileName(processPath));
        return File.Exists(launcherPath) ? launcherPath : null;
    }

    private static void NotifyAssociationChanged()
        => SHChangeNotify(ShcneAssocChanged, ShcnfDword | ShcnfFlush, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
