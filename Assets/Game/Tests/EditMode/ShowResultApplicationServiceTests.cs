using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class ShowResultApplicationServiceTests
    {
        [TestCase(25f, 20f)]
        [TestCase(3f, 0f)]
        [TestCase(0f, 0f)]
        public void Apply_UnprogressedStoryLosesFiveInterestOnceAndStaysActive(float initial, float expected)
        {
            var save = CreateSave();
            save.Wrestlers.Add(new WrestlerState { Id = "c" });
            var story = new StorylineService().Create(save, "미전개", "a", "c");
            story.IsAutomatic = true; story.Interest = initial;
            story.LastEventDate = new GameDate(2026, 6, 1);
            var service = new ShowResultApplicationService(Ids());
            service.Apply(save, "show_result");
            Assert.That(story.Interest, Is.EqualTo(expected));
            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Active));
            Assert.That(story.LastEventDate, Is.EqualTo(new GameDate(2026, 6, 1)));
            Assert.That(StorylineHistory.ForStory(save, story), Is.Empty);
            Assert.That(save.StoryRelationships, Is.Empty);
            var changes = save.ShowResults[0].StoryChanges.Where(x => x.TargetId == story.Id).ToList();
            Assert.That(changes.Sum(x => x.Amount), Is.EqualTo(expected - initial));
            Assert.Throws<InvalidOperationException>(() => service.Apply(save, "show_result"));
            Assert.That(story.Interest, Is.EqualTo(expected));
        }

        [Test]
        public void Apply_ProgressedCompletedInactiveAndOtherPromotionsStoriesAreNotPenalized()
        {
            var save = CreateSave();
            var service = new StorylineService();
            var progressed = service.Create(save, "진행", "a", "b");
            var completed = service.Create(save, "완료", "a", "b"); completed.Status = StorylineStatus.Completed;
            var inactive = service.Create(save, "비활성", "a", "b"); inactive.Status = StorylineStatus.Inactive;
            var foreign = service.Create(save, "다른 단체", "a", "b"); foreign.PromotionId = "other";
            save.ShowResults[0].StorylineChanges.Add(new StorylineResultChangeState { StorylineId = progressed.Id,
                SourceResultId = "promo_result", Action = StoryAction.Progress, EventType = StoryEventType.Promo,
                Incident = StoryIncident.Challenge, ActorId = "a", TargetId = "b", InterestChange = 2 });
            new ShowResultApplicationService(Ids()).Apply(save, "show_result");
            Assert.That(progressed.Interest, Is.EqualTo(27));
            Assert.That(new[] { completed.Interest, inactive.Interest, foreign.Interest }, Is.All.EqualTo(25));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Apply_UnreviewedOrCancelledResultDoesNotReduceStoryInterest(bool cancelled)
        {
            var save = CreateSave();
            var story = new StorylineService().Create(save, "검토 중", "a", "b");
            if (cancelled) save.Schedules[0].Status = ScheduleStatus.Cancelled;
            else save.Shows[0].Status = ShowStatus.ResultReview;
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService(Ids()).Apply(save, "show_result"));
            Assert.That(story.Interest, Is.EqualTo(25));
            Assert.That(save.ShowResults[0].StoryChanges, Is.Empty);
        }

        [TestCase(false, 200, SalaryPaymentKind.MatchAppearance)]
        [TestCase(false, 400, SalaryPaymentKind.PromoAppearance)]
        [TestCase(true, 100, SalaryPaymentKind.PromoAppearance)]
        public void AppearanceFees_PayHighestActualAppearanceOnceAndPreserveAfterReload(bool cancelledMatch, long promoFee, SalaryPaymentKind expected)
        {
            var save = CreateSave();
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = "a", Type = ContractType.Wrestler,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active,
                MatchAppearanceFee = 300, PromoAppearanceFee = promoFee });
            save.MatchPlans[0].ParticipantIds.AddRange(new[] { "a", "b" });
            Assert.That(SalaryPaymentService.EstimateAppearanceFees(save, save.Shows[0]), Is.EqualTo(Math.Max(300, promoFee)));
            if (cancelledMatch) save.MatchResults[0].CancellationReason = "취소";
            new ShowResultApplicationService().Apply(save, "show_result");
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(restored.SalaryPayments, Has.Count.EqualTo(1));
            Assert.That(restored.SalaryPayments[0].Kind, Is.EqualTo(expected));
            Assert.That(restored.SalaryPayments[0].Amount, Is.EqualTo(cancelledMatch ? promoFee : Math.Max(300, promoFee)));
            Assert.That(restored.SalaryPayments[0].RemainingAmount, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(restored, "show_result"));
        }

        [Test]
        public void AppearanceFees_UnpaidFeeSurvivesReloadAndRepaysAsSalaryDebt()
        {
            var save = CreateSave();
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = "a", Type = ContractType.Wrestler,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active,
                MatchAppearanceFee = 1000 });
            new ShowResultApplicationService().Apply(save, "show_result");
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(SalaryPaymentService.Outstanding(restored), Is.EqualTo(400));
            new SalaryPaymentService().ProcessDay(restored, new GameDate(2026, 6, 8));
            Assert.That(restored.SalaryPayments[0].WasOverdue, Is.True);
            restored.Promotion.InitialCash += 400;
            new SalaryPaymentService().SettleOutstanding(restored, new GameDate(2026, 6, 8));
            Assert.That(SalaryPaymentService.Outstanding(restored), Is.Zero);
        }

        [Test]
        public void Apply_CompleteResult_UpdatesWrestlersFinanceScheduleAndRoundTrips()
        {
            var save = CreateSave();

            new ShowResultApplicationService(Ids()).Apply(save, "show_result");
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));

            Assert.That(restored.Wrestlers.Single(x => x.Id == "a").Condition.Condition, Is.EqualTo(87f));
            Assert.That(restored.Wrestlers.Single(x => x.Id == "a").Status.OfficialMatchCount, Is.EqualTo(1));
            Assert.That(restored.Wrestlers.Single(x => x.Id == "b").Roster.LastMatchDate.Value, Is.EqualTo(new GameDate(2026, 6, 7)));
            Assert.That(restored.Wrestlers.Single(x => x.Id == "a").Roster.LastAppearanceDate.HasValue, Is.True);
            Assert.That(restored.Schedules.Single().Status, Is.EqualTo(ScheduleStatus.Completed));
            Assert.That(restored.Transactions.Select(x => x.Amount), Does.Contain(1000L));
            Assert.That(restored.Transactions.Select(x => x.Amount), Does.Contain(-400L));
            Assert.That(restored.Promotion.CalculateCurrentCash(restored.Transactions), Is.EqualTo(600));
            Assert.That(restored.ProcessedIds, Does.Contain("show-result:show:1"));
        }

        [Test]
        public void Apply_SameResultTwice_IsRejectedWithoutFurtherChanges()
        {
            var save = CreateSave();
            var service = new ShowResultApplicationService(Ids());
            service.Apply(save, "show_result");
            var cash = save.Promotion.CalculateCurrentCash(save.Transactions);

            Assert.Throws<InvalidOperationException>(() => service.Apply(save, "show_result"));
            Assert.That(save.Transactions, Has.Count.EqualTo(2));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(cash));
            Assert.That(save.Wrestlers.Single(x => x.Id == "a").Condition.Condition, Is.EqualTo(87f));
        }

        [Test]
        public void Apply_InvalidConditionChange_IsRejectedWithoutPartialMutation()
        {
            var save = CreateSave();
            save.MatchResults[0].WrestlerConditionChanges[1].WrestlerId = "missing";

            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService(Ids()).Apply(save, "show_result"));
            Assert.That(save.Wrestlers.Single(x => x.Id == "a").Condition.Condition, Is.EqualTo(100f));
            Assert.That(save.Transactions, Is.Empty);
            Assert.That(save.Schedules.Single().Status, Is.EqualTo(ScheduleStatus.Confirmed));
            Assert.That(save.ProcessedIds, Is.Empty);
        }

        [Test]
        public void Apply_MissingResult_IsRejectedWithoutMutation()
        {
            var save = CreateSave();

            Assert.Throws<ArgumentException>(() => new ShowResultApplicationService(Ids()).Apply(save, "missing_result"));
            Assert.That(save.Wrestlers.Single(x => x.Id == "a").Condition.Condition, Is.EqualTo(100f));
            Assert.That(save.Transactions, Is.Empty);
            Assert.That(save.Schedules.Single().Status, Is.EqualTo(ScheduleStatus.Confirmed));
        }

        [Test]
        public void Apply_TwoMatches_CountsEachMatchEvenWithoutConditionChanges()
        {
            var save = CreateSave();
            save.Shows[0].TimelineEventIds.Add("second_event");
            save.ShowEvents.Add(new ShowEventState { Id = "second_event", ShowId = "show", EventType = ShowEventType.Match, DetailId = "second_plan" });
            save.MatchPlans.Add(new MatchPlanState { Id = "second_plan" });
            save.MatchResults.Add(new MatchResultState
            {
                Id = "second_result", ShowId = "show", ShowVersion = 1, ShowEventId = "second_event", MatchPlanId = "second_plan",
                ParticipantProfiles = { new MatchParticipantProfileState { MemberIds = { "a", "b" } } }
            });
            save.ShowResults[0].MatchResultIds.Add("second_result");

            new ShowResultApplicationService(Ids()).Apply(save, "show_result");

            Assert.That(save.Wrestlers.Select(x => x.Status.OfficialMatchCount), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(save.Wrestlers[0].Condition.Condition, Is.EqualTo(87f));
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService(Ids()).Apply(save, "show_result"));
            Assert.That(save.Wrestlers[0].Status.OfficialMatchCount, Is.EqualTo(2));
        }

        private static Func<string> Ids()
        {
            var value = 0;
            return () => $"00000000-0000-0000-0000-{++value:D12}";
        }

        [Test]
        public void Apply_ShowNetIncomeRepaysArrearsAfterVenueCostAndOnlyOnce()
        {
            var save = CreateSave();
            save.CurrentDate = new GameDate(2026, 6, 1);
            save.Wrestlers[0].Condition.Satisfaction = 70;
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = "a",
                StartDate = save.CurrentDate, EndDate = new GameDate(2027, 5, 31), MonthlySalary = 800,
                Status = ContractStatus.Active });
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            Assert.That(SalaryPaymentService.Outstanding(save), Is.EqualTo(800));

            var service = new ShowResultApplicationService(Ids());
            service.Apply(save, "show_result");

            Assert.That(SalaryPaymentService.Outstanding(save), Is.EqualTo(200));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.Zero);
            Assert.That(save.Transactions.Single(x => x.Type == TransactionType.LatePayment).Amount, Is.EqualTo(-600));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(60));
            Assert.Throws<InvalidOperationException>(() => service.Apply(save, "show_result"));
            Assert.That(SalaryPaymentService.Outstanding(save), Is.EqualTo(200));
        }

        [Test]
        public void Apply_InvalidSalaryTransactionIdDoesNotPartiallyApplyShow()
        {
            var save = CreateSave();
            save.CurrentDate = new GameDate(2026, 6, 1);
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = "a",
                StartDate = save.CurrentDate, EndDate = new GameDate(2027, 5, 31), MonthlySalary = 800,
                Status = ContractStatus.Active });
            new SalaryPaymentService().ProcessDay(save, save.CurrentDate);
            var before = JsonUtility.ToJson(save);
            var ids = new[] { "revenue", "venue", "revenue" };
            var index = 0;
            Assert.Throws<InvalidOperationException>(() =>
                new ShowResultApplicationService(() => ids[index++]).Apply(save, "show_result"));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            new ShowResultApplicationService().Apply(save, "show_result");
            Assert.That(SalaryPaymentService.Outstanding(save), Is.EqualTo(200));
            Assert.That(save.Wrestlers[0].Status.OfficialMatchCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_TitleChange_IsPersistedAndCannotBeAppliedTwice()
        {
            var save = CreateSave();
            save.CurrentDate = new GameDate(2026, 6, 1);
            foreach (var wrestler in save.Wrestlers) wrestler.PromotionId = save.Promotion.Id;
            var title = ChampionshipService.Create(save, "월드", ChampionshipType.Individual, true, "a");
            save.MatchPlans[0].ChampionshipId = title.Id;
            var result = save.MatchResults[0];
            result.ParticipantProfiles.Clear();
            result.ParticipantProfiles.Add(new MatchParticipantProfileState { SideId = "a-side", MemberIds = { "a" } });
            result.ParticipantProfiles.Add(new MatchParticipantProfileState { SideId = "b-side", MemberIds = { "b" } });
            result.WinningSideId = "b-side";
            result.FinishType = MatchFinishType.Pinfall;
            var service = new ShowResultApplicationService(Ids());
            service.Apply(save, "show_result");
            Assert.That(save.Championships[0].HolderId, Is.EqualTo("b"));
            Assert.That(save.Championships[0].MatchHistory, Has.Count.EqualTo(1));
            var prestige = save.Championships[0].Prestige;
            Assert.That(save.Championships[0].Reigns[1].WonDate, Is.EqualTo(save.Shows[0].Date));
            Assert.Throws<InvalidOperationException>(() => service.Apply(save, "show_result"));
            Assert.That(save.Championships[0].Reigns, Has.Count.EqualTo(2));
            Assert.That(save.Championships[0].MatchHistory, Has.Count.EqualTo(1));
            Assert.That(save.Championships[0].Prestige, Is.EqualTo(prestige));
        }

        [Test]
        public void Apply_InvalidTitleWinner_RejectsBeforeAnyMutation()
        {
            var save = CreateSave();
            save.CurrentDate = new GameDate(2026, 6, 1);
            foreach (var wrestler in save.Wrestlers) wrestler.PromotionId = save.Promotion.Id;
            var title = ChampionshipService.Create(save, "월드", ChampionshipType.Individual, true, "a");
            save.MatchPlans[0].ChampionshipId = title.Id;
            var result = save.MatchResults[0];
            result.ParticipantProfiles.Clear();
            result.ParticipantProfiles.Add(new MatchParticipantProfileState { SideId = "a-side", MemberIds = { "a" } });
            result.ParticipantProfiles.Add(new MatchParticipantProfileState { SideId = "b-side", MemberIds = { "b" } });
            result.WinningSideId = "missing";
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService(Ids()).Apply(save, "show_result"));
            Assert.That(save.Championships[0].HolderId, Is.EqualTo("a"));
            Assert.That(save.Transactions, Is.Empty);
            Assert.That(save.Wrestlers[0].Condition.Condition, Is.EqualTo(100));
        }

        private static GameSave CreateSave()
        {
            var save = new GameSave
            {
                Promotion = new PromotionState { Id = "promotion", InitialCash = 0 }
            };
            save.Wrestlers.Add(new WrestlerState { Id = "a" });
            save.Wrestlers.Add(new WrestlerState { Id = "b" });
            save.Schedules.Add(new ScheduleState { Id = "schedule", Status = ScheduleStatus.Confirmed });
            save.Shows.Add(new ShowState
            {
                Id = "show", ScheduleId = "schedule", Date = new GameDate(2026, 6, 7), ShowVersion = 1,
                Status = ShowStatus.ResultsReviewed, TimelineEventIds = { "match_event", "promo_event" }
            });
            save.ShowEvents.Add(new ShowEventState { Id = "match_event", ShowId = "show", EventType = ShowEventType.Match, DetailId = "match_plan" });
            save.ShowEvents.Add(new ShowEventState { Id = "promo_event", ShowId = "show", EventType = ShowEventType.Promo, DetailId = "promo_plan" });
            save.MatchPlans.Add(new MatchPlanState { Id = "match_plan" });
            save.PromoPlans.Add(new PromoPlanState { Id = "promo_plan", ParticipantIds = { "a" } });
            save.MatchResults.Add(new MatchResultState
            {
                Id = "match_result", ShowId = "show", ShowVersion = 1, ShowEventId = "match_event", MatchPlanId = "match_plan",
                ParticipantProfiles = { new MatchParticipantProfileState { MemberIds = { "a", "b" } } },
                WrestlerConditionChanges =
                {
                    new WrestlerConditionChangeData { SourceResultId = "match_result", WrestlerId = "a", ConditionDelta = -13f },
                    new WrestlerConditionChangeData { SourceResultId = "match_result", WrestlerId = "b", ConditionDelta = -13f }
                }
            });
            save.PromoResults.Add(new PromoResultState { Id = "promo_result", ShowId = "show", ShowVersion = 1, ShowEventId = "promo_event", PromoPlanId = "promo_plan" });
            save.ShowResults.Add(new ShowResultState
            {
                Id = "show_result", ShowId = "show", ShowVersion = 1,
                MatchResultIds = { "match_result" }, PromoResultIds = { "promo_result" },
                FinancialSettlement = new FinancialSettlementState { Revenue = 1000, Cost = 400, NetIncome = 600 }
            });
            return save;
        }
    }
}
