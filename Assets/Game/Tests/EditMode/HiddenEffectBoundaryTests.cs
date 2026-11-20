using System;
using System.Linq;
using System.IO;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using PWManager.Infrastructure.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class HiddenEffectBoundaryTests
    {
        [TestCase(1, false)]
        [TestCase(20, true)]
        public void ConflictProneness_ChangesConflictChanceOnlyAfterRealContactAndTension(int conflict, bool expected)
        {
            var save = Training(2); ExistingRelationship(save, -12);
            foreach (var w in save.Wrestlers) w.Hidden.ConflictProneness = conflict;
            new WrestlerBehaviorService(random: new Sequence(0, 0, .1)).ProcessDay(save);
            Assert.That(save.LockerRoomEvents.Any(e => e.Date.Equals(save.CurrentDate) && e.Kind == LockerRoomEventKind.Conflict), Is.EqualTo(expected));
        }

        [TestCase(false, 50, false)]
        [TestCase(true, 50, true)]
        [TestCase(true, 0, false)]
        public void SharedOpinion_IsInterpretedThroughRecipientsOwnExperienceAndReportsActualChange(bool sameProblem, float trust, bool changed)
        {
            var save = Training(2); ExistingRelationship(save, 16); var first = save.Wrestlers[0]; var second = save.Wrestlers[1];
            first.Hidden.Assertiveness = 20; first.Hidden.Leadership = 1;
            var concern = new WrestlerConcernState { Id = Id(2001), StartedDate = save.CurrentDate, Severity = 80 };
            var origin = new WrestlerBehaviorEventState { Id = Id(2002), ConcernId = concern.Id, Kind = WrestlerBehaviorEventKind.Request, Date = save.CurrentDate };
            save.WrestlerBehaviors.Add(new() { PromotionId = save.Promotion.Id, WrestlerId = first.Id, Concerns = new() { concern }, Events = new() { origin } });
            var receiver = new WrestlerBehaviorState { PromotionId = save.Promotion.Id, WrestlerId = second.Id, Trust = trust };
            if (sameProblem) receiver.Concerns.Add(new() { Id = Id(2003), StartedDate = save.CurrentDate, Severity = 70 });
            save.WrestlerBehaviors.Add(receiver);
            new LockerRoomService(random: new Constant(0)).ProcessTraining(save, save.CurrentDate, (_, _) => { });
            Assert.That(receiver.Trust < trust, Is.EqualTo(changed)); Assert.That(receiver.Concerns.Count, Is.EqualTo(sameProblem ? 1 : 0));
            var entry = save.LockerRoomEvents.Single(e => e.Kind == LockerRoomEventKind.OpinionShared);
            Assert.That(entry.OriginBehaviorEventId, Is.EqualTo(origin.Id)); Assert.That(entry.Description.Contains("신뢰가 낮아졌습니다"), Is.EqualTo(changed));
            new LockerRoomService(random: new Constant(0)).ProcessTraining(save, save.CurrentDate, (_, _) => { });
            Assert.That(save.LockerRoomEvents.Count(e => e.Kind == LockerRoomEventKind.OpinionShared), Is.EqualTo(1));
            Assert.That(receiver.Events, Is.Empty);
        }

        [Test]
        public void ActualSaveLoad_PreservesAdaptationAndRealRelationshipsAndReportLinks()
        {
            var save = Training(3); ExistingRelationship(save, -12);
            new WrestlerBehaviorService(random: new Sequence(0, 0, 0, 0, .99, .99)).ProcessDay(save);
            save.Wrestlers[0].Presentation.WrestlingStyleId = "style_002";
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-hidden-effects-" + Guid.NewGuid());
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                var storage = new SaveService(directory); storage.Save("effects", save); var restored = storage.Load("effects");
                Assert.That(JsonUtility.ToJson(restored.Wrestlers[0].Adaptation), Is.EqualTo(JsonUtility.ToJson(save.Wrestlers[0].Adaptation)));
                Assert.That(restored.LockerRoomEvents.Select(JsonUtility.ToJson), Is.EqualTo(save.LockerRoomEvents.Select(JsonUtility.ToJson)));
                Assert.That(restored.LockerRoomRelationships.Select(JsonUtility.ToJson), Is.EqualTo(save.LockerRoomRelationships.Select(JsonUtility.ToJson)));
                DashboardSession.SetActiveSave(restored, true); var overlay = WrestlerBehaviorView.Open(new VisualElement(), restored.Wrestlers[0].Id);
                Assert.That(overlay.Query<Button>(className: "wrestler-link").ToList().Count, Is.GreaterThanOrEqualTo(4));
                Assert.That(string.Join("\n", overlay.Query<Label>().ToList().Select(l => l.text)), Does.Contain("라커룸").And.Not.Contain("Leadership"));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void ActualVersion29File_MigratesMissingFieldsWithoutChangingHiddenValuesOrMemories()
        {
            var save = Save(1); save.Wrestlers[0].Hidden.Leadership = 19;
            save.WrestlerBehaviors.Add(new() { PromotionId = save.Promotion.Id, WrestlerId = Id(1), Trust = 37,
                Concerns = new() { new() { Id = Id(2001), StartedDate = save.CurrentDate, Severity = 35 } } });
            save.SaveVersion = 29; var hidden = JsonUtility.ToJson(save.Wrestlers[0].Hidden); var memory = JsonUtility.ToJson(save.WrestlerBehaviors[0]);
            var legacy = JsonUtility.ToJson(save).Replace(",\"Adaptation\":" + JsonUtility.ToJson(save.Wrestlers[0].Adaptation), "")
                .Replace(",\"LockerRoomRelationships\":[]", "").Replace(",\"LockerRoomEvents\":[]", "");
            Assert.That(legacy, Does.Not.Contain("\"Adaptation\"").And.Not.Contain("\"LockerRoomEvents\""));
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-hidden-v29-" + Guid.NewGuid());
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "old.json"), legacy);
                var restored = new SaveService(directory).Load("old");
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
                Assert.That(JsonUtility.ToJson(restored.Wrestlers[0].Hidden), Is.EqualTo(hidden));
                Assert.That(JsonUtility.ToJson(restored.WrestlerBehaviors[0]), Is.EqualTo(memory));
                Assert.That(WrestlerAdaptationService.Penalty(restored, restored.Wrestlers[0], restored.CurrentDate), Is.Zero);
                Assert.That(restored.LockerRoomRelationships, Is.Empty); Assert.That(restored.LockerRoomEvents, Is.Empty);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void PublicComplaint_ActualSaveLoadPreservesCauseAndPreventsReplay()
        {
            var save = CardPlacementBehaviorTests.WithConcern(); var person = save.Wrestlers[0];
            person.Hidden.Professionalism = 1; person.Hidden.MediaExpression = 20;
            save.CurrentDate = save.WrestlerBehaviors.Single(s => s.WrestlerId == person.Id).Concerns.Single().ResponseDueDate.Value.AddDays(1);
            new WrestlerBehaviorService(random: new Constant(.5)).ProcessDay(save);
            save.CurrentDate = save.WrestlerBehaviors.Single(s => s.WrestlerId == person.Id).NextActionDate.Value;
            new WrestlerBehaviorService(random: new Constant(.5)).ProcessDay(save);
            var state = save.WrestlerBehaviors.Single(s => s.WrestlerId == person.Id);
            Assert.That(state.Events.Any(e => e.Kind == WrestlerBehaviorEventKind.PublicComplaint), Is.True);
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-hidden-public-" + Guid.NewGuid());
            try
            {
                var storage = new SaveService(directory); storage.Save("public", save); var restored = storage.Load("public");
                var before = JsonUtility.ToJson(restored.WrestlerBehaviors.Single(s => s.WrestlerId == person.Id));
                new WrestlerBehaviorService(random: new Constant(0)).ProcessDay(restored);
                Assert.That(JsonUtility.ToJson(restored.WrestlerBehaviors.Single(s => s.WrestlerId == person.Id)), Is.EqualTo(before));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void ExistingGuaranteedRole_RemainsRelevantWhenStatusIsLow()
        {
            var save = Save(1); var w = save.Wrestlers[0]; w.Status.StatusValue = 10; w.Hidden.Ambition = 20;
            save.Contracts[0].HasExpectedRole = true; save.Contracts[0].ExpectedRole = ExpectedRole.MainEventer;
            var d = ContractNegotiationService.Demand(save, w.Id, ContractType.Wrestler);
            float Chance(ExpectedRole role) => ContractNegotiationService.Acceptance(save, w.Id, ContractType.Wrestler, d.SigningBonus, d.MonthlySalary, true, role);
            Assert.That(Chance(ExpectedRole.Jobber), Is.LessThan(Chance(ExpectedRole.MainEventer)));
        }

        [Test]
        public void Loyalty_UsesContiguousEarlierContractsInsteadOfResettingOnRenewal()
        {
            var save = Save(1); var w = save.Wrestlers[0]; w.Hidden.Loyalty = 20; var current = save.Contracts[0];
            current.StartDate = save.CurrentDate.AddDays(-1);
            var previous = current.Copy(); previous.Id = Guid.NewGuid().ToString(); previous.Status = ContractStatus.Expired;
            previous.StartDate = save.CurrentDate.AddDays(-200); previous.EndDate = current.StartDate.AddDays(-1); save.Contracts.Add(previous);
            var d = ContractNegotiationService.Demand(save, w.Id, ContractType.Wrestler);
            float Chance() => ContractNegotiationService.Acceptance(save, w.Id, ContractType.Wrestler, d.SigningBonus, d.MonthlySalary);
            var continuous = Chance(); previous.EndDate = current.StartDate.AddDays(-2);
            Assert.That(continuous, Is.GreaterThan(Chance()));
        }

        [Test]
        public void DepartedTrainingPlans_CannotInventCrowdingAndConflicts()
        {
            var save = Training(3);
            for (var i = 0; i < 20; i++)
            {
                var former = JsonUtility.FromJson<WrestlerState>(JsonUtility.ToJson(save.Wrestlers[0]));
                former.Id = Id(20 + i); former.Roster.ActivityState = RosterActivityState.Released; save.Wrestlers.Add(former);
                save.Training.Plans.Add(new() { WrestlerId = former.Id, Mode = TrainingMode.Normal });
            }
            new WrestlerBehaviorService(random: new Constant(0)).ProcessDay(save);
            Assert.That(save.LockerRoomEvents.Any(e => e.Kind == LockerRoomEventKind.Conflict), Is.False);
        }

        [Test]
        public void MinimumLeadership_SuccessfulMediationActuallyReducesTheConflict()
        {
            var save = Training(3); ExistingRelationship(save, -12);
            save.Wrestlers[2].Hidden.Leadership = 1;
            new WrestlerBehaviorService(random: new Sequence(0, 0, 0, 0, .99, .99)).ProcessDay(save);
            Assert.That(save.LockerRoomEvents.Any(e => e.Kind == LockerRoomEventKind.Mediation), Is.True);
            Assert.That(save.LockerRoomRelationships.Single(r => r.FirstWrestlerId == Id(1) && r.SecondWrestlerId == Id(2)).Affinity, Is.GreaterThan(-16));
        }

        [Test]
        public void MediatorWithoutOtherInteractions_ReceivesTheirOwnWeeklyObservation()
        {
            var save = Training(3); ExistingRelationship(save, -12); save.Wrestlers[2].Hidden.Leadership = 20;
            new WrestlerBehaviorService(random: new Sequence(0, 0, 0, 0, .99, .99)).ProcessDay(save);
            Assert.That(save.InboxMessages.Any(m => m.SourceEventId == $"wrestler-weekly:{Id(3)}:20260601" && m.Body.Contains("중재")), Is.True);
        }

        [Test]
        public void SocialEventId_CannotReuseAnExistingConcernId()
        {
            var save = ShowExecutionServiceTests.CreateSave(); foreach (var w in save.Wrestlers)
            { w.PromotionId = save.Promotion.Id; w.Roster.ActivityState = RosterActivityState.Active; w.Hidden = new() { Sociability = 20 }; }
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 1000, 30); save.Shows[0].Status = ShowStatus.ResultsReviewed;
            var collision = Guid.NewGuid().ToString();
            save.WrestlerBehaviors.Add(new() { PromotionId = save.Promotion.Id, WrestlerId = "a", Concerns = new() { new() {
                Id = collision, StartedDate = save.CurrentDate, Severity = 1, Status = WrestlerConcernStatus.Watching } } });
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new WrestlerBehaviorService(() => collision, new Constant(0)).PrepareShow(save, result, new[] { "a", "b" }));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void DepartureAndRehire_RequireFreshEnvironmentAdaptation()
        {
            var save = Save(1); var w = save.Wrestlers[0]; var c = save.Contracts[0];
            c.Status = ContractStatus.Terminated; w.Roster.ActivityState = RosterActivityState.Released;
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            c.Status = ContractStatus.Active; c.StartDate = save.CurrentDate; w.Roster.ActivityState = RosterActivityState.Active;
            Assert.That(WrestlerAdaptationService.Penalty(save, w, save.CurrentDate), Is.GreaterThan(0));
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(w.Adaptation.EnvironmentReadiness, Is.LessThan(1));
        }

        [Test]
        public void PlayerCharacter_StillPassivelyAdaptsWithoutAnEmploymentContract()
        {
            var save = Save(1); var w = save.Wrestlers[0]; save.Contracts.Clear(); save.Player = new() { WrestlerId = w.Id };
            w.Presentation.WrestlingStyleId = "style_002";
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(w.Adaptation.StyleId, Is.EqualTo("style_002"));
            Assert.That(w.Adaptation.StyleReadiness, Is.LessThan(1).And.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OldIgnoredRequest_CannotTriggerPublicComplaintDuringOrAfterNewKeptPromise(bool kept)
        {
            var save = AllHiddenEffectsTests.Unhappy(); save.Contracts[0].ExpectedRole = ExpectedRole.MainEventer;
            save.CurrentDate = save.WrestlerBehaviors.Single().NextActionDate.Value;
            Observe(save, false);
            var concern = save.WrestlerBehaviors.Single().Concerns.Single();
            new WrestlerBehaviorService(random: new Constant(0)).Respond(save, save.Wrestlers[0].Id, concern.Id, WrestlerConcernResponse.OfferAppearancePromise);
            if (kept) { save.CurrentDate = save.CurrentDate.AddDays(7); Observe(save, true); }
            save.Wrestlers[0].Hidden.Professionalism = 1; save.Wrestlers[0].Hidden.MediaExpression = 20;
            save.CurrentDate = save.WrestlerBehaviors.Single().NextActionDate.Value;
            new WrestlerBehaviorService(random: new Constant(.5)).ProcessDay(save);
            Assert.That(save.WrestlerBehaviors.Single().Events.Any(e => e.Kind == WrestlerBehaviorEventKind.PublicComplaint), Is.False);
        }

        [Test]
        public void TemporaryExecutionBonus_CannotUnlockAnUntrainedTechnique()
        {
            var save = ShowExecutionServiceTests.CreateSave(); var w = save.Wrestlers[0];
            w.Attributes.Technical = 14.8f; w.Hidden = new() { BigMatchPerformance = 20, Consistency = 20 };
            ShowExecutionServiceTests.Service().Execute(save, "show", 73, 1000, 30);
            var actor = save.MatchResults.Single().EngineState.Participants.Single(p => p.Id == w.Id);
            Assert.That(actor.Attributes.Technical, Is.GreaterThan(15));
            actor.Position = MatchPosition.Center; actor.IsDowned = false;
            var target = new MatchEngineParticipant { Id = "target", Position = MatchPosition.Center };
            var previous = MatchTechniques.All;
            try
            {
                var technique = new MatchTechnique("trained-only", "trained-only", MatchActionType.Grapple, false, MoveKind.Grapple, BodyRegion.Body)
                    { RequiredStat = "Technical", RequiredStatValue = 15 };
                MatchTechniques.Configure(new[] { technique });
                Assert.That(MatchTechniques.Candidates(MatchActionType.Grapple, actor, target), Is.Empty);
            }
            finally { MatchTechniques.Configure(previous); }
        }

        [Test]
        public void FormerEmployeeReport_DoesNotInventAdaptationObservationsInAnotherPromotion()
        {
            var save = Save(1); var w = save.Wrestlers[0]; var foreign = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Other" };
            save.OtherPromotions.Add(foreign); save.Contracts[0].Status = ContractStatus.Terminated; w.PromotionId = foreign.Id;
            save.WrestlerBehaviors.Add(new() { PromotionId = save.Promotion.Id, WrestlerId = w.Id });
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                DashboardSession.SetActiveSave(save, true); var overlay = WrestlerBehaviorView.Open(new VisualElement(), w.Id);
                Assert.That(string.Join("\n", overlay.Query<Label>().ToList().Select(l => l.text)), Does.Not.Contain("적응"));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        private static void Observe(GameSave save, bool appeared)
        {
            var show = new ShowState { Id = Guid.NewGuid().ToString(), Date = save.CurrentDate, Status = ShowStatus.ResultsReviewed, ShowVersion = 1 }; save.Shows.Add(show);
            var result = new ShowResultState { Id = Guid.NewGuid().ToString(), ShowId = show.Id, ShowVersion = 1, HasAvailabilitySnapshot = true,
                AvailableWrestlerIds = new() { save.Wrestlers[0].Id } }; save.ShowResults.Add(result);
            new WrestlerBehaviorService(random: new Constant(0)).PrepareShow(save, result, appeared ? new[] { save.Wrestlers[0].Id } : Array.Empty<string>()).Apply();
            show.Status = ShowStatus.Completed;
        }
        private static GameSave Save(int count)
        {
            var save = CardPlacementBehaviorTests.Create(); save.Wrestlers = save.Wrestlers.Take(count).ToList();
            save.Contracts = save.Contracts.Take(count).ToList();
            for (var i = 0; i < count; i++)
            {
                save.Wrestlers[i].Id = Id(i + 1); save.Contracts[i].PersonId = Id(i + 1);
                save.Wrestlers[i].Hidden = new(); save.Wrestlers[i].Presentation.WrestlingStyleId = "style_001";
            }
            WrestlerAdaptationService.InitializeSave(save, initialGame: true); return save;
        }
        private static GameSave Training(int count)
        {
            var save = Save(count); TrainingService.Initialize(save);
            foreach (var w in save.Wrestlers) { w.Hidden.Sociability = 1; w.Hidden.ConflictProneness = 20;
                save.Training.Plans.Add(new() { WrestlerId = w.Id, Mode = TrainingMode.Normal }); }
            save.Wrestlers[0].Hidden.Sociability = 20; return save;
        }
        private static void ExistingRelationship(GameSave save, float affinity)
        {
            var date = save.CurrentDate.AddDays(-1);
            save.LockerRoomRelationships.Add(new() { FirstWrestlerId = Id(1), SecondWrestlerId = Id(2), Affinity = affinity, Interactions = 1, LastInteractionDate = new(date) });
            save.LockerRoomEvents.Add(new() { Id = Id(1001), PromotionId = save.Promotion.Id, ActorId = Id(1), PartnerId = Id(2),
                Date = date, WorkKind = LockerRoomWorkKind.Training, SourceKey = $"training:{date.Year:D4}{date.Month:D2}{date.Day:D2}",
                Kind = LockerRoomEventKind.Conflict, Description = "이전 공동 훈련 관찰", IncludedInWeeklyReport = true });
        }
        private static string Id(int value) => "00000000-0000-0000-0000-" + value.ToString("D12");
        private sealed class Constant : System.Random
        { private readonly double value; public Constant(double value) => this.value = value; public override double NextDouble() => value; }
        private sealed class Sequence : System.Random
        { private readonly double[] values; private int index; public Sequence(params double[] values) => this.values = values; public override double NextDouble() => values[index++]; }
    }
}
