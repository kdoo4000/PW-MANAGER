using System.Collections.Generic;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion1To2Migration : ISaveMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;

        public void Migrate(GameSave save)
        {
            save.SalaryPayments ??= new List<SalaryPaymentState>();
            foreach (var manager in save.Managers ?? new())
                if (manager != null) manager.Satisfaction = 50f;
            save.SaveVersion = ToVersion;
        }
    }
}
