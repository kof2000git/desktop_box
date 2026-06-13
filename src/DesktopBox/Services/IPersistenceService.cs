using DesktopBox.Models;

namespace DesktopBox.Services;

public interface IPersistenceService
{
    AppConfig Load();
    void Save(AppConfig config);
}
