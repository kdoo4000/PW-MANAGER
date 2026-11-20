using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion14To15Migration : ISaveMigration
    {
        public int FromVersion => 14;
        public int ToVersion => 15;
        public void Migrate(GameSave save)
        {
            if (save.Training?.IsInitialized == true) save.Training.TrainerLevel = save.Training.FacilityLevel;
            save.SaveVersion = ToVersion;
        }
    }
}
