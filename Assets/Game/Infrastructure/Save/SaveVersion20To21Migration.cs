using PWManager.Domain.Models;
using PWManager.Domain.Services;
namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion20To21Migration : ISaveMigration
    {
        public int FromVersion => 20;
        public int ToVersion => 21;
        public void Migrate(GameSave save)
        {
            if (save.Promotion?.Audience != null)
                for (var group = 0; group < 3; group++) FanAudienceService.Group(save.Promotion.Audience, group).PendingAcquisition = 0;
            save.SaveVersion = ToVersion;
        }
    }
}
