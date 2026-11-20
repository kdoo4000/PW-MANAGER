using PWManager.Domain.Models;
using PWManager.Domain.Services;
namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion18To19Migration : ISaveMigration
    {
        public int FromVersion => 18;
        public int ToVersion => 19;
        public void Migrate(GameSave save)
        {
            save.Merchandise = new MerchandiseState();
            MerchandiseService.Initialize(save);
            save.SaveVersion = ToVersion;
        }
    }
}
