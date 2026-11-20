using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion10To11Migration : ISaveMigration
    {
        public int FromVersion => 10;
        public int ToVersion => 11;
        public void Migrate(GameSave save) => save.SaveVersion = ToVersion;
    }
}
