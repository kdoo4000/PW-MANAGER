using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class WrestlerCareerServiceTests
    {
        [Test]
        public void MainEventUpset_AddsCardAndWinWithoutMutatingDuringPreparation()
        {
            var save = Create(); var ctx = AddMatch(save); var before = JsonUtility.ToJson(save);
            var plan = new WrestlerCareerService().PrepareShow(ctx);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before)); plan.Apply();
            Assert.That(save.Wrestlers[0].Status.StatusValue, Is.EqualTo(38));
            Assert.That(save.Wrestlers[1].Status.StatusValue, Is.EqualTo(74));
            Assert.That(save.Wrestlers[0].Status.OfficialMatchCount, Is.EqualTo(1));
        }

        [Test]
        public void ShowApplication_ComposesCareerAndOnlyCountsEachMatchOnce()
        {
            var save = Create(); var ctx = AddMatch(save);
            new ShowResultApplicationService().Apply(save, ctx.ShowResult.Id);
            Assert.That(save.Wrestlers[0].Status.StatusValue, Is.EqualTo(38));
            Assert.That(save.Wrestlers[0].Status.OfficialMatchCount, Is.EqualTo(1));
            Assert.That(save.CareerEvents, Has.Count.EqualTo(2));
            var json = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, ctx.ShowResult.Id));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(json));
        }

        [Test]
        public void DateExit_SettlesInactiveQuarterAndRookieAnniversary()
        {
            var save = Create(); save.CurrentDate = new GameDate(2026, 8, 31); var w = save.Wrestlers[0];
            w.Momentum.Momentum = 80; w.Status.IsRookie = true; w.Status.DebutDate = new GameDate(2025, 9, 1);
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 9, 1));
            Assert.That(w.Momentum.Momentum, Is.Zero); Assert.That(w.Status.IsRookie, Is.False);
            Assert.That(save.LastCareerQuarterEnd.Value, Is.EqualTo(new GameDate(2026, 8, 31)));
        }

        [Test]
        public void MultipleAppearances_OnlyHighestCardPositionRaisesStatusOnce()
        {
            var save = Create(); AddMatch(save); var first = save.MatchResults[0]; first.FinishType = MatchFinishType.Draw;
            first.CriticReview.FinalScore = 0;
            var second = JsonUtility.FromJson<MatchResultState>(JsonUtility.ToJson(first)); second.Id = "match2"; second.ShowEventId = "event2";
            save.MatchResults.Add(second); save.ShowEvents.Add(new() { Id = "event2", ShowId = "show", EventType = ShowEventType.Match, DetailId = "plan" });
            save.Shows[0].TimelineEventIds.Add("event2"); save.Shows[0].MainEventId = "event2"; save.ShowResults[0].MatchResultIds.Add("match2");
            new WrestlerCareerService().PrepareShow(ResultApplicationContext.Create(save, "result")).Apply();
            Assert.That(save.Wrestlers[0].Status.StatusValue, Is.EqualTo(26));
            Assert.That(save.CareerEvents.Where(x => x.WrestlerId == save.Wrestlers[0].Id).SelectMany(x => x.StatusReasons).Count(x => x.Reason == StatusChangeReason.Card), Is.EqualTo(1));
        }

        [Test]
        public void TagMatch_SeparatesDirectAndIndirectStatusChanges()
        {
            var save = Create(); var ctx = AddMatch(save); var r = save.MatchResults[0];
            var partner = JsonUtility.FromJson<WrestlerState>(JsonUtility.ToJson(save.Wrestlers[0])); partner.Id = Id(); save.Wrestlers.Add(partner);
            var other = JsonUtility.FromJson<WrestlerState>(JsonUtility.ToJson(save.Wrestlers[1])); other.Id = Id(); save.Wrestlers.Add(other);
            r.ParticipantProfiles[0].MemberIds.Add(partner.Id); r.ParticipantProfiles[1].MemberIds.Add(other.Id);
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(partner.Status.StatusValue, Is.EqualTo(31)); Assert.That(other.Status.StatusValue, Is.EqualTo(78));
            Assert.That(save.Wrestlers[0].Status.StatusValue, Is.EqualTo(38)); Assert.That(save.Wrestlers[1].Status.StatusValue, Is.EqualTo(74));
        }

        [Test]
        public void FullRelease_ChangesIndividualInterestAndPromotionSatisfactionButKeepsPreference()
        {
            var save = Create(); var ctx = AddMatch(save); var w = save.Wrestlers[0]; w.Momentum.Momentum = 60;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            var e = save.CareerEvents.Single(x => x.WrestlerId == w.Id);
            Assert.That(e.ReleaseType, Is.EqualTo(MomentumReleaseType.Full)); Assert.That(w.Momentum.Momentum, Is.Zero);
            Assert.That(w.FanReaction.Mark.Interest, Is.GreaterThan(50)); Assert.That(w.FanReaction.Mark.Preference, Is.EqualTo(20));
            Assert.That(save.Promotion.Audience.Mark.Satisfaction, Is.GreaterThan(50));
            Assert.That(w.Momentum.MomentumFailure, Is.Zero);
        }

        [Test]
        public void ImpressiveDefeat_AutomaticallyReleasesExactlyHalfAndClearsFailure()
        {
            var save = Create(); var ctx = AddMatch(save); var w = save.Wrestlers[0];
            LoseFirst(save); w.Momentum.Momentum = 60; w.Momentum.MomentumFailure = 2;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            var e = save.CareerEvents.Single(x => x.WrestlerId == w.Id);
            Assert.That(e.ReleaseType, Is.EqualTo(MomentumReleaseType.Partial));
            Assert.That(e.ReleasedMomentum, Is.EqualTo((e.MomentumBefore + e.MomentumGain) * .5f));
            Assert.That(w.Momentum.Momentum, Is.EqualTo(e.ReleasedMomentum)); Assert.That(w.Momentum.MomentumFailure, Is.Zero);
        }

        [Test]
        public void Collapse_PenalizesIndividualAndPromotionAndDoesNotChangeWrestlerSatisfaction()
        {
            var save = Create(); var ctx = AddMatch(save); var w = save.Wrestlers[0]; LoseFirst(save);
            w.Status.StatusValue = 80; save.Wrestlers[1].Status.StatusValue = 20;
            save.MatchResults[0].CriticReview.FinalScore = 0; w.Momentum.Momentum = 80; w.Momentum.MomentumFailure = 2;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(w.Momentum.Momentum, Is.Zero); Assert.That(w.Momentum.MomentumFailure, Is.Zero);
            Assert.That(w.FanReaction.Mark.Interest, Is.EqualTo(46)); Assert.That(w.FanReaction.Mark.Preference, Is.EqualTo(20));
            Assert.That(save.Promotion.Audience.Mark.Satisfaction, Is.EqualTo(40));
            Assert.That(w.Condition.Satisfaction, Is.EqualTo(50)); Assert.That(w.Status.StatusValue, Is.EqualTo(71));
            Assert.That(save.CareerEvents.Single(x => x.WrestlerId == w.Id).Collapsed, Is.True);
        }

        [TestCase(MatchFinishType.Disqualification, .5f)]
        [TestCase(MatchFinishType.Pinfall, 1f)]
        [TestCase(MatchFinishType.Draw, 0f)]
        public void Failure_UsesActualFinishAndDoesNotPunishDraw(MatchFinishType finish, float added)
        {
            var save = Create(); var ctx = AddMatch(save); LoseFirst(save); var w = save.Wrestlers[0];
            w.Momentum.Momentum = 60; save.MatchResults[0].CriticReview.FinalScore = 0; save.MatchResults[0].FinishType = finish;
            new WrestlerCareerService().PrepareShow(ctx).Apply(); Assert.That(w.Momentum.MomentumFailure, Is.EqualTo(added));
        }

        [Test]
        public void InactiveQuarter_ResetsMomentumAndFailureWithoutFansOrStatusPenalty()
        {
            var save = Create(); save.CurrentDate = new GameDate(2026, 8, 31); var w = save.Wrestlers[0];
            w.Momentum.Momentum = 80; w.Momentum.MomentumFailure = 2;
            save.Wrestlers[1].Momentum.Momentum = 60; save.Wrestlers[1].Momentum.LastMomentumActivityDate = new(new GameDate(2026, 6, 7));
            new WrestlerCareerService().PrepareDue(save).Apply();
            Assert.That(w.Momentum.Momentum, Is.Zero); Assert.That(w.Status.StatusValue, Is.EqualTo(20));
            Assert.That(w.FanReaction.Mark.Interest, Is.EqualTo(50)); Assert.That(save.Promotion.Audience.Mark.Satisfaction, Is.EqualTo(50));
            Assert.That(save.Wrestlers[1].Momentum.Momentum, Is.EqualTo(60));
            var json = JsonUtility.ToJson(save); new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(json));
        }

        [Test]
        public void Rookie_TenthOfficialMatchAndCalendarAnniversaryRemoveFlag()
        {
            var save = Create(); var ctx = AddMatch(save); var w = save.Wrestlers[0]; w.Status.IsRookie = true; w.Status.OfficialMatchCount = 9;
            new WrestlerCareerService().PrepareShow(ctx).Apply(); Assert.That(w.Status.IsRookie, Is.False);
            var other = save.Wrestlers[1]; other.Status.IsRookie = true; other.Status.DebutDate = new GameDate(2025, 6, 7);
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(other.Status.IsRookie, Is.False);
        }

        [Test]
        public void InvalidIdAndRepeatedSource_DoNotPartiallyMutate()
        {
            var save = Create(); var ctx = AddMatch(save); var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new WrestlerCareerService(() => "bad").PrepareShow(ctx));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            new WrestlerCareerService().PrepareShow(ctx).Apply(); var after = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new WrestlerCareerService().PrepareShow(ctx)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(after));
        }

        [Test]
        public void VersionSevenMigration_PreservesStateAndStartsObservationWithoutRetroactiveReset()
        {
            var save = Create(); save.SaveVersion = 7; save.CurrentDate = new GameDate(2027, 5, 31); var w = save.Wrestlers[0];
            w.Momentum.Momentum = 80; var before = JsonUtility.ToJson(w); new SaveVersion7To8Migration().Migrate(save);
            Assert.That(JsonUtility.ToJson(w), Is.EqualTo(before)); Assert.That(save.CareerEvents, Is.Empty);
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(w.Momentum.Momentum, Is.EqualTo(80));
        }

        [Test]
        public void RiskPreview_ProtectedFailureMustActuallyReachCollapseThreshold()
        {
            var save = Create(); AddMatch(save); var w = save.Wrestlers[0]; var plan = save.MatchPlans[0];
            w.Momentum.Momentum = 60; w.Momentum.MomentumFailure = 2; plan.FinishType = MatchFinishType.Disqualification;
            plan.LoserTargetId = w.Id; plan.WinningSideId = "right";
            Assert.That(WrestlerCareerService.HasMomentumRisk(save, save.Shows[0], plan, w.Id), Is.False);
            w.Momentum.MomentumFailure = 2.5f;
            Assert.That(WrestlerCareerService.HasMomentumRisk(save, save.Shows[0], plan, w.Id), Is.True);
            plan.FinishType = MatchFinishType.Draw;
            Assert.That(WrestlerCareerService.HasMomentumRisk(save, save.Shows[0], plan, w.Id), Is.False);
        }

        [Test]
        public void RiskPreview_IncludesCrossingFiftyDuringTheMatch()
        {
            var save = Create(); var ctx = AddMatch(save); LoseFirst(save); var w = save.Wrestlers[0]; var plan = save.MatchPlans[0];
            w.Momentum.Momentum = 49; w.Momentum.MomentumFailure = 2;
            plan.LoserTargetId = w.Id; plan.WinningSideId = "right";
            save.MatchResults[0].CriticReview.FinalScore = 0;
            save.MatchResults[0].FanReaction = new() { IsCalculated = true, Family = 60, Light = 60, Mania = 60 };
            Assert.That(WrestlerCareerService.HasMomentumRisk(save, save.Shows[0], plan, w.Id), Is.True);
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(save.CareerEvents.Single(x => x.WrestlerId == w.Id).Collapsed, Is.True);
        }

        [Test]
        public void DateEntryAndQuarterIds_AreValidatedBeforeAnyCareerOrDateCommit()
        {
            var save = Create(); save.CurrentDate = new GameDate(2026, 8, 31); save.Wrestlers[0].Momentum.Momentum = 80;
            save.Wrestlers[1].Status.IsRookie = true; save.Wrestlers[1].Status.DebutDate = new GameDate(2025, 9, 1);
            new InboxService().EnsureForCurrentDate(save);
            var before = JsonUtility.ToJson(save); var repeated = Id(); var index = 0;
            Assert.Throws<InvalidOperationException>(() => new TimeFlowService(() => ++index is 1 or 4 ? repeated : Id()).AdvanceTo(save, new GameDate(2026, 9, 1)));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void NormalFanResultAndAchievementBonus_AreEachAppliedOnce()
        {
            var save = Create(); var ctx = AddMatch(save); var w = save.Wrestlers[0]; w.Momentum.Momentum = 60;
            ctx.ShowResult.FanSatisfaction = new() { IsCalculated = true, Family = 70, Light = 70, Mania = 70 };
            ctx.ShowResult.FanExpectation = new() { IsCalculated = true, Family = 50, Light = 50, Mania = 50 }; ctx.ShowResult.FanExposure = .5f;
            ctx.ShowResult.FanResponseChanges.Add(new() { WrestlerId = w.Id, Mark = new() { Preference = 2, Interest = 1 },
                Casual = new() { Preference = 2, Interest = 1 }, Hardcore = new() { Preference = 2, Interest = 1 } });
            new ShowResultApplicationService().Apply(save, ctx.ShowResult.Id);
            Assert.That(w.FanReaction.Mark.Interest, Is.EqualTo(54)); Assert.That(w.FanReaction.Mark.Preference, Is.EqualTo(22));
            Assert.That(save.Promotion.Audience.Mark.Satisfaction, Is.EqualTo(57));
        }

        [Test]
        public void TitleAcquisition_AddsIndependentChallengeAndChampionReasonsAndRemovesRookie()
        {
            var save = Create(); var ctx = AddMatch(save); var w = save.Wrestlers[0]; w.Status.IsRookie = true;
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true);
            save.MatchPlans[0].ChampionshipId = title.Id;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(w.Status.StatusValue, Is.EqualTo(56)); Assert.That(w.Status.IsRookie, Is.False);
            var reasons = save.CareerEvents.Single(x => x.WrestlerId == w.Id).StatusReasons.Select(x => x.Reason);
            Assert.That(reasons, Does.Contain(StatusChangeReason.TitleChallenge)); Assert.That(reasons, Does.Contain(StatusChangeReason.Championship));
        }

        [Test]
        public void FinancialAndCareerIdCollision_RejectsBeforeAnyResultMutation()
        {
            var save = Create(); var ctx = AddMatch(save); ctx.ShowResult.FinancialSettlement = new() { Revenue = 100, NetIncome = 100 };
            var before = JsonUtility.ToJson(save); var repeated = Id(); var index = 0;
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService(() => ++index <= 2 ? repeated : Id()).Apply(save, ctx.ShowResult.Id));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        internal static GameSave Create()
        {
            var save = GameSave.CreateNew(14, DateTime.UtcNow); save.CurrentDate = new GameDate(2026, 6, 7);
            save.Promotion = new PromotionState { Id = Id(), InitialCash = 10000, Audience = FanAudienceService.CreateInitial(save.CurrentDate) };
            for (var i = 0; i < 2; i++)
            {
                var w = new WrestlerState { Id = Id(), PromotionId = save.Promotion.Id };
                foreach (var f in typeof(WrestlerAttributesState).GetFields()) f.SetValue(w.Attributes, 10f);
                w.Growth.MatchPotentialCap = w.Growth.PromoPotentialCap = 20; w.Status.StatusValue = i == 0 ? 20 : 80;
                w.Roster.ActivityState = RosterActivityState.Active; w.Condition.Satisfaction = 50;
                w.Identity.CreatedDate = new GameDate(2026, 6, 1); w.Identity.BirthDate = new GameDate(2000, 1, 1);
                w.FanReaction.UsesTwoAxes = true; w.FanReaction.Mark = w.FanReaction.Casual = w.FanReaction.Hardcore = new() { Preference = 20, Interest = 50 };
                save.Wrestlers.Add(w);
            }
            return save;
        }
        internal static ResultApplicationContext AddMatch(GameSave save)
        {
            var a = save.Wrestlers[0].Id; var b = save.Wrestlers[1].Id;
            save.Schedules.Add(new() { Id = "schedule", Date = save.CurrentDate, Status = ScheduleStatus.Confirmed });
            save.Shows.Add(new() { Id = "show", ScheduleId = "schedule", Date = save.CurrentDate, ShowVersion = 1, Status = ShowStatus.ResultsReviewed,
                TimelineEventIds = { "event" }, OpeningEventId = "event", MainEventId = "event" });
            save.ShowEvents.Add(new() { Id = "event", ShowId = "show", EventType = ShowEventType.Match, DetailId = "plan" });
            save.MatchPlans.Add(new() { Id = "plan", Sides = { new() { Id = "left", MemberIds = { a } }, new() { Id = "right", MemberIds = { b } } } });
            save.MatchResults.Add(new() { Id = "match", ShowId = "show", ShowVersion = 1, ShowEventId = "event", MatchPlanId = "plan",
                WinningSideId = "left", WinnerId = a, FinishPerformerId = a, LoserTargetId = b, FinishType = MatchFinishType.Pinfall,
                CriticReview = new() { FinalScore = 75 }, ParticipantProfiles = { new() { SideId = "left", MemberIds = { a } }, new() { SideId = "right", MemberIds = { b } } } });
            save.ShowResults.Add(new() { Id = "result", ShowId = "show", ShowVersion = 1, MatchResultIds = { "match" } });
            return ResultApplicationContext.Create(save, "result");
        }
        private static void LoseFirst(GameSave save)
        {
            var r = save.MatchResults[0]; r.WinningSideId = "right"; r.WinnerId = r.FinishPerformerId = save.Wrestlers[1].Id; r.LoserTargetId = save.Wrestlers[0].Id;
        }
        private static string Id() => Guid.NewGuid().ToString("D");
    }
}
