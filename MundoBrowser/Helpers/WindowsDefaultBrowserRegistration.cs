using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MundoBrowser.Helpers;

public static class WindowsDefaultBrowserRegistration
{
    private const string ApplicationName = "Mundo Browser";
    private const string ApplicationDescription = "Navigateur web pour ouvrir les liens web et les fichiers HTML.";
    private const string BrowserClientKeyName = "MundoBrowser.exe";
    private const string HtmlProgId = "MundoBrowserHTML";
    private const string UrlProgId = "MundoBrowserURL";
    private const string RegisterCommandSwitch = "--register-default-browser";
    private const string CapabilitiesPath = @"Software\Clients\StartMenuInternet\" + BrowserClientKeyName + @"\Capabilities";

    private static readonly string[] HtmlExtensions =
    [
        ".htm",
        ".html",
        ".shtml",
        ".xht",
        ".xhtml"
    ];

    public static bool TryHandleCommandLine(string[]? args)
    {
        if (args?.Any(arg => string.Equals(arg, RegisterCommandSwitch, StringComparison.OrdinalIgnoreCase)) != true)
            return false;

        Register();
        return true;
    }

    public static void Register()
    {
        if (!OperatingSystem.IsWindows() || AppRuntime.IsDevelopment)
            return;

        try
        {
            string? executablePath = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath))
                return;

            string executableDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
            string iconPath = executablePath + ",0";
            string openCommand = Quote(executablePath) + " \"%1\"";
            string appCommand = Quote(executablePath);

            RegisterAppPath(executablePath, executableDirectory);
            RegisterApplicationEntry(iconPath, openCommand);
            RegisterProgIds(iconPath, openCommand);
            RegisterOpenWithProgIds();
            RegisterBrowserClient(iconPath, appCommand, openCommand);

