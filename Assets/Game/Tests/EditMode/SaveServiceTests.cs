using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using GameEntityId = PWManager.Domain.Identifiers.EntityId;

namespace PWManager.Tests
{
    public sealed class SaveServiceTests
    {
        [Test]
        public void StreamedSave_PreservesUnityFieldsFloatingPointAndDateText()
        {
            var save = CreateManagerSalarySave();
            save.Promotion.Abbreviation = PromotionState.CreateAbbreviation(save.Promotion.Name);
            save.Managers[0].Attributes.Charisma = 10.123456f;
            save.Managers[0].Name = "2026-07-01T12:30:00+09:00";
            FanAudienceService.Initialize(save);
            save.Promotion.Audience.Mark.PendingAcquisition = 0.123456789012345;
            service.Save("fields", save);
            var restored = service.Load("fields");
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(JsonUtility.ToJson(save)));
            using var input = File.OpenRead(Path.Combine(directory, "fields.json"));
            input.Position = 41;
            using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            var json = reader.ReadToEnd();
            Assert.That(json, Does.Not.Contain("\"TotalFollowers\""));
            Assert.That(json, Does.Not.Contain("\"HasRunningShow\""));
            Assert.That(JsonUtility.ToJson(JsonUtility.FromJson<GameSave>(json)), Is.EqualTo(JsonUtility.ToJson(save)));
        }

