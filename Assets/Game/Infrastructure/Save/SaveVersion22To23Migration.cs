using System;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion22To23Migration : ISaveMigration
    {
        public int FromVersion => 22;
        public int ToVersion => 23;

        public void Migrate(GameSave save)
        {
            if (save.SaveVersion >= ToVersion) return;
            if (save.SaveVersion != FromVersion) throw new InvalidOperationException("Expected save version 22.");
            foreach (var report in save.ScoutCandidates ?? new())
            {
                var wrestler = save.Wrestlers?.FirstOrDefault(x => x?.Id == report?.WrestlerId);
                if (report == null || wrestler?.Identity == null) continue;
                report.ExpiryDate = wrestler.Identity.ExpiryDate;
                wrestler.Identity.ExpiryDate = default;
            }
            save.SaveVersion = ToVersion;
        }
    }
}
