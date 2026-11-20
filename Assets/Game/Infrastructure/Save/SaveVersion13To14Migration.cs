using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion13To14Migration : ISaveMigration
    {
        public int FromVersion => 13;
        public int ToVersion => 14;
        public void Migrate(GameSave save) { TrainingService.Initialize(save); save.SaveVersion = ToVersion; }
    }
}
