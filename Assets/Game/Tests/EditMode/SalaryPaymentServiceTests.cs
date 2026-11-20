using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class SalaryPaymentServiceTests
    {
        [Test]
        public void MidmonthStart_ProrationIsChargedOnceAndDoesNotEraseFutureGuarantee()
        {
            var save = CreateSave(10000);
            save.CurrentDate = new GameDate(2026, 7, 17);
            save.Contracts[0].StartDate = save.CurrentDate;
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            Assert.That(save.SalaryPayments, Has.Count.EqualTo(1));
            Assert.That(save.SalaryPayments[0].Amount, Is.EqualTo(97));
        }

        [Test]
        public void Payday_PartiallyPaysAndRepeatedProcessingDoesNotRepeatDebtOrPenalty()
        {
            var save = CreateSave(100);
            var service = new SalaryPaymentService();
            service.ProcessDay(save, save.CurrentDate);
            service.ProcessDay(save, save.CurrentDate);
            Assert.That(save.SalaryPayments, Has.Count.EqualTo(1));
            Assert.That(save.SalaryPayments[0].RemainingAmount, Is.EqualTo(100));
            Assert.That(save.Transactions.Single().Amount, Is.EqualTo(-100));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.Zero);
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(60));
            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Overdue));
            Assert.That(save.InboxMessages.Count(x => x.SourceEventId.StartsWith("salary-overdue:")), Is.EqualTo(1));
        }

        [Test]
        public void WeeklyPenalty_UsesSevenDayBoundariesAndPersistsAcrossReload()
        {
            var save = CreateSave(0);
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            save = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 7));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(60));
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 8));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(55));
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(55));
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 15));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(50));
        }

        [Test]
        public void MultipleMissedMonths_DoNotStackWeeklyPenaltyForEachDebt()
        {
            var save = CreateSave(0);
            var service = new SalaryPaymentService();
            service.ProcessDay(save, save.CurrentDate);
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 31));
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 8, 1));
            Assert.That(save.SalaryPayments, Has.Count.EqualTo(2));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(30));
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 8, 5));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(25));
        }

        [Test]
        public void Repayment_PaysOldestFirstAndDoesNotRestoreSatisfaction()
        {
            var save = CreateSave(0);
            var service = new SalaryPaymentService();
            service.ProcessDay(save, save.CurrentDate);
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 31));
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 8, 1));
            Credit(save, 250);
            service.SettleOutstanding(save, save.CurrentDate);
            Assert.That(save.SalaryPayments[0].RemainingAmount, Is.Zero);
            Assert.That(save.SalaryPayments[1].RemainingAmount, Is.EqualTo(150));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(30));
            Credit(save, 300);
            service.SettleOutstanding(save, save.CurrentDate);
            var count = save.Transactions.Count;
            service.SettleOutstanding(save, save.CurrentDate);
            Assert.That(save.Transactions, Has.Count.EqualTo(count));
            Assert.That(SalaryPaymentService.Outstanding(save), Is.Zero);
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(150));
            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Active));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(30));
            Assert.That(save.Contracts[0].LastSalaryPenaltyDate.HasValue, Is.False);
        }

        [Test]
        public void FullyPaidSalary_DoesNotReduceSatisfactionOrWarn()
        {
            var save = CreateSave(200);
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            Assert.That(SalaryPaymentService.Outstanding(save), Is.Zero);
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(70));
            Assert.That(save.InboxMessages, Is.Empty);
        }

        [TestCase(ContractStatus.Expired)]
        [TestCase(ContractStatus.Terminated)]
        public void EndedContract_DebtSurvivesAndRepaymentDoesNotReactivate(ContractStatus status)
        {
            var save = CreateSave(0);
            var service = new SalaryPaymentService();
            service.ProcessDay(save, save.CurrentDate);
            save.Contracts[0].Status = status;
            Credit(save, 250);
            service.SettleOutstanding(save, save.CurrentDate);
            Assert.That(save.Contracts[0].Status, Is.EqualTo(status));
            Assert.That(SalaryPaymentService.Outstanding(save), Is.Zero);
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(50));
        }

        [Test]
        public void ManagerSalary_ReducesManagerSatisfactionAndClampsAtZero()
        {
            var save = CreateSave(0);
            save.Contracts[0].Type = ContractType.Manager;
            save.Managers.Add(new ManagerState { Id = save.Contracts[0].PersonId, Satisfaction = 12 });
            var service = new SalaryPaymentService();
            service.ProcessDay(save, save.CurrentDate);
            Assert.That(save.Managers[0].Satisfaction, Is.EqualTo(2));
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 8));
            Assert.That(save.Managers[0].Satisfaction, Is.Zero);
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(70));
        }

        [Test]
        public void SameDueDate_UsesStableContractOrderRegardlessOfListOrder()
        {
            var save = CreateSave(150);
            var first = save.Contracts[0];
            first.Id = "00000000-0000-0000-0000-000000000001";
            save.Contracts.Insert(0, new ContractState { Id = "00000000-0000-0000-0000-000000000002",
                PersonId = first.PersonId, StartDate = first.StartDate, EndDate = first.EndDate,
                MonthlySalary = 200, Status = ContractStatus.Active });
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            Assert.That(save.SalaryPayments.Single(x => x.ContractId == first.Id).RemainingAmount, Is.EqualTo(50));
            Assert.That(save.SalaryPayments.Single(x => x.ContractId != first.Id).RemainingAmount, Is.EqualTo(200));
        }

        [Test]
        public void LegacyProcessedSalary_IsNotChargedAgain()
        {
            var save = CreateSave(500);
            save.ProcessedIds.Add($"salary:{save.Contracts[0].Id}:20260701");
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            Assert.That(save.SalaryPayments, Is.Empty);
            Assert.That(save.Transactions, Is.Empty);
        }

        private static GameSave CreateSave(long cash)
        {
            var save = new GameSave { CurrentDate = new GameDate(2026, 7, 1),
                Promotion = new PromotionState { Id = Guid.NewGuid().ToString("D"), Name = "Test", InitialCash = cash } };
            var personId = Guid.NewGuid().ToString("D");
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = personId, RingName = "테스트 선수" },
                Condition = new WrestlerConditionState { Satisfaction = 70 } });
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = personId,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31),
                MonthlySalary = 200, Status = ContractStatus.Active });
            return save;
        }

        private static void Credit(GameSave save, long amount) => save.Transactions.Add(new TransactionRecord {
            Id = Guid.NewGuid().ToString("D"), PromotionId = save.Promotion.Id, Date = save.CurrentDate,
            Type = TransactionType.TicketRevenue, Amount = amount });
    }
}
