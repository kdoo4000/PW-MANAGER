using System.Collections.Generic;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion4To5Migration : ISaveMigration
    {
        public int FromVersion => 4;
        public int ToVersion => 5;
        public void Migrate(GameSave save)
        {
            save.SeasonResults ??= new List<SeasonResultState>();
            save.SaveVersion = ToVersion;
        }
    }
}
