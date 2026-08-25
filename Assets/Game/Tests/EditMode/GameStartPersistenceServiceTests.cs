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
