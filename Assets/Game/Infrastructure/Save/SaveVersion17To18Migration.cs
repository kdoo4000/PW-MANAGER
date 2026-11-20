using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion17To18Migration : ISaveMigration
    {
        public int FromVersion => 17;
        public int ToVersion => 18;
        public void Migrate(GameSave save)
        {
            save.SponsorSeasons ??= new();
            save.SaveVersion = ToVersion;
        }
    }
}
