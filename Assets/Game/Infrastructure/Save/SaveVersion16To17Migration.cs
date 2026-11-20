using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion16To17Migration : ISaveMigration
    {
        public int FromVersion => 16;
        public int ToVersion => 17;
        public void Migrate(GameSave save)
        {
            ChampionshipService.Upgrade(save);
            save.SaveVersion = ToVersion;
        }
    }
}
