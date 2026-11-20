using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class ContractOwnershipTests
    {
        [Test]
        public void Payday_ChargesOnlyOurEmployerAndPreservesForeignContract()
        {
            var save = Create(); var foreign = save.Contracts[1]; var before = JsonUtility.ToJson(foreign);
            var foreignPerson = JsonUtility.ToJson(save.Wrestlers[1]);
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            Assert.That(save.SalaryPayments.Select(x => x.ContractId), Is.EqualTo(new[] { save.Contracts[0].Id }));
            Assert.That(save.Transactions.Sum(x => x.Amount), Is.EqualTo(-100));
            Assert.That(FinanceReportService.ContractsDue(save, save.CurrentDate).Select(x => x.Id), Is.EqualTo(new[] { save.Contracts[0].Id }));
            Assert.That(JsonUtility.ToJson(foreign), Is.EqualTo(before));
            Assert.That(JsonUtility.ToJson(save.Wrestlers[1]), Is.EqualTo(foreignPerson));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OurTimeFlow_DoesNotActivateOrExpireForeignContract(bool scheduled)
        {
            var save = Create(); var foreign = save.Contracts[1]; var person = save.Wrestlers[1];
            person.Status.IsRookie = false;
            if (scheduled)
            {
                foreign.Status = ContractStatus.Scheduled; foreign.StartDate = save.CurrentDate.AddDays(1);
                person.Roster.ActivityState = RosterActivityState.Inactive;
            }
            else foreign.EndDate = new GameDate(2026, 5, 31);
            var original = JsonUtility.ToJson(foreign); var identity = person.PromotionId;
            Assert.That(new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(2)).State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(JsonUtility.ToJson(foreign), Is.EqualTo(original));
            Assert.That(person.PromotionId, Is.EqualTo(identity));
            Assert.That(person.Roster.ActivityState, Is.EqualTo(scheduled ? RosterActivityState.Inactive : RosterActivityState.Active));
            Assert.That(save.InboxMessages.Any(x => x.SourceEventId?.Contains(foreign.Id) == true), Is.False);
            Assert.That(save.SalaryPayments.Any(x => x.ContractId == foreign.Id), Is.False);
        }

        [Test]
        public void ForeignContract_StoresAndReloadsWithItsEmployerAndCannotBeTerminatedByUs()
        {
            var save = Create(); var contract = JsonUtility.ToJson(save.Contracts[1]);
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-contract-owners-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new SaveService(directory); storage.Save("owners", save); var restored = storage.Load("owners");
                Assert.That(JsonUtility.ToJson(restored.Contracts[1]), Is.EqualTo(contract));
                Assert.That(restored.OtherPromotions.Single().Id, Is.EqualTo(save.OtherPromotions[0].Id));
                Assert.That(restored.Wrestlers[1].PromotionId, Is.EqualTo(save.OtherPromotions[0].Id));
                Assert.That(restored.OwnContracts.Select(x => x.Id), Is.EqualTo(new[] { save.Contracts[0].Id }));
                Assert.That(GameSaveValidator.Validate(restored), Is.Empty);
                var before = JsonUtility.ToJson(restored);
                Assert.Throws<InvalidOperationException>(() => ContractTerminationService.Quote(restored, restored.Wrestlers[1].Id, ContractType.Wrestler));
                Assert.Throws<InvalidOperationException>(() => new ContractTerminationService().Submit(restored, restored.Wrestlers[1].Id, ContractType.Wrestler, false));
                Assert.Throws<InvalidOperationException>(() => new SalaryPaymentService().PrepareSettlement(restored, restored.CurrentDate,
                    Array.Empty<TransactionRecord>(), startingContract: restored.Contracts[1]));
                Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(before));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void Version25File_FillsLegacyEmployerWithoutChangingContractTermsOrRecords()
        {
            var save = Create(); save.Contracts.RemoveAt(1); save.Wrestlers.RemoveAt(1); save.OtherPromotions.Clear();
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            var expectedContract = JsonUtility.ToJson(save.Contracts[0]);
            var salary = save.SalaryPayments.Select(x => JsonUtility.ToJson(x)).ToArray();
            var transactions = save.Transactions.Select(x => JsonUtility.ToJson(x)).ToArray();
            save.Contracts[0].PromotionId = null; save.SaveVersion = 25;
            var legacy = JsonUtility.ToJson(save).Replace(",\"PromotionId\":\"\"", string.Empty).Replace(",\"PromotionId\":null", string.Empty)
                .Replace(",\"OtherPromotions\":[]", string.Empty);
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-legacy-owners-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "legacy.json"), legacy);
                var storage = new SaveService(directory); var restored = storage.Load("legacy");
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
                Assert.That(JsonUtility.ToJson(restored.Contracts[0]), Is.EqualTo(expectedContract));
                Assert.That(restored.SalaryPayments.Select(x => JsonUtility.ToJson(x)), Is.EqualTo(salary));
                Assert.That(restored.Transactions.Select(x => JsonUtility.ToJson(x)), Is.EqualTo(transactions));
                Assert.That(restored.OtherPromotions, Is.Empty);
                var before = JsonUtility.ToJson(restored); new SaveVersion25To26Migration().Migrate(restored);
                Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(before));
                storage.Save("legacy", restored);
                Assert.That(storage.Load("legacy").Contracts[0].PromotionId, Is.EqualTo(restored.Promotion.Id));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void OwnershipValidation_RejectsUnknownOrConflictingReferences(int invalidCase)
        {
            var save = Create();
            if (invalidCase == 0) save.Contracts[1].PromotionId = Guid.NewGuid().ToString();
            if (invalidCase == 1) save.OtherPromotions[0].Id = save.Promotion.Id;
            if (invalidCase == 2) save.Contracts[1].PromotionId = save.Promotion.Id;
            if (invalidCase == 3) save.SalaryPayments.Add(new SalaryPaymentState { Id = Guid.NewGuid().ToString(), ContractId = save.Contracts[1].Id,
                DueDate = save.CurrentDate, Amount = 100, RemainingAmount = 100 });
            Assert.That(GameSaveValidator.Validate(save), Is.Not.Empty);
        }

        [Test]
        public void ForeignSalaryReference_IsRejectedBeforeAnyPaymentOrPenalty()
        {
            var save = Create(); save.SalaryPayments.Add(new SalaryPaymentState { Id = Guid.NewGuid().ToString(),
                ContractId = save.Contracts[1].Id, DueDate = save.CurrentDate, Amount = 100, RemainingAmount = 100 });
            Assert.That(SalaryPaymentService.Outstanding(save), Is.Zero);
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new SalaryPaymentService().ProcessDay(save, save.CurrentDate));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void ForeignFutureCommitment_StillBlocksOverlappingOwnRenewal()
        {
            var save = Create(); var person = save.Wrestlers[0]; var other = save.Contracts[1];
            other.PersonId = person.Id; other.Status = ContractStatus.Scheduled;
            other.StartDate = save.Contracts[0].EndDate.AddDays(1); other.EndDate = new GameDate(2028, 5, 31);
            var demand = ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler);
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ContractNegotiationService().Submit(save, person.Id, ContractType.Wrestler,
                demand.SigningBonus, demand.MonthlySalary));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void NewHire_RecordsOwnEmployerAndKeepsPreviousForeignContract()
        {
            var save = Create(); var person = save.Wrestlers[1]; var previous = save.Contracts[1];
            previous.Status = ContractStatus.Expired; previous.EndDate = new GameDate(2026, 5, 31);
            person.PromotionId = null; person.Roster.ActivityState = RosterActivityState.Inactive;
            var history = JsonUtility.ToJson(previous);
            var demand = ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler);
            var result = new ContractNegotiationService(random: new ZeroRandom()).Submit(save, person.Id, ContractType.Wrestler,
                demand.SigningBonus, demand.MonthlySalary);
            Assert.That(result.Accepted, Is.True);
            Assert.That(save.Contracts.Single(x => x.Id == result.ContractId).PromotionId, Is.EqualTo(save.Promotion.Id));
            Assert.That(person.PromotionId, Is.EqualTo(save.Promotion.Id));
            Assert.That(JsonUtility.ToJson(previous), Is.EqualTo(history));
        }

        [Test]
        public void ForeignProfile_DoesNotExposeOurContractControlsOrSalary()
        {
            var save = Create(); var person = save.Wrestlers[1];
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Foreign employer profile"); go.SetActive(false);
            var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            try
            {
                DashboardSession.SetActiveSave(save, true);
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { root });
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { person });
                Assert.That(root.Q<Button>("wp-open-contract").enabledSelf, Is.False);
                Assert.That(root.Q<Button>("wp-edit-appearance").enabledSelf, Is.False);
                Assert.That(root.Query<Label>().ToList().Any(x => x.text == "$50,000"), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OldOwnArrearsExpiry_DoesNotReleaseCurrentForeignEmployment(bool manager)
        {
            var save = Create(); var foreign = save.Contracts[1]; var personId = ForeignPerson(save, manager);
            foreign.PersonId = personId; foreign.Type = manager ? ContractType.Manager : ContractType.Wrestler;
            var past = save.Contracts[0]; past.PersonId = personId; past.Type = foreign.Type;
            past.StartDate = new GameDate(2025, 6, 1); past.EndDate = new GameDate(2026, 5, 31);
            past.Status = ContractStatus.Overdue; past.LastSalaryPenaltyDate = new(save.CurrentDate);
            save.SalaryPayments.Add(new SalaryPaymentState { Id = Guid.NewGuid().ToString(), ContractId = past.Id,
                DueDate = new GameDate(2026, 5, 1), Amount = 100, RemainingAmount = 100, WasOverdue = true });
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            var original = JsonUtility.ToJson(foreign);
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(JsonUtility.ToJson(foreign), Is.EqualTo(original));
            Assert.That(manager ? save.Managers.Single().PromotionId : save.Wrestlers[1].PromotionId, Is.EqualTo(save.OtherPromotions[0].Id));
            Assert.That(manager ? save.Managers.Single().ActivityState : save.Wrestlers[1].Roster.ActivityState, Is.EqualTo(RosterActivityState.Active));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OurFutureContract_CanBeCancelledWithoutReleasingCurrentForeignEmployment(bool manager)
        {
            var save = Create(); var foreign = save.Contracts[1]; var personId = ForeignPerson(save, manager);
            foreign.PersonId = personId; foreign.Type = manager ? ContractType.Manager : ContractType.Wrestler;
            var future = new ContractState { Id = Guid.NewGuid().ToString(), PersonId = personId, PromotionId = save.Promotion.Id,
                Type = foreign.Type, StartDate = foreign.EndDate.AddDays(1), EndDate = new GameDate(2028, 5, 31),
                MonthlySalary = 100, Status = ContractStatus.Scheduled };
            save.Contracts.Add(future); Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            var original = JsonUtility.ToJson(foreign);
            var result = new ContractTerminationService(random: new ZeroRandom()).Submit(save, personId, foreign.Type, false);
            Assert.That(result.Accepted, Is.True); Assert.That(result.ContractIds, Is.EqualTo(new[] { future.Id }));
            Assert.That(future.Status, Is.EqualTo(ContractStatus.Terminated));
            Assert.That(JsonUtility.ToJson(foreign), Is.EqualTo(original));
            Assert.That(manager ? save.Managers.Single().ActivityState : save.Wrestlers[1].Roster.ActivityState, Is.EqualTo(RosterActivityState.Active));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void DailyBriefing_CountsOnlyOurActiveRoster()
        {
            var save = Create(); new InboxService().EnsureForCurrentDate(save);
            var brief = save.InboxMessages.Single(x => x.SourceEventId.StartsWith("daily-brief:"));
            Assert.That(brief.Body, Does.Contain("활동 선수 1명"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OurFutureAgreement_CanBeManagedInViewWithoutReleasingForeignEmployment(bool expiredInformation)
        {
            var save = Create(); var person = save.Wrestlers[1]; var foreign = save.Contracts[1];
            if (expiredInformation) person.Identity.ExpiryDate = new(save.CurrentDate.AddDays(-1));
            var future = new ContractState { Id = Guid.NewGuid().ToString(), PersonId = person.Id, PromotionId = save.Promotion.Id,
                Type = ContractType.Wrestler, StartDate = foreign.EndDate.AddDays(1), EndDate = new GameDate(2028, 5, 31),
                MonthlySalary = 100, Status = ContractStatus.Scheduled };
            save.Contracts.Add(future); var original = JsonUtility.ToJson(foreign);
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Future own contract profile"); go.SetActive(false);
            var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            try
            {
                DashboardSession.SetActiveSave(save, true);
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { root });
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { person });
                Assert.That(root.Q<Button>("wp-open-contract").enabledSelf, Is.True);
                var overlay = ContractNegotiationView.Open(new VisualElement(), person.Id, ContractType.Wrestler);
                Assert.That(overlay.Q<Button>("contract-submit").enabledSelf, Is.False);
                Assert.That(overlay.Query<Label>().ToList().Any(x => x.text?.Contains("다른 단체 소속") == true), Is.False);
                var click = typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
                click.Invoke(overlay.Q<Button>("contract-release").clickable, new object[] { null });
                click.Invoke(overlay.Q<Button>("contract-termination-confirm").clickable, new object[] { null });
                Assert.That(future.Status, Is.EqualTo(ContractStatus.Terminated));
                Assert.That(person.Roster.ActivityState, Is.EqualTo(RosterActivityState.Active));
                Assert.That(person.PromotionId, Is.EqualTo(save.OtherPromotions[0].Id));
                Assert.That(JsonUtility.ToJson(foreign), Is.EqualTo(original));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        private static string ForeignPerson(GameSave save, bool manager)
        {
            if (!manager) return save.Wrestlers[1].Id;
            var person = new ManagerState { Id = Guid.NewGuid().ToString(), Name = "Foreign Manager", PromotionId = save.OtherPromotions[0].Id,
                ActivityState = RosterActivityState.Active, PromoPotentialCap = 10, Satisfaction = 70 };
            foreach (var field in typeof(ManagerAttributesState).GetFields()) field.SetValue(person.Attributes, 10f);
            save.Managers.Add(person); return person.Id;
        }

        private sealed class ZeroRandom : System.Random { public override double NextDouble() => 0; }

        private static GameSave Create()
        {
            var save = GameSave.CreateNew(7, DateTime.UtcNow);
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Our Promotion", InitialCash = 10000 };
            var other = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Other Promotion", PromotionPrestige = 5000 };
            save.OtherPromotions.Add(other);
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
            var generator = new WrestlerGenerator(new StaticContentRegistry(catalog), 71);
            foreach (var employer in new[] { save.Promotion, other })
            {
                var person = generator.GenerateCandidate(WrestlerGender.Male, save.CurrentDate);
                person.PromotionId = employer.Id; person.Roster.ActivityState = RosterActivityState.Active;
                person.Identity.ExpiryDate = default; person.Condition.Satisfaction = 70;
                save.Wrestlers.Add(person);
                save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString(), PersonId = person.Id, PromotionId = employer.Id,
                    Type = ContractType.Wrestler, StartDate = save.CurrentDate, EndDate = new GameDate(2027, 5, 31),
                    MonthlySalary = employer == save.Promotion ? 100 : 50000, Status = ContractStatus.Active });
            }
            return save;
        }
    }
}
