using System.Collections.Generic;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion2To3Migration : ISaveMigration
    {
        public int FromVersion => 2;
        public int ToVersion => 3;
        public void Migrate(GameSave save)
        {
            save.ContractNegotiations ??= new List<ContractNegotiationState>();
            foreach (var manager in save.Managers ?? new())
                if (manager != null && string.IsNullOrWhiteSpace(manager.Name))
                    manager.Name = save.Player?.ManagerId == manager.Id ? save.Player.Name : "전문 매니저";
            save.SaveVersion = ToVersion;
        }
    }
}
