using PWManager.Domain.Models;
using PWManager.Domain.Services;
namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion19To20Migration : ISaveMigration
    {
        public int FromVersion => 19;
        public int ToVersion => 20;
        public void Migrate(GameSave save)
        {
            // Existing contracts keep their guaranteed salary and have no appearance fees.
            save.SaveVersion = ToVersion;
        }
    }
}
