using MundoBrowser.Models;

namespace MundoBrowser.Interfaces;

public interface IExtensionService
{
    Task<List<ExtensionInfo>> LoadExtensionsAsync();

    Task<ExtensionInfo> InstallExtensionAsync(string extensionId);
}
