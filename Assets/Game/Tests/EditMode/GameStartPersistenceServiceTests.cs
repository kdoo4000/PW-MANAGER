using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using UnityEditor;

namespace PWManager.Tests
{
    public sealed class GameStartPersistenceServiceTests
    {
        [Test]
        public void NewPromotion_RejectsOperationsThatHaveNotBeenUnlocked()
        {
            var request = CreateRequest("Locked", 481); request.RegularShowFrequency = RegularShowFrequency.Weekly;
            Assert.Throws<InvalidOperationException>(() => new GameStartService().CreateInitialSave(request));
            request.RegularShowFrequency = RegularShowFrequency.Monthly; request.PpvFrequency = PpvFrequency.Monthly;
            Assert.Throws<InvalidOperationException>(() => new GameStartService().CreateInitialSave(request));
        }

        [Test]
        public void Progression_ClosesOneSeasonAndUnlocksNewOperationsAfterSaveReload()
        {
            var storage = new SaveService(directory); var request = CreateRequest("Progression", 481); request.InitialPrestige = 995; request.InitialCash = 1000000;
            var save = new GameStartPersistenceService(storage).CreateSaveAndReload("progression", request);
            foreach (var schedule in save.Schedules) schedule.Status = ScheduleStatus.Cancelled;
            var end = save.SeasonPolicy.EndDate;
            while (save.CurrentDate.CompareTo(end) < 0)
            {
                var next = save.CurrentDate.DaysUntil(end) > 30 ? save.CurrentDate.AddDays(30) : end;
                Assert.That(new TimeFlowService().AdvanceTo(save, next).State, Is.EqualTo(TimeFlowState.Completed));
            }
            Assert.That(save.Promotion.PromotionPrestige, Is.GreaterThanOrEqualTo(1000)); Assert.That(PromotionProgressionRules.ActiveTier(save), Is.EqualTo(1));
            var settings = SeasonTransitionService.DefaultSettings(save); settings.RegularFrequency = RegularShowFrequency.Weekly;
            settings.PpvFrequency = PpvFrequency.Bimonthly; settings.CancelMissedShows = true;
            settings.VenueId = "venue_003"; settings.VenueRequiredPrestige = 1000; settings.VenueUnlockCost = 300000; settings.ProductionCost = 20000;
            var cash = save.Promotion.CalculateCurrentCash(save.Transactions);
            new SeasonTransitionService().ConfirmNextSeason(save, settings);
            Assert.That(save.Promotion.QuarterlyGrowth.History, Has.Count.EqualTo(4)); Assert.That(PromotionProgressionRules.ActiveTier(save), Is.GreaterThanOrEqualTo(2));
            Assert.That(save.SeasonPolicy.RegularShowFrequency, Is.EqualTo(RegularShowFrequency.Weekly));
            Assert.That(save.SeasonPolicy.PpvFrequency, Is.EqualTo(PpvFrequency.Bimonthly));
            Assert.That(save.Promotion.UnlockedIds, Does.Contain("venue_003"));
            Assert.That(save.VenueContracts.Single(x => x.Id == save.SeasonPolicy.RegularVenueContractId).VenueId, Is.EqualTo("venue_003"));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(cash - 300000));
            storage.Save("progression", save); var restored = storage.Load("progression");
            Assert.That(restored.Promotion.QuarterlyGrowth.History, Has.Count.EqualTo(4)); Assert.That(PromotionProgressionRules.ActiveTier(restored), Is.GreaterThanOrEqualTo(1));
            Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
            Assert.That(restored.Promotion.UnlockedIds, Does.Contain("venue_003"));
        }

