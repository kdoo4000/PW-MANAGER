using System;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class ResultApplicationContextTests
    {
        [Test]
        public void Create_CompleteUnappliedResult_ResolvesOrderedEventResults()
        {
            var save = CreateSave();

            var context = ResultApplicationContext.Create(save, "show_result");

            Assert.That(context.Show.Id, Is.EqualTo("show"));
            Assert.That(context.MatchResults[0].Id, Is.EqualTo("match_result"));
            Assert.That(context.PromoResults[0].Id, Is.EqualTo("promo_result"));
            Assert.That(context.ApplicationKey, Is.EqualTo("show-result:show:2"));
        }

        [Test]
        public void Create_ProcessedApplicationKey_IsBlocked()
        {
            var save = CreateSave();
            save.ProcessedIds.Add("show-result:show:2");

            Assert.Throws<InvalidOperationException>(() => ResultApplicationContext.Create(save, "show_result"));
        }

        [Test]
        public void Create_ResultVersionMismatch_IsBlocked()
        {
            var save = CreateSave();
            save.ShowResults[0].ShowVersion = 1;

            Assert.Throws<InvalidOperationException>(() => ResultApplicationContext.Create(save, "show_result"));
        }

        [Test]
        public void Create_MissingTimelineResult_IsBlockedBeforeMutation()
        {
            var save = CreateSave();
            save.ShowResults[0].PromoResultIds.Clear();
            var processedBefore = save.ProcessedIds.Count;

            Assert.Throws<InvalidOperationException>(() => ResultApplicationContext.Create(save, "show_result"));
            Assert.That(save.ProcessedIds.Count, Is.EqualTo(processedBefore));
        }

        [Test]
        public void Create_InconsistentSettlement_IsBlocked()
        {
            var save = CreateSave();
            save.ShowResults[0].FinancialSettlement.NetIncome = 999;

            Assert.Throws<InvalidOperationException>(() => ResultApplicationContext.Create(save, "show_result"));
        }

        private static GameSave CreateSave()
        {
            var save = new GameSave();
            save.Shows.Add(new ShowState
            {
                Id = "show", ShowVersion = 2, Status = ShowStatus.InProgress,
                TimelineEventIds = { "match_event", "promo_event" }
            });
            save.ShowEvents.Add(new ShowEventState
            {
                Id = "match_event", ShowId = "show", EventType = ShowEventType.Match, DetailId = "match_plan"
            });
            save.ShowEvents.Add(new ShowEventState
            {
                Id = "promo_event", ShowId = "show", EventType = ShowEventType.Promo, DetailId = "promo_plan"
            });
            save.MatchResults.Add(new MatchResultState
            {
                Id = "match_result", ShowId = "show", ShowVersion = 2,
                ShowEventId = "match_event", MatchPlanId = "match_plan"
            });
            save.PromoResults.Add(new PromoResultState
            {
                Id = "promo_result", ShowId = "show", ShowVersion = 2,
                ShowEventId = "promo_event", PromoPlanId = "promo_plan"
            });
            save.ShowResults.Add(new ShowResultState
            {
                Id = "show_result", ShowId = "show", ShowVersion = 2,
                MatchResultIds = { "match_result" }, PromoResultIds = { "promo_result" },
                FinancialSettlement = new FinancialSettlementState { Revenue = 1000, Cost = 400, NetIncome = 600 }
            });
            return save;
        }
    }
}
