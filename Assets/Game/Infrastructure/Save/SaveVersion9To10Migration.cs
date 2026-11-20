using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Infrastructure.Save
{
    public sealed class SaveVersion9To10Migration : ISaveMigration
    {
        public int FromVersion => 9;
        public int ToVersion => 10;
        public void Migrate(GameSave save)
        {
            save.Promotion.AppliedPrestigeReasonIds ??= new();
            foreach (var contract in save.Contracts.Where(x => x.Type == ContractType.Wrestler))
            {
                var w = save.Wrestlers.FirstOrDefault(x => x.Id == contract.PersonId);
                if (w == null) continue;
                var renewal = save.Contracts.Any(x => x != contract && x.PersonId == contract.PersonId && x.Type == contract.Type &&
                    x.StartDate.CompareTo(contract.StartDate) < 0 && x.EndDate.CompareTo(contract.StartDate.AddDays(-1)) >= 0 && x.Status != ContractStatus.Terminated);
                if (renewal) continue;
                contract.WasRookieWhenHired = w.Status.IsRookie ||
                    w.Identity.Background == WrestlerBackground.Rookie && w.Identity.CreatedDate.Equals(contract.StartDate) ||
                    save.CareerEvents.Any(x => x.WrestlerId == w.Id && (x.WasRookie || x.FameWasRookie) &&
                        x.Date.CompareTo(contract.StartDate) >= 0 && x.Date.CompareTo(contract.EndDate) <= 0);
            }
            save.SaveVersion = ToVersion;
        }
    }
}
