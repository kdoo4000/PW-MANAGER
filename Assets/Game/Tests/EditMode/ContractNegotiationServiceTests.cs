using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using Random = System.Random;

namespace PWManager.Tests
{
    public sealed class ContractNegotiationServiceTests
    {
        [Test]
        public void RookieHiringSnapshot_IsRecordedOnNewHireAndPreservedAcrossRenewal()
        {
            var save = CreateSave(); var w = save.Wrestlers[0]; w.Status.IsRookie = true;
            var demand = ContractNegotiationService.Demand(save, w.Id, ContractType.Wrestler);
            var first = new ContractNegotiationService(random: new Random(1)).Submit(save, w.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            var field = typeof(ContractState).GetField("WasRookieWhenHired"); Assert.That(field, Is.Not.Null);
            Assert.That((bool)field.GetValue(save.Contracts.Single(x => x.Id == first.ContractId)), Is.True);
            save.CurrentDate = save.CurrentDate.AddDays(1); w.Status.IsRookie = false;
            var renewal = new ContractNegotiationService(random: new Random(1)).Submit(save, w.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That((bool)field.GetValue(save.Contracts.Single(x => x.Id == renewal.ContractId)), Is.False);
            Assert.That((bool)field.GetValue(save.Contracts.Single(x => x.Id == first.ContractId)), Is.True);
        }

        [Test]
        public void FutureSeason_NewHireStartsAtSeasonStartInsteadOfOneDayContract()
        {
            var save = CreateSave(); save.CurrentDate = new GameDate(2027, 5, 31);
            save.SeasonPolicy = new SeasonPolicyState { StartDate = new GameDate(2027, 6, 1), EndDate = new GameDate(2028, 5, 31) };
            var id = save.Wrestlers[0].Id; var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            var outcome = new ContractNegotiationService(random: new Random(1)).Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That(outcome.Accepted, Is.True);
            var contract = save.Contracts.Single(x => x.Id == outcome.ContractId);
            Assert.That(contract.StartDate, Is.EqualTo(new GameDate(2027, 6, 1))); Assert.That(contract.Status, Is.EqualTo(ContractStatus.Scheduled));
        }

        [Test]
        public void Acceptance_UsesSeventyPercentBaselineAndImprovesWithTermsAndSatisfaction()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            float Chance(long bonus, long salary) => ContractNegotiationService.Acceptance(save, id, ContractType.Wrestler, bonus, salary);
            Assert.That(Chance(demand.SigningBonus, demand.MonthlySalary), Is.EqualTo(70));
            Assert.That(Chance(demand.SigningBonus, demand.MonthlySalary * 2), Is.EqualTo(95));
            Assert.That(Chance(0, 1), Is.Zero);
            save.Wrestlers[0].Condition.Satisfaction = 70;
            Assert.That(Chance(demand.SigningBonus, demand.MonthlySalary), Is.EqualTo(80));
        }

        [Test]
        public void Rejection_BlocksSixDaysAndAllowsDaySevenAfterReload()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            var service = new ContractNegotiationService(random: new Random(0));
            var rejection = service.Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That(rejection.Accepted, Is.False);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            restored.CurrentDate = save.CurrentDate.AddDays(6);
            Assert.Throws<InvalidOperationException>(() => service.Submit(restored, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary));
            restored.CurrentDate = save.CurrentDate.AddDays(7);
            Assert.DoesNotThrow(() => new ContractNegotiationService(random: new Random(1)).Submit(restored, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary));
        }

