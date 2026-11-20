using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class PromotionQuarterTests
    {
        [Test]
        public void ExecutedShowAndDailyTimeFlow_ReachQuarterSettlementWithActualAttendance()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            foreach (var w in save.Wrestlers)
            {
                w.Identity.CreatedDate = save.CurrentDate; w.Identity.BirthDate = new GameDate(2000, 1, 1);
                w.Growth.MatchPotentialCap = w.Growth.PromoPotentialCap = 20;
                foreach (var field in typeof(WrestlerAttributesState).GetFields())
                    if ((float)field.GetValue(w.Attributes) < 1) field.SetValue(w.Attributes, 10f);
            }
            PromotionQuarterService.Initialize(save);
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            save.Shows.Single().Status = ShowStatus.ResultsReviewed; new ShowResultApplicationService().Apply(save, result.Id);
            var target = new GameDate(2026, 9, 1);
            while (save.CurrentDate.CompareTo(target) < 0)
            {
                var next = save.CurrentDate.DaysUntil(target) > 30 ? save.CurrentDate.AddDays(30) : target;
                Assert.That(new TimeFlowService().AdvanceTo(save, next).State, Is.EqualTo(TimeFlowState.Completed));
            }
            var record = save.Promotion.QuarterlyGrowth.History.Single();
            Assert.That(record.CompletedShows, Is.EqualTo(1)); Assert.That(record.TotalAttendance, Is.EqualTo(result.FinancialSettlement.Attendance));
            Assert.That(record.BaseScore, Is.EqualTo(10)); Assert.That(record.PrestigeAfter, Is.EqualTo(save.Promotion.PromotionPrestige));
            Assert.That(record.EndDate, Is.EqualTo(new GameDate(2026, 8, 31)));
        }

        private static GameSave ObservedSave()
        {
            var save = WrestlerCareerServiceTests.Create(); PromotionQuarterService.Initialize(save);
            var ctx = WrestlerCareerServiceTests.AddMatch(save); ctx.Show.Status = ShowStatus.Completed;
            ctx.ShowResult.FinancialSettlement.Attendance = 500; ctx.ShowResult.ShowEvaluation.Score = 80;
            PromotionPrestigeRules.Capture(ctx.ShowResult, ScheduledShowType.Regular, 1000);
            return save;
        }

        [Test]
        public void Quarter_RecordsActualComponentsAndAppliesOnceWithoutPreparingMutation()
        {
            var save = ObservedSave(); save.CurrentDate = new GameDate(2026, 8, 31); var before = JsonUtility.ToJson(save);
            var plan = new WrestlerCareerService().PrepareDue(save); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before)); plan.Apply(); plan.Apply();
            var r = save.Promotion.QuarterlyGrowth.History.Single();
            Assert.That(r.CompletedShows, Is.EqualTo(1)); Assert.That(r.BaseScore, Is.EqualTo(10));
            Assert.That(r.FanScore, Is.EqualTo(9)); Assert.That(r.AttendanceScore, Is.EqualTo(15)); Assert.That(r.RosterScore, Is.Zero);
            Assert.That(r.TotalGain, Is.EqualTo(34)); Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(34));
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.QuarterlyGrowth.History, Has.Count.EqualTo(1));
            Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(34));
        }

        [Test]
        public void LastDay_WaitsForShowAndIncludesCurrentResultWithoutDoublePayingPpv()
        {
            var save = ObservedSave(); var show = save.Shows.Single(); save.CurrentDate = show.Date = new GameDate(2026, 8, 31);
            show.Status = ShowStatus.ResultsReviewed; show.ShowType = ScheduledShowType.PpvRegular;
            var result = save.ShowResults.Single(); result.FinancialSettlement.Attendance = 800;
            PromotionPrestigeRules.Capture(result, show.ShowType, 1000);
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.QuarterlyGrowth.History, Is.Empty);
            new WrestlerCareerService().PrepareShow(ResultApplicationContext.Create(save, result.Id)).Apply();
            var r = save.Promotion.QuarterlyGrowth.History.Single(); Assert.That(r.PpvBonus, Is.EqualTo(18));
            Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(r.TotalGain)); Assert.That(r.PrestigeAfter, Is.EqualTo(r.PrestigeBefore + r.TotalGain));
        }

        [Test]
        public void PrestigeCrossing_DoesNotUnlockDuringSeasonAndUnlocksArePermanent()
        {
            var save = ObservedSave(); save.Promotion.PromotionPrestige = 295; save.CurrentDate = new GameDate(2026, 8, 31);
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.GreaterThan(300));
            Assert.That(PromotionProgressionRules.ActiveTier(save), Is.Zero);
            Assert.Throws<InvalidOperationException>(() => PromotionProgressionRules.ValidateOperations(0, RegularShowFrequency.Weekly, PpvFrequency.Quarterly, new[] { ScheduledShowType.PpvRegular }));
            save.Promotion.UnlockedIds = PromotionProgressionRules.Unlocks(save, save.Promotion.PromotionPrestige);
            save.Promotion.PromotionPrestige = 0;
            Assert.That(PromotionProgressionRules.ActiveTier(save), Is.EqualTo(1));
            Assert.DoesNotThrow(() => PromotionProgressionRules.ValidateOperations(1, RegularShowFrequency.Weekly, PpvFrequency.Quarterly, new[] { ScheduledShowType.PpvRegular }));
        }

        [Test]
        public void TierTwo_AllowsSignatureButMajorRequiresTierFour()
        {
            Assert.That(PromotionProgressionRules.PpvTypes(2), Does.Contain(ScheduledShowType.PpvSignature));
            Assert.That(PromotionProgressionRules.PpvTypes(2).Contains(ScheduledShowType.PpvMajor), Is.False);
            Assert.That(PromotionProgressionRules.PpvTypes(4), Does.Contain(ScheduledShowType.PpvMajor));
            Assert.That(PromotionProgressionRules.MaxRegular(6), Is.EqualTo((int)RegularShowFrequency.ThreeTimesWeekly));
        }

        [Test]
        public void Migration_PreservesExistingOperationsAndSkipsAlreadyCompletedResults()
        {
            var save = ObservedSave(); save.Promotion.QuarterlyGrowth = null; save.SaveVersion = 11;
            save.Promotion.UnlockedIds.Add("venue_003"); save.SeasonPolicy = new() { RegularShowFrequency = RegularShowFrequency.Weekly };
            new SaveVersion11To12Migration().Migrate(save);
            Assert.That(PromotionProgressionRules.ActiveTier(save), Is.GreaterThanOrEqualTo(1));
            Assert.That(save.Promotion.UnlockedIds, Does.Contain("venue_003")); Assert.That(save.Promotion.PromotionPrestige, Is.Zero);
            save.CurrentDate = new GameDate(2026, 8, 31); new WrestlerCareerService().PrepareDue(save).Apply();
            Assert.That(save.Promotion.QuarterlyGrowth.History.Single().CompletedShows, Is.Zero);
        }

        [Test]
        public void Preview_IsReadOnlyAndOverflowDoesNotMutateQuarterOrPrestige()
        {
            var save = ObservedSave(); var before = JsonUtility.ToJson(save); var preview = PromotionQuarterService.Preview(save);
            Assert.That(preview.CompletedShows, Is.EqualTo(1)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            save.Promotion.PromotionPrestige = long.MaxValue; save.CurrentDate = new GameDate(2026, 8, 31); before = JsonUtility.ToJson(save);
            Assert.Throws<OverflowException>(() => new WrestlerCareerService().PrepareDue(save)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void TamperedQuarterBreakdown_IsRejectedEvenWhenItsTotalWasAdjusted()
        {
            var save = ObservedSave(); save.CurrentDate = new GameDate(2026, 8, 31); new WrestlerCareerService().PrepareDue(save).Apply();
            var record = save.Promotion.QuarterlyGrowth.History.Single(); record.FanScore++; record.PrestigeAfter++; save.Promotion.PromotionPrestige++;
            Assert.Throws<InvalidOperationException>(() => WrestlerCareerService.ValidateState(save));
        }
    }
}
