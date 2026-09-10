using System;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Loading;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;

namespace PWManager.Data.Generation
{
    public static class ShowTestSaveFactory
    {
        public static GameSave Create(StaticContentCatalog catalog, int seed)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var venue = catalog.Venues.First(x => x.RequiredPrestige <= 0);
            var generator = new WrestlerGenerator(new StaticContentRegistry(catalog), seed);
            var initialCandidates = generator.GenerateInitialCandidates(new GameDate(2026, 6, 1)).Take(20).ToList();
            var save = new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "WWE Show Test", PromotionAbbreviation = "WWE", WorldSeed = seed,
                InitialCash = 100000, UtcNow = DateTime.UtcNow,
                RegularVenueId = venue.Id, RegularVenueProductionCost = venue.ProductionCost,
                RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.EveryFourMonths,
                WrestlerContracts = initialCandidates.Select(wrestler =>
                {
                    var offer = InitialContractOfferRules.Calculate(wrestler);
                    return new InitialWrestlerContractInput
                    {
                        Wrestler = wrestler, ContractEndYear = 2027, SigningBonus = offer.SigningBonus,
                        MonthlySalary = offer.MonthlySalary, TerminationCost = offer.TerminationCost
                    };
                }).ToList()
            });
            var wrestlerContractIds = save.Contracts.Where(x => x.Type == ContractType.Wrestler).Select(x => x.Id).ToHashSet();
            save.Wrestlers.Clear();
            save.Contracts.RemoveAll(x => x.Type == ContractType.Wrestler);
            save.Transactions.RemoveAll(x => wrestlerContractIds.Contains(x.ReasonId));
            foreach (var wrestler in WweShowTestRoster.Create(generator, save.CurrentDate))
            {
                wrestler.PromotionId = save.Promotion.Id;
                wrestler.Roster.ActivityState = RosterActivityState.Active;
                wrestler.Identity.ExpiryDate = default;
                save.Wrestlers.Add(wrestler);
                var offer = InitialContractOfferRules.Calculate(wrestler);
                save.Contracts.Add(new ContractState
                {
                    Id = EntityId.CreateRuntimeId(), PersonId = wrestler.Id, Type = ContractType.Wrestler,
                    StartDate = save.CurrentDate, EndDate = new GameDate(2027, 5, 31),
                    MonthlySalary = offer.MonthlySalary, TerminationCost = offer.TerminationCost,
                    Status = ContractStatus.Active
                });
            }
            var show = save.Shows.OrderBy(x => x.Date).First();
            var schedule = save.Schedules.Single(x => x.Id == show.ScheduleId);
            save.Shows.RemoveAll(x => x != show);
            save.Schedules.RemoveAll(x => x != schedule);
            save.SeasonPolicy.SignaturePpvScheduleId = null;
            show.Name = "WWE Raw vs SmackDown";
            show.Date = save.CurrentDate;
            schedule.Date = save.CurrentDate;
            schedule.BookingDeadline = save.CurrentDate;
            var errors = GameSaveValidator.Validate(save);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
            return save;
        }
    }
}
