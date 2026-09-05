using System;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Loading;
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
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), seed)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1)).Take(20).ToList();
            var save = new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Show Test", PromotionAbbreviation = "TEST", WorldSeed = seed,
                InitialCash = 100000, UtcNow = DateTime.UtcNow,
                RegularVenueId = venue.Id, RegularVenueProductionCost = venue.ProductionCost,
                RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.EveryFourMonths,
                WrestlerContracts = candidates.Select(wrestler =>
                {
                    var offer = InitialContractOfferRules.Calculate(wrestler);
                    return new InitialWrestlerContractInput
                    {
                        Wrestler = wrestler, ContractEndYear = 2027, SigningBonus = offer.SigningBonus,
                        MonthlySalary = offer.MonthlySalary, TerminationCost = offer.TerminationCost
                    };
                }).ToList()
            });
            var show = save.Shows.OrderBy(x => x.Date).First();
            var schedule = save.Schedules.Single(x => x.Id == show.ScheduleId);
            save.Shows.RemoveAll(x => x != show);
            save.Schedules.RemoveAll(x => x != schedule);
            save.SeasonPolicy.SignaturePpvScheduleId = null;
            show.Name = "쇼 테스트";
            show.Date = save.CurrentDate;
            schedule.Date = save.CurrentDate;
            schedule.BookingDeadline = save.CurrentDate;
            var errors = GameSaveValidator.Validate(save);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
            return save;
        }
    }
}
