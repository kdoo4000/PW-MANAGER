using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion7To8Migration : ISaveMigration
    {
        public int FromVersion => 7;
        public int ToVersion => 8;
        public void Migrate(GameSave save)
        {
            save.CareerEvents ??= new();
            save.CareerObservationStartDate = save.CurrentDate;
            save.LastCareerQuarterEnd = WrestlerGrowthService.LatestQuarterEnd(save.CurrentDate,
                save.LastGrowthQuarterEnd.HasValue && save.LastGrowthQuarterEnd.Value.Equals(save.CurrentDate));
            save.SaveVersion = ToVersion;
        }
    }
}
