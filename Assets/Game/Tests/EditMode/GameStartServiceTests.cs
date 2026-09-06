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
            Assert.That(save.SeasonPolicy.PpvShowTitles, Has.Count.EqualTo(4));
            Assert.That(save.SeasonPolicy.PpvShowTitles.Select(x => x.Title), Is.Unique);
            Assert.That(save.Schedules, Has.All.Matches<ScheduleState>(x => x.Status == ScheduleStatus.Confirmed));
            Assert.That(save.Shows, Has.Count.EqualTo(save.Schedules.Count));
            Assert.That(save.Shows.Select(x => x.ScheduleId), Is.EquivalentTo(save.Schedules.Select(x => x.Id)));
            Assert.That(save.Shows, Has.All.Matches<ShowState>(x =>
                x.Status == ShowStatus.Draft && x.DurationLimit == 120 &&
                x.VenueContractId == save.VenueContracts[0].Id && x.EstimatedCost == 10000));
            Assert.That(save.Shows, Has.All.Matches<ShowState>(x => x.Name.StartsWith("TW ", StringComparison.Ordinal)));
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

        [Test]
        public void CreateInitialSave_PlayerChoicesSetDifficultyBenefits()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 3)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));
            var request = new GameStartRequest
            {
                HasPlayerCharacter = true, PlayerName = "김 선수", PlayerRole = PlayerCareerRole.WrestlerManager,
                PlayerGender = WrestlerGender.Female,
                PlayerReputation = PlayerReputation.Star, PlayerWrestlingType = PlayerWrestlingType.Showman,
                PromotionName = "Player Test", InitialCash = 100000, WorldSeed = 3,
                UtcNow = new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput { Wrestler = x }).ToList(),
                RegularVenueId = "venue_001", RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths
            };

            var save = new GameStartService().CreateInitialSave(request);

            Assert.That(save.Player.Name, Is.EqualTo("김 선수"));
            Assert.That(save.Player.BirthDate, Is.EqualTo(new GameDate(1994, 6, 1)));
            Assert.That(save.Player.Gender, Is.EqualTo(WrestlerGender.Female));
            Assert.That(save.Player.MatchAbility, Is.EqualTo(10));
            Assert.That(save.Player.PromoAbility, Is.EqualTo(14));
            Assert.That(save.Promotion.Audience.TotalFollowers, Is.EqualTo(5000));
            Assert.That(save.Wrestlers.Single(x => x.Id == save.Player.WrestlerId).Attributes.Charisma, Is.EqualTo(16));
            Assert.That(save.Wrestlers.Single(x => x.Id == save.Player.WrestlerId).Identity.Gender, Is.EqualTo(WrestlerGender.Female));
            Assert.That(save.Wrestlers.Single(x => x.Id == save.Player.WrestlerId).Attributes.Technical, Is.EqualTo(9));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void CreateInitialSave_ProfessionalManagerGetsPromoAbilityOnly()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 4)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));
            var request = new GameStartRequest
            {
                HasPlayerCharacter = true, PlayerName = "박 단장", PlayerRole = PlayerCareerRole.ProfessionalManager,
                PlayerReputation = PlayerReputation.National, PlayerWrestlingType = PlayerWrestlingType.Worker,
                PromotionName = "Manager Test", InitialCash = 100000, WorldSeed = 4,
                UtcNow = new DateTime(2026, 8, 20, 3, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput { Wrestler = x }).ToList(),
                RegularVenueId = "venue_001", RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths
            };

            var save = new GameStartService().CreateInitialSave(request);

            Assert.That(save.Player.MatchAbility, Is.Zero);
            Assert.That(save.Player.PromoAbility, Is.EqualTo(10));
            Assert.That(save.Player.WrestlerId, Is.Null.Or.Empty);
            Assert.That(save.Managers.Single(x => x.Id == save.Player.ManagerId).Attributes.ManagerOverall, Is.EqualTo(10));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void PhysicalLimits_UseGenderSpecificRanges()
        {
            Assert.That(PlayerCharacterRules.IsPhysicalProfileValid(WrestlerGender.Male, 165, 55), Is.True);
            Assert.That(PlayerCharacterRules.IsPhysicalProfileValid(WrestlerGender.Male, 164, 55), Is.False);
            Assert.That(PlayerCharacterRules.IsPhysicalProfileValid(WrestlerGender.Female, 190, 150), Is.True);
            Assert.That(PlayerCharacterRules.IsPhysicalProfileValid(WrestlerGender.Female, 191, 150), Is.False);
            Assert.That(PlayerCharacterRules.IsStyleAvailable("style_006", 194, WrestlerGender.Male), Is.False);
            Assert.That(PlayerCharacterRules.IsStyleAvailable("style_006", 180, WrestlerGender.Female), Is.True);
        }
    }
}
