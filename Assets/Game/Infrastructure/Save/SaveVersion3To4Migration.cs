using System.Collections.Generic;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion3To4Migration : ISaveMigration
    {
        public int FromVersion => 3;
        public int ToVersion => 4;
        public void Migrate(GameSave save)
        {
            save.ContractTerminations ??= new List<ContractTerminationState>();
            save.SaveVersion = ToVersion;
        }
    }
}
