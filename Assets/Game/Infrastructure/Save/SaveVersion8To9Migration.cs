using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion8To9Migration : ISaveMigration
    {
        public int FromVersion => 8;
        public int ToVersion => 9;
        public void Migrate(GameSave save)
        {
            foreach (var wrestler in save.Wrestlers)
                wrestler.Fame = WrestlerFameRules.Create(save.Player?.WrestlerId == wrestler.Id
                    ? WrestlerFameRules.InitialForPlayer(save.Player.Reputation)
                    : WrestlerFameRules.InitialForCareer(wrestler.Identity.CareerYears));
            save.SaveVersion = ToVersion;
        }
    }
}
