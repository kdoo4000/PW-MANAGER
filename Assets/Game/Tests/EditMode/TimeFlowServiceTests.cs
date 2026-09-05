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
        public void Inbox_RequiredShowMessageIsUniqueAndResolvesWhenConfirmed()
        {
            var save = CreateSave(1000, new GameDate(2026, 6, 1), 0, new GameDate(2027, 5, 31));
            save.Schedules.Add(new ScheduleState { Id = "schedule", Date = save.CurrentDate.AddDays(2), BookingDeadline = save.CurrentDate, Status = ScheduleStatus.Confirmed });
            save.Shows.Add(new ShowState { Id = "show", ScheduleId = "schedule", Name = "Test Show", Status = ShowStatus.Preparing });
            var service = new InboxService(() => Guid.NewGuid().ToString("D"));

            service.EnsureForCurrentDate(save);
            service.EnsureForCurrentDate(save);

            Assert.That(save.InboxMessages.Count(x => x.Priority == InboxPriority.Required), Is.EqualTo(1));
            Assert.That(service.GetBlockingMessage(save), Is.Not.Null);
            save.Shows[0].Status = ShowStatus.Confirmed;
            service.EnsureForCurrentDate(save);
            Assert.That(service.GetBlockingMessage(save), Is.Null);
        }

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

        [Test]
        public void AdvanceTo_TodaysUnconfirmedBookingDeadline_IsBlockedBeforeDateChanges()
        {
            var save = CreateSave(1000, new GameDate(2026, 6, 1), 0, new GameDate(2027, 5, 31));
            save.Schedules.Add(new ScheduleState
            {
                Id = "schedule", Date = new GameDate(2026, 6, 4), BookingDeadline = save.CurrentDate,
                Status = ScheduleStatus.Confirmed
            });
            save.Shows.Add(new ShowState { Id = "show", ScheduleId = "schedule", Status = ShowStatus.Preparing });

            var result = new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));

            Assert.That(result.State, Is.EqualTo(TimeFlowState.Blocked));
            Assert.That(result.BlockingReason, Is.EqualTo("오늘 마감되는 쇼의 편성을 확정해야 합니다."));
            Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2026, 6, 1)));
        }

        [Test]
        public void AdvanceTo_StopsAtIntermediateBookingDeadlineAndResumesAfterConfirmation()
        {
            var save = CreateSave(1000, new GameDate(2026, 6, 1), 0, new GameDate(2027, 5, 31));
            save.Schedules.Add(new ScheduleState
            {
                Id = "schedule", Date = new GameDate(2026, 6, 4), BookingDeadline = new GameDate(2026, 6, 2),
                Status = ScheduleStatus.Confirmed
            });
            save.Shows.Add(new ShowState { Id = "show", ScheduleId = "schedule", Status = ShowStatus.Preparing });
            var service = new TimeFlowService();

            var result = service.AdvanceTo(save, new GameDate(2026, 6, 3));

            Assert.That(result.State, Is.EqualTo(TimeFlowState.Blocked));
            Assert.That(result.ReachedDate, Is.EqualTo(new GameDate(2026, 6, 2)));
            Assert.That(save.CurrentDate, Is.EqualTo(result.ReachedDate));
            Assert.That(result.ProcessedDays, Is.EqualTo(1));
            save.Shows[0].Status = ShowStatus.Confirmed;
            Assert.That(service.AdvanceTo(save, new GameDate(2026, 6, 3)).State, Is.EqualTo(TimeFlowState.Completed));
        }

        [Test]
        public void AdvanceTo_StopsWhenRequiredMessageBecomesDueMidway()
        {
            var save = CreateSave(1000, new GameDate(2026, 6, 1), 0, new GameDate(2027, 5, 31));
            new InboxService().Publish(save, "decision", InboxMessageType.Decision, InboxPriority.Required,
                "운영실", "결정 필요", "", dueDate: new GameDate(2026, 6, 2));

            var result = new TimeFlowService().AdvanceTo(save, new GameDate(2026, 6, 3));

            Assert.That(result.State, Is.EqualTo(TimeFlowState.Blocked));
            Assert.That(result.BlockingReason, Is.EqualTo("결정 필요"));
            Assert.That(result.ReachedDate, Is.EqualTo(new GameDate(2026, 6, 2)));
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
