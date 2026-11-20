using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests.EditMode
{
    public sealed class WrestlerGrowthServiceTests
    {
        private sealed class ZeroRandom : System.Random
        {
            public override double NextDouble() => 0;
            public override int Next(int maxValue) => 0;
        }
        private sealed class HalfRandom : System.Random
        {
            public override double NextDouble() => .5;
            public override int Next(int maxValue) => 0;
        }

        [Test]
        public void Quarter_GrowsWithinRawCap_ConsumesProgress_AndDoesNotRepeat()
        {
            var save = Create(new GameDate(2026, 8, 31));
            var w = save.Wrestlers[0];
            w.Growth.MatchPotentialCap = 10.005f;
            w.Growth.GrowthProgress.Brawling = 50;
            new WrestlerGrowthService(random: new ZeroRandom()).PrepareDue(save).Apply();
            Assert.That(w.Attributes.Brawling, Is.GreaterThan(10));
            Assert.That(WrestlerOverallCalculator.MatchTotal(w.Attributes), Is.LessThanOrEqualTo(100.051f));
            Assert.That(w.Growth.GrowthProgress.Brawling, Is.Zero);
            var before = JsonUtility.ToJson(save);
            new WrestlerGrowthService().PrepareDue(save).Apply();
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void Quarter_KeepsFutureExperienceAndConsumesLegacyRemainder()
        {
            var save = Create(new GameDate(2026, 8, 31));
            var w = save.Wrestlers[0];
            w.Growth.GrowthProgress.Technical = 60;
            var record = new WrestlerExperienceState { Id = Guid.NewGuid().ToString("D"), WrestlerId = w.Id,
                SourceResultId = "future", Activity = ExperienceActivity.Promo, Date = new GameDate(2026, 9, 1), Progress = new() { Technical = 10 } };
            save.PromoResults.Add(new PromoResultState { Id = "future" });
            save.ExperienceRecords.Add(record); w.Growth.ExperienceLogIds.Add(record.Id);
            new WrestlerGrowthService(random: new ZeroRandom()).PrepareDue(save).Apply();
            Assert.That(w.Growth.GrowthProgress.Technical, Is.EqualTo(10));
            Assert.That(save.ExperienceRecords[0].SettledQuarter.HasValue, Is.False);
            Assert.That(save.GrowthQuarterResults.Single().ConsumedProgress.Technical, Is.EqualTo(50));
        }

        [Test]
        public void AnnualAging_OnlyPhysicalSkills_LegacyLateBloomerNoLongerGrantsImmunity()
        {
            var save = Create(new GameDate(2027, 5, 31));
            save.LastGrowthQuarterEnd = new(new GameDate(2027, 2, 28));
            var w = save.Wrestlers[0]; w.Identity.BirthDate = new GameDate(1977, 1, 1);
            var immune = CreateWrestler(); immune.Identity.BirthDate = w.Identity.BirthDate;
            immune.Presentation.TraitIds.Add("trait_009"); save.Wrestlers.Add(immune);
            new WrestlerGrowthService(random: new ZeroRandom()).PrepareDue(save).Apply();
            Assert.That(w.Attributes.Power, Is.LessThan(10));
            Assert.That(w.Attributes.Technical, Is.EqualTo(10));
            Assert.That(w.Attributes.MicWork, Is.EqualTo(10));
            Assert.That(immune.Attributes.Power, Is.LessThan(10));
            Assert.That(save.GrowthQuarterResults.SelectMany(x => x.Changes).All(x => x.Cause == AttributeChangeCause.Aging), Is.True);
        }

        [Test]
        public void InvalidGeneratedId_LeavesOriginalUntouched()
        {
            var save = Create(new GameDate(2026, 8, 31));
            save.Wrestlers[0].Growth.GrowthProgress.Power = 50;
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new WrestlerGrowthService(() => "bad").PrepareDue(save));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void OptionalTraitList_DoesNotBlockQuarterlyGrowth()
        {
            var save = Create(new GameDate(2026, 8, 31)); var w = save.Wrestlers[0];
            w.Presentation.TraitIds = null; w.Growth.GrowthProgress.Power = 50;
            new WrestlerGrowthService(random: new ZeroRandom()).PrepareDue(save).Apply();
            Assert.That(w.Attributes.Power, Is.GreaterThan(10));
        }

        [Test]
        public void Migration_DoesNotBackfillOrChangeAttributes()
        {
            var save = Create(new GameDate(2028, 4, 1)); save.SaveVersion = 6;
            save.LastGrowthQuarterEnd = default;
            var before = JsonUtility.ToJson(save.Wrestlers[0].Attributes);
            new SaveVersion6To7Migration().Migrate(save);
            Assert.That(save.LastGrowthQuarterEnd.Value, Is.EqualTo(new GameDate(2028, 2, 29)));
            Assert.That(save.GrowthQuarterResults, Is.Empty);
            Assert.That(JsonUtility.ToJson(save.Wrestlers[0].Attributes), Is.EqualTo(before));
            Assert.That(save.SaveVersion, Is.EqualTo(7));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Match_StacksBonuses_UsesRecordedStyleAndSellingActor_WithoutImmediateGrowth(bool performerSells)
        {
            var save = Create(new GameDate(2026, 7, 1)); var context = AddShow(save); var w = save.Wrestlers[0];
            var other = save.Wrestlers[1]; var match = save.MatchResults[0];
            context.Show.ShowType = ScheduledShowType.PpvRegular; match.CriticReview.DisplayedStars = 4;
            match.WrestlerPerformances.Add(new() { WrestlerId = w.Id, BrawlingGrowthBonus = .2f });
            match.EngineState = new MatchEngineState();
            match.EngineState.Events.Add(new() { Type = MatchActionType.BasicStrike, PerformerId = w.Id, ReceiverId = other.Id,
                IsPhysicalAttack = true, SellingWrestlerId = performerSells ? w.Id : other.Id });
            match.SpotResults.Add(new() { ActorId = w.Id, WasApplied = true, WasSkipped = true });
            var before = JsonUtility.ToJson(w.Attributes);
            new WrestlerGrowthService().PrepareShow(context).Apply();
            var xp = save.ExperienceRecords.Single(x => x.WrestlerId == w.Id && x.Activity == ExperienceActivity.Match);
            Assert.That(xp.BaseExperience, Is.EqualTo(30)); Assert.That(xp.Progress.Brawling, Is.EqualTo(36));
            Assert.That(xp.Progress.Selling > 0, Is.EqualTo(performerSells)); Assert.That(xp.Progress.SpotWork, Is.Zero);
            Assert.That(JsonUtility.ToJson(w.Attributes), Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(() => new WrestlerGrowthService().PrepareShow(context));
        }

        [Test]
        public void VideoPromo_UsesSnapshotRole_ExcludesMentionedTargetsAndMicrophone()
        {
            var save = Create(new GameDate(2026, 7, 1)); var ctx = AddShow(save); var w = save.Wrestlers[0];
            save.MatchResults[0].CancellationReason = "cancelled";
            save.PromoPlans[0].Presentation = PromoPresentation.VideoPackage;
            save.PromoPlans[0].TargetIds.Add(save.Wrestlers[1].Id);
            save.PromoResults[0].ParticipantUsage.Add(new() { WrestlerId = w.Id, Alignment = KayfabeAlignment.Heel, Disposition = PromoDisposition.Comic });
            w.Roster.Alignment = KayfabeAlignment.Face;
            new WrestlerGrowthService().PrepareShow(ctx).Apply();
            var xp = save.ExperienceRecords.Single();
            Assert.That(xp.WrestlerId, Is.EqualTo(w.Id)); Assert.That(xp.BaseExperience, Is.EqualTo(3));
            Assert.That(xp.Progress.MicWork, Is.Zero); Assert.That(xp.Progress.HeelWork, Is.Positive);
            Assert.That(xp.Progress.FaceWork, Is.Zero); Assert.That(xp.Progress.Comedy, Is.Positive);
        }

        [Test]
        public void ClosingPromo_DoesNotGivePriorMatchTheMainEventBonuses()
        {
            var save = Create(new GameDate(2026, 7, 1)); var ctx = AddShow(save);
            ctx.Show.ShowType = ScheduledShowType.PpvRegular; ctx.Show.MainEventId = "promo_event";
            ctx.Show.TimelineEventIds = new() { "match_event", "promo_event" };
            new WrestlerGrowthService().PrepareShow(ctx).Apply();
            Assert.That(save.ExperienceRecords.Where(x => x.Activity == ExperienceActivity.Match).All(x => x.BaseExperience == 10), Is.True);
        }

        [Test]
        public void Migration_AlreadyPreparedNextSeasonDoesNotRepeatClosingQuarter()
        {
            var save = Create(new GameDate(2027, 5, 31)); save.SaveVersion = 6;
            save.SeasonPolicy = new() { Id = Guid.NewGuid().ToString("D"), StartDate = new GameDate(2027, 6, 1) };
            new SaveVersion6To7Migration().Migrate(save);
            Assert.That(save.LastGrowthQuarterEnd.Value, Is.EqualTo(save.CurrentDate));
            new WrestlerGrowthService().PrepareDue(save).Apply(); Assert.That(save.GrowthQuarterResults, Is.Empty);
        }

        [Test]
        public void LastDayShow_DefersQuarterUntilItsExperienceIsIncluded()
        {
            var save = Create(new GameDate(2026, 8, 31)); var ctx = AddShow(save); var w = save.Wrestlers[0];
            w.Growth.GrowthProgress.Brawling = 50;
            new WrestlerGrowthService().PrepareDue(save).Apply(); Assert.That(save.GrowthQuarterResults, Is.Empty);
            Assert.That(TimeFlowService.GetBlockingReasonForToday(save), Is.Not.Null);
            new WrestlerGrowthService(random: new ZeroRandom()).PrepareShow(ctx).Apply();
            Assert.That(save.LastGrowthQuarterEnd.Value, Is.EqualTo(save.CurrentDate));
            Assert.That(save.GrowthQuarterResults.Single(x => x.WrestlerId == w.Id).ConsumedProgress.Brawling, Is.GreaterThan(50));
            Assert.That(save.ExperienceRecords.All(x => x.SettledQuarter.HasValue), Is.True);
            Assert.That(w.Attributes.Brawling, Is.GreaterThan(10));
        }

        [Test]
        public void LastDayGrowth_UsesConditionAndRealInjuryAfterTheShow()
        {
            var save = Create(new GameDate(2026, 8, 31)); var ctx = AddShow(save); var w = save.Wrestlers[0];
            w.Growth.GrowthProgress.Power = 50;
            save.MatchResults[0].WrestlerConditionChanges.Add(new() { SourceResultId = "match_result", WrestlerId = w.Id, ConditionDelta = -75 });
            save.MatchResults[0].InjuryChanges.Add(new() { SourceResultId = "match_result", WrestlerId = w.Id, Severity = InjuryStatus.Severe,
                Region = BodyRegion.Body, RecoveryDays = 90 });
            new WrestlerGrowthService(random: new HalfRandom()).PrepareShow(ctx).Apply();
            Assert.That(w.Attributes.Power, Is.EqualTo(10), "Final condition .25 and severe injury .2 reduce a 100% raw chance below the 50% roll.");
            Assert.That(w.Condition.Condition, Is.EqualTo(100), "Growth preparation must not commit the separate health plan.");
            Assert.That(w.Condition.InjuryStatus, Is.EqualTo(InjuryStatus.None));
        }

        [Test]
        public void LastDayGrowth_UsesMomentumAfterAchievementWithoutCommittingCareerEarly()
        {
            var save = PWManager.Tests.WrestlerCareerServiceTests.Create(); save.CurrentDate = new GameDate(2026, 8, 31);
            var ctx = PWManager.Tests.WrestlerCareerServiceTests.AddMatch(save); var w = save.Wrestlers[0];
            w.Momentum.Momentum = 80; w.Growth.GrowthProgress.Power = 24;
            var baseline = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            var projected = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            new WrestlerCareerService().PrepareShow(ResultApplicationContext.Create(projected, "result")).Apply();
            new WrestlerGrowthService(random: new HalfRandom()).PrepareShow(ctx, projected.Wrestlers.ToDictionary(x => x.Id, x => x.Momentum)).Apply();
            new WrestlerGrowthService(random: new HalfRandom()).PrepareShow(ResultApplicationContext.Create(baseline, "result")).Apply();
            Assert.That(w.Attributes.Power, Is.EqualTo(10)); Assert.That(baseline.Wrestlers[0].Attributes.Power, Is.GreaterThan(10));
            Assert.That(w.Momentum.Momentum, Is.EqualTo(80));
        }

        [Test]
        public void GrowthAndAging_KeepSeparateCausesAndRunInThatOrder()
        {
            var save = Create(new GameDate(2027, 5, 31)); save.LastGrowthQuarterEnd = new(new GameDate(2027, 2, 28));
            var w = save.Wrestlers[0]; w.Identity.BirthDate = new GameDate(1977, 1, 1); w.Growth.GrowthProgress.Power = 50;
            new WrestlerGrowthService(random: new ZeroRandom()).PrepareDue(save).Apply();
            var changes = save.GrowthQuarterResults.Single().Changes.Where(x => x.Attribute == WrestlerAttribute.Power).ToList();
            Assert.That(changes.Select(x => x.Cause), Is.EqualTo(new[] { AttributeChangeCause.Growth, AttributeChangeCause.Aging }));
            Assert.That(changes[1].Before, Is.EqualTo(changes[0].After));
        }

        [Test]
        public void Quarter_IsDeterministicAfterReload_AndRespectsIndividualMaximum()
        {
            var save = Create(new GameDate(2026, 8, 31)); var w = save.Wrestlers[0];
            w.Attributes.MicWork = 19.99f; w.Growth.GrowthProgress.MicWork = 50; w.Growth.GrowthProgress.Technical = 50;
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            new WrestlerGrowthService().PrepareDue(save).Apply(); new WrestlerGrowthService().PrepareDue(restored).Apply();
            Assert.That(JsonUtility.ToJson(w.Attributes), Is.EqualTo(JsonUtility.ToJson(restored.Wrestlers[0].Attributes)));
            Assert.That(w.Attributes.MicWork, Is.LessThanOrEqualTo(20));
            var again = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save)); var before = JsonUtility.ToJson(again);
            new WrestlerGrowthService().PrepareDue(again).Apply(); Assert.That(JsonUtility.ToJson(again), Is.EqualTo(before));
        }

        [Test]
        public void Show_GrowthIdCollisionWithRevenueDoesNotChangeMoneyHealthOrLogs()
        {
            var save = Create(new GameDate(2026, 7, 1)); var ctx = AddShow(save); var before = JsonUtility.ToJson(save);
            var repeated = Guid.NewGuid().ToString("D"); var i = 0;
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService(() => ++i is 1 or 3 ? repeated : Guid.NewGuid().ToString("D")).Apply(save, ctx.ShowResult.Id));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void LegacyFastLearner_NoLongerImprovesEarnedProgress_AndDoesNotRaisePotential()
        {
            var save = Create(new GameDate(2026, 7, 1)); var ctx = AddShow(save); var w = save.Wrestlers[0];
            w.Presentation.TraitIds.Add("trait_005"); var cap = w.Growth.MatchPotentialCap;
            new WrestlerGrowthService().PrepareShow(ctx).Apply();
            Assert.That(save.ExperienceRecords.Single(x => x.WrestlerId == w.Id && x.Activity == ExperienceActivity.Promo).Progress.MicWork, Is.EqualTo(3f).Within(.001));
            Assert.That(w.Growth.MatchPotentialCap, Is.EqualTo(cap));
        }

        [Test]
        public void QuarterlyStateAndJournal_RoundTripAndRejectInvalidProgress()
        {
            var save = Create(new GameDate(2026, 8, 31)); var ctx = AddShow(save);
            new ShowResultApplicationService().Apply(save, ctx.ShowResult.Id);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.DoesNotThrow(() => WrestlerGrowthService.ValidateState(restored));
            Assert.That(restored.ExperienceRecords, Has.Count.EqualTo(3));
            Assert.That(restored.Wrestlers[0].Growth.ExperienceLogIds, Has.Count.EqualTo(2));
            Assert.That(restored.LastGrowthQuarterEnd.Value, Is.EqualTo(save.CurrentDate));
            restored.Wrestlers[0].Growth.GrowthProgress.Power = float.NaN;
            Assert.Throws<InvalidOperationException>(() => new WrestlerGrowthService().PrepareDue(restored));
        }

        [Test]
        public void ProfileGrowthButton_OpensReadOnlyHistoryAndDoesNotExposeScalarOrTrait()
        {
            var save = Create(new GameDate(2026, 8, 31)); save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString("D"), Name = "test" };
            var w = save.Wrestlers[0]; w.Presentation.WrestlingStyleId = "style_001"; w.Presentation.TraitIds.Add("trait_009");
            w.Growth.GrowthProgress.Power = 50; new WrestlerGrowthService(random: new ZeroRandom()).PrepareDue(save).Apply();
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Growth history test"); go.SetActive(false); var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { root });
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { w });
                var button = root.Q<Button>("wp-open-growth"); Assert.That(button, Is.Not.Null);
                var before = JsonUtility.ToJson(save);
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
                var overlay = root.Q("growth-history-overlay"); Assert.That(overlay, Is.Not.Null);
                Assert.That(overlay.Query<Label>().ToList().Any(x => x.text.Contains("분기 성장")), Is.True);
                Assert.That(overlay.Query<Label>().ToList().Any(x => x.text.Contains("10.1") || x.text.Contains("늦게 핀 꽃")), Is.False);
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        private static ResultApplicationContext AddShow(GameSave save)
        {
            save.Promotion ??= new PromotionState { Id = "promotion", InitialCash = 1000 };
            save.Wrestlers.Add(CreateWrestler());
            save.Schedules.Add(new() { Id = "schedule", Date = save.CurrentDate, Status = ScheduleStatus.Confirmed });
            save.Shows.Add(new() { Id = "show", ScheduleId = "schedule", ShowVersion = 1, Date = save.CurrentDate,
                Status = ShowStatus.ResultsReviewed, MainEventId = "match_event", OpeningEventId = "promo_event", TimelineEventIds = { "promo_event", "match_event" } });
            save.ShowEvents.Add(new() { Id = "match_event", ShowId = "show", EventType = ShowEventType.Match, DetailId = "match_plan" });
            save.ShowEvents.Add(new() { Id = "promo_event", ShowId = "show", EventType = ShowEventType.Promo, DetailId = "promo_plan" });
            save.MatchPlans.Add(new() { Id = "match_plan" }); save.PromoPlans.Add(new() { Id = "promo_plan", ParticipantIds = { save.Wrestlers[0].Id } });
            save.MatchResults.Add(new() { Id = "match_result", ShowId = "show", ShowVersion = 1, ShowEventId = "match_event", MatchPlanId = "match_plan",
                ActualMatchDuration = 20, ParticipantProfiles = { new() { MemberIds = { save.Wrestlers[0].Id, save.Wrestlers[1].Id } } } });
            save.PromoResults.Add(new() { Id = "promo_result", ShowId = "show", ShowVersion = 1, ShowEventId = "promo_event", PromoPlanId = "promo_plan" });
            save.ShowResults.Add(new() { Id = "show_result", ShowId = "show", ShowVersion = 1, MatchResultIds = { "match_result" }, PromoResultIds = { "promo_result" },
                FinancialSettlement = new() { Revenue = 1000, Cost = 400, NetIncome = 600 } });
            return ResultApplicationContext.Create(save, "show_result");
        }

        internal static GameSave Create(GameDate date)
        {
            var save = GameSave.CreateNew(42, DateTime.UtcNow); save.CurrentDate = date;
            save.LastGrowthQuarterEnd = new(new GameDate(2026, 5, 31));
            save.Wrestlers.Add(CreateWrestler()); return save;
        }

        private static WrestlerState CreateWrestler()
        {
            var w = new WrestlerState { Id = Guid.NewGuid().ToString("D") };
            foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(w.Attributes, 10f);
            w.Growth.MatchPotentialCap = w.Growth.PromoPotentialCap = 20;
            w.Identity.BirthDate = new GameDate(2000, 1, 1); w.Identity.CreatedDate = new GameDate(2026, 6, 1);
            return w;
        }
    }
}