        [TestCase("{}")]
        [TestCase(" trailing")]
        public void TrailingJson_IsRejectedWithoutRewritingLegacyFile(string trailing)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "trailing.json");
            var json = JsonUtility.ToJson(CreateValidSave("trailing")) + trailing;
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => service.Load("trailing"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(json));
        }

        [Test]
        public void CompressedSave_PreservesUnicodeWhitespaceAndUsesMuchLessSpace()
        {
            var save = CreateValidSave("압축 테스트");
            var body = string.Concat(Enumerable.Repeat("한글 기록  \n\t다음 줄\"내용\" ", 10000));
            new InboxService().Publish(save, "compression-test", InboxMessageType.Information, InboxPriority.Normal, "운영실", "기록", body);
            var plainBytes = System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(save));
            service.Save("compressed", save);
            var path = Path.Combine(directory, "compressed.json");
            Assert.That(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path).Take(5).ToArray()), Is.EqualTo("PWMZ1"));
            Assert.That(new FileInfo(path).Length, Is.LessThan(plainBytes / 4));
            var restored = service.Load("compressed");
            Assert.That(restored.Promotion.Name, Is.EqualTo("압축 테스트"));
            Assert.That(restored.InboxMessages.Single(x => x.SourceEventId == "compression-test").Body, Is.EqualTo(body));
        }

        [Test]
        public void PlainJsonSave_BecomesCompressedOnExplicitSaveAndKeepsPlainBackup()
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "plain.json"); var json = JsonUtility.ToJson(CreateValidSave("기존 저장"), true);
            File.WriteAllText(path, json);
            var loaded = service.Load("plain"); Assert.That(File.ReadAllText(path), Is.EqualTo(json));
            loaded.Promotion.Name = "새 저장"; service.Save("plain", loaded);
            Assert.That(service.Load("plain").Promotion.Name, Is.EqualTo("새 저장"));
            Assert.That(service.LoadBackup("plain").Promotion.Name, Is.EqualTo("기존 저장"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "plain.bak")), Is.EqualTo(json));
        }

        [TestCase("payload")]
        [TestCase("truncated")]
        [TestCase("length")]
        [TestCase("version")]
        public void DamagedCompressedSave_IsRejectedAndPreservesBackup(string damage)
        {
            service.Save("damage", CreateValidSave("이전")); service.Save("damage", CreateValidSave("현재"));
            var path = Path.Combine(directory, "damage.json"); var bytes = File.ReadAllBytes(path);
            if (damage == "payload") bytes[bytes.Length - 1] ^= 1;
            if (damage == "truncated") Array.Resize(ref bytes, bytes.Length - 8);
            if (damage == "length") bytes[5] ^= 1;
            if (damage == "version") bytes[4] = (byte)'9';
            File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => service.Load("damage"));
            Assert.That(service.LoadBackup("damage").Promotion.Name, Is.EqualTo("이전"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        }

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

        [TestCase(ScoutAssignmentStatus.InProgress, 28, 7)]
        [TestCase(ScoutAssignmentStatus.Completed, 28, 28)]
        [TestCase(ScoutAssignmentStatus.InProgress, 3, 3)]
        public void Load_SevenDayScoutingUpgrade_ShortensOnlyLongPendingAssignments(ScoutAssignmentStatus status, int oldDays, int expectedDays)
        {
            var save = CreateValidSave("Legacy"); save.SaveVersion = 12;
            var start = save.CurrentDate.AddDays(-10);
            save.ScoutAssignments.Add(new ScoutAssignmentState { Id = GameEntityId.CreateRuntimeId(), StartDate = start,
                CompleteDate = start.AddDays(oldDays), Status = status });
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "scouting.json");
            var json = JsonUtility.ToJson(save); File.WriteAllText(path, json);
            var restored = service.Load("scouting");
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            Assert.That(restored.ScoutAssignments.Single().CompleteDate, Is.EqualTo(start.AddDays(expectedDays)));
            Assert.That(restored.ScoutAssignments.Single().Status, Is.EqualTo(status));
            Assert.That(File.ReadAllText(path), Is.EqualTo(json));
            service.Save("scouting", restored);
            Assert.That(service.Load("scouting").ScoutAssignments.Single().CompleteDate, Is.EqualTo(start.AddDays(expectedDays)));
        }

        [Test]
        public void Load_LegacyScoutReport_KeepsOriginalOpinionWithoutRerollAndRoundTrips()
        {
            var save = CreateValidSave("Legacy scout"); save.SaveVersion = 15;
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            save.Wrestlers.Add(new WrestlerGenerator(new StaticContentRegistry(catalog), 7).GenerateCandidate(WrestlerGender.Female, save.CurrentDate));
            var assignment = new ScoutAssignmentState { Id = GameEntityId.CreateRuntimeId(), StartDate = save.CurrentDate, CompleteDate = save.CurrentDate.AddDays(7), Status = ScoutAssignmentStatus.Completed };
            save.ScoutAssignments.Add(assignment);
            var report = new ScoutCandidateState { WrestlerId = save.Wrestlers[0].Id, AssignmentId = assignment.Id, KnowledgeLevel = 2 };
            foreach (ScoutValueType type in Enum.GetValues(typeof(ScoutValueType)))
                report.ValueEstimates.Add(new ScoutValueEstimate { Type = type, Minimum = type == ScoutValueType.Power ? 10 : 12, Maximum = type == ScoutValueType.Power ? 10 : 16 });
            save.ScoutCandidates.Add(report);
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "scout.json");
            var json = JsonUtility.ToJson(save); File.WriteAllText(path, json);
            var restored = service.Load("scout"); var estimates = restored.ScoutCandidates.Single().ValueEstimates;
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            Assert.That(estimates.All(x => x.HasEvaluation), Is.True);
            Assert.That(ScoutAssessmentRules.Display(estimates.Single(x => x.Type == ScoutValueType.Power)), Is.EqualTo("D+"));
            Assert.That(estimates.Where(x => x.Type != ScoutValueType.Power).All(x => !x.IsFullyRevealed && x.Assessment == ScoutAssessment.High && x.Minimum == 12 && x.Maximum == 16), Is.True);
            Assert.That(File.ReadAllText(path), Is.EqualTo(json));
            service.Save("scout", restored);
            Assert.That(JsonUtility.ToJson(service.Load("scout").ScoutCandidates.Single()), Is.EqualTo(JsonUtility.ToJson(restored.ScoutCandidates.Single())));
        }

        [Test]
        public void Load_UpgradesLegacyTitlePrestigeAndPreservesZeroOnSubsequentLoad()
        {
            var save = CreateValidSave("Legacy");
            save.Championships.Add(new ChampionshipState { Id = GameEntityId.CreateRuntimeId(), PromotionId = save.Promotion.Id,
                Name = "Legacy World Championship", IsWorldChampionship = true, HasPrestige = false, MatchHistory = null });
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "slot_1.json"), JsonUtility.ToJson(save));
            var restored = service.Load("slot_1");
            Assert.That(restored.Championships[0].HasPrestige, Is.True);
            Assert.That(restored.Championships[0].Prestige, Is.EqualTo(60));
            Assert.That(restored.Championships[0].MatchHistory, Is.Empty);
            restored.Championships[0].Prestige = 0;
            service.Save("slot_1", restored);
            Assert.That(service.Load("slot_1").Championships[0].Prestige, Is.Zero);
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
            json = json.Replace($"\"SaveVersion\":{GameSave.CurrentSaveVersion}", "\"SaveVersion\":999");
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

        [Test]
        public void Termination_SaveLoadPreservesPaymentAndReleasedPerson()
        {
            var save = CreateManagerSalarySave(); save.Promotion.InitialCash = 100000;
            var result = new ContractTerminationService().Submit(save, save.Managers[0].Id, ContractType.Manager, false);
            service.Save("termination", save);
            var restored = service.Load("termination");
            Assert.That(restored.ContractTerminations.Single().Id, Is.EqualTo(result.Id));
            Assert.That(restored.Managers[0].ActivityState, Is.EqualTo(RosterActivityState.Released));
            Assert.That(restored.Transactions.Single().Amount, Is.EqualTo(-result.Compensation));
            Assert.That(new ContractTerminationService().Submit(restored, restored.Managers[0].Id, ContractType.Manager, false).Id, Is.EqualTo(result.Id));
            Assert.That(restored.Transactions, Has.Count.EqualTo(1));
        }

        [Test]
        public void VersionThreeMigration_AddsEmptyTerminationHistoryWithoutChangingCashOrContract()
        {
            var save = CreateManagerSalarySave(); save.SaveVersion = 3; save.ContractTerminations = null;
            var contractId = save.Contracts[0].Id; var cash = save.Promotion.CalculateCurrentCash(save.Transactions);
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "v3.json"), JsonUtility.ToJson(save));
            var restored = service.Load("v3");
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion)); Assert.That(restored.ContractTerminations, Is.Empty);
            Assert.That(restored.Contracts[0].Id, Is.EqualTo(contractId));
            Assert.That(restored.Promotion.CalculateCurrentCash(restored.Transactions), Is.EqualTo(cash));
        }

        [Test]
        public void VersionFourMigration_AddsSeasonHistoryWithoutChangingExistingCash()
        {
            var save = CreateValidSave("Season Legacy"); save.SaveVersion = 4; save.SeasonResults = null;
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "v4.json"), JsonUtility.ToJson(save));
            var restored = service.Load("v4");
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion)); Assert.That(restored.SeasonResults, Is.Not.Null.And.Empty);
            Assert.That(restored.Promotion.InitialCash, Is.EqualTo(save.Promotion.InitialCash));
        }

        [Test]
        public void NextSeason_SaveLoadPreservesArchivedPolicyAndDoesNotDuplicateGeneration()
        {
            var save = CreateValidSave("Season"); save.CurrentDate = new GameDate(2027, 5, 31);
            var venue = new VenueContractState { Id = GameEntityId.CreateRuntimeId(), VenueId = "venue_001",
                StartDate = new GameDate(2026, 6, 1), EndDate = save.CurrentDate, ProductionCost = 250, Status = VenueContractStatus.Reserved };
            save.VenueContracts.Add(venue);
            save.SeasonPolicy = new SeasonPolicyState { Id = GameEntityId.CreateRuntimeId(), StartDate = venue.StartDate, EndDate = venue.EndDate,
                RegularVenueContractId = venue.Id, RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.Quarterly,
                RegularShowName = "Old", RegularShowDurationMinutes = 120 };
            save.Promotion.SeasonPolicyId = save.SeasonPolicy.Id;
            var request = SeasonTransitionService.DefaultSettings(save); new SeasonTransitionService().ConfirmNextSeason(save, request);
            service.Save("season", save); var restored = service.Load("season");
            Assert.That(restored.SeasonResults.Single().Policy.Id, Is.EqualTo(request.SourcePolicyId));
            Assert.That(restored.SeasonResults.Single().NextSeasonPolicyId, Is.EqualTo(restored.SeasonPolicy.Id));
            var count = restored.Shows.Count; new SeasonTransitionService().ConfirmNextSeason(restored, request);
            Assert.That(restored.Shows, Has.Count.EqualTo(count));
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

        [Test]
        public void SalaryArrears_SaveLoadPreservesDebtAndDoesNotRepeatInitialPenalty()
        {
            var save = CreateManagerSalarySave();
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            service.Save("salary", save);
            var restored = service.Load("salary");
            new SalaryPaymentService().ProcessDay(restored, restored.CurrentDate);
            Assert.That(restored.SalaryPayments, Has.Count.EqualTo(1));
            Assert.That(SalaryPaymentService.Outstanding(restored), Is.EqualTo(100));
            Assert.That(restored.Managers[0].Satisfaction, Is.EqualTo(60));
            new TimeFlowService().AdvanceTo(restored, restored.CurrentDate.AddDays(7));
            Assert.That(restored.Managers[0].Satisfaction, Is.EqualTo(55));
            service.Save("salary", restored);
            Assert.That(service.Load("salary").Managers[0].Satisfaction, Is.EqualTo(55));
        }

        [Test]
        public void Load_VersionOneAddsSalaryStateWithoutChargingAlreadyProcessedPayday()
        {
            var save = CreateManagerSalarySave();
            save.SaveVersion = 1;
            save.SalaryPayments = null;
            save.Managers[0].Satisfaction = 0;
            save.ProcessedIds.Add($"salary:{save.Contracts[0].Id}:20260701");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "legacy.json"), JsonUtility.ToJson(save));
            var restored = service.Load("legacy");
            new SalaryPaymentService().ProcessDay(restored, restored.CurrentDate);
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            Assert.That(restored.SalaryPayments, Is.Empty);
            Assert.That(restored.Managers[0].Satisfaction, Is.EqualTo(50));
            Assert.That(restored.Promotion.CalculateCurrentCash(restored.Transactions), Is.EqualTo(100));
        }

        [Test]
        public void Save_InvalidSalaryDebtDoesNotReplaceExistingSave()
        {
            var save = CreateManagerSalarySave();
            service.Save("salary", save);
            save.SalaryPayments.Add(new SalaryPaymentState { Id = GameEntityId.CreateRuntimeId(), ContractId = save.Contracts[0].Id,
                DueDate = save.CurrentDate, Amount = 200, RemainingAmount = 201 });
            Assert.Throws<InvalidDataException>(() => service.Save("salary", save));
            Assert.That(service.Load("salary").SalaryPayments, Is.Empty);
        }

        private static GameSave CreateManagerSalarySave()
        {
            var save = CreateValidSave("Salary");
            save.CurrentDate = new GameDate(2026, 7, 1);
            save.Promotion.InitialCash = 100;
            var manager = new ManagerState { Id = GameEntityId.CreateRuntimeId(), PromotionId = save.Promotion.Id,
                PromoPotentialCap = 10, Satisfaction = 70, Attributes = new ManagerAttributesState {
                    Charisma = 10, MicWork = 10, Improvisation = 10, Acting = 10, FaceWork = 10,
                    HeelWork = 10, Comedy = 10, RingImprovisation = 10, SpotWork = 10, Selling = 10 } };
            save.Managers.Add(manager);
            save.Contracts.Add(new ContractState { Id = GameEntityId.CreateRuntimeId(), PersonId = manager.Id,
                Type = ContractType.Manager, StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31),
                MonthlySalary = 200, Status = ContractStatus.Active });
            return save;
        }

        [Test]
        public void Negotiation_SaveLoadPreservesScheduledContractAndRecordedOutcome()
        {
            var save = CreateManagerSalarySave(); save.Promotion.InitialCash = 100000;
            var manager = save.Managers[0]; manager.Name = "계약 매니저"; manager.Satisfaction = 50;
            var demand = ContractNegotiationService.Demand(save, manager.Id, ContractType.Manager);
            var result = new ContractNegotiationService(random: new System.Random(1)).Submit(save, manager.Id,
                ContractType.Manager, demand.SigningBonus, demand.MonthlySalary);
            service.Save("contract", save);
            var restored = service.Load("contract");
            Assert.That(restored.ContractNegotiations.Single().RollPercent, Is.EqualTo(result.RollPercent));
            Assert.That(restored.ContractNegotiations.Single().Accepted, Is.True);
            Assert.That(restored.Contracts.Single(x => x.Id == result.ContractId).Status, Is.EqualTo(ContractStatus.Scheduled));
            Assert.That(restored.Managers[0].Name, Is.EqualTo("계약 매니저"));
            Assert.That(restored.Transactions.Single().Amount, Is.EqualTo(-demand.SigningBonus));
        }

        [Test]
        public void Load_VersionTwoPreservesSatisfactionAndAddsManagerNameAndNegotiationHistory()
        {
            var save = CreateManagerSalarySave(); save.SaveVersion = 2;
            save.Player = new PlayerCharacterState { Name = "이름 보존", ManagerId = save.Managers[0].Id };
            save.Managers[0].Name = null; save.Managers[0].Satisfaction = 35; save.ContractNegotiations = null;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "legacy2.json"), JsonUtility.ToJson(save));
            var restored = service.Load("legacy2");
            Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            Assert.That(restored.ContractNegotiations, Is.Empty);
            Assert.That(restored.Managers[0].Name, Is.EqualTo("이름 보존"));
            Assert.That(restored.Managers[0].Satisfaction, Is.EqualTo(35));
        }
    }
}
