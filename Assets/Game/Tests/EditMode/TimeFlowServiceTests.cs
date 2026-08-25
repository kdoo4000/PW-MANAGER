using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class TimeFlowServiceTests
    {
        [Test]
        public void AdvanceTo_ProcessesMonthlySalaryOnceAndMarksExpiring()
        {
            var save = CreateSave(1000, new GameDate(2027, 4, 30), 200, new GameDate(2027, 5, 31));
            var service = new TimeFlowService(() => Guid.NewGuid().ToString("D"));

            var result = service.AdvanceTo(save, new GameDate(2027, 5, 1));

            Assert.That(result.State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2027, 5, 1)));
            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Expiring));
            Assert.That(save.Transactions.Single(x => x.Type == TransactionType.Salary).Amount, Is.EqualTo(-200));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(800));
        }

        [Test]
        public void AdvanceTo_StopsBeforeSalaryDateWhenCashIsInsufficient()
        {
            var save = CreateSave(100, new GameDate(2026, 6, 30), 200, new GameDate(2027, 5, 31));

            var result = new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 1));

            Assert.That(result.State, Is.EqualTo(TimeFlowState.Blocked));
            Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2026, 6, 30)));
            Assert.That(save.Transactions, Is.Empty);
        }

        [Test]
        public void AdvanceTo_EndDateExpiresContractAfterFinalDayProcessing()
        {
            var save = CreateSave(1000, new GameDate(2027, 5, 30), 0, new GameDate(2027, 5, 31));

            new TimeFlowService().AdvanceTo(save, new GameDate(2027, 5, 31));

            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Expired));
            Assert.That(save.ProcessedIds.Single(), Does.StartWith("contract-expiry:"));
        }

        [Test]
        public void AdvanceTo_RejectsMoreThanThirtyDays()
        {
            var save = CreateSave(1000, new GameDate(2026, 6, 1), 0, new GameDate(2027, 5, 31));
            Assert.Throws<ArgumentException>(() => new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 2)));
        }

        private static GameSave CreateSave(long cash, GameDate currentDate, long salary, GameDate endDate)
        {
            var promotionId = Guid.NewGuid().ToString("D");
            return new GameSave
            {
                CurrentDate = currentDate,
                Promotion = new PromotionState { Id = promotionId, Name = "Test", InitialCash = cash },
                Contracts =
                {
                    new ContractState
                    {
                        Id = Guid.NewGuid().ToString("D"), PersonId = Guid.NewGuid().ToString("D"),
                        StartDate = new GameDate(2026, 6, 1), EndDate = endDate,
                        MonthlySalary = salary, Status = ContractStatus.Active
                    }
                }
            };
        }
    }
}
