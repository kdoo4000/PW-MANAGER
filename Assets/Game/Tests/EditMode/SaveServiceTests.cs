using System;
using System.IO;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Infrastructure.Save;
using GameEntityId = PWManager.Domain.Identifiers.EntityId;

namespace PWManager.Tests
{
    public sealed class SaveServiceTests
    {
        private string directory;
        private SaveService service;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "PWManagerTests", Guid.NewGuid().ToString("N"));
            service = new SaveService(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void SaveAndLoad_PreservesValidState()
        {
            var save = CreateValidSave("First");

            service.Save("slot_1", save);
            var restored = service.Load("slot_1");

            Assert.That(restored.Promotion.Name, Is.EqualTo("First"));
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
        }

        [Test]
        public void SecondSave_CreatesLoadableBackupOfPreviousState()
        {
            service.Save("slot_1", CreateValidSave("First"));
            service.Save("slot_1", CreateValidSave("Second"));

            Assert.That(service.Load("slot_1").Promotion.Name, Is.EqualTo("Second"));
            Assert.That(service.LoadBackup("slot_1").Promotion.Name, Is.EqualTo("First"));
        }

        [Test]
        public void InvalidSave_DoesNotReplaceExistingState()
        {
            service.Save("slot_1", CreateValidSave("First"));
            var invalid = CreateValidSave("Invalid");
            invalid.Promotion.InitialCash = -1;

            Assert.Throws<InvalidDataException>(() => service.Save("slot_1", invalid));
            Assert.That(service.Load("slot_1").Promotion.Name, Is.EqualTo("First"));
        }

        [Test]
        public void FutureVersion_IsRejectedWithoutChangingFile()
        {
            var path = Path.Combine(directory, "slot_1.json");
            Directory.CreateDirectory(directory);
            var json = UnityEngine.JsonUtility.ToJson(CreateValidSave("Future"));
            json = json.Replace("\"SaveVersion\":1", "\"SaveVersion\":999");
            File.WriteAllText(path, json);

            Assert.Throws<InvalidDataException>(() => service.Load("slot_1"));
            Assert.That(File.ReadAllText(path), Does.Contain("\"SaveVersion\":999"));
        }

        [Test]
        public void Load_LegacyNWayMatch_NormalizesToIndividual()
        {
            var path = Path.Combine(directory, "slot_1.json");
            var save = CreateValidSave("Legacy");
            save.MatchPlans.Add(new MatchPlanState { Id = GameEntityId.CreateRuntimeId(), MatchTypeId = "matchtype_003" });
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(save));

            var restored = service.Load("slot_1");

            Assert.That(restored.MatchPlans[0].MatchTypeId, Is.EqualTo("matchtype_001"));
        }

        [TestCase("../slot")]
        [TestCase("folder/slot")]
        [TestCase("")]
        public void UnsafeSlotName_IsRejected(string slotName)
        {
            Assert.Throws<ArgumentException>(() => service.Save(slotName, CreateValidSave("Name")));
        }

        private static GameSave CreateValidSave(string promotionName)
        {
            var save = GameSave.CreateNew(1, new DateTime(2026, 8, 18, 7, 0, 0, DateTimeKind.Utc));
            save.Promotion = new PromotionState
            {
                Id = GameEntityId.CreateRuntimeId(),
                Name = promotionName,
                InitialCash = 10000
            };
            return save;
        }
    }
}
