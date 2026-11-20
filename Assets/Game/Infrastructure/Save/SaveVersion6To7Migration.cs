using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion6To7Migration : ISaveMigration
    {
        public int FromVersion => 6;
        public int ToVersion => 7;
        public void Migrate(GameSave save)
        {
            save.ExperienceRecords ??= new(); save.GrowthQuarterResults ??= new();
            foreach (var w in save.Wrestlers ?? new())
            {
                if (w?.Growth == null) continue;
                w.Growth.GrowthProgress ??= new(); w.Growth.ExperienceLogIds ??= new(); w.Growth.AttributeHistoryIds ??= new();
            }
            save.LastGrowthQuarterEnd = WrestlerGrowthService.LatestQuarterEnd(save.CurrentDate,
                save.SeasonPolicy != null && save.SeasonPolicy.StartDate.CompareTo(save.CurrentDate) > 0);
            save.SaveVersion = ToVersion;
        }
    }
}