            using (RegistryKey? registeredApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications", true))
            {
                registeredApps?.SetValue(ApplicationName, CapabilitiesPath, RegistryValueKind.String);
            }

            NotifyShellAssociationsChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register default browser capabilities: {ex}");
        }
    }

    public static void Unregister()
    {
        if (!OperatingSystem.IsWindows() || AppRuntime.IsDevelopment)
            return;

        try
        {
            using RegistryKey? registeredApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true);
            registeredApps?.DeleteValue(ApplicationName, false);

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clients\StartMenuInternet\" + BrowserClientKeyName, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + HtmlProgId, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + UrlProgId, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\" + BrowserClientKeyName, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + BrowserClientKeyName, false);
            UnregisterOpenWithProgIds();

            NotifyShellAssociationsChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to unregister default browser capabilities: {ex}");
        }
    }

    private static void RegisterAppPath(string executablePath, string executableDirectory)
    {
        using RegistryKey? appPath = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + BrowserClientKeyName,
            true);

        appPath?.SetValue("", executablePath, RegistryValueKind.String);
        appPath?.SetValue("Path", executableDirectory, RegistryValueKind.String);
        appPath?.SetValue("UseUrl", 1, RegistryValueKind.DWord);
        appPath?.SetValue("SupportedProtocols", "http:https", RegistryValueKind.String);
    }

    private static void RegisterApplicationEntry(string iconPath, string openCommand)
    {
        using RegistryKey? application = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\Applications\" + BrowserClientKeyName,
            true);

        application?.SetValue("FriendlyAppName", ApplicationName, RegistryValueKind.String);

        using RegistryKey? defaultIcon = application?.CreateSubKey("DefaultIcon", true);
        defaultIcon?.SetValue("", iconPath, RegistryValueKind.String);

        using RegistryKey? supportedTypes = application?.CreateSubKey("SupportedTypes", true);
        foreach (string extension in HtmlExtensions)
            supportedTypes?.SetValue(extension, "", RegistryValueKind.String);

        using RegistryKey? command = application?.CreateSubKey(@"shell\open\command", true);
        command?.SetValue("", openCommand, RegistryValueKind.String);
    }

    private static void RegisterProgIds(string iconPath, string openCommand)
    {
        using RegistryKey? html = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + HtmlProgId, true);
        html?.SetValue("", "Mundo Browser HTML Document", RegistryValueKind.String);
        html?.SetValue("FriendlyTypeName", "Mundo Browser HTML Document", RegistryValueKind.String);
        html?.SetValue("AppUserModelID", AppRuntime.AppUserModelId, RegistryValueKind.String);
        RegisterProgIdApplication(html, iconPath);

        using (RegistryKey? defaultIcon = html?.CreateSubKey("DefaultIcon", true))
        {
            defaultIcon?.SetValue("", iconPath, RegistryValueKind.String);
        }

        using (RegistryKey? command = html?.CreateSubKey(@"shell\open\command", true))
        {
            command?.SetValue("", openCommand, RegistryValueKind.String);
        }

        using RegistryKey? url = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + UrlProgId, true);
        url?.SetValue("", "Mundo Browser URL", RegistryValueKind.String);
        url?.SetValue("FriendlyTypeName", "Mundo Browser URL", RegistryValueKind.String);
        url?.SetValue("URL Protocol", "", RegistryValueKind.String);
        url?.SetValue("AppUserModelID", AppRuntime.AppUserModelId, RegistryValueKind.String);
        RegisterProgIdApplication(url, iconPath);

        using (RegistryKey? defaultIcon = url?.CreateSubKey("DefaultIcon", true))
        {
            defaultIcon?.SetValue("", iconPath, RegistryValueKind.String);
        }

        using (RegistryKey? command = url?.CreateSubKey(@"shell\open\command", true))
        {
            command?.SetValue("", openCommand, RegistryValueKind.String);
        }
    }

    private static void RegisterProgIdApplication(RegistryKey? progId, string iconPath)
    {
        using RegistryKey? application = progId?.CreateSubKey("Application", true);
        application?.SetValue("ApplicationDescription", ApplicationDescription, RegistryValueKind.String);
        application?.SetValue("ApplicationIcon", iconPath, RegistryValueKind.String);
        application?.SetValue("ApplicationName", ApplicationName, RegistryValueKind.String);
        application?.SetValue("AppUserModelId", AppRuntime.AppUserModelId, RegistryValueKind.String);
    }

    private static void RegisterOpenWithProgIds()
    {
        foreach (string extension in HtmlExtensions)
        {
            using RegistryKey? openWithProgIds = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\" + extension + @"\OpenWithProgids",
                true);

            openWithProgIds?.SetValue(HtmlProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
    }

    private static void UnregisterOpenWithProgIds()
    {
        foreach (string extension in HtmlExtensions)
        {
            using RegistryKey? openWithProgIds = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\" + extension + @"\OpenWithProgids",
                true);

            openWithProgIds?.DeleteValue(HtmlProgId, false);
        }
    }

    private static void RegisterBrowserClient(string iconPath, string appCommand, string openCommand)
    {
        using RegistryKey? client = Registry.CurrentUser.CreateSubKey(
            @"Software\Clients\StartMenuInternet\" + BrowserClientKeyName,
            true);

        client?.SetValue("", ApplicationName, RegistryValueKind.String);
        client?.DeleteValue("LocalizedString", false);

        using (RegistryKey? defaultIcon = client?.CreateSubKey("DefaultIcon", true))
        {
            defaultIcon?.SetValue("", iconPath, RegistryValueKind.String);
        }

        using (RegistryKey? command = client?.CreateSubKey(@"shell\open\command", true))
        {
            command?.SetValue("", appCommand, RegistryValueKind.String);
        }

        using (RegistryKey? installInfo = client?.CreateSubKey("InstallInfo", true))
        {
            installInfo?.SetValue("ReinstallCommand", appCommand + " " + RegisterCommandSwitch, RegistryValueKind.String);
            installInfo?.SetValue("IconsVisible", 1, RegistryValueKind.DWord);
        }

        using RegistryKey? capabilities = client?.CreateSubKey("Capabilities", true);
        capabilities?.SetValue("ApplicationDescription", ApplicationDescription, RegistryValueKind.String);
        capabilities?.SetValue("ApplicationIcon", iconPath, RegistryValueKind.String);
        capabilities?.SetValue("ApplicationName", ApplicationName, RegistryValueKind.String);

        using (RegistryKey? fileAssociations = capabilities?.CreateSubKey("FileAssociations", true))
        {
            foreach (string extension in HtmlExtensions)
                fileAssociations?.SetValue(extension, HtmlProgId, RegistryValueKind.String);
        }

        using (RegistryKey? mimeAssociations = capabilities?.CreateSubKey("MIMEAssociations", true))
        {
            mimeAssociations?.SetValue("text/html", HtmlProgId, RegistryValueKind.String);
            mimeAssociations?.SetValue("application/xhtml+xml", HtmlProgId, RegistryValueKind.String);
        }

        using (RegistryKey? startmenu = capabilities?.CreateSubKey("Startmenu", true))
        {
            startmenu?.SetValue("StartmenuInternet", BrowserClientKeyName, RegistryValueKind.String);
        }

        using (RegistryKey? urlAssociations = capabilities?.CreateSubKey("UrlAssociations", true))
        {
            urlAssociations?.SetValue("http", UrlProgId, RegistryValueKind.String);
            urlAssociations?.SetValue("https", UrlProgId, RegistryValueKind.String);
        }
    }

    private static string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            return Path.GetFullPath(Environment.ProcessPath);

        try
        {
            string? mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModulePath) && File.Exists(mainModulePath))
                return Path.GetFullPath(mainModulePath);
        }
        catch
        {
            // Ignore and use the AppContext fallback below.
        }

        string fallbackPath = Path.Combine(AppContext.BaseDirectory, BrowserClientKeyName);
        return File.Exists(fallbackPath) ? Path.GetFullPath(fallbackPath) : null;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void NotifyShellAssociationsChanged()
    {
        try
        {
            NativeMethods.SHChangeNotify(
                NativeMethods.SHCNE_ASSOCCHANGED,
                NativeMethods.SHCNF_IDLIST,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        catch
        {
            // Registry registration is still valid even if the Shell notification fails.
        }
    }
}
