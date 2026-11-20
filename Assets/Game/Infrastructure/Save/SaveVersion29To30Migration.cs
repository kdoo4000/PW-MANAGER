using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion29To30Migration : ISaveMigration
    {
        public int FromVersion => 29;
        public int ToVersion => 30;
        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 29.");
            WrestlerAdaptationService.InitializeSave(save);
            save.LockerRoomRelationships ??= new();
            save.LockerRoomEvents ??= new();
            save.SaveVersion = ToVersion;
        }
    }
}
