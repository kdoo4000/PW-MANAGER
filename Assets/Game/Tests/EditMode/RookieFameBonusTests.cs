using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using UnityEngine;
using PWManager.Presentation;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class RookieFameBonusTests
    {
        private static void RookieHire(ContractState contract, bool value)
        {
            var field = typeof(ContractState).GetField("WasRookieWhenHired"); Assert.That(field, Is.Not.Null); field.SetValue(contract, value);
        }
        private static int Bonus(WrestlerCareerEventState record)
        {
            var field = record.GetType().GetField("FamePromotionBonus"); Assert.That(field, Is.Not.Null); return (int)field.GetValue(record);
        }
        private static (GameSave Save, ResultApplicationContext Context, WrestlerState Wrestler) Setup(bool rookieHire = true, long fame = 4999)
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = WrestlerCareerServiceTests.AddMatch(save); var w = save.Wrestlers[0];
            w.Fame = WrestlerFameRules.Create(fame); w.Status.IsRookie = false;
            var contract = new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = w.Id, Type = ContractType.Wrestler,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active };
            RookieHire(contract, rookieHire); save.Contracts.Add(contract);
            ctx.ShowResult.HasFameSnapshot = true; ctx.ShowResult.FameReach = 10000; save.MatchResults[0].ActualMatchDuration = 10;
            return (save, ctx, w);
        }
        private static WrestlerCareerEventState FameEvent(GameSave save, WrestlerState w) => save.CareerEvents.Single(x => x.WrestlerId == w.Id && x.Kind == CareerEventKind.Fame);

        [TestCase(true, 20)]
        [TestCase(false, 10)]
        public void WorldMilestone_UsesHiringSnapshotAfterRookieGraduation(bool rookieHire, int expected)
        {
            var (save, ctx, w) = Setup(rookieHire); new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(Bonus(FameEvent(save, w)), Is.EqualTo(expected)); Assert.That(save.Promotion.PromotionPrestige, Is.Zero);
        }

        [Test]
        public void FamousMilestone_AddsFiveAndDoesNotSpendRookieWorldBonus()
        {
            var (save, ctx, w) = Setup(fame: 499); new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(Bonus(FameEvent(save, w)), Is.EqualTo(5)); Assert.That(w.Fame.HighestMilestone, Is.EqualTo(1));
        }

        [Test]
        public void BothThresholdsInOneShow_CombineEachFirstBonusOnlyOnce()
        {
            var (save, ctx, w) = Setup(fame: 499); ctx.ShowResult.FameReach = 1000000000;
            new WrestlerCareerService().PrepareShow(ctx).Apply(); Assert.That(Bonus(FameEvent(save, w)), Is.EqualTo(25));
            save.CurrentDate = new GameDate(2026, 8, 31); new WrestlerCareerService().PrepareDue(save).Apply();
            Assert.That(Bonus(FameEvent(save, save.Wrestlers[1])), Is.EqualTo(15));
            Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(40)); Assert.That(save.Promotion.AppliedPrestigeReasonIds, Has.Count.EqualTo(2));
        }

        [Test]
        public void DuplicatePaidReason_IsRejectedWithoutChangingAnything()
        {
            var (save, ctx, w) = Setup(); new WrestlerCareerService().PrepareShow(ctx).Apply();
            var reason = FameEvent(save, w).Id; save.Promotion.AppliedPrestigeReasonIds.Add(reason); save.Promotion.AppliedPrestigeReasonIds.Add(reason);
            save.CurrentDate = new GameDate(2026, 8, 31); var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new WrestlerCareerService().PrepareDue(save)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void OutsidePromotion_DoesNotEarnPromotionMilestoneBonus()
        {
            var (save, ctx, w) = Setup(); w.PromotionId = null; new WrestlerCareerService().PrepareShow(ctx).Apply();
            Assert.That(Bonus(FameEvent(save, w)), Is.Zero);
        }

        [Test]
        public void QuarterClose_PaysOnceAfterReleaseAndPreparesWithoutMutation()
        {
            var (save, ctx, w) = Setup(); new WrestlerCareerService().PrepareShow(ctx).Apply();
            w.Roster.ActivityState = RosterActivityState.Released; save.CurrentDate = new GameDate(2026, 8, 31);
            var before = JsonUtility.ToJson(save); var plan = new WrestlerCareerService().PrepareDue(save);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before)); plan.Apply(); plan.Apply();
            Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(20));
            Assert.That(save.Promotion.AppliedPrestigeReasonIds, Does.Contain(FameEvent(save, w).Id));
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(20));
        }

        [Test]
        public void LastDayShow_DefersCloseUntilResultAndIncludesItsMilestone()
        {
            var (save, ctx, w) = Setup(); save.CurrentDate = new GameDate(2026, 8, 31); ctx.Show.Date = save.CurrentDate;
            save.LastCareerQuarterEnd = new(new GameDate(2026, 5, 31));
            new WrestlerCareerService().PrepareDue(save).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.Zero);
            new WrestlerCareerService().PrepareShow(ctx).Apply(); Assert.That(save.Promotion.PromotionPrestige, Is.EqualTo(20));
        }

        [Test]
        public void QuarterOverflow_LeavesPrestigeReasonsAndOtherStateUntouched()
        {
            var (save, ctx, _) = Setup(); new WrestlerCareerService().PrepareShow(ctx).Apply();
            save.Promotion.PromotionPrestige = long.MaxValue; save.CurrentDate = new GameDate(2026, 8, 31); var before = JsonUtility.ToJson(save);
            Assert.Throws<OverflowException>(() => new WrestlerCareerService().PrepareDue(save));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void Migration_RecognizesProvenRookieHireWithoutBackfillingOldMilestoneBonus()
        {
            var (save, ctx, w) = Setup(); new WrestlerCareerService().PrepareShow(ctx).Apply(); RookieHire(save.Contracts[0], false);
            foreach (var record in save.CareerEvents)
            {
                record.GetType().GetField("HasPromotionFameBonus")?.SetValue(record, false);
                record.GetType().GetField("FameRookieDevelopment")?.SetValue(record, false);
                record.GetType().GetField("FamePromotionBonus")?.SetValue(record, 0);
            }
            w.Identity.Background = WrestlerBackground.Rookie; w.Identity.CreatedDate = save.Contracts[0].StartDate;
            var migration = typeof(SaveService).Assembly.GetType("PWManager.Infrastructure.Save.SaveVersion9To10Migration"); Assert.That(migration, Is.Not.Null);
            save.SaveVersion = 9; ((ISaveMigration)Activator.CreateInstance(migration)).Migrate(save);
            Assert.That((bool)typeof(ContractState).GetField("WasRookieWhenHired").GetValue(save.Contracts[0]), Is.True);
            Assert.That(save.SaveVersion, Is.EqualTo(10)); Assert.That(save.Promotion.AppliedPrestigeReasonIds, Is.Empty);
            Assert.That(Bonus(FameEvent(save, w)), Is.Zero);
        }

        [Test]
        public void History_ShowsPendingThenPaidBonusWithoutChangingSave()
        {
            var (save, ctx, w) = Setup(); new WrestlerCareerService().PrepareShow(ctx).Apply();
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient; var host = new VisualElement();
            try
            {
                DashboardSession.SetActiveSave(save, transient: true); var before = JsonUtility.ToJson(save);
                var overlay = WrestlerCareerView.Open(host, w.Id);
                string Text(VisualElement element) => string.Join(" ", element.Query<Label>().ToList().Select(x => x.text));
                Assert.That(Text(overlay), Does.Contain("신인 육성 보너스 +10")); Assert.That(Text(overlay), Does.Contain("분기 정산 예정"));
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
                save.CurrentDate = new GameDate(2026, 8, 31); new WrestlerCareerService().PrepareDue(save).Apply(); before = JsonUtility.ToJson(save);
                overlay = WrestlerCareerView.Open(host, w.Id); Assert.That(Text(overlay), Does.Contain("분기 정산 완료"));
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }
    }
}
