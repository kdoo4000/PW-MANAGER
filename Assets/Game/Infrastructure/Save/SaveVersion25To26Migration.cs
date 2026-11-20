using System;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion25To26Migration : ISaveMigration
    {
        public int FromVersion => 25;
        public int ToVersion => 26;

        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 25.");
            foreach (var contract in save.Contracts ?? new())
                if (contract != null && string.IsNullOrEmpty(contract.PromotionId)) contract.PromotionId = save.Promotion?.Id;
            save.OtherPromotions ??= new();
            save.SaveVersion = ToVersion;
        }
    }
}
