using MundoBrowser.Models;

namespace MundoBrowser.Interfaces;

public interface IAppSettingsService
{
    AppSettings Current { get; }
    void Update(Action<AppSettings> update);
}
