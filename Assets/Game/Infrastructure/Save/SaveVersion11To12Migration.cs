using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion11To12Migration : ISaveMigration
    {
        public int FromVersion => 11;
        public int ToVersion => 12;
        public void Migrate(GameSave save)
        {
            PromotionQuarterService.Initialize(save, legacy: true);
            save.SaveVersion = ToVersion;
        }
    }
}
