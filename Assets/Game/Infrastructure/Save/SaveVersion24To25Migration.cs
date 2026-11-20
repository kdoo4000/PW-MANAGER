using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion24To25Migration : ISaveMigration
    {
        public int FromVersion => 24;
        public int ToVersion => 25;

        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 24.");
            save.SaveVersion = ToVersion;
        }
    }
}
