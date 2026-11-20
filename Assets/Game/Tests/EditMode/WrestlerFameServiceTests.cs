using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class WrestlerFameServiceTests
    {
        private static ResultApplicationContext Setup(GameSave save, long reach = 10000)
        {
            var ctx = WrestlerCareerServiceTests.AddMatch(save);
            var snapshot = typeof(ShowResultState).GetField("HasFameSnapshot");
            Assert.That(snapshot, Is.Not.Null, "New show results must snapshot fame reach."); snapshot.SetValue(ctx.ShowResult, true);
            typeof(ShowResultState).GetField("FameReach").SetValue(ctx.ShowResult, reach);
            save.MatchResults[0].ActualMatchDuration = 10;
            return ctx;
        }

        [Test]
        public void Show_AwardsBothSidesOnceAndPreservesOriginalUntilApply()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save); var before = JsonUtility.ToJson(save);
            var plan = new WrestlerCareerService().PrepareShow(ctx);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before)); plan.Apply(); plan.Apply();
            Assert.That(save.Wrestlers.Select(x => x.Fame.Value), Is.EqualTo(new long[] { 20, 20 }));
            Assert.That(save.CareerEvents.Count(x => x.Kind.ToString() == "Fame"), Is.EqualTo(2));
        }

        [Test]
        public void MultipleAppearances_UseHighestContributionInsteadOfAdding()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save);
            var second = JsonUtility.FromJson<MatchResultState>(JsonUtility.ToJson(save.MatchResults[0]));
            second.Id = "second"; second.ShowEventId = "event2"; second.CriticReview.FinalScore = 90;
            save.MatchResults.Add(second); save.ShowEvents.Add(new() { Id = "event2", ShowId = "show", EventType = ShowEventType.Match, DetailId = "plan" });
            save.Shows[0].TimelineEventIds.Add("event2"); ctx.ShowResult.MatchResultIds.Add(second.Id);
            new WrestlerCareerService().PrepareShow(ResultApplicationContext.Create(save, "result")).Apply();
            Assert.That(save.Wrestlers[0].Fame.Value, Is.EqualTo(30));
        }

        [Test]
        public void ShortPromo_OnlyAwardsActualParticipantsAndScalesByMinutes()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save);
            save.MatchResults[0].CancellationReason = "cancelled";
            save.PromoPlans.Add(new() { Id = "promo-plan", ParticipantIds = { save.Wrestlers[0].Id }, TargetIds = { save.Wrestlers[1].Id } });
            save.PromoResults.Add(new() { Id = "promo", ShowId = "show", ShowVersion = 1, ShowEventId = "promo-event", PromoPlanId = "promo-plan", PromoScore = 90 });
            save.ShowEvents.Add(new() { Id = "promo-event", ShowId = "show", EventType = ShowEventType.Promo, DetailId = "promo-plan", PlannedDuration = 1 });
            ctx.Show.TimelineEventIds.Add("promo-event"); ctx.ShowResult.PromoResultIds.Add("promo");
            new WrestlerCareerService().PrepareShow(ResultApplicationContext.Create(save, "result")).Apply();
            Assert.That(save.Wrestlers[0].Fame.Value, Is.EqualTo(3)); Assert.That(save.Wrestlers[1].Fame.Value, Is.Zero);
        }

        [Test]
        public void WorldTitle_UsesActualOutcomeAndKeepsShowStartRookieForMilestone()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save); var w = save.Wrestlers[0];
            w.Fame = WrestlerFameRules.Create(499); w.Status.IsRookie = true;
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true); save.MatchPlans[0].ChampionshipId = title.Id;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(w.Fame.Value, Is.EqualTo(539)); Assert.That(w.Fame.HighestMilestone, Is.EqualTo(1));
            var e = save.CareerEvents.Single(x => x.Kind.ToString() == "Fame" && x.WrestlerId == w.Id);
            Assert.That((bool)e.GetType().GetField("FameWasRookie").GetValue(e), Is.True);
        }

        [Test]
        public void InvalidReach_RejectsWithoutAnyPartialCareerChanges()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save, -1); var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new WrestlerCareerService().PrepareShow(ctx));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void LegacyResults_DoNotAcquireFameOrMilestones()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = WrestlerCareerServiceTests.AddMatch(save);
            save.MatchResults[0].ActualMatchDuration = 10;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(save.Wrestlers[0].Fame.Value, Is.Zero);
            Assert.That(save.CareerEvents.Any(x => x.Kind.ToString() == "Fame"), Is.False);
        }

        [Test]
        public void Migration_SeedsCareerAndPlayerWithoutRetroactiveAwards()
        {
            var save = WrestlerCareerServiceTests.Create(); save.SaveVersion = 8; var w = save.Wrestlers[0]; w.Identity.CareerYears = 12;
            save.Player = new() { WrestlerId = save.Wrestlers[1].Id, Reputation = PlayerReputation.Legend };
            var migration = typeof(SaveService).Assembly.GetType("PWManager.Infrastructure.Save.SaveVersion8To9Migration"); Assert.That(migration, Is.Not.Null);
            ((ISaveMigration)Activator.CreateInstance(migration)).Migrate(save);
            Assert.That(w.Fame.Value, Is.EqualTo(120)); Assert.That(save.Wrestlers[1].Fame.Value, Is.EqualTo(15000));
            Assert.That(save.Wrestlers[1].Fame.HighestMilestone, Is.EqualTo(2)); Assert.That(save.CareerEvents, Is.Empty);
        }

        [Test]
        public void FractionsAndLargeValues_ArePreservedAndOverflowDoesNotMutate()
        {
            var state = WrestlerFameRules.Create(16000); for (var i = 0; i < 10; i++) WrestlerFameRules.Accumulate(state, .1);
            Assert.That(state.Value, Is.EqualTo(16001)); Assert.That(state.Remainder, Is.Zero);
            state = WrestlerFameRules.Create(long.MaxValue); Assert.Throws<OverflowException>(() => WrestlerFameRules.Accumulate(state, 1));
            Assert.That(state.Value, Is.EqualTo(long.MaxValue)); Assert.That(state.Remainder, Is.Zero);
        }

        [TestCase(MatchFinishType.Pinfall, 40L)]
        [TestCase(MatchFinishType.Disqualification, 20L)]
        [TestCase(MatchFinishType.CountOut, 20L)]
        [TestCase(MatchFinishType.Draw, 20L)]
        public void TitleDefense_OnlyActualSuccessfulDefenseAddsAchievement(MatchFinishType finish, long expected)
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save); var w = save.Wrestlers[0];
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, w.Id);
            save.MatchPlans[0].ChampionshipId = title.Id; save.MatchResults[0].FinishType = finish;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(w.Fame.Value, Is.EqualTo(expected));
        }

        [Test]
        public void FameRecordIdCollision_RejectsBeforeMutatingAnySourceState()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save); var before = JsonUtility.ToJson(save);
            var first = Guid.NewGuid().ToString("D"); var calls = 0;
            Assert.Throws<InvalidOperationException>(() => new WrestlerCareerService(() => ++calls == 2 ? Guid.NewGuid().ToString("D") : first).PrepareShow(ctx));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void ResultReapplication_RejectsAndKeepsFameAndRemainder()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save);
            new ShowResultApplicationService().Apply(save, ctx.ShowResult.Id); var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, ctx.ShowResult.Id));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void InvalidRemainderAndForgedFameHistory_AreRejected()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = Setup(save); save.Wrestlers[0].Fame.Remainder = double.NaN;
            Assert.Throws<InvalidOperationException>(() => new WrestlerCareerService().PrepareShow(ctx));
            save.Wrestlers[0].Fame.Remainder = 0; new WrestlerCareerService().PrepareShow(ctx).Apply();
            save.CareerEvents.Single(x => x.Kind == CareerEventKind.Fame && x.WrestlerId == save.Wrestlers[0].Id).FameAfter++;
            Assert.Throws<InvalidOperationException>(() => WrestlerCareerService.ValidateState(save));
        }
    }
}
