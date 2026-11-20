using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion27To28Migration : ISaveMigration
    {
        public int FromVersion => 27;
        public int ToVersion => 28;
        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 27.");
            save.WrestlerBehaviors ??= new();
            save.SaveVersion = ToVersion;
        }
    }
}