        [Test]
        public void RookieHire_InitialSnapshotAndProvenLegacyContractSurviveSaveReload()
        {
            var storage = new SaveService(directory); var save = new GameStartPersistenceService(storage).CreateSaveAndReload("rookie", CreateRequest("Rookie", 481));
            foreach (var contract in save.Contracts.Where(x => x.Type == ContractType.Wrestler))
                Assert.That(contract.WasRookieWhenHired, Is.EqualTo(save.Wrestlers.Single(x => x.Id == contract.PersonId).Status.IsRookie));
            var c = save.Contracts.First(x => x.Type == ContractType.Wrestler); var w = save.Wrestlers.Single(x => x.Id == c.PersonId);
            w.Status.IsRookie = false; w.Identity.Background = WrestlerBackground.Rookie; w.Identity.CreatedDate = c.StartDate;
            c.WasRookieWhenHired = false; save.SaveVersion = 9;
            File.WriteAllText(Path.Combine(directory, "rookie.json"), UnityEngine.JsonUtility.ToJson(save));
            var migrated = storage.Load("rookie"); Assert.That(migrated.Contracts.Single(x => x.Id == c.Id).WasRookieWhenHired, Is.True);
            Assert.That(migrated.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            storage.Save("rookie", migrated); var restored = storage.Load("rookie");
            Assert.That(restored.Contracts.Single(x => x.Id == c.Id).WasRookieWhenHired, Is.True);
            Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
        }

        [Test]
        public void Fame_InitialBackgroundAndLegacyMigrationSurviveReloadWithoutReseeding()
        {
            var storage = new SaveService(directory); var request = CreateRequest("Fame", 481); request.HasPlayerCharacter = true; request.PlayerReputation = PlayerReputation.Star;
            var save = new GameStartPersistenceService(storage).CreateSaveAndReload("fame", request);
            var player = save.Wrestlers.Single(x => x.Id == save.Player.WrestlerId);
            Assert.That(player.Fame.Value, Is.EqualTo(5000)); Assert.That(player.Fame.HighestMilestone, Is.EqualTo(2));
            var w = save.Wrestlers.First(x => x.Id != player.Id);
            Assert.That(w.Fame.Value, Is.EqualTo(w.Identity.CareerYears * 10L));
            save.SaveVersion = 8;
            File.WriteAllText(Path.Combine(directory, "fame.json"), UnityEngine.JsonUtility.ToJson(save));
            var migrated = storage.Load("fame"); Assert.That(migrated.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            w = migrated.Wrestlers.Single(x => x.Id == w.Id); w.Fame = WrestlerFameRules.Create(19000); w.Fame.Remainder = .25;
            storage.Save("fame", migrated); var restored = storage.Load("fame");
            Assert.That(restored.Wrestlers.Single(x => x.Id == w.Id).Fame.Value, Is.EqualTo(19000));
            Assert.That(restored.Wrestlers.Single(x => x.Id == w.Id).Fame.Remainder, Is.EqualTo(.25));
            Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
        }

        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "PWManagerGameStartTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void Career_SaveReloadPreservesQuarterResetAndItsSourceHistory()
        {
            var storage = new SaveService(directory);
            var save = new GameStartPersistenceService(storage).CreateSaveAndReload("career", CreateRequest("Career", 481));
            save.CurrentDate = new GameDate(2026, 8, 31); var w = save.Wrestlers[0];
            w.Momentum.Momentum = 80; w.Momentum.MomentumFailure = 2; w.Momentum.LastMomentumActivityDate = default;
            new WrestlerCareerService().PrepareDue(save).Apply(); storage.Save("career", save);
            var restored = storage.Load("career"); Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
            Assert.That(restored.Wrestlers[0].Momentum.Momentum, Is.Zero);
            Assert.That(restored.CareerEvents.Any(x => x.WrestlerId == w.Id && x.Kind == CareerEventKind.Inactivity), Is.True);
            Assert.That(restored.LastCareerQuarterEnd.Value, Is.EqualTo(save.CurrentDate));
            var count = restored.CareerEvents.Count; new WrestlerCareerService().PrepareDue(restored).Apply();
            Assert.That(restored.CareerEvents, Has.Count.EqualTo(count));
        }

        [Test]
        public void QuarterlyGrowth_SaveReloadPreservesHistoryCheckpointAndAttributes()
        {
            var storage = new SaveService(directory);
            var save = new GameStartPersistenceService(storage).CreateSaveAndReload("growth", CreateRequest("Growth", 281));
            save.CurrentDate = new GameDate(2026, 8, 31);
            var w = save.Wrestlers[0]; w.Growth.GrowthProgress.Power = 50;
            new WrestlerGrowthService().PrepareDue(save).Apply();
            var attributes = UnityEngine.JsonUtility.ToJson(w.Attributes);
            storage.Save("growth", save); var restored = storage.Load("growth");
            Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
            Assert.That(restored.GrowthQuarterResults, Has.Count.EqualTo(save.Wrestlers.Count));
            Assert.That(restored.Wrestlers[0].Growth.AttributeHistoryIds, Is.EqualTo(w.Growth.AttributeHistoryIds));
            Assert.That(UnityEngine.JsonUtility.ToJson(restored.Wrestlers[0].Attributes), Is.EqualTo(attributes));
            Assert.That(restored.LastGrowthQuarterEnd.Value, Is.EqualTo(save.CurrentDate));
            var count = restored.GrowthQuarterResults.Count; new WrestlerGrowthService().PrepareDue(restored).Apply();
            Assert.That(restored.GrowthQuarterResults, Has.Count.EqualTo(count));
        }

        [Test]
        public void CreateSaveAndReload_PersistsCompleteInitialGame()
        {
            var service = new GameStartPersistenceService(new SaveService(directory));
            var restored = service.CreateSaveAndReload("initial", CreateRequest("Saved Promotion", 81));

            Assert.That(File.Exists(Path.Combine(directory, "initial.json")), Is.True);
            Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
            Assert.That(restored.Promotion.Name, Is.EqualTo("Saved Promotion"));
            Assert.That(restored.Wrestlers, Has.Count.EqualTo(8));
            Assert.That(restored.Contracts, Has.Count.EqualTo(8));
            Assert.That(restored.SeasonPolicy, Is.Not.Null);
            Assert.That(restored.Schedules, Is.Not.Empty);
        }

        [Test]
        public void FailedReplacement_KeepsPreviousInitialSave()
        {
            var saveService = new SaveService(directory);
            var service = new GameStartPersistenceService(saveService);
            service.CreateSaveAndReload("initial", CreateRequest("First Promotion", 82));
            var invalid = CreateRequest("Invalid Promotion", 83);
            invalid.InitialCash = -1;

            Assert.Throws<ArgumentException>(() => service.CreateSaveAndReload("initial", invalid));
            Assert.That(saveService.Load("initial").Promotion.Name, Is.EqualTo("First Promotion"));
        }

        [Test]
        public void SecondSuccessfulSave_CreatesBackupOfPreviousInitialSave()
        {
            var saveService = new SaveService(directory);
            var service = new GameStartPersistenceService(saveService);
            service.CreateSaveAndReload("initial", CreateRequest("First Promotion", 84));
            service.CreateSaveAndReload("initial", CreateRequest("Second Promotion", 85));

            Assert.That(saveService.Load("initial").Promotion.Name, Is.EqualTo("Second Promotion"));
            Assert.That(saveService.LoadBackup("initial").Promotion.Name, Is.EqualTo("First Promotion"));
        }

        [Test]
        public void InitialCandidates_UnselectedPeopleAndKnownInformationSurviveReload()
        {
            var request = CreateRequest("Persistent Market", 195);
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 195)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1)).Take(20).ToList();
            request.InitialCandidates = candidates;
            request.WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput
                { Wrestler = x, MonthlySalary = 500 }).ToList();
            var originals = candidates.ToDictionary(x => x.Id, UnityEngine.JsonUtility.ToJson);
            var storage = new SaveService(directory);
            var save = new GameStartPersistenceService(storage).CreateSaveAndReload("market", request);

            Assert.That(save.Wrestlers.Select(x => x.Id), Is.EquivalentTo(candidates.Select(x => x.Id)));
            Assert.That(save.Contracts, Has.Count.EqualTo(8));
            Assert.That(save.SalaryPayments, Has.Count.EqualTo(8));
            Assert.That(save.ScoutCandidates.Select(x => x.WrestlerId), Is.EquivalentTo(candidates.Skip(8).Select(x => x.Id)));
            Assert.That(save.ScoutAssignments, Is.Empty);
            foreach (var source in candidates.Skip(8))
            {
                var person = save.Wrestlers.Single(x => x.Id == source.Id);
                var report = save.ScoutCandidates.Single(x => x.WrestlerId == source.Id);
                Assert.That(person.PromotionId, Is.Null.Or.Empty);
                Assert.That(person.Roster.ActivityState, Is.EqualTo(RosterActivityState.Inactive));
                Assert.That(person.Identity.ExpiryDate.HasValue, Is.False);
                Assert.That(report.ExpiryDate.Value, Is.EqualTo(source.Identity.ExpiryDate.Value));
                Assert.That(report.ValueEstimates.Count, Is.EqualTo(Enum.GetValues(typeof(ScoutValueType)).Length));
                Assert.That(report.ValueEstimates.All(x => x.IsFullyRevealed), Is.True);
                Assert.That(report.RevealedTraitIds, Is.EquivalentTo(source.Presentation.TraitIds));
                Assert.That(UnityEngine.JsonUtility.ToJson(person.Presentation), Is.EqualTo(UnityEngine.JsonUtility.ToJson(source.Presentation)));
                Assert.That(ContractNegotiationService.HasCurrentRecruitmentInformation(save, person), Is.True);
            }
            foreach (var source in candidates)
                Assert.That(UnityEngine.JsonUtility.ToJson(source), Is.EqualTo(originals[source.Id]));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void InitialCandidates_RejectInvalidPoolWithoutChangingInputs(int invalidCase)
        {
            var request = CreateRequest("Invalid Market", 196);
            var pool = request.WrestlerContracts.Select(x => x.Wrestler).ToList();
            if (invalidCase == 0) pool.Add(pool[0]);
            if (invalidCase == 1) pool.Add(null);
            if (invalidCase == 2) pool[0].PromotionId = Guid.NewGuid().ToString();
            if (invalidCase == 3) pool.RemoveAt(0);
            request.InitialCandidates = pool;
            var originals = pool.Where(x => x != null).Distinct().ToDictionary(x => x.Id, UnityEngine.JsonUtility.ToJson);

            Assert.Throws<ArgumentException>(() => new GameStartService().CreateInitialSave(request));
            foreach (var person in pool.Where(x => x != null))
                Assert.That(UnityEngine.JsonUtility.ToJson(person), Is.EqualTo(originals[person.Id]));
        }

        [Test]
        public void Version23File_WithoutReportSourceMigratesWithoutChangingPeopleOrKnowledge()
        {
            var save = new GameStartService().CreateInitialSave(CreateRequest("Legacy Market", 197));
            var scout = save.StaffDepartments.Single(x => x.DepartmentType == StaffDepartmentType.Scout);
            scout.CurrentLevel = scout.UnlockedLevel = 1;
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 197), catalog.ScoutTeamLevels);
            var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; service.CompleteDue(save);
            var storage = new SaveService(directory);
            storage.Save("baseline", save); save = storage.Load("baseline");
            var people = save.Wrestlers.ToDictionary(x => x.Id, UnityEngine.JsonUtility.ToJson);
            var reports = save.ScoutCandidates.Select(x => UnityEngine.JsonUtility.ToJson(x)).ToArray();
            save.SaveVersion = 23;
            var legacy = UnityEngine.JsonUtility.ToJson(save).Replace(",\"Source\":0", string.Empty);
            Assert.That(legacy, Does.Not.Contain("\"Source\""));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "legacy23.json"), legacy);

            var restored = storage.Load("legacy23");
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            Assert.That(restored.ScoutCandidates.All(x => x.Source == ScoutReportSource.Scouting), Is.True);
            Assert.That(restored.ScoutCandidates.Select(x => UnityEngine.JsonUtility.ToJson(x)), Is.EqualTo(reports));
            foreach (var person in restored.Wrestlers)
                Assert.That(UnityEngine.JsonUtility.ToJson(person), Is.EqualTo(people[person.Id]));
            var before = UnityEngine.JsonUtility.ToJson(restored);
            new SaveVersion23To24Migration().Migrate(restored);
            Assert.That(UnityEngine.JsonUtility.ToJson(restored), Is.EqualTo(before));
            storage.Save("legacy23", restored);
            Assert.That(storage.Load("legacy23").ScoutCandidates.Select(x => UnityEngine.JsonUtility.ToJson(x)), Is.EqualTo(reports));
        }

        private static GameStartRequest CreateRequest(string name, int seed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), seed)
                .GenerateInitialCandidates(new GameDate(2026, 6, 1));
            return new GameStartRequest
            {
                PromotionName = name,
                InitialCash = 100000,
                WorldSeed = seed,
                UtcNow = new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc),
                WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput
                {
                    Wrestler = x, ContractEndYear = 2027, SigningBonus = 1000, MonthlySalary = 500
                }).ToList(),
                RegularVenueId = "venue_001",
                RegularVenueProductionCost = 500,
                RegularShowFrequency = RegularShowFrequency.Monthly,
                PpvFrequency = PpvFrequency.EveryFourMonths
            };
        }
    }
}
