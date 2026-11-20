using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine.UIElements;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class SeasonTransitionViewTests
    {
        [Test]
        public void LegacyRecovery_CancelledRunningShowDoesNotBlockContractManagement()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient; var save = CreateSave();
            save.CurrentDate = new GameDate(2027, 7, 15);
            var schedule = new ScheduleState { Id = Id(), Date = new GameDate(2027, 5, 31), Status = ScheduleStatus.Confirmed }; save.Schedules.Add(schedule);
            save.Shows.Add(new ShowState { Id = Id(), ScheduleId = schedule.Id, Date = schedule.Date, Status = ShowStatus.InProgress });
            var settings = SeasonTransitionService.DefaultSettings(save); settings.CancelMissedShows = true;
            new SeasonTransitionService().ConfirmNextSeason(save, settings);
            var wrestler = new WrestlerState { Id = Id(), PromotionId = save.Promotion.Id, Condition = new WrestlerConditionState { Satisfaction = 50 } }; save.Wrestlers.Add(wrestler);
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                Assert.That(DashboardSession.IsShowRunning, Is.False);
                var demand = ContractNegotiationService.Demand(save, wrestler.Id, ContractType.Wrestler);
                Assert.That(new ContractNegotiationService(random: new System.Random(1)).Submit(save, wrestler.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary).Accepted, Is.True);
                Assert.That(new ContractTerminationService().Submit(save, wrestler.Id, ContractType.Wrestler, false).Accepted, Is.True);
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void LegacyRecovery_RunningShowGuardAllowsRecoveryDialogButtons()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient; var save = CreateSave();
            save.CurrentDate = new GameDate(2027, 7, 15);
            var schedule = new ScheduleState { Id = Id(), Date = new GameDate(2027, 5, 31), Status = ScheduleStatus.Confirmed }; save.Schedules.Add(schedule);
            save.Shows.Add(new ShowState { Id = Id(), ScheduleId = schedule.Id, Date = schedule.Date, Status = ShowStatus.InProgress });
            var go = new GameObject("Season guard test"); go.SetActive(false);
            var controller = go.AddComponent<DashboardController>();
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var overlay = SeasonTransitionView.Open(new VisualElement()); Assert.That(overlay, Is.Not.Null);
                var guard = typeof(DashboardController).GetMethod("BlockShowInteraction", BindingFlags.Instance | BindingFlags.NonPublic);
                using (var evt = ClickEvent.GetPooled())
                {
                    evt.target = overlay.Q<Button>("season-confirm"); guard.Invoke(controller, new object[] { evt });
                    Assert.That(evt.isPropagationStopped, Is.False);
                }
                using (var evt = ClickEvent.GetPooled())
                {
                    evt.target = new Button { name = "unrelated" }; guard.Invoke(controller, new object[] { evt });
                    Assert.That(evt.isPropagationStopped, Is.True);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void CopiedSettings_CanBeEditedAndConfirmedWithoutChangingPastPolicy()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient; var save = CreateSave(); var old = save.SeasonPolicy;
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var host = new VisualElement(); var overlay = SeasonTransitionView.Open(host);
                Assert.That(overlay, Is.Not.Null);
                Assert.That(overlay.Q<TextField>("season-regular-name").value, Is.EqualTo("지난 정기 쇼"));
                overlay.Q<TextField>("season-regular-name").value = "새 정기 쇼";
                Assert.That(old.RegularShowName, Is.EqualTo("지난 정기 쇼"));
                Click(overlay.Q<Button>("season-confirm"));
                var sponsors = host.Q("sponsor-overlay");
                Assert.That(save.SeasonPolicy.Id, Is.EqualTo(old.Id));
                foreach (var row in sponsors.Q("sponsor-offers").Query<Button>().ToList().Take(3))
                {
                    Click(row);
                }
                Click(sponsors.Q<Button>("sponsor-confirm"));
                Assert.That(save.SeasonPolicy.Id, Is.Not.EqualTo(old.Id));
                Assert.That(save.SeasonPolicy.RegularShowName, Is.EqualTo("새 정기 쇼"));
                Assert.That(save.SeasonResults.Single().Policy.RegularShowName, Is.EqualTo("지난 정기 쇼"));
                var count = save.Shows.Count; Click(overlay.Q<Button>("season-confirm")); Assert.That(save.Shows, Has.Count.EqualTo(count));
                Assert.That(overlay.Q<Label>("season-result").text, Does.Contain("완료"));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void LastDayUnfinishedShow_CannotOpenSeasonConfiguration()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient; var save = CreateSave();
            save.Schedules.Add(new ScheduleState { Id = Id(), Date = save.CurrentDate, Status = ScheduleStatus.Confirmed });
            save.Shows.Add(new ShowState { Id = Id(), ScheduleId = save.Schedules[0].Id, Date = save.CurrentDate, Status = ShowStatus.Draft });
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                Assert.That(SeasonTransitionView.Open(new VisualElement()), Is.Null); Assert.That(save.SeasonResults, Is.Empty);
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });

        [Test]
        public void SeasonTitleEditing_PreservesCancelledPastShowAndRestartsSequence()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient; var save = CreateSave();
            var schedule = new ScheduleState { Id = Id(), Date = new GameDate(2026, 6, 6), Status = ScheduleStatus.Cancelled }; save.Schedules.Add(schedule);
            var past = new ShowState { Id = Id(), ScheduleId = schedule.Id, Date = schedule.Date, Name = "지난 취소 쇼",
                VenueContractId = save.VenueContracts[0].Id, DurationLimit = 120 }; save.Shows.Add(past);
            new SeasonTransitionService().ConfirmNextSeason(save, SeasonTransitionService.DefaultSettings(save));
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/DashboardSchedule").CloneTree();
                var type = typeof(SeasonTransitionView).Assembly.GetType("PWManager.Presentation.DashboardScheduleController");
                var controller = Activator.CreateInstance(type, new object[] { root, new Func<ShowState, string>(_ => "Studio"), new Action<string>(_ => { }) });
                type.GetMethod("Render").Invoke(controller, new object[] { save });
                Click(root.Q<Button>("schedule-regular-title-edit")); root.Q<TextField>("schedule-regular-title-field").value = "바뀐 쇼";
                Click(root.Q<Button>("schedule-title-save"));
                Assert.That(past.Name, Is.EqualTo("지난 취소 쇼"));
                Assert.That(save.Shows.Where(x => x.Date.CompareTo(save.SeasonPolicy.StartDate) >= 0 && x.ShowType == ScheduledShowType.Regular)
                    .OrderBy(x => x.Date).First().Name, Does.EndWith("#1"));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }
        private static string Id() => Guid.NewGuid().ToString("D");
        private static GameSave CreateSave()
        {
            var save = GameSave.CreateNew(15, DateTime.UtcNow); save.CurrentDate = new GameDate(2027, 5, 31);
            save.Promotion = new PromotionState { Id = Id(), Name = "Test", Abbreviation = "TEST", InitialCash = 100000 };
            var venue = new VenueContractState { Id = Id(), VenueId = "venue_001", ProductionCost = 250,
                StartDate = new GameDate(2026, 6, 1), EndDate = save.CurrentDate, Status = VenueContractStatus.Reserved }; save.VenueContracts.Add(venue);
            save.SeasonPolicy = new SeasonPolicyState { Id = Id(), StartDate = venue.StartDate, EndDate = venue.EndDate,
                RegularVenueContractId = venue.Id, RegularShowName = "지난 정기 쇼", RegularShowDurationMinutes = 120,
                RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.Quarterly };
            save.Promotion.SeasonPolicyId = save.SeasonPolicy.Id; return save;
        }
    }
}
