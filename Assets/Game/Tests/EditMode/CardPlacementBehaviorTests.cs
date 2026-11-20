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
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class CardPlacementBehaviorTests
    {
        [Test]
        public void FourActualOpeningMatches_CreateTreatmentConcernDespiteRegularAppearance()
        {
            var save = Create(); var service = Execution();
            for (var i = 0; i < 4; i++)
            {
                var result = Execute(save, service); save.Shows.Last().Status = ShowStatus.ResultsReviewed;
                new ShowResultApplicationService().Apply(save, result.Id);
            }
            var state = save.WrestlerBehaviors.Single(x => x.WrestlerId == save.Wrestlers[0].Id);
            Assert.That(state.Observations.All(x => x.Appeared), Is.True);
            Assert.That(state.Concerns, Has.Count.EqualTo(1), "정규 출연이어도 반복된 낮은 카드에 별도의 대우 문제가 생겨야 합니다.");
            Assert.That(save.Wrestlers[0].Condition.Satisfaction, Is.LessThan(50));
        }

        [Test]
        public void RoleAtExecution_IsPreservedWhenContractChangesBeforeApplication()
        {
            var save = Create(); var result = Pending(save); save.Contracts[0].ExpectedRole = ExpectedRole.Jobber;
            Apply(save, result); Assert.That(State(save).CardObservations.Single().ExpectedRole, Is.EqualTo(ExpectedRole.MainEventer));
        }

        [Test]
        public void RealRepeatedAppearance_InTagMainEvent_UsesTheHighestActualPositionOnce()
        {
            var save = Create(); var show = Queue(save); var main = save.MatchPlans.Last();
            main.MatchTypeId = "matchtype_002"; main.TeamCount = 2; main.MembersPerTeam = 2;
            main.ParticipantIds = new() { save.Wrestlers[4].Id, save.Wrestlers[0].Id, save.Wrestlers[5].Id, save.Wrestlers[1].Id };
            main.WinnerId = save.Wrestlers[4].Id; main.LoserTargetId = save.Wrestlers[5].Id;
            var result = Execution().Execute(save, show.Id, 73, 1000, 30);
            var hero = result.CardPlacements.Single(p => p.WrestlerId == save.Wrestlers[0].Id);
            Assert.That(hero.MainEvent && hero.UpperCard && hero.HasMatchAppearance, Is.True);
            show.Status = ShowStatus.ResultsReviewed; new ShowResultApplicationService().Apply(save, result.Id);
            Assert.That(State(save).CardObservations, Has.Count.EqualTo(1));
            Assert.That(State(save).CardObservations.Single().MainEvent, Is.True);
        }

        [TestCase(1, false, false)]
        [TestCase(3, false, false)]
        [TestCase(3, true, true)]
        public void LastMatchDefinesCardPosition_ClosingPromoAndCancellationDoNotPromoteIt(int matches, bool cancelLast, bool closingPromo)
        {
            var save = Create(); var result = Pending(save, position: 0, matches: matches, closingPromo: closingPromo, cancelLast: cancelLast);
            var placement = result.CardPlacements.Single(p => p.WrestlerId == save.Wrestlers[0].Id);
            Assert.That(placement.MainEvent, Is.EqualTo(matches == 1 && !cancelLast));
            Assert.That(result.HasMainEventOpportunity, Is.EqualTo(!cancelLast));
            if (closingPromo) Assert.That(save.ShowEvents.Single(e => e.Id == save.Shows.Last().MainEventId).EventType, Is.EqualTo(ShowEventType.Promo));
            Apply(save, result);
        }

        [Test]
        public void LegacyResultAndPromoOnlyAppearances_DoNotInventCardDemotions()
        {
            var save = Create();
            for (var i = 0; i < 4; i++)
            {
                var result = Pending(save); result.HasCardPlacementSnapshot = false; result.CardPlacements.Clear();
                result.HasUpperCardOpportunity = result.HasMainEventOpportunity = false; Apply(save, result);
            }
            for (var i = 0; i < 4; i++) Apply(save, Pending(save, absent: true, closingPromo: true));
            Assert.That(State(save).CardObservations, Is.Empty); Assert.That(State(save).Concerns, Is.Empty);
        }

        [Test]
        public void LowCardComplaint_DependsOnExpectedRoleAndAmbition_NotProfessionalismOrMatchWinner()
        {
            var low = Create(); low.Contracts[0].ExpectedRole = ExpectedRole.Jobber; low.Wrestlers[0].Hidden.Ambition = 1;
            var high = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(low)); high.Wrestlers[0].Hidden.Ambition = 20;
            for (var i = 0; i < 4; i++) { Apply(low, Pending(low)); Apply(high, Pending(high)); }
            Assert.That(State(low).Concerns, Is.Empty); Assert.That(Concern(high).Severity, Is.EqualTo(25));
            Assert.That(Concern(high).Status, Is.EqualTo(WrestlerConcernStatus.Watching));
            var professional = Create(); professional.Wrestlers[0].Hidden.Professionalism = 20;
            for (var i = 0; i < 4; i++) Apply(professional, Pending(professional));
            Assert.That(Concern(professional).Severity, Is.EqualTo(100)); Assert.That(professional.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(35));
        }

        [Test]
        public void Promise_CanBeRejectedWithoutManufacturingAnAgreement()
        {
            var save = WithConcern(); Reply(save, .999);
            Assert.That(Concern(save).Promises, Is.Empty); Assert.That(State(save).Events.Last().Kind, Is.EqualTo(WrestlerBehaviorEventKind.PromiseRejected));
        }

        [Test]
        public void MainEventPromise_IsNotKeptByAWinInTheOpeningMatch()
        {
            var save = WithConcern(); Reply(save); Apply(save, Pending(save));
            Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Active));
            Assert.That(Concern(save).Promises.Single().EligibleShows, Is.EqualTo(1));
            Apply(save, Pending(save)); Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Broken));
            Assert.That(State(save).Trust, Is.EqualTo(38)); Assert.That(State(save).BrokenPromises, Is.EqualTo(1));
        }

        [Test]
        public void MainEventPromise_IsKeptByActualPlacement_AndOldContractChangeDoesNotRewriteIt()
        {
            var save = WithConcern(); Reply(save); save.Contracts[0].ExpectedRole = ExpectedRole.Jobber;
            Apply(save, Pending(save, position: 1));
            Assert.That(Concern(save).Promises.Single().RequiredCardRole, Is.EqualTo(ExpectedRole.MainEventer));
            Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Active));
            Apply(save, Pending(save, position: 2)); Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Kept));
            Assert.That(State(save).Trust, Is.EqualTo(55));
        }

        [Test]
        public void PromiseStillActive_WhenFinalMatchWasCancelled_AndNoOpportunityByDeadlineIsUnassessable()
        {
            var save = WithConcern(); Reply(save); Apply(save, Pending(save, cancelLast: true));
            Assert.That(Concern(save).Promises.Single().EligibleShows, Is.Zero);
            save.CurrentDate = Concern(save).Promises.Single().DueDate.AddDays(1); Service().ProcessDay(save);
            Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Unassessable));
            Assert.That(State(save).Trust, Is.EqualTo(50));
        }

        [Test]
        public void UnbookedHealthyPlayer_CanBreakAnExplicitMainEventPromise()
        {
            var save = WithConcern(); Reply(save);
            Apply(save, Pending(save, absent: true)); Apply(save, Pending(save, absent: true));
            Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Broken));
            Assert.That(State(save).CardObservations, Has.Count.EqualTo(4));
        }

        [Test]
        public void AbsenceAndCardConcerns_CoexistWithoutSharingTheirPromiseStatus()
        {
            var save = WithConcern(); Reply(save);
            for (var i = 0; i < 4; i++) Apply(save, Pending(save, absent: true));
            Assert.That(State(save).Concerns.Where(c => c.Status != WrestlerConcernStatus.Resolved).Select(c => c.Kind),
                Is.EquivalentTo(new[] { WrestlerConcernKind.AppearanceOpportunity, WrestlerConcernKind.CardPlacement }));
            Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Broken));
            Assert.That(State(save).Concerns.Single(c => c.Kind == WrestlerConcernKind.AppearanceOpportunity).Promises, Is.Empty);
            Assert.DoesNotThrow(() => WrestlerBehaviorService.ValidateState(save));
        }

        [Test]
        public void UpperCardPromise_IsKeptWhenAnAdditionalMainEventBookingWasCancelled()
        {
            var save = WithConcern(ExpectedRole.Midcarder); Reply(save);
            var result = Pending(save, position: 1, cancelLast: true, heroAlsoInLast: true);
            Apply(save, result);
            Assert.That(Concern(save).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Kept));
        }

        [Test]
        public void CancelledUnrelatedPromo_DoesNotEraseAnAvailableMainEventPromiseOpportunity()
        {
            var save = WithConcern(); Reply(save);
            var result = Pending(save, absent: true, closingPromo: true);
            var promo = save.PromoPlans.Last(); var cancelled = new PromoResultState { Id = Id(), ShowId = result.ShowId, ShowVersion = 1,
                PromoPlanId = promo.Id, CancellationReason = "별도 프로모 취소" };
            save.PromoResults.Add(cancelled); result.PromoResultIds.Add(cancelled.Id);
            Apply(save, result); Assert.That(Concern(save).Promises.Single().EligibleShows, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MalformedCardSource_IsRejectedBySaveValidationWithoutCrashing(int corruption)
        {
            var save = Create(); var result = Pending(save);
            if (corruption == 0) save.MatchResults.Insert(0, null);
            if (corruption == 1) save.Shows.Last().TimelineEventIds = null;
            if (corruption == 2) { result.MatchResultIds.Clear(); result.CardPlacements.Clear(); result.HasUpperCardOpportunity = result.HasMainEventOpportunity = false; }
            Assert.Throws<InvalidOperationException>(() => WrestlerBehaviorService.ValidateState(save));
            Assert.DoesNotThrow(() => GameSaveValidator.Validate(save));
            Assert.That(GameSaveValidator.Validate(save), Is.Not.Empty);
        }

        [Test]
        public void LeavingPromotion_ClosesBothCausesAndReportsCardClosureAccurately()
        {
            var save = WithConcern(); Reply(save); save.Contracts[0].Status = ContractStatus.Terminated; Service().ProcessDay(save);
            Assert.That(Concern(save).Status, Is.EqualTo(WrestlerConcernStatus.Resolved)); Assert.That(State(save).CardObservations, Is.Empty);
            Assert.That(State(save).Events.Last().Description, Does.Contain("카드"));
        }

        [Test]
        public void ActualSaveLoad_PreservesCardPromiseAndJudgesItAfterReload()
        {
            var save = WithConcern(); Reply(save);
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-card-" + Id());
            try
            {
                var storage = new SaveService(directory); storage.Save("card", save); var restored = storage.Load("card");
                Assert.That(JsonUtility.ToJson(State(restored)), Is.EqualTo(JsonUtility.ToJson(State(save))));
                Apply(restored, Pending(restored, position: 2));
                Assert.That(Concern(restored).Promises.Single().Status, Is.EqualTo(WrestlerPromiseStatus.Kept));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void Migration28_KeepsAppearancePromiseAndStartsWithoutCardEvidence()
        {
            var save = Create(); var service = Service();
            for (var i = 0; i < 4; i++)
            {
                var result = Pending(save, absent: true); result.HasCardPlacementSnapshot = false; result.CardPlacements.Clear();
                result.HasMainEventOpportunity = result.HasUpperCardOpportunity = false; Apply(save, result);
            }
            var concern = State(save).Concerns.Single(); service.Respond(save, save.Wrestlers[0].Id, concern.Id, WrestlerConcernResponse.OfferAppearancePromise);
            save.SaveVersion = 28; State(save).CardObservations = null; var before = JsonUtility.ToJson(State(save).Concerns.Single());
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-card-legacy-" + Id());
            try
            {
                Directory.CreateDirectory(directory);
                var legacy = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(save), ",\"CardObservations\":(null|\\[\\])", "");
                Assert.That(legacy, Does.Not.Contain("\"CardObservations\""));
                File.WriteAllText(Path.Combine(directory, "old.json"), legacy);
                var storage = new SaveService(directory); var restored = storage.Load("old");
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion)); Assert.That(State(restored).CardObservations, Is.Empty);
                Assert.That(JsonUtility.ToJson(State(restored).Concerns.Single()), Is.EqualTo(before));
                Assert.That(State(restored).Concerns.Single().Kind, Is.EqualTo(WrestlerConcernKind.AppearanceOpportunity));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void ReportView_RespondsToCardConcernWithoutExposingHiddenPersonality()
        {
            var save = WithConcern(); var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                DashboardSession.SetActiveSave(save, true); var overlay = WrestlerBehaviorView.Open(new VisualElement(), save.Wrestlers[0].Id);
                var text = string.Join("\n", overlay.Query<Label>().ToList().Select(l => l.text));
                Assert.That(text, Does.Contain("메인이벤트").And.Contain("카드"));
                Assert.That(text, Does.Not.Contain("Ambition").And.Not.Contain("Assertiveness").And.Not.Contain("야심가형"));
                Click(overlay.Q<Button>("wrestler-decline-card-request"));
                Assert.That(Concern(save).Status, Is.EqualTo(WrestlerConcernStatus.Watching));
                Assert.That(State(save).Events.Last().Kind, Is.EqualTo(WrestlerBehaviorEventKind.Declined));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
        private sealed class FixedRandom : System.Random
        { private readonly double value; public FixedRandom(double value) => this.value = value; public override double NextDouble() => value; }
        private static WrestlerBehaviorService Service(double roll = 0) => new(random: new FixedRandom(roll));
        private static WrestlerBehaviorState State(GameSave save) => save.WrestlerBehaviors.Single(s => s.WrestlerId == save.Wrestlers[0].Id && s.PromotionId == save.Promotion.Id);
        private static WrestlerConcernState Concern(GameSave save) => State(save).Concerns.Single(c => c.Kind == WrestlerConcernKind.CardPlacement);
        internal static GameSave WithConcern(ExpectedRole role = ExpectedRole.MainEventer)
        { var save = Create(); save.Contracts[0].ExpectedRole = role; for (var i = 0; i < 4; i++) Apply(save, Pending(save)); return save; }
        private static void Reply(GameSave save, double roll = 0) => Service(roll).Respond(save, save.Wrestlers[0].Id, Concern(save).Id, WrestlerConcernResponse.OfferAppearancePromise);
        private static void Apply(GameSave save, ShowResultState result)
        {
            var appeared = save.MatchResults.Where(m => result.MatchResultIds.Contains(m.Id) && string.IsNullOrEmpty(m.CancellationReason))
                .SelectMany(m => m.ParticipantProfiles.SelectMany(p => p.MemberIds))
                .Concat(save.PromoResults.Where(p => result.PromoResultIds.Contains(p.Id) && string.IsNullOrEmpty(p.CancellationReason)).SelectMany(p => p.ParticipantUsage.Select(u => u.WrestlerId))).Distinct().ToArray();
            Service().PrepareShow(save, result, appeared).Apply(); save.Shows.Last().Status = ShowStatus.Completed; save.Schedules.Last().Status = ScheduleStatus.Completed;
        }
        private static ShowResultState Pending(GameSave save, int position = 0, int matches = 3, bool absent = false, bool closingPromo = false,
            bool cancelLast = false, bool heroAlsoInLast = false)
        {
            var show = Queue(save, position, matches, absent, closingPromo);
            var result = new ShowResultState { Id = Id(), ShowId = show.Id, ShowVersion = 1, HasAvailabilitySnapshot = true,
                AvailableWrestlerIds = WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate) };
            foreach (var entry in save.ShowEvents.Where(e => e.ShowId == show.Id))
            {
                if (entry.EventType == ShowEventType.Match)
                {
                    var plan = save.MatchPlans.Single(m => m.Id == entry.DetailId); var last = entry.Id == show.TimelineEventIds[matches - 1];
                    var ids = plan.ParticipantIds.ToList(); if (last && heroAlsoInLast) ids[0] = save.Wrestlers[0].Id;
                    var match = new MatchResultState { Id = Id(), ShowId = show.Id, ShowVersion = 1, ShowEventId = entry.Id, MatchPlanId = plan.Id,
                        PlannedDuration = 10, ActualMatchDuration = last && cancelLast ? 0 : 10, WinnerId = ids[0],
                        CancellationReason = last && cancelLast ? "상대 부상으로 경기 취소" : null,
                        ParticipantProfiles = new() { new() { SideId = "a", MemberIds = new() { ids[0] } }, new() { SideId = "b", MemberIds = new() { ids[1] } } } };
                    save.MatchResults.Add(match); result.MatchResultIds.Add(match.Id); result.TimelineResultIds.Add(match.Id);
                }
                else
                {
                    var promo = new PromoResultState { Id = Id(), ShowId = show.Id, ShowVersion = 1, ShowEventId = entry.Id, PromoPlanId = entry.DetailId,
                        ParticipantUsage = new() { new() { WrestlerId = save.Wrestlers[0].Id } } };
                    save.PromoResults.Add(promo); result.PromoResultIds.Add(promo.Id); result.TimelineResultIds.Add(promo.Id);
                }
            }
            var roles = result.AvailableWrestlerIds.ToDictionary(id => id, id => WrestlerBehaviorService.ExpectedRoleAt(save, save.Wrestlers.Single(w => w.Id == id), show.Date));
            WrestlerBehaviorService.CaptureCardPlacements(save, result, save.MatchResults.Where(m => result.MatchResultIds.Contains(m.Id)).ToList(), roles);
            save.ShowResults.Add(result); show.Status = ShowStatus.ResultsReviewed; return result;
        }

        private static StaticContentRegistry Registry() => new(AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset"));
        private static ShowExecutionService Execution()
        { var registry = Registry(); return new(new ShowPlanningService(registry), new MatchEvaluator(registry, registry, accidentsEnabled: false), new PromoEvaluator()); }
        internal static GameSave Create()
        {
            var save = GameSave.CreateNew(71, DateTime.UtcNow); save.Promotion = new() { Id = Id(), Name = "Card Test", InitialCash = 10000 };
            var generator = new WrestlerGenerator(Registry(), 71);
            for (var i = 0; i < 6; i++)
            {
                var person = generator.GenerateCandidate(WrestlerGender.Male, save.CurrentDate);
                person.PromotionId = save.Promotion.Id; person.Identity.ExpiryDate = default; person.Roster.ActivityState = RosterActivityState.Active;
                person.Hidden = new() { Ambition = i == 0 ? 20 : 1, Assertiveness = 20 }; person.Condition.Satisfaction = 50;
                save.Wrestlers.Add(person); save.Contracts.Add(new() { Id = Id(), PromotionId = save.Promotion.Id, PersonId = person.Id,
                    Type = ContractType.Wrestler, StartDate = save.CurrentDate, EndDate = new(2027, 5, 31), Status = ContractStatus.Active,
                    HasExpectedRole = true, ExpectedRole = i == 0 ? ExpectedRole.MainEventer : ExpectedRole.Jobber });
            }
            save.VenueContracts.Add(new() { Id = Id(), VenueId = "venue_001" }); return save;
        }
        private static ShowResultState Execute(GameSave save, ShowExecutionService execution)
        {
            var show = Queue(save); return execution.Execute(save, show.Id, 73, 1000, 30);
        }
        private static ShowState Queue(GameSave save, int position = 0, int matches = 3, bool absent = false, bool closingPromo = false)
        {
            save.CurrentDate = save.CurrentDate.AddDays(7);
            var schedule = new ScheduleState { Id = Id(), Date = save.CurrentDate, BookingDeadline = save.CurrentDate, Status = ScheduleStatus.Confirmed };
            save.Schedules.Add(schedule);
            var show = new ShowState { Id = Id(), ScheduleId = schedule.Id, VenueContractId = save.VenueContracts[0].Id,
                Name = "Card Show", Date = save.CurrentDate, DurationLimit = matches * 10 + (closingPromo ? 10 : 0), ShowVersion = 1, Status = ShowStatus.InProgress };
            save.Shows.Add(show);
            var pairs = Enumerable.Range(0, matches).ToList(); pairs.Remove(0); pairs.Insert(position, 0);
            for (var i = 0; i < matches; i++)
            {
                var pair = absent && pairs[i] == 0 ? 1 : pairs[i];
                var a = save.Wrestlers[pair * 2].Id; var b = save.Wrestlers[pair * 2 + 1].Id;
                var match = new MatchPlanState { Id = Id(), MatchTypeId = "matchtype_001", ParticipantIds = new() { a, b },
                    WinnerId = a, LoserTargetId = b, FinishType = MatchFinishType.Pinfall };
                save.MatchPlans.Add(match); var entry = new ShowEventState { Id = Id(), ShowId = show.Id, EventType = ShowEventType.Match, DetailId = match.Id, PlannedDuration = 10 };
                save.ShowEvents.Add(entry); show.TimelineEventIds.Add(entry.Id);
            }
            if (closingPromo)
            {
                var promo = new PromoPlanState { Id = Id(), Purpose = PromoPurpose.CharacterIntroduction, Presentation = PromoPresentation.InRingMic, ParticipantIds = new() { save.Wrestlers[0].Id } };
                save.PromoPlans.Add(promo); var entry = new ShowEventState { Id = Id(), ShowId = show.Id, EventType = ShowEventType.Promo, DetailId = promo.Id, PlannedDuration = 10 };
                save.ShowEvents.Add(entry); show.TimelineEventIds.Add(entry.Id);
            }
            show.OpeningEventId = show.TimelineEventIds[0]; show.MainEventId = show.TimelineEventIds.Last();
            return show;
        }
        private static string Id() => Guid.NewGuid().ToString();
    }
}
