using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion0To1Migration : ISaveMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;

        public void Migrate(GameSave save)
        {
            if (save.MatchPlans != null)
            {
                foreach (var match in save.MatchPlans)
                {
                    if (match != null && match.MatchTypeId == "matchtype_003")
                        match.MatchTypeId = "matchtype_001";
                }
            }

            save.SaveVersion = ToVersion;
        }
    }
}