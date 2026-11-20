using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion23To24Migration : ISaveMigration
    {
        public int FromVersion => 23;
        public int ToVersion => 24;

        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 23.");
            save.SaveVersion = ToVersion;
        }
    }
}
