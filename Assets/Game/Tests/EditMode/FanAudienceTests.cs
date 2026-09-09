using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using UnityEngine;
using UnityEngine.UIElements;
using System.Reflection;
using PWManager.Presentation;

namespace PWManager.Tests
{
    public sealed class FanAudienceTests
    {
        [Test]
        public void Dashboard_MountsExistingViewsInsideWorkspace()
        {
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree();
            var misplacedRoster = new VisualElement { name = "roster-content" };
            root.Add(misplacedRoster);

            DashboardViewHost.MountMissingViews(root);

            var workspace = root.Q("dashboard-workspace");
            Assert.That(misplacedRoster.parent, Is.Null);
            Assert.That(workspace.Contains(root.Q("roster-content")), Is.True);
        }

        [Test]
        public void Dashboard_ShowsPromotionFollowersAndMeasuredSatisfaction()
        {
            var ui = new GameObject("Fan Display Test");
            var host = new GameObject("Fan Controller Test");
            host.SetActive(false);
            try
            {
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                DashboardViewHost.MountMissingViews(document.rootVisualElement);
                var controller = host.AddComponent<DashboardController>();
                var save = ShowExecutionServiceTests.CreateSave();
                FanAudienceService.Initialize(save);
                save.Promotion.Audience.Mark.Satisfaction = 73;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DashboardController).GetField("document", flags).SetValue(controller, document);
                typeof(DashboardController).GetField("boundSave", flags).SetValue(controller, save);
                typeof(DashboardController).GetMethod("BindFanReaction", flags).Invoke(controller, null);
                Assert.That(document.rootVisualElement.Q<Label>("family-value").text, Is.EqualTo("875명 · 만족 73"));
                Assert.That(document.rootVisualElement.Q<Label>("fan-caption").text, Does.Contain("2,500명"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(ui); }
        }

        [Test]
        public void Reactions_ExpectationsAudiencesAndHeelAxesRemainIndependent()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            Assert.That(result.FanSatisfaction.IsCalculated, Is.True);
            Assert.That(save.MatchResults[0].FanReaction.IsCalculated, Is.True);
            Assert.That(result.FanSatisfaction.Mania, Is.Not.EqualTo(result.FanSatisfaction.Family));
            var before = result.FanSatisfaction.Mania;
            foreach (var w in save.Wrestlers)
            {
                w.FanReaction.Upgrade();
                w.FanReaction.Hardcore.Interest = 90;
                w.Roster.Alignment = KayfabeAlignment.Heel;
            }
            FanAudienceService.Evaluate(save, save.Shows[0], result, save.MatchResults, save.PromoResults);
            Assert.That(result.FanSatisfaction.Mania, Is.LessThan(before));
            Assert.That(result.FanResponseChanges[0].Mark.Preference, Is.LessThan(0));
            Assert.That(result.FanResponseChanges[0].Mark.Interest, Is.GreaterThan(0));
        }

