using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using UnityEditor;

namespace PWManager.Tests
{
    public sealed class GameStartServiceTests
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        [Test]
        public void CreateInitialSave_ConnectsRosterContractsStaffVenuePolicyAndSchedules()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 20260820)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));
            var inputs = candidates.Take(8).Select(x => new InitialWrestlerContractInput
            {
                Wrestler = x, ContractEndYear = 2027, SigningBonus = 1000,
                MonthlySalary = 500, TerminationCost = 2000
            }).ToList();

            var save = new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Test Wrestling",
                PromotionAbbreviation = "TW",
                InitialCash = 100000,
                InitialPrestige = 0,
                WorldSeed = 77,
                UtcNow = new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = inputs,
                RegularVenueId = "venue_002",
                RegularVenueProductionCost = 10000,
                RegularShowFrequency = RegularShowFrequency.Biweekly,
                PpvFrequency = PpvFrequency.Quarterly,
                PpvTypes = new[]
                {
                    ScheduledShowType.PpvRegular, ScheduledShowType.PpvRegular,
                    ScheduledShowType.PpvRegular, ScheduledShowType.PpvSignature
                }
            });

            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            Assert.That(save.Wrestlers, Has.Count.EqualTo(8));
            Assert.That(save.Promotion.Abbreviation, Is.EqualTo("TW"));
            Assert.That(save.Contracts, Has.Count.EqualTo(8));
            Assert.That(save.StaffDepartments, Has.Count.EqualTo(4));
            Assert.That(save.StaffDepartments.Single(x => x.DepartmentType == StaffDepartmentType.Commentary).CurrentLevel, Is.Zero);
            Assert.That(save.VenueContracts, Has.Count.EqualTo(1));
            Assert.That(save.SeasonPolicy.RegularVenueContractId, Is.EqualTo(save.VenueContracts[0].Id));
            Assert.That(save.Schedules.Count(x => x.ShowType != ScheduledShowType.Regular), Is.EqualTo(4));
            Assert.That(save.Schedules, Has.All.Matches<ScheduleState>(x => x.Status == ScheduleStatus.Confirmed));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(92000));
            Assert.That(save.Wrestlers, Has.All.Property(nameof(WrestlerState.PromotionId)).EqualTo(save.Promotion.Id));
            Assert.That(save.Wrestlers, Has.All.Matches<WrestlerState>(x => x.Roster.ActivityState == RosterActivityState.Active));
            Assert.That(candidates.Take(8), Has.All.Matches<WrestlerState>(x => string.IsNullOrEmpty(x.PromotionId)));
        }

        [Test]
        public void CreateInitialSave_RejectsRosterBelowMinimum()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 1)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));
            var request = new GameStartRequest
            {
                PromotionName = "Too Small",
                InitialCash = 100000,
                WorldSeed = 1,
                UtcNow = new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = candidates.Take(7).Select(x => new InitialWrestlerContractInput { Wrestler = x }).ToList(),
                RegularVenueId = "venue_001",
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths
            };

            Assert.Throws<ArgumentException>(() => new GameStartService().CreateInitialSave(request));
            Assert.That(candidates.Take(7), Has.All.Matches<WrestlerState>(x => string.IsNullOrEmpty(x.PromotionId)));
        }

        [Test]
        public void CreateInitialSave_GeneratedTwelveWrestlerPreviewRoster_IsValid()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 9090)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));

            var save = new GameStartService().CreateInitialSave(new GameStartRequest
            {
                PromotionName = "Preview Promotion",
                InitialCash = 100000,
                WorldSeed = 9090,
                UtcNow = new DateTime(2026, 8, 22, 3, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = candidates.Take(12).Select(x => new InitialWrestlerContractInput
                {
                    Wrestler = x, ContractEndYear = 2027, MonthlySalary = 500
                }).ToList(),
                RegularVenueId = "venue_001",
                RegularVenueProductionCost = 500,
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths
            });

            Assert.That(save.Wrestlers, Has.Count.EqualTo(12));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void CreateInitialSave_RejectsSigningBonusesAboveCash()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 2)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));
            var request = new GameStartRequest
            {
                PromotionName = "Over Budget",
                InitialCash = 100,
                WorldSeed = 2,
                UtcNow = new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput { Wrestler = x, SigningBonus = 100 }).ToList(),
                RegularVenueId = "venue_001",
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths
            };

            Assert.Throws<ArgumentException>(() => new GameStartService().CreateInitialSave(request));
        }
    }
}
