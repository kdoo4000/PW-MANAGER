using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class PpvPrestigeTests
    {
        private static object Call(string name, params object[] values)
        {
            var type = typeof(GameSave).Assembly.GetType("PWManager.Domain.Services.PromotionPrestigeRules"); Assert.That(type, Is.Not.Null);
            try { return type.GetMethod(name).Invoke(null, values); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }
        private static (GameSave Save, ResultApplicationContext Context) Setup(bool completed = true)
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = WrestlerCareerServiceTests.AddMatch(save);
            ctx.Show.ShowType = ScheduledShowType.PpvRegular; ctx.ShowResult.ShowEvaluation.Score = 80; ctx.ShowResult.FinancialSettlement.Attendance = 800;
            Call("Capture", ctx.ShowResult, ctx.Show.ShowType, 1000);
            if (completed) ctx.Show.Status = ShowStatus.Completed;
            save.CurrentDate = new GameDate(2026, 8, 31); return (save, ctx);
        }

        [TestCase(ScheduledShowType.Regular, 1000L, 100f, 0)]
        [TestCase(ScheduledShowType.PpvRegular, 500L, 69f, 0)]
        [TestCase(ScheduledShowType.PpvRegular, 500L, 70f, 10)]
        [TestCase(ScheduledShowType.PpvRegular, 800L, 80f, 18)]
        [TestCase(ScheduledShowType.PpvRegular, 1000L, 100f, 25)]
        [TestCase(ScheduledShowType.PpvMajor, 500L, 70f, 25)]
        [TestCase(ScheduledShowType.PpvMajor, 800L, 80f, 43)]
        [TestCase(ScheduledShowType.PpvMajor, 1000L, 100f, 60)]
        [TestCase(ScheduledShowType.PpvSignature, 500L, 70f, 50)]
        [TestCase(ScheduledShowType.PpvSignature, 800L, 80f, 85)]
        [TestCase(ScheduledShowType.PpvSignature, 1000L, 100f, 120)]
        [TestCase(ScheduledShowType.PpvSignature, 2000L, 100f, 120)]
        [TestCase(ScheduledShowType.PpvSignature, 0L, 100f, 0)]
        public void Bonus_UsesApprovedSuccessThresholdAndRanges(ScheduledShowType type, long attendance, float quality, int expected) =>
            Assert.That(Call("PpvBonus", type, attendance, 1000, quality), Is.EqualTo(expected));

        [Test]
        public void InvalidInputs_AreRejected()
        {
            Assert.Throws<InvalidOperationException>(() => Call("PpvBonus", ScheduledShowType.PpvRegular, 500L, 0, 80f));
            Assert.Throws<InvalidOperationException>(() => Call("PpvBonus", ScheduledShowType.PpvRegular, -1L, 1000, 80f));
            Assert.Throws<InvalidOperationException>(() => Call("PpvBonus", ScheduledShowType.PpvRegular, 500L, 1000, float.NaN));
        }

        [Test]
        public void CompletedPpv_PaysAtQuarterEndOnceAndDoesNotMutateDuringPreparation()
        {
            var (save, _) = Setup(); var before = JsonUtility.ToJson(save); var plan = new WrestlerCareerService().PrepareDue(save);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before)); plan.Apply(); plan.Apply();
            Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(18)); Assert.That(save.Promotion.AppliedPrestigeReasonIds, Does.Contain("ppv:show"));
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(18));
        }

        [Test]
        public void IncompleteOrIgnoredCancelledPpv_DoesNotEarnReward()
        {
            var (save, ctx) = Setup(completed: false);
            new WrestlerCareerService().PrepareDue(save, new[] { ctx.Show.Id }).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.Zero);
        }

        [Test]
        public void LastDayApplyingPpv_IsIncludedInClosingQuarter()
        {
            var (save, ctx) = Setup(completed: false); ctx.Show.Date = save.CurrentDate;
            new WrestlerCareerService().PrepareShow(ctx).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(18));
        }

        [Test]
        public void SameShowNewResultVersion_DoesNotEarnAnotherReward()
        {
            var (save, ctx) = Setup(); new WrestlerCareerService().PrepareDue(save).Apply();
            ctx.Show.ShowVersion = 2; ctx.Show.Date = new GameDate(2026, 9, 1);
            var second = JsonUtility.FromJson<ShowResultState>(JsonUtility.ToJson(ctx.ShowResult)); second.Id = "result2"; second.ShowVersion = 2;
            save.ShowResults.Add(second); save.CurrentDate = new GameDate(2026, 11, 30);
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(18));
        }

        [Test]
        public void LaterShowTypeChanges_DoNotRewriteSavedPpvBonus()
        {
            var (save, ctx) = Setup(); ctx.Show.ShowType = ScheduledShowType.PpvSignature;
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(18));
            Assert.That(ctx.ShowResult.PrestigeShowType, Is.EqualTo(ScheduledShowType.PpvRegular));
        }

        [Test]
        public void LegacyPpvWithoutSnapshot_DoesNotReceiveRetroactiveBonus()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = WrestlerCareerServiceTests.AddMatch(save);
            ctx.Show.ShowType = ScheduledShowType.PpvSignature; ctx.Show.Status = ShowStatus.Completed; save.CurrentDate = new GameDate(2026, 8, 31);
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.Zero);
        }

        [Test]
        public void OverflowOrCorruptSnapshot_RejectsBeforeChangingAnyState()
        {
            var (save, ctx) = Setup(); save.Promotion.PromotionPrestige = long.MaxValue; var before = JsonUtility.ToJson(save);
            Assert.Throws<OverflowException>(() => new WrestlerCareerService().PrepareDue(save)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            save.Promotion.PromotionPrestige = 0; ctx.ShowResult.ShowEvaluation.Score = 0; before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new WrestlerCareerService().PrepareDue(save)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void ResultBonus_ShowsSavedScoreAndPendingThenPaidState()
        {
            var (save, ctx) = Setup(); var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(root);
            var bind = typeof(DashboardController).GetMethod("BindPrestigeBonus", BindingFlags.Static | BindingFlags.NonPublic); Assert.That(bind, Is.Not.Null);
            var before = JsonUtility.ToJson(save); bind.Invoke(null, new object[] { root, save, ctx.ShowResult });
            var label = root.Q<Label>("result-prestige-bonus"); Assert.That(label, Is.Not.Null);
            Assert.That(label.text, Does.Contain("80")); Assert.That(label.text, Does.Contain("+18")); Assert.That(label.text, Does.Contain("분기 정산 예정"));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before)); new WrestlerCareerService().PrepareDue(save).Apply();
            bind.Invoke(null, new object[] { root, save, ctx.ShowResult }); Assert.That(label.text, Does.Contain("분기 정산 완료"));
            save.Schedules.Single(x => x.Id == ctx.Show.ScheduleId).Status = ScheduleStatus.Cancelled;
            bind.Invoke(null, new object[] { root, save, ctx.ShowResult }); Assert.That(label.style.display.value, Is.EqualTo(DisplayStyle.None));
            bind.Invoke(null, new object[] { root, save, null }); Assert.That(label.style.display.value, Is.EqualTo(DisplayStyle.None));
        }
    }
}