        [Test]
        public void SameSavedInput_ProducesSameRollAndOutcome()
        {
            var save = CreateSave(); var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            var id = save.Wrestlers[0].Id; var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            var a = new ContractNegotiationService().Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            var b = new ContractNegotiationService().Submit(restored, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That(a.RollPercent, Is.EqualTo(b.RollPercent)); Assert.That(a.Accepted, Is.EqualTo(b.Accepted));
        }

        [Test]
        public void AcceptedSigning_ChargesOnceAndActivatesCandidateWithoutResettingSatisfaction()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id; var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            var service = new ContractNegotiationService(random: new Random(1));
            var result = service.Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            Assert.That(result.Accepted, Is.True);
            Assert.That(service.Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary), Is.SameAs(result));
            Assert.That(save.Contracts, Has.Count.EqualTo(1)); Assert.That(save.Transactions, Has.Count.EqualTo(2));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(100000 - demand.SigningBonus - demand.MonthlySalary));
            Assert.That(save.Wrestlers[0].Roster.ActivityState, Is.EqualTo(RosterActivityState.Active));
            Assert.That(save.Wrestlers[0].PromotionId, Is.EqualTo(save.Promotion.Id));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(50));
        }

        [Test]
        public void Renewal_IsScheduledAfterCurrentContractAndDuplicateBookingIsBlocked()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = id,
                Status = ContractStatus.Overdue, StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31) });
            var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            var result = new ContractNegotiationService(random: new Random(1)).Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary);
            var contract = save.Contracts.Single(x => x.Id == result.ContractId);
            Assert.That(contract.Status, Is.EqualTo(ContractStatus.Scheduled));
            Assert.That(contract.StartDate, Is.EqualTo(new GameDate(2027, 6, 1)));
            Assert.That(contract.EndDate, Is.EqualTo(new GameDate(2028, 5, 31)));
            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Overdue));
            save.CurrentDate = save.CurrentDate.AddDays(1);
            Assert.Throws<InvalidOperationException>(() => new ContractNegotiationService().Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary));
        }

        [Test]
        public void InsufficientCashAndInvalidIds_DoNotMutateAnyState()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id; var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ContractNegotiationService().Submit(save, id, ContractType.Wrestler, 100001, demand.MonthlySalary));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            var repeated = Guid.NewGuid().ToString("D");
            Assert.Throws<InvalidOperationException>(() => new ContractNegotiationService(() => repeated, new Random(1)).Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void MissingRequiredId_DoesNotPartiallyApplyContract(int nullAt)
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            var demand = ContractNegotiationService.Demand(save, id, ContractType.Wrestler);
            var before = JsonUtility.ToJson(save); var index = 0;
            var service = new ContractNegotiationService(() => index++ == nullAt ? null : Guid.NewGuid().ToString("D"), new Random(1));
            Assert.Throws<InvalidOperationException>(() => service.Submit(save, id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void ZeroBonus_DoesNotCreateZeroAmountTransactionAndManagersCanSign()
        {
            var save = CreateSave(); var manager = new ManagerState { Id = Guid.NewGuid().ToString("D"), Name = "매니저", Satisfaction = 50 };
            save.Managers.Add(manager);
            var demand = ContractNegotiationService.Demand(save, manager.Id, ContractType.Manager);
            var result = new ContractNegotiationService(random: new Random(1)).Submit(save, manager.Id, ContractType.Manager, 0, demand.MonthlySalary * 2);
            Assert.That(result.Accepted, Is.True); Assert.That(save.Transactions.Any(x => x.Type == TransactionType.SigningFee), Is.False);
            Assert.That(manager.ActivityState, Is.EqualTo(RosterActivityState.Active));
        }

        [Test]
        public void ZeroChanceAlwaysRejectsAndShowInProgressBlocksNegotiations()
        {
            var save = CreateSave(); var id = save.Wrestlers[0].Id;
            var result = new ContractNegotiationService(random: new Random(1)).Submit(save, id, ContractType.Wrestler, 0, 1);
            Assert.That(result.Accepted, Is.False); Assert.That(save.Contracts, Is.Empty);
            save.CurrentDate = save.CurrentDate.AddDays(7);
            save.Shows.Add(new ShowState { Status = ShowStatus.InProgress });
            Assert.Throws<InvalidOperationException>(() => new ContractNegotiationService().Submit(save, id, ContractType.Wrestler, 0, 1));
        }

        private static GameSave CreateSave()
        {
            var save = GameSave.CreateNew(1234, DateTime.UtcNow); save.CurrentDate = new GameDate(2026, 7, 1);
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString("D"), Name = "Test", InitialCash = 100000 };
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = Guid.NewGuid().ToString("D"), RingName = "테스트 선수" },
                Condition = new WrestlerConditionState { Satisfaction = 50 }, Roster = new WrestlerRosterState { ActivityState = RosterActivityState.Inactive } });
            return save;
        }
    }
}
