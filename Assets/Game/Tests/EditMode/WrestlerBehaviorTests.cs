using System;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class WrestlerBehaviorTests
    {
        [Test]
        public void FourAvailableShowsWithoutAppearance_CreateConcernEmotionAndPrivateRequest()
        {
            var save = Create(); var person = save.Wrestlers[0]; var service = new WrestlerBehaviorService(random: new FixedRandom(0));
            for (var i = 0; i < 4; i++) Observe(save, service, false);
            var state = save.WrestlerBehaviors.Single(); var concern = state.Concerns.Single();
            Assert.That(state.Observations, Has.Count.EqualTo(4));
            Assert.That(concern.Severity, Is.GreaterThan(0));
            Assert.That(person.Condition.Satisfaction, Is.LessThan(50));
            Assert.That(concern.Status, Is.EqualTo(WrestlerConcernStatus.AwaitingResponse));
            Assert.That(save.InboxMessages.Any(x => x.SourceEventId == "wrestler-request:" + concern.RequestId && x.Priority == InboxPriority.Important), Is.True);
            Assert.That(person.Hidden.Ambition, Is.EqualTo(20));
        }

        [Test]
        public void FewerThanFourShows_AndLegacyResults_DoNotInventComplaints()
        {
            var save = Create(); var service = Service();
            for (var i = 0; i < 3; i++) Observe(save, service, false);
            Assert.That(save.WrestlerBehaviors.Single().Concerns, Is.Empty);
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(50));
            var result = Observe(save, service, false, false);
            Assert.That(result.HasAvailabilitySnapshot, Is.False);
            Assert.That(save.WrestlerBehaviors.Single().Observations, Has.Count.EqualTo(3));
            Assert.That(save.WrestlerBehaviors.Single().Concerns, Is.Empty);
        }

        [Test]
        public void Professionalism_DoesNotEraseDissatisfaction_AndAssertivenessControlsPrivateRequests()
        {
            var low = Create(); low.Contracts[0].HasExpectedRole = true; low.Contracts[0].ExpectedRole = ExpectedRole.Midcarder;
            low.Wrestlers[0].Hidden = new() { Ambition = 1, Assertiveness = 1, Professionalism = 1 };
            var high = Copy(low); high.Wrestlers[0].Hidden.Assertiveness = 20; high.Wrestlers[0].Hidden.Professionalism = 20;
            foreach (var save in new[] { low, high }) for (var i = 0; i < 4; i++) Observe(save, Service(), false);
            Assert.That(low.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(high.Wrestlers[0].Condition.Satisfaction).And.LessThan(50));
            Assert.That(low.WrestlerBehaviors.Single().Concerns.Single().Status, Is.EqualTo(WrestlerConcernStatus.Watching));
            Assert.That(high.WrestlerBehaviors.Single().Concerns.Single().Status, Is.EqualTo(WrestlerConcernStatus.AwaitingResponse));
        }

        [Test]
        public void HigherAmbition_ExpectsMoreAppearanceAtTheSameContractRole()
        {
            var low = Create(); low.Contracts[0].HasExpectedRole = true; low.Contracts[0].ExpectedRole = ExpectedRole.Midcarder;
            low.Wrestlers[0].Hidden.Ambition = 1; var high = Copy(low); high.Wrestlers[0].Hidden.Ambition = 20;
            foreach (var save in new[] { low, high }) for (var i = 0; i < 4; i++) Observe(save, Service(), i < 2);
            Assert.That(low.WrestlerBehaviors.Single().Concerns, Is.Empty);
            Assert.That(high.WrestlerBehaviors.Single().Concerns.Single().Severity, Is.EqualTo(25));
        }

        [Test]
        public void SameResultAndSamePlan_AreIdempotent_AndDailyTicksDoNotRepeatTheMoodPenalty()
        {
            var save = Concern(); var result = save.ShowResults.Last(); var before = JsonUtility.ToJson(save);
            Service().PrepareShow(save, result, Array.Empty<string>()).Apply();
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            var mood = save.Wrestlers[0].Condition.Satisfaction;
            Service().ProcessDay(save); var after = JsonUtility.ToJson(save); Service().ProcessDay(save);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(after));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(mood));
        }

        [TestCase(0, WrestlerConcernStatus.PromiseActive, WrestlerBehaviorEventKind.PromiseAgreed)]
        [TestCase(.999, WrestlerConcernStatus.Watching, WrestlerBehaviorEventKind.PromiseRejected)]
        public void PromiseProposal_CanBeAcceptedOrRejected(double roll, WrestlerConcernStatus expected, WrestlerBehaviorEventKind kind)
        {
            var save = Concern(); Respond(save, new WrestlerBehaviorService(random: new FixedRandom(roll)));
            var state = save.WrestlerBehaviors.Single(); var concern = state.Concerns.Single();
            Assert.That(concern.Status, Is.EqualTo(expected)); Assert.That(state.Events.Last().Kind, Is.EqualTo(kind));
            Assert.That(concern.Promises.Count, Is.EqualTo(expected == WrestlerConcernStatus.PromiseActive ? 1 : 0));
            Assert.That(save.InboxMessages.Single(x => x.SourceEventId == "wrestler-request:" + concern.RequestId).Status, Is.EqualTo(InboxMessageStatus.Resolved));
        }

        [Test]
        public void Promise_IsKeptOnlyByRealAppearance_AndDoesNotEraseRemainingConcern()
        {
            var save = Concern(); Respond(save, Service()); var mood = save.Wrestlers[0].Condition.Satisfaction;
            Assert.That(save.WrestlerBehaviors.Single().Trust, Is.EqualTo(50));
            Observe(save, Service(), true); var state = save.WrestlerBehaviors.Single();
            Assert.That(state.Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Kept));
            Assert.That(state.Trust, Is.EqualTo(55)); Assert.That(state.Concerns.Single().Severity, Is.GreaterThan(0));
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.GreaterThan(mood));
            Assert.That(state.Events.Count(x => x.Kind == WrestlerBehaviorEventKind.PromiseKept), Is.EqualTo(1));
        }

        [Test]
        public void TwoMissedEligibleShows_BreakPromiseOnce_AndReloadPreservesTheDecision()
        {
            var save = Concern(); Respond(save, Service()); Observe(save, Service(), false);
            save = Copy(save); Observe(save, Service(), false); var state = save.WrestlerBehaviors.Single();
            Assert.That(state.Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Broken));
            Assert.That(state.Trust, Is.EqualTo(38)); Assert.That(state.BrokenPromises, Is.EqualTo(1));
            var mood = save.Wrestlers[0].Condition.Satisfaction; Service().ProcessDay(save); Service().ProcessDay(save);
            Assert.That(state.BrokenPromises, Is.EqualTo(1)); Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(mood));
        }

        [TestCase(0, WrestlerPromiseStatus.Unassessable, 50)]
        [TestCase(1, WrestlerPromiseStatus.Broken, 38)]
        public void Deadline_RequiresAnActualEligibleOpportunity(int missedShows, WrestlerPromiseStatus expected, int trust)
        {
            var save = Concern(); Respond(save, Service());
            if (missedShows > 0) Observe(save, Service(), false);
            save.CurrentDate = save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single().DueDate.AddDays(1);
            Service().ProcessDay(save); var state = save.WrestlerBehaviors.Single();
            Assert.That(state.Concerns.Single().Promises.Single().Status, Is.EqualTo(expected)); Assert.That(state.Trust, Is.EqualTo(trust));
        }

        [Test]
        public void UnappliedShowInPromiseWindow_DefersExpiryUntilItsResultIsKnown()
        {
            var save = Concern(); Respond(save, Service());
            var promise = save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single();
            var pending = new ShowState { Id = Guid.NewGuid().ToString(), Date = promise.StartDate.AddDays(7), ShowVersion = 1, Status = ShowStatus.ResultReview };
            save.Shows.Add(pending); save.CurrentDate = promise.DueDate.AddDays(1); Service().ProcessDay(save);
            Assert.That(save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Active));
            pending.Status = ShowStatus.ResultsReviewed;
            var result = new ShowResultState { Id = Guid.NewGuid().ToString(), ShowId = pending.Id, ShowVersion = 1,
                HasAvailabilitySnapshot = true, AvailableWrestlerIds = new() { save.Wrestlers[0].Id } };
            save.ShowResults.Add(result); Service().PrepareShow(save, result, new[] { save.Wrestlers[0].Id }).Apply();
            Assert.That(save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Kept));
        }

        [Test]
        public void IgnoringRequest_ChangesTrustOnceAndNeverBlocksTime()
        {
            var save = Concern(); Assert.That(new InboxService().GetBlockingMessage(save), Is.Null);
            var concern = save.WrestlerBehaviors.Single().Concerns.Single(); save.CurrentDate = concern.ResponseDueDate.Value.AddDays(1);
            Service().ProcessDay(save); save.CurrentDate = save.CurrentDate.AddDays(1); Service().ProcessDay(save);
            var state = save.WrestlerBehaviors.Single(); Assert.That(state.Trust, Is.EqualTo(45)); Assert.That(state.IgnoredRequests, Is.EqualTo(1));
            Assert.That(state.Events.Count(x => x.Kind == WrestlerBehaviorEventKind.Ignored), Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => Respond(save, Service()));
        }

        [Test]
        public void DecliningRequest_IsRecordedWithoutCreatingAFalsePromise()
        {
            var save = Concern(); var state = save.WrestlerBehaviors.Single();
            Service().Respond(save, state.WrestlerId, state.Concerns.Single().Id, WrestlerConcernResponse.Decline);
            state = save.WrestlerBehaviors.Single(); Assert.That(state.Trust, Is.EqualTo(48)); Assert.That(state.Concerns.Single().Promises, Is.Empty);
            Assert.That(state.Events.Last().Stance, Is.EqualTo(WrestlerInteractionStance.Disagree));
            Assert.Throws<InvalidOperationException>(() => Respond(save, Service()));
        }

        [Test]
        public void PreparingPlan_IsReadOnly_AndApplyingItMergesOtherNoticesKeysAndMoodChanges()
        {
            var save = Concern(); var person = save.Wrestlers[0]; var before = JsonUtility.ToJson(save);
            var plan = Service().PrepareDay(save, save.CurrentDate);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            var other = new InboxService().Publish(save, "salary-test", InboxMessageType.Information, InboxPriority.Normal, "급여", "지급", "지급");
            save.ProcessedIds.Add("other-key"); person.Condition.Satisfaction += 2;
            plan.Apply(); var after = JsonUtility.ToJson(save); plan.Apply();
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(after)); Assert.That(save.InboxMessages.Any(x => x.Id == other.Id), Is.True);
            Assert.That(save.ProcessedIds, Does.Contain("other-key")); Assert.That(person.Condition.Satisfaction, Is.EqualTo(52 - save.WrestlerBehaviors.Single().Concerns.Single().Severity * .15f).Within(.001));
        }

        [Test]
        public void WeeklyReport_ContainsOnlyRealEvents_AndLateMondayEventsReachNextWeek()
        {
            var save = Concern(); Assert.That(new DateTime(save.CurrentDate.Year, save.CurrentDate.Month, save.CurrentDate.Day).DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Service().ProcessDay(save); var report = save.InboxMessages.Single(x => x.SourceEventId.StartsWith("wrestler-weekly:"));
            Assert.That(report.Body, Does.Contain("4회")); Assert.That(report.TargetType, Is.EqualTo(InboxTargetType.WrestlerReport));
            var state = save.WrestlerBehaviors.Single(); Service().Respond(save, state.WrestlerId, state.Concerns.Single().Id, WrestlerConcernResponse.Decline);
            Service().ProcessDay(save); Assert.That(save.InboxMessages.Count(x => x.SourceEventId.StartsWith("wrestler-weekly:")), Is.EqualTo(1));
            save.CurrentDate = save.CurrentDate.AddDays(7); Service().ProcessDay(save);
            Assert.That(save.InboxMessages.Last(x => x.SourceEventId.StartsWith("wrestler-weekly:")).Body, Does.Contain("거절"));
            Assert.That(save.WrestlerBehaviors.Single().Events.All(x => x.IncludedInWeeklyReport), Is.True);
        }

        [Test]
        public void Transfer_ClosesOurPromiseWithoutPenalizingForeignEmployment()
        {
            var save = Concern(); Respond(save, Service()); var foreign = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Foreign" };
            save.OtherPromotions.Add(foreign); save.Wrestlers[0].PromotionId = foreign.Id; save.Contracts[0].Status = ContractStatus.Terminated;
            var foreignState = new WrestlerBehaviorState { PromotionId = foreign.Id, WrestlerId = save.Wrestlers[0].Id, Trust = 83 };
            save.WrestlerBehaviors.Add(foreignState); Service().ProcessDay(save);
            var own = save.WrestlerBehaviors.Single(x => x.PromotionId == save.Promotion.Id);
            Assert.That(own.Concerns.Single().Status, Is.EqualTo(WrestlerConcernStatus.Resolved)); Assert.That(own.Trust, Is.EqualTo(50));
            Assert.That(own.Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Unassessable));
            Assert.That(save.WrestlerBehaviors.Single(x => x.PromotionId == foreign.Id).Trust, Is.EqualTo(83));
            Assert.Throws<InvalidOperationException>(() => Respond(save, Service()));
        }

        [Test]
        public void ContractEndBeforeAConcern_ClearsOldObservationWindow()
        {
            var save = Create(); for (var i = 0; i < 3; i++) Observe(save, Service(), false);
            save.Contracts[0].Status = ContractStatus.Terminated; Service().ProcessDay(save);
            Assert.That(save.WrestlerBehaviors.Single().Observations, Is.Empty);
            Assert.That(save.WrestlerBehaviors.Single().MissedStreak, Is.Zero);
        }

        [Test]
        public void AvailabilitySnapshot_ExcludesInjuryAbsenceForeignAndPlayer()
        {
            var save = Create(); var person = save.Wrestlers[0]; Assert.That(WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate), Does.Contain(person.Id));
            person.Condition.InjuryStatus = InjuryStatus.Minor; Assert.That(WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate), Is.Empty);
            person.Condition.InjuryStatus = InjuryStatus.None; person.Condition.Availability = WrestlerAvailability.Limited;
            Assert.That(WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate), Is.Empty);
            person.Condition.Availability = WrestlerAvailability.Available; person.KayfabeInjury.Absent = true;
            Assert.That(WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate), Is.Empty);
            person.KayfabeInjury.Absent = false; save.Player = new() { WrestlerId = person.Id };
            Assert.That(WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate), Is.Empty);
            save.Player = null; person.PromotionId = Guid.NewGuid().ToString();
            Assert.That(WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate), Is.Empty);
        }

        [Test]
        public void MalformedStateAndDuplicateGeneratedIds_FailBeforeMutation()
        {
            var save = Concern(); save.WrestlerBehaviors.Single().Trust = float.NaN;
            Assert.Throws<InvalidOperationException>(() => Service().PrepareDay(save, save.CurrentDate));
            save.WrestlerBehaviors.Single().Trust = 50; var before = JsonUtility.ToJson(save);
            var duplicate = save.WrestlerBehaviors.Single().Concerns.Single().Id;
            Assert.Throws<InvalidOperationException>(() => new WrestlerBehaviorService(() => duplicate, new FixedRandom(0)).PrepareDay(save, save.CurrentDate));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void Legacy27File_InitializesEmptyBehaviorWithoutRetroactiveJudgment()
        {
            var save = Create(); save.SaveVersion = 27; var hidden = JsonUtility.ToJson(save.Wrestlers[0].Hidden);
            var json = JsonUtility.ToJson(save).Replace(",\"WrestlerBehaviors\":[]", "");
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-behavior-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "legacy.json"), json);
                var storage = new SaveService(directory); var restored = storage.Load("legacy");
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion)); Assert.That(restored.WrestlerBehaviors, Is.Empty);
                Assert.That(JsonUtility.ToJson(restored.Wrestlers[0].Hidden), Is.EqualTo(hidden));
                storage.Save("legacy", restored); Assert.That(storage.Load("legacy").WrestlerBehaviors, Is.Empty);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void ReportView_OffersRealResponseAndKeepsHiddenStatsPrivate()
        {
            var save = Concern(); var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                DashboardSession.SetActiveSave(save, true); var host = new VisualElement();
                var overlay = WrestlerBehaviorView.Open(host, save.Wrestlers[0].Id); Assert.That(overlay, Is.Not.Null);
                Assert.That(overlay.Q<Button>("wrestler-offer-promise"), Is.Not.Null);
                var text = string.Join("\n", overlay.Query<Label>().ToList().Select(x => x.text));
                Assert.That(text, Does.Contain("4회").And.Not.Contain("Trust").And.Not.Contain("Severity").And.Not.Contain("Assertiveness"));
                Click(overlay.Q<Button>("wrestler-decline-request"));
                Assert.That(save.WrestlerBehaviors.Single().Events.Last().Kind, Is.EqualTo(WrestlerBehaviorEventKind.Declined));
                Assert.That(overlay.Q<Button>("wrestler-decline-request"), Is.Null);
                Click(overlay.Q<Button>("wrestler-report-close")); Assert.That(host.Q("wrestler-report-overlay"), Is.Null);
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void ReportView_DoesNotExposeUnknownPlayersOrForeignPrivateHistory()
        {
            var save = Concern(); var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                DashboardSession.SetActiveSave(save, true); save.UndiscoveredWrestlerIds.Add(save.Wrestlers[0].Id);
                Assert.That(WrestlerBehaviorView.Open(new VisualElement(), save.Wrestlers[0].Id), Is.Null);
                save.UndiscoveredWrestlerIds.Clear(); save.WrestlerBehaviors.Clear(); save.Wrestlers[0].PromotionId = Guid.NewGuid().ToString();
                Assert.That(WrestlerBehaviorView.Open(new VisualElement(), save.Wrestlers[0].Id), Is.Null);
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void RealShowExecutionAndApplication_CaptureUnbookedPlayersAndCountActualAppearances()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            foreach (var wrestler in save.Wrestlers) { wrestler.PromotionId = save.Promotion.Id; wrestler.Roster.ActivityState = RosterActivityState.Active; }
            var standby = new WrestlerState { PromotionId = save.Promotion.Id, Identity = new() { Id = "standby", LegalName = "Standby" } };
            standby.Roster.ActivityState = RosterActivityState.Active; save.Wrestlers.Add(standby);
            var contract = save.Contracts[0].Copy(); contract.Id = "standby-contract"; contract.PersonId = standby.Id; save.Contracts.Add(contract);
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            Assert.That(result.HasAvailabilitySnapshot, Is.True); Assert.That(result.AvailableWrestlerIds, Is.EquivalentTo(new[] { "a", "b", "standby" }));
            Assert.That(save.WrestlerBehaviors, Is.Empty);
            save.Shows[0].Status = ShowStatus.ResultsReviewed; new ShowResultApplicationService().Apply(save, result.Id);
            Assert.That(save.WrestlerBehaviors.Single(x => x.WrestlerId == "a").Observations.Single().Appeared, Is.True);
            Assert.That(save.WrestlerBehaviors.Single(x => x.WrestlerId == "standby").Observations.Single().Appeared, Is.False);
            Assert.That(save.InboxMessages.Any(x => x.SourceEventId.StartsWith("show-result-applied:")), Is.True);
        }

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button.clickable, new object[] { null });
        [Test]
        public void CancelledOnlyBooking_DoesNotBecomeAnOmissionOrPromiseOpportunity()
        {
            var save = Concern(); Respond(save, Service()); var show = QueueShow(save);
            var match = new MatchResultState { Id = Guid.NewGuid().ToString(), ShowId = show.ShowId, ShowVersion = 1, CancellationReason = "상대 부상으로 취소",
                ParticipantProfiles = new() { new() { MemberIds = new() { save.Wrestlers[0].Id } } } };
            save.MatchResults.Add(match); show.MatchResultIds.Add(match.Id);
            var before = save.WrestlerBehaviors.Single().MissedStreak;
            Service().PrepareShow(save, show, Array.Empty<string>()).Apply();
            var state = save.WrestlerBehaviors.Single(); Assert.That(state.MissedStreak, Is.EqualTo(before));
            Assert.That(state.Concerns.Single().Promises.Single().EligibleShows, Is.Zero);
            Assert.That(state.Observations.Any(x => x.ShowId == show.ShowId), Is.False);
            // An actual promo still counts even when the same person's match was cancelled.
            var next = QueueShow(save); next.MatchResultIds.Add(match.Id);
            Service().PrepareShow(save, next, new[] { save.Wrestlers[0].Id }).Apply();
            Assert.That(save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Kept));
        }

        [Test]
        public void ReplayBeforeApplyingSameShow_KeepsPromiseDecisionDespiteNewRecordIds()
        {
            var save = Create(); save.Wrestlers[0].Id = "00000000-0000-0000-0000-000000000071"; save.Contracts[0].PersonId = save.Wrestlers[0].Id;
            for (var i = 0; i < 3; i++) Observe(save, Service(), false);
            var result = QueueShow(save); var replay = Copy(save); var firstId = 0; var secondId = 200;
            new WrestlerBehaviorService(() => TestId(++firstId), new FixedRandom(0)).PrepareShow(save, result, Array.Empty<string>()).Apply();
            new WrestlerBehaviorService(() => TestId(++secondId), new FixedRandom(0)).PrepareShow(replay, replay.ShowResults.Last(), Array.Empty<string>()).Apply();
            save.Shows.Last().Status = replay.Shows.Last().Status = ShowStatus.Completed;
            Respond(save, new WrestlerBehaviorService()); Respond(replay, new WrestlerBehaviorService());
            Assert.That(save.WrestlerBehaviors.Single().Concerns.Single().Status, Is.EqualTo(replay.WrestlerBehaviors.Single().Concerns.Single().Status));
        }

        [Test]
        public void DuplicateInboxId_IsRejectedWithoutLosingAnUnreportedObservation()
        {
            var save = Concern(); var before = JsonUtility.ToJson(save); var duplicate = save.InboxMessages[0].Id;
            Assert.Throws<InvalidOperationException>(() => new WrestlerBehaviorService(() => duplicate).PrepareDay(save, save.CurrentDate));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void PreparedBehavior_CannotCommitAnIdAllocatedByAnotherDaySystem()
        {
            var save = Concern(); var plan = Service().PrepareDay(save, save.CurrentDate);
            var stateBefore = JsonUtility.ToJson(save.WrestlerBehaviors.Single());
            save.SalaryPayments.Add(new() { Id = plan.NewRecordIds.Last(), ContractId = save.Contracts[0].Id, DueDate = save.CurrentDate, Amount = 1, RemainingAmount = 1 });
            Assert.Throws<InvalidOperationException>(() => plan.Apply());
            Assert.That(JsonUtility.ToJson(save.WrestlerBehaviors.Single()), Is.EqualTo(stateBefore));
        }

        [Test]
        public void CurrentSnapshotWithNullIdList_IsRejectedAtValidation()
        {
            var save = Create(); var result = QueueShow(save); result.AvailableWrestlerIds = null;
            Assert.Throws<InvalidOperationException>(() => WrestlerBehaviorService.ValidateState(save));
        }

        [Test]
        public void TimeFlow_ExpiresPrivateRequestWithoutBlockingOrDroppingDailyNotices()
        {
            var save = Concern(); var result = new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(9));
            Assert.That(result.State, Is.EqualTo(TimeFlowState.Completed));
            Assert.That(save.WrestlerBehaviors.Single().IgnoredRequests, Is.EqualTo(1));
            Assert.That(save.InboxMessages.Any(x => x.SourceEventId.StartsWith("daily-brief:")), Is.True);
            Assert.That(save.InboxMessages.Any(x => x.SourceEventId.StartsWith("wrestler-weekly:")), Is.True);
        }

        [Test]
        public void ActivePromise_RealSaveLoadPreservesHistoryAndContinuesItsActualJudgment()
        {
            var save = Concern(); Respond(save, Service());
            var venue = new VenueContractState { Id = Guid.NewGuid().ToString(), VenueId = "venue_001" }; save.VenueContracts.Add(venue);
            foreach (var show in save.Shows)
            {
                var schedule = new ScheduleState { Id = Guid.NewGuid().ToString(), Date = show.Date, BookingDeadline = show.Date, Status = ScheduleStatus.Completed };
                save.Schedules.Add(schedule); show.ScheduleId = schedule.Id; show.VenueContractId = venue.Id; show.DurationLimit = 60;
            }
            var expected = JsonUtility.ToJson(save.WrestlerBehaviors.Single());
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-promise-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new SaveService(directory); storage.Save("promise", save); var restored = storage.Load("promise");
                Assert.That(JsonUtility.ToJson(restored.WrestlerBehaviors.Single()), Is.EqualTo(expected));
                Observe(restored, Service(), true);
                Assert.That(restored.WrestlerBehaviors.Single().Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Kept));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void ProfileAndInbox_OpenTheSameActionablePersonalReport()
        {
            var save = Concern(); var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Personal report navigation"); go.SetActive(false);
            try
            {
                DashboardSession.SetActiveSave(save, true); var controller = go.AddComponent<WrestlerProfileController>();
                var profile = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { profile });
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { save.Wrestlers[0] });
                Click(profile.Q<Button>("wp-open-report")); Assert.That(profile.Q<Button>("wrestler-decline-request"), Is.Not.Null);
                var dashboard = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree();
                DashboardViewHost.MountMissingViews(dashboard);
                var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardInboxController");
                var inbox = Activator.CreateInstance(type, new object[] { dashboard, (Action)(() => { }), (Action<string>)(_ => { }) });
                type.GetMethod("Bind").Invoke(inbox, new object[] { save }); Click(dashboard.Q<Button>("message-open-target"));
                Assert.That(dashboard.Q<Button>("wrestler-decline-request"), Is.Not.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void RunningShow_AllowsReadingAndClosingReportButDisablesResponses()
        {
            var save = Concern(); save.Shows.Last().Status = ShowStatus.InProgress;
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Personal report running-show guard"); go.SetActive(false);
            try
            {
                DashboardSession.SetActiveSave(save, true); var controller = go.AddComponent<DashboardController>();
                var host = new VisualElement(); var open = new Button { name = "wp-open-report" }; host.Add(open);
                var report = WrestlerBehaviorView.Open(host, save.Wrestlers[0].Id);
                Assert.That(report.Q<Button>("wrestler-offer-promise").enabledInHierarchy, Is.False);
                var guard = typeof(DashboardController).GetMethod("BlockShowInteraction", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var target in new[] { open, report.Q<Button>("wrestler-report-close") })
                    using (var evt = ClickEvent.GetPooled())
                    {
                        evt.target = target; guard.Invoke(controller, new object[] { evt });
                        Assert.That(evt.isPropagationStopped, Is.False, target.name);
                    }
                Assert.Throws<InvalidOperationException>(() => Respond(save, Service()));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        [TestCase(false, WrestlerPromiseStatus.Unassessable)]
        [TestCase(true, WrestlerPromiseStatus.Broken)]
        public void LastPendingShowWithoutThePlayer_SettlesDeferredPromiseImmediately(bool earlierOpportunity, WrestlerPromiseStatus expected)
        {
            var save = Concern(); Respond(save, Service()); if (earlierOpportunity) Observe(save, Service(), false);
            var result = QueueShow(save); result.AvailableWrestlerIds.Clear();
            var promise = save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single(); save.CurrentDate = promise.DueDate.AddDays(1);
            Service().ProcessDay(save); Assert.That(save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Active));
            Service().PrepareShow(save, result, Array.Empty<string>()).Apply();
            Assert.That(save.WrestlerBehaviors.Single().Concerns.Single().Promises.Single().Status, Is.EqualTo(expected));
        }

        private static string TestId(int n) => "00000000-0000-0000-0000-" + n.ToString("D12");
        private static WrestlerBehaviorService Service() => new(random: new FixedRandom(0));
        private static GameSave Copy(GameSave save) => JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
        private static GameSave Concern() { var save = Create(); for (var i = 0; i < 4; i++) Observe(save, Service(), false); return save; }
        private static void Respond(GameSave save, WrestlerBehaviorService service)
        { var state = save.WrestlerBehaviors.Single(x => x.PromotionId == save.Promotion.Id); service.Respond(save, state.WrestlerId, state.Concerns.Single().Id, WrestlerConcernResponse.OfferAppearancePromise); }

        private static ShowResultState Observe(GameSave save, WrestlerBehaviorService service, bool appeared, bool snapshot = true)
        {
            var result = QueueShow(save, snapshot);
            service.PrepareShow(save, result, appeared ? new[] { save.Wrestlers[0].Id } : Array.Empty<string>()).Apply();
            save.Shows.Last().Status = ShowStatus.Completed; return result;
        }

        private static ShowResultState QueueShow(GameSave save, bool snapshot = true)
        {
            save.CurrentDate = save.CurrentDate.AddDays(7);
            var show = new ShowState { Id = Guid.NewGuid().ToString(), ShowVersion = 1, Date = save.CurrentDate, Status = ShowStatus.ResultsReviewed };
            var result = new ShowResultState { Id = Guid.NewGuid().ToString(), ShowId = show.Id, ShowVersion = 1,
                HasAvailabilitySnapshot = snapshot, AvailableWrestlerIds = snapshot ? new() { save.Wrestlers[0].Id } : new() };
            save.Shows.Add(show); save.ShowResults.Add(result);
            return result;
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly double value; public FixedRandom(double value) => this.value = value;
            public override double NextDouble() => value;
        }

        private static GameSave Create()
        {
            var save = GameSave.CreateNew(71, DateTime.UtcNow);
            save.Promotion = new() { Id = Guid.NewGuid().ToString(), Name = "Behavior Test", InitialCash = 10000 };
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
            var person = new WrestlerGenerator(new StaticContentRegistry(catalog), 71).GenerateCandidate(WrestlerGender.Male, save.CurrentDate);
            person.PromotionId = save.Promotion.Id; person.Roster.ActivityState = RosterActivityState.Active; person.Identity.ExpiryDate = default;
            person.Hidden = new() { Ambition = 20, Assertiveness = 20 }; person.Condition.Satisfaction = 50;
            save.Wrestlers.Add(person);
            save.Contracts.Add(new() { Id = Guid.NewGuid().ToString(), PersonId = person.Id, PromotionId = save.Promotion.Id,
                Type = ContractType.Wrestler, StartDate = save.CurrentDate, EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active });
            return save;
        }
    }
}
