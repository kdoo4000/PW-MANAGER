using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion28To29Migration : ISaveMigration
    {
        public int FromVersion => 28;
        public int ToVersion => 29;
        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 28.");
            foreach (var state in save.WrestlerBehaviors ?? new())
                if (state != null) state.CardObservations ??= new();
            save.SaveVersion = ToVersion;
        }
    }
}
