using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public interface ISaveMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        void Migrate(GameSave save);
    }
}
