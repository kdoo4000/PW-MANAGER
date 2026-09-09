using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
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
            var initialUpdatedAtUtc = save.UpdatedAtUtc;

            service.Save("slot_1", save);
            var restored = service.Load("slot_1");

            Assert.That(restored.Promotion.Name, Is.EqualTo("First"));
            Assert.That(restored.Promotion.Abbreviation, Is.EqualTo("FIRST"));
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            Assert.That(restored.UpdatedAtUtc, Is.Not.EqualTo(initialUpdatedAtUtc));
            Assert.That(DateTime.Parse(restored.UpdatedAtUtc, null, DateTimeStyles.RoundtripKind).Kind, Is.EqualTo(DateTimeKind.Utc));
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
            save.SaveVersion = 0;
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

        [Test]
        public void CorruptPrimary_BackupRemainsAvailableAndLoadingDoesNotRewriteFiles()
        {
            service.Save("slot_1", CreateValidSave("First"));
            Assert.That(service.HasBackup("slot_1"), Is.False);
            service.Save("slot_1", CreateValidSave("Second"));
            var backup = File.ReadAllText(Path.Combine(directory, "slot_1.bak"));
            File.WriteAllText(Path.Combine(directory, "slot_1.json"), "broken JSON");

            Assert.Throws<InvalidDataException>(() => service.Load("slot_1"));
            Assert.That(service.HasBackup("slot_1"), Is.True);
            Assert.That(service.ListSlots(), Does.Contain("slot_1"));
            Assert.That(service.LoadBackup("slot_1").Promotion.Name, Is.EqualTo("First"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "slot_1.json")), Is.EqualTo("broken JSON"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "slot_1.bak")), Is.EqualTo(backup));
            Assert.Throws<ArgumentException>(() => service.HasBackup("../slot"));
        }

        [Test]
        public void TitleLoadMenu_FailedContinueOffersBackupAndButtonLoadsIt()
        {
            service.Save("slot_1", CreateValidSave("First"));
            service.Save("slot_1", CreateValidSave("Second"));
            File.WriteAllText(Path.Combine(directory, "slot_1.json"), "broken JSON");
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var owner = new GameObject("Backup load test");
            owner.SetActive(false);
            try
            {
                var controller = owner.AddComponent<TitleScreenController>();
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/TitleScreen").Instantiate();
                var rows = root.Q("title-load-rows");
                var overlay = root.Q("title-load-overlay");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                void Set(string name, object value) => typeof(TitleScreenController).GetField(name, flags).SetValue(controller, value);
                Set("root", root); Set("saveService", service); Set("loadRows", rows); Set("loadOverlay", overlay);
                Set("status", root.Q<Label>("title-status"));
                Set("dashboard", new VisualElement()); Set("newGameDocumentRoot", new VisualElement());

                typeof(TitleScreenController).GetMethod("ContinueGame", flags).Invoke(controller, null);

                Assert.That(DashboardSession.ActiveSave, Is.SameAs(previous));
                Assert.That(overlay.ClassListContains("hidden"), Is.False);
                var backup = rows.Children().OfType<Button>().Single(x => x.text.Contains("이전 백업 불러오기"));
                typeof(Clickable).GetMethod("Invoke", flags).Invoke(backup.clickable, new object[] { null });
                Assert.That(DashboardSession.ActiveSave.Promotion.Name, Is.EqualTo("First"));
                Assert.That(overlay.ClassListContains("hidden"), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(directory, "slot_1.json")), Is.EqualTo("broken JSON"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                DashboardSession.SetActiveSave(previous, transient);
            }
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
