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
        public void DaysWithoutShows_DoNotExpireStoryOrReduceInterest()
        {
            var save = CreateSave(1000, new GameDate(2026, 6, 1), 0, new GameDate(2027, 5, 31));
            var second = new WrestlerState { Id = Guid.NewGuid().ToString("D"), PromotionId = save.Promotion.Id };
            save.Wrestlers.Add(second);
            var story = new StorylineService().Create(save, "기다리는 이야기", save.Wrestlers[0].Id, second.Id);
            story.IsAutomatic = true; story.LastEventDate = save.CurrentDate;
            story.Interest = 30; story.Tension = 40; story.Fatigue = 7;
            var service = new TimeFlowService();
            Assert.That(service.AdvanceTo(save, new GameDate(2026, 6, 30)).State, Is.EqualTo(TimeFlowState.Completed));
            var flow = service.AdvanceTo(save, new GameDate(2026, 7, 20));
            Assert.That(flow.State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Active));
            Assert.That(story.Interest, Is.EqualTo(30));
            Assert.That(story.Tension, Is.EqualTo(40));
            Assert.That(story.Fatigue, Is.EqualTo(7));
            Assert.That(StorylineHistory.FindActive(save, new[] { save.Wrestlers[0].Id, second.Id }), Is.SameAs(story));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RenewalActivation_PreservesSuspensionAndStillPaysContractedSalary(bool manager)
        {
            var save = CreateSave(1000, new GameDate(2027, 5, 31), 0, new GameDate(2027, 5, 31));
            var id = save.Wrestlers[0].Id; var type = manager ? ContractType.Manager : ContractType.Wrestler;
            if (manager)
            {
                var person = new ManagerState { Id = Guid.NewGuid().ToString("D"), PromotionId = save.Promotion.Id, ActivityState = RosterActivityState.Suspended };
                save.Managers.Add(person); id = person.Id; save.Contracts[0].PersonId = id; save.Contracts[0].Type = type;
            }
            else save.Wrestlers[0].Roster.ActivityState = RosterActivityState.Suspended;
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = id, Type = type,
                StartDate = new GameDate(2027, 6, 1), EndDate = new GameDate(2028, 5, 31), MonthlySalary = 100, Status = ContractStatus.Scheduled });
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(manager ? save.Managers[0].ActivityState : save.Wrestlers[0].Roster.ActivityState, Is.EqualTo(RosterActivityState.Suspended));
            Assert.That(save.Contracts.Last().Status, Is.EqualTo(ContractStatus.Active)); Assert.That(save.Transactions.Single().Amount, Is.EqualTo(-100));
        }

        [Test]
        public void FutureCandidate_ActivatesRosterAndClearsCandidateBeforePayingSalary()
        {
            var save = CreateSave(1000, new GameDate(2027, 5, 31), 100, new GameDate(2028, 5, 31));
            var wrestler = save.Wrestlers[0]; wrestler.PromotionId = null; wrestler.Roster.ActivityState = RosterActivityState.Inactive;
            wrestler.Identity.ExpiryDate = new OptionalGameDate(new GameDate(2027, 6, 2));
            save.ScoutCandidates.Add(new ScoutCandidateState { WrestlerId = wrestler.Id });
            save.Contracts[0].StartDate = new GameDate(2027, 6, 1); save.Contracts[0].Status = ContractStatus.Scheduled;
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(wrestler.Roster.ActivityState, Is.EqualTo(RosterActivityState.Active));
            Assert.That(wrestler.PromotionId, Is.EqualTo(save.Promotion.Id)); Assert.That(wrestler.Identity.ExpiryDate.HasValue, Is.False);
            Assert.That(save.ScoutCandidates, Is.Empty); Assert.That(save.Transactions.Single().Amount, Is.EqualTo(-100));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Expiry_CleansDepartureOnlyWhenNoRenewalContinuesEmployment(bool renewed)
        {
            var save = CreateSave(1000, new GameDate(2027, 5, 31), 0, new GameDate(2027, 5, 31));
            var wrestler = save.Wrestlers[0];
            var title = ChampionshipService.Create(save, "Title", ChampionshipType.Individual, holderId: wrestler.Id);
            if (renewed) save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = wrestler.Id,
                StartDate = new GameDate(2027, 6, 1), EndDate = new GameDate(2028, 5, 31), MonthlySalary = 100, Status = ContractStatus.Scheduled });
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(wrestler.Roster.ActivityState, Is.EqualTo(renewed ? RosterActivityState.Active : RosterActivityState.Inactive));
            Assert.That(title.HolderId, renewed ? Is.EqualTo(wrestler.Id) : Is.Null);
            Assert.That(save.ContractTerminations, Is.Empty);
        }

        [Test]
        public void AdvanceTo_ScheduledRenewalActivatesAndPaysOnlyOnStartDate()
        {
            var save = CreateSave(1000, new GameDate(2027, 5, 31), 0, new GameDate(2027, 5, 31));
            var renewal = new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = save.Contracts[0].PersonId,
                Status = ContractStatus.Scheduled, StartDate = new GameDate(2027, 6, 1), EndDate = new GameDate(2028, 5, 31), MonthlySalary = 100 };
            save.Contracts.Add(renewal);
            Assert.That(save.Transactions, Is.Empty);
            new TimeFlowService().AdvanceTo(save, renewal.StartDate);
            Assert.That(renewal.Status, Is.EqualTo(ContractStatus.Active));
            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Expired));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(900));
        }

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
        public void AdvanceTo_ContinuesPastSalaryDateWhenCashIsInsufficient()
        {
            var save = CreateSave(100, new GameDate(2026, 6, 30), 200, new GameDate(2027, 5, 31));

            var result = new TimeFlowService().AdvanceTo(save, new GameDate(2026, 7, 1));

            Assert.That(result.State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(save.CurrentDate, Is.EqualTo(new GameDate(2026, 7, 1)));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.Zero);
            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Overdue));
        }

        [Test]
        public void AdvanceTo_ContractRemainsValidThroughSeasonEndAndExpiresNextDay()
        {
            var save = CreateSave(1000, new GameDate(2027, 5, 30), 0, new GameDate(2027, 5, 31));

            new TimeFlowService().AdvanceTo(save, new GameDate(2027, 5, 31));

            Assert.That(save.Contracts[0].Status, Is.EqualTo(ContractStatus.Expiring));
            Assert.That(save.ProcessedIds, Is.Empty);

            new TimeFlowService().AdvanceTo(save, new GameDate(2027, 6, 1));

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
            var personId = Guid.NewGuid().ToString("D");
            return new GameSave
            {
                CurrentDate = currentDate,
                Promotion = new PromotionState { Id = promotionId, Name = "Test", InitialCash = cash },
                Wrestlers = { new WrestlerState { Id = personId, PromotionId = promotionId } },
                Contracts =
                {
                    new ContractState
                    {
                        Id = Guid.NewGuid().ToString("D"), PersonId = personId,
                        StartDate = new GameDate(2026, 6, 1), EndDate = endDate,
                        MonthlySalary = salary, Status = ContractStatus.Active
                    }
                }
            };
        }
    }
}
