using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion26To27Migration : ISaveMigration
    {
        public int FromVersion => 26;
        public int ToVersion => 27;

        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 26.");
            WrestlerHiddenRules.InitializeSave(save);
            save.SaveVersion = ToVersion;
        }
    }
}
