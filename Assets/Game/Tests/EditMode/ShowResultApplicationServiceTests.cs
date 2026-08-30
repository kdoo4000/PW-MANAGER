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
            Assert.That(restored.Transactions.Select(x => x.Amount), Does.Contain(1000).And.Contain(-400));
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

        private static Func<string> Ids()
        {
            var value = 0;
            return () => $"transaction-{++value}";
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
                Status = ShowStatus.Completed, TimelineEventIds = { "match_event", "promo_event" }
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