        [Test]
        public void Apply_IsAtomicAndCannotRewardTheSameShowTwice()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            save.Shows[0].Status = ShowStatus.ResultsReviewed;
            var original = result.FanResponseChanges[0].Mark;
            result.FanResponseChanges[0].Mark.Interest = float.NaN;
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, result.Id));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            result.FanResponseChanges[0].Mark = original;
            FanAudienceService.Initialize(save);
            var group = save.Promotion.Audience.Hardcore;
            save.Promotion.Audience.Hardcore = null;
            before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, result.Id));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            save.Promotion.Audience.Hardcore = group;
            new ShowResultApplicationService().Apply(save, result.Id);
            before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, result.Id));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            Assert.That(save.Wrestlers[0].FanReaction.MarkResponse.Interest, Is.GreaterThan(0));
            Assert.That(save.Promotion.Audience.TotalFollowers, Is.EqualTo(2500), "A show supplies exposure; followers settle weekly.");
        }

        [TestCase(0)]
        [TestCase(50)]
        [TestCase(100)]
        public void WeeklyGrowth_IsBoundedAndIndependentOfAdvanceChunkSize(float satisfaction)
        {
            var save = new GameSave { Promotion = new PromotionState(), CurrentDate = new GameDate(2026, 6, 1) };
            FanAudienceService.Initialize(save);
            for (var g = 0; g < 3; g++)
            {
                var fans = FanAudienceService.Group(save.Promotion.Audience, g);
                fans.Satisfaction = satisfaction;
                fans.WeeklyExposure = 1.5f;
            }
            var daily = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(28));
            for (var day = 0; day < 28; day++) new TimeFlowService().AdvanceTo(daily, daily.CurrentDate.AddDays(1));
            Assert.That(JsonUtility.ToJson(daily.Promotion.Audience), Is.EqualTo(JsonUtility.ToJson(save.Promotion.Audience)));
            Assert.That(save.Promotion.Audience.TotalFollowers, Is.InRange(2350, 2550));
            var before = JsonUtility.ToJson(save.Promotion.Audience);
            FanAudienceService.AdvanceWeek(save, save.CurrentDate);
            Assert.That(JsonUtility.ToJson(save.Promotion.Audience), Is.EqualTo(before));
        }

        [Test]
        public void Demand_UsesFollowersPriceAndCompetitionInsteadOfCapacity()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var show = save.Shows[0];
            foreach (var item in save.ShowEvents) item.PlannedDuration = 45;
            var demand = FanAudienceService.BaseDemand(save, show, 30);
            var gym = TicketPricingRules.GetActualAttendance(demand, 30, 30, 500, 10000);
            var stadium = TicketPricingRules.GetActualAttendance(demand, 30, 30, 70000, 10000);
            Assert.That(stadium, Is.EqualTo(gym).And.InRange(300, 400));
            Assert.That(FanAudienceService.BaseDemand(save, show, 150), Is.LessThan(demand));
            FanAudienceService.Initialize(save);
            for (var g = 0; g < 3; g++) FanAudienceService.Group(save.Promotion.Audience, g).Satisfaction = 10;
            Assert.That(FanAudienceService.BaseDemand(save, show, 30), Is.LessThan(demand * .6));
            for (var g = 0; g < 3; g++) FanAudienceService.Group(save.Promotion.Audience, g).Satisfaction = 50;
            Assert.That(TicketPricingRules.GetActualAttendance(demand, 48, 30, 500, 10000), Is.LessThan(gym));
            Assert.That(TicketPricingRules.GetActualAttendance(demand, 18, 30, 500, 10000), Is.GreaterThan(gym));
            save.Shows.Add(new ShowState { Id = "previous", Date = show.Date.AddDays(-2), Status = ShowStatus.Completed });
            Assert.That(FanAudienceService.BaseDemand(save, show, 30), Is.LessThan(demand));
            FanAudienceService.Initialize(save);
            save.Promotion.Audience.Mark.Followers = save.Promotion.Audience.Casual.Followers = save.Promotion.Audience.Hardcore.Followers = 0;
            Assert.That(FanAudienceService.BaseDemand(save, show, 30), Is.Zero);
        }

        [Test]
        public void SaveLoad_MigratesOnceAndPreservesWeeklyProgressAndBackup()
        {
            var path = Path.Combine(Path.GetTempPath(), "PWManagerFans", Guid.NewGuid().ToString("N"));
            try
            {
                var save = ShowTestSaveFactory.Create(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"), 73);
                save.Promotion.Audience = null;
                var service = new SaveService(path);
                service.Save("fans", save);
                var original = File.ReadAllText(Path.Combine(path, "fans.json"));
                save = service.Load("fans");
                Assert.That(save.Promotion.Audience.TotalFollowers, Is.EqualTo(2500));
                Assert.That(File.ReadAllText(Path.Combine(path, "fans.json")), Is.EqualTo(original));
                save.Promotion.Audience.Mark.Followers = 888;
                save.Promotion.Audience.Mark.WeeklyExposure = .75f;
                save.Promotion.Audience.Mark.FollowerRemainder = .35;
                service.Save("fans", save);
                Assert.That(JsonUtility.ToJson(service.Load("fans").Promotion.Audience), Is.EqualTo(JsonUtility.ToJson(save.Promotion.Audience)));
                Assert.That(service.LoadBackup("fans").Promotion.Audience.TotalFollowers, Is.EqualTo(2500));
                save.Promotion.Audience.Mark.Satisfaction = float.NaN;
                Assert.Throws<InvalidDataException>(() => service.Save("fans", save));
                Assert.That(service.Load("fans").Promotion.Audience.Mark.Followers, Is.EqualTo(888));
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        [Test]
        public void RepeatedShows_CannotAccumulateUnlimitedExposureOrWrestlerInterest()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var show = save.Shows[0];
            var result = ShowExecutionServiceTests.Service().Execute(save, show.Id, 73, 500, 30);
            FanAudienceService.Apply(save, show, result);
            var first = save.Wrestlers[0].FanReaction.MarkResponse.Interest;
            for (var i = 0; i < 20; i++)
            {
                FanAudienceService.Evaluate(save, show, result, save.MatchResults, save.PromoResults);
                FanAudienceService.Apply(save, show, result);
            }
            Assert.That(save.Wrestlers[0].FanReaction.MarkResponse.Interest, Is.EqualTo(first));
            Assert.That(save.Promotion.Audience.Mark.WeeklyExposure, Is.EqualTo(1.5f));
        }

        [Test]
        public void Rest_RecoversOnlyElapsedHealthyDaysAndHonorsBlockedTime()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Shows.Clear(); save.Schedules.Clear(); save.Contracts.Clear();
            save.Wrestlers[0].Condition.Condition = 70;
            save.Wrestlers[1].Condition.Condition = 70;
            save.Wrestlers[1].Condition.InjuryStatus = (InjuryStatus)1;
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(2));
            Assert.That(save.Wrestlers[0].Condition.Condition, Is.EqualTo(76));
            Assert.That(save.Wrestlers[1].Condition.Condition, Is.EqualTo(70));
            new InboxService().Publish(save, "block", InboxMessageType.Decision, InboxPriority.Required, "test", "test", "test");
            Assert.That(new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(2)).State, Is.EqualTo(TimeFlowState.Blocked));
            Assert.That(save.Wrestlers[0].Condition.Condition, Is.EqualTo(76));
        }

        [TestCase(0f)]
        [TestCase(5f)]
        [TestCase(10f)]
        [TestCase(15f)]
        public void TwelveWeeks_RealBookingSettlementSalaryAndReload(float ability)
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var content = new StaticContentRegistry(catalog);
            var save = ShowTestSaveFactory.Create(catalog, 73);
            var kept = save.Wrestlers.Take(12).Select(x => x.Id).ToHashSet();
            save.Wrestlers.RemoveAll(x => !kept.Contains(x.Id));
            save.Contracts.RemoveAll(x => x.Type == ContractType.Wrestler && !kept.Contains(x.PersonId));
            if (ability > 0)
            {
                save.Transactions.Clear();
                foreach (var w in save.Wrestlers)
                    foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(w.Attributes, ability);
                // Fixed wages isolate programming quality from recruitment cost in this controlled scenario.
                foreach (var c in save.Contracts.Where(x => x.Type == ContractType.Wrestler)) c.MonthlySalary = 1000;
            }
            else save.Transactions.RemoveAll(x => !save.Contracts.Any(c => c.Id == x.ReasonId));
            save.Shows.Clear(); save.Schedules.Clear();
            var rows = new List<string> { "week,followers,satisfaction,attendance,revenue,cash,condition" };
            for (var week = 0; week < 12; week++)
            {
                var schedule = new ScheduleState { Id = Guid.NewGuid().ToString(), Date = save.CurrentDate, BookingDeadline = save.CurrentDate, Status = ScheduleStatus.Confirmed };
                save.Schedules.Add(schedule);
                var show = new ShowState { Id = Guid.NewGuid().ToString(), ScheduleId = schedule.Id, Date = save.CurrentDate,
                    VenueContractId = save.VenueContracts[0].Id, DurationLimit = 120, EstimatedCost = 4000, Name = "Weekly" };
                save.Shows.Add(show);
                var planning = new ShowPlanningService(content);
                for (var i = 0; i < 6; i++)
                    planning.AddMatch(save, show.Id, new MatchPlanState { MatchTypeId = "matchtype_001", MatchGimmickId = "gimmick_000",
                        ParticipantIds = { save.Wrestlers[i * 2].Id, save.Wrestlers[i * 2 + 1].Id },
                        WinnerId = save.Wrestlers[i * 2].Id, LoserTargetId = save.Wrestlers[i * 2 + 1].Id, FinishType = MatchFinishType.Pinfall }, 20);
                planning.Confirm(save, show.Id); show.Status = ShowStatus.InProgress;
                var result = new ShowExecutionService(planning, new MatchEvaluator(content, content), new PromoEvaluator()).Execute(save, show.Id, 73, 500, 30);
                show.Status = ShowStatus.ResultsReviewed;
                new ShowResultApplicationService().Apply(save, result.Id);
                var advance = new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(7));
                Assert.That(advance.State, Is.EqualTo(TimeFlowState.Completed));
                rows.Add(FormattableString.Invariant($"{week + 1},{save.Promotion.Audience.TotalFollowers},{result.FanSatisfaction.Mania:F2},{result.FinancialSettlement.Attendance},{result.FinancialSettlement.Revenue},{save.Promotion.CalculateCurrentCash(save.Transactions)},{save.Wrestlers.Average(w => w.Condition.Condition):F2}"));
                save = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
                if (ability == 0) Assert.That(GameSaveValidator.Validate(save), Is.Empty);
                Assert.That(save.Promotion.Audience.TotalFollowers, Is.InRange(1900, 3200));
            }
            Directory.CreateDirectory("artifacts/balance");
            File.WriteAllLines($"artifacts/balance/fans-12-weeks-{ability:0}.csv", rows);
            TestContext.WriteLine(rows.Last());
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.GreaterThan(0));
            Assert.That(save.Wrestlers.All(w => w.Condition.Condition == 100), Is.True, "Weekly 20-minute matches must allow recovery.");
            if (ability == 5) Assert.That(save.Promotion.Audience.TotalFollowers, Is.LessThan(2300));
            if (ability == 15) Assert.That(save.Promotion.Audience.TotalFollowers, Is.GreaterThan(2900));
        }
    }
}
