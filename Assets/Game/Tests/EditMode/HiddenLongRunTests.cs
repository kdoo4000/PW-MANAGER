using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class HiddenLongRunTests
    {
        [Serializable]
        public sealed class RunMetrics
        {
            public int Seed, Days, Shows, Contacts, Conflicts, Mediations, Opinions, Requests, PublicComplaints, KeptPromises, BrokenPromises;
            public bool Responsive;
            public float AverageAffinity, FriendFraction, SaturatedFraction, MinimumTrust, MinimumSatisfaction, AverageTrust;
            public List<PersonMetrics> People = new();
            public List<LockerRoomRelationshipState> Relationships;
            public List<StorylineState> Storylines;
            public List<StoryRelationshipState> StoryRelationships;
        }
        [Serializable]
        public sealed class PersonMetrics
        {
            public string Id, Name;
            public int Professionalism, Sociability, Requests, PublicComplaints, InitiatedContacts;
            public float Satisfaction, Trust, UnderpayAcceptance, FairPayAcceptance;
        }

        [Test]
        public void RoutineTraining_DoesNotCreateCloseFriendsInOneMonthFromGreetingsAlone()
        {
            var save = TrainingPair();
            for (var day = 0; day < 30; day++)
            {
                new WrestlerBehaviorService(random: new Constant(0)).ProcessDay(save);
                save.CurrentDate = save.CurrentDate.AddDays(1);
            }
            var relation = save.LockerRoomRelationships.Single();
            Assert.That(relation.Interactions, Is.EqualTo(30));
            Assert.That(relation.Affinity, Is.LessThan(15).And.GreaterThan(0));
        }

        [Test]
        public void FriendshipThreshold_IsReportedOnlyWhenCrossed()
        {
            var save = TrainingPair();
            save.LockerRoomRelationships.Add(new() { FirstWrestlerId = save.Wrestlers[0].Id, SecondWrestlerId = save.Wrestlers[1].Id,
                Affinity = 14.95f, Interactions = 20, LastInteractionDate = new(save.CurrentDate.AddDays(-1)) });
            new WrestlerBehaviorService(random: new Constant(0)).ProcessDay(save);
            Assert.That(save.LockerRoomEvents.Last().Significant, Is.True);
            save.CurrentDate = save.CurrentDate.AddDays(1);
            new WrestlerBehaviorService(random: new Constant(0)).ProcessDay(save);
            Assert.That(save.LockerRoomEvents.Last().Significant, Is.False);
        }

        [TestCase(71)]
        [TestCase(314)]
        [TestCase(907)]
        public void SharedWorkFrequencyAndSociability_ChangeFriendshipWithoutForcedOutcomes(int seed)
        {
            float Run(int sociability, int interval, bool mistakes)
            {
                var save = TrainingPair(); save.WorldSeed = seed;
                foreach (var w in save.Wrestlers) { w.Hidden.Sociability = sociability; w.Hidden.ConflictProneness = 20; }
                for (var day = 0; day < 180; day += interval)
                {
                    save.CurrentDate = save.CurrentDate.AddDays(interval);
                    var show = new ShowState { Id = Guid.NewGuid().ToString(), Date = save.CurrentDate, ShowVersion = 1 };
                    var match = new MatchResultState { Id = Guid.NewGuid().ToString(), ActualMatchDuration = 10 };
                    match.ParticipantProfiles.Add(new() { MemberIds = save.Wrestlers.Select(w => w.Id).ToList() });
                    if (mistakes) match.ExecutionEvents.Add(new() { EventType = MatchExecutionEventType.Mistake });
                    var result = new ShowResultState { ShowId = show.Id, MatchResultIds = { match.Id } };
                    save.Shows.Add(show); save.MatchResults.Add(match);
                    new LockerRoomService().ProcessShow(save, result, (_, _) => { });
                }
                return save.LockerRoomRelationships.SingleOrDefault()?.Affinity ?? 0;
            }
            var frequent = Run(20, 2, false); var rare = Run(20, 30, false); var reserved = Run(1, 2, false); var tense = Run(20, 2, true);
            Assert.That(frequent, Is.GreaterThan(15).And.LessThan(100));
            Assert.That(rare, Is.LessThan(15)); Assert.That(reserved, Is.LessThan(frequent)); Assert.That(tense, Is.LessThan(frequent));
            TestContext.WriteLine($"seed={seed} frequent={frequent:0.0} rare={rare:0.0} reserved={reserved:0.0} mistakes={tense:0.0}");
        }

        private static GameSave TrainingPair()
        {
            var save = CardPlacementBehaviorTests.Create(); save.Wrestlers = save.Wrestlers.Take(2).ToList(); save.Contracts = save.Contracts.Take(2).ToList();
            for (var i = 0; i < 2; i++) { save.Wrestlers[i].Id = Id(i + 1); save.Contracts[i].PersonId = Id(i + 1); }
            TrainingService.Initialize(save);
            foreach (var w in save.Wrestlers) save.Training.Plans.Add(new() { WrestlerId = w.Id, Mode = TrainingMode.Normal });
            return save;
        }
        private sealed class Constant : System.Random
        { private readonly double value; public Constant(double value) => this.value = value; public override double NextDouble() => value; }

        [TestCase(71, false)]
        [TestCase(71, true)]
        [TestCase(314, false)]
        [TestCase(314, true)]
        [TestCase(907, false)]
        [TestCase(907, true)]
        public void TwoYears_ControlledWorkloadPreservesValidStateAndMeasuresBehavior(int seed, bool responsive)
        {
            var save = CardPlacementBehaviorTests.Create(); save.WorldSeed = seed; save.Promotion.InitialCash = 100000000;
            var start = save.CurrentDate; var metrics = new RunMetrics { Seed = seed, Responsive = responsive };
            for (var i = 0; i < save.Wrestlers.Count; i++)
            {
                var w = save.Wrestlers[i]; w.Id = Id(i + 1); save.Contracts[i].PersonId = w.Id;
                save.Contracts[i].EndDate = new GameDate(2029, 5, 31); save.Contracts[i].ExpectedRole = i < 2 ? ExpectedRole.MainEventer : ExpectedRole.Midcarder;
                w.Hidden = new() { Professionalism = i % 2 == 0 ? 4 : 16, Sociability = i % 2 == 0 ? 4 : 16,
                    ConflictProneness = i % 2 == 0 ? 4 : 16, Leadership = i % 2 == 0 ? 4 : 16, Ambition = 16,
                    Assertiveness = 16, MediaExpression = 16, MoneyOrientation = i % 2 == 0 ? 4 : 16 };
                w.Fame.Value = 100;
            }
            WrestlerAdaptationService.InitializeSave(save, initialGame: true); TrainingService.Initialize(save);
            foreach (var w in save.Wrestlers) save.Training.Plans.Add(new() { WrestlerId = w.Id, Mode = TrainingMode.Normal });
            var storagePath = Path.Combine(Path.GetTempPath(), "pwmanager-long-run-" + Guid.NewGuid());
            try
            {
                for (var day = 1; day <= 730; day++)
                {
                    var advanced = new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
                    Assert.That(advanced.State, Is.EqualTo(TimeFlowState.Completed), $"seed {seed}, responsive {responsive}, day {day}: {advanced.BlockingReason}");
                    if (day % 7 == 0)
                    {
                        var week = day / 7;
                        ControlledShow(save, week, responsive); metrics.Shows++;
                    }
                    if (responsive)
                        foreach (var state in save.WrestlerBehaviors.ToList())
                            foreach (var c in state.Concerns.Where(c => c.Status == WrestlerConcernStatus.AwaitingResponse).ToList())
                                new WrestlerBehaviorService().Respond(save, state.WrestlerId, c.Id, WrestlerConcernResponse.OfferAppearancePromise);
                    if (day % 365 == 0)
                    {
                        var storage = new SaveService(storagePath); storage.Save("run", save); save = storage.Load("run");
                    }
                }
                metrics.Days = start.DaysUntil(save.CurrentDate);
                metrics.Contacts = save.LockerRoomEvents.Count(e => e.Kind == LockerRoomEventKind.Contact);
                metrics.Conflicts = save.LockerRoomEvents.Count(e => e.Kind == LockerRoomEventKind.Conflict);
                metrics.Mediations = save.LockerRoomEvents.Count(e => e.Kind == LockerRoomEventKind.Mediation);
                metrics.Opinions = save.LockerRoomEvents.Count(e => e.Kind == LockerRoomEventKind.OpinionShared);
                var events = save.WrestlerBehaviors.SelectMany(s => s.Events).ToList();
                metrics.Requests = events.Count(e => e.Kind == WrestlerBehaviorEventKind.Request);
                metrics.PublicComplaints = events.Count(e => e.Kind == WrestlerBehaviorEventKind.PublicComplaint);
                metrics.KeptPromises = events.Count(e => e.Kind == WrestlerBehaviorEventKind.PromiseKept);
                metrics.BrokenPromises = events.Count(e => e.Kind == WrestlerBehaviorEventKind.PromiseBroken);
                metrics.AverageAffinity = save.LockerRoomRelationships.Average(r => r.Affinity);
                metrics.FriendFraction = save.LockerRoomRelationships.Count(r => r.Affinity >= 15) / 15f;
                metrics.SaturatedFraction = save.LockerRoomRelationships.Count(r => Math.Abs(r.Affinity) >= 99) / 15f;
                metrics.AverageTrust = save.WrestlerBehaviors.Average(s => s.Trust); metrics.MinimumTrust = save.WrestlerBehaviors.Min(s => s.Trust);
                metrics.MinimumSatisfaction = save.Wrestlers.Min(w => w.Condition.Satisfaction);
                metrics.Relationships = save.LockerRoomRelationships;
                metrics.Storylines = save.Storylines; metrics.StoryRelationships = save.StoryRelationships;
                foreach (var w in save.Wrestlers)
                {
                    var state = save.WrestlerBehaviors.Single(s => s.WrestlerId == w.Id);
                    var demand = ContractNegotiationService.Demand(save, w.Id, ContractType.Wrestler);
                    metrics.People.Add(new() { Id = w.Id, Name = string.IsNullOrWhiteSpace(w.Identity.RingName) ? w.Identity.LegalName : w.Identity.RingName,
                        Professionalism = w.Hidden.Professionalism, Sociability = w.Hidden.Sociability,
                        Requests = state.Events.Count(e => e.Kind == WrestlerBehaviorEventKind.Request),
                        PublicComplaints = state.Events.Count(e => e.Kind == WrestlerBehaviorEventKind.PublicComplaint),
                        InitiatedContacts = save.LockerRoomEvents.Count(e => e.ActorId == w.Id && e.Kind is LockerRoomEventKind.Contact or LockerRoomEventKind.Conflict),
                        Satisfaction = w.Condition.Satisfaction, Trust = state.Trust,
                        UnderpayAcceptance = ContractNegotiationService.Acceptance(save, w.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary * 4 / 5),
                        FairPayAcceptance = ContractNegotiationService.Acceptance(save, w.Id, ContractType.Wrestler, demand.SigningBonus, demand.MonthlySalary) });
                }
                Assert.That(metrics.Days, Is.EqualTo(730)); Assert.That(metrics.Shows, Is.EqualTo(104));
                Assert.That(metrics.People.All(p => float.IsFinite(p.Trust) && p.Trust is >= 0 and <= 100 && p.Satisfaction is >= 0 and <= 100 && p.FairPayAcceptance <= 95), Is.True);
                WrestlerBehaviorService.ValidateState(save);
                var output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reports", $"hidden-long-run-{seed}-{(responsive ? "responsive" : "ignored")}.json");
                File.WriteAllText(output, JsonUtility.ToJson(metrics, true));
                TestContext.WriteLine(JsonUtility.ToJson(metrics));
                Assert.That(metrics.FriendFraction, Is.LessThan(1), "Routine contact must leave room for distinct friendship groups.");
                Assert.That(metrics.SaturatedFraction, Is.LessThan(.9f), "Repeated ordinary work must not saturate almost every relationship.");
                Assert.That(metrics.People.Sum(p => p.Sociability > 10 ? p.InitiatedContacts : 0), Is.GreaterThan(metrics.People.Sum(p => p.Sociability < 10 ? p.InitiatedContacts : 0)));
                Assert.That(metrics.People.All(p => p.UnderpayAcceptance < p.FairPayAcceptance), Is.True);
            }
            finally { if (Directory.Exists(storagePath)) Directory.Delete(storagePath, true); }
        }

        private static void ControlledShow(GameSave save, int week, bool responsive)
        {
            var schedule = new ScheduleState { Id = Id(10000 + week), Date = save.CurrentDate, BookingDeadline = save.CurrentDate, Status = ScheduleStatus.Confirmed };
            var show = new ShowState { Id = Id(20000 + week), ScheduleId = schedule.Id, VenueContractId = save.VenueContracts[0].Id,
                Name = "Controlled weekly show", Date = save.CurrentDate, DurationLimit = 10, ShowVersion = 1, Status = ShowStatus.ResultsReviewed };
            save.Schedules.Add(schedule); save.Shows.Add(show);
            var selected = responsive ? (week - 1) % 3 * 2 : week % 2 == 0 ? 2 : 4;
            var a = save.Wrestlers[selected]; var b = save.Wrestlers[selected + 1];
            var plan = new MatchPlanState { Id = Id(30000 + week), MatchTypeId = "matchtype_001", ParticipantIds = new() { a.Id, b.Id }, WinnerId = a.Id, LoserTargetId = b.Id };
            save.MatchPlans.Add(plan);
            var entry = new ShowEventState { Id = Id(40000 + week), ShowId = show.Id, EventType = ShowEventType.Match, DetailId = plan.Id, PlannedDuration = 10 };
            save.ShowEvents.Add(entry); show.TimelineEventIds.Add(entry.Id); show.OpeningEventId = show.MainEventId = entry.Id;
            var match = new MatchResultState { Id = Id(50000 + week), ShowId = show.Id, ShowVersion = 1, ShowEventId = entry.Id, MatchPlanId = plan.Id,
                PlannedDuration = 10, ActualMatchDuration = 10, WinnerId = a.Id,
                ParticipantProfiles = new() { new() { SideId = "a", MemberIds = new() { a.Id } }, new() { SideId = "b", MemberIds = new() { b.Id } } } };
            if (week % 4 == 0) match.ExecutionEvents.Add(new() { EventType = MatchExecutionEventType.Mistake });
            save.MatchResults.Add(match);
            var result = new ShowResultState { Id = Id(60000 + week), ShowId = show.Id, ShowVersion = 1, HasAvailabilitySnapshot = true,
                AvailableWrestlerIds = WrestlerBehaviorService.AvailableForShow(save, save.CurrentDate),
                MatchResultIds = new() { match.Id }, TimelineResultIds = new() { match.Id } };
            var roles = result.AvailableWrestlerIds.ToDictionary(id => id, id => WrestlerBehaviorService.ExpectedRoleAt(save, save.Wrestlers.Single(w => w.Id == id), save.CurrentDate));
            WrestlerBehaviorService.CaptureCardPlacements(save, result, new[] { match }, roles); save.ShowResults.Add(result);
            new WrestlerBehaviorService().PrepareShow(save, result, new[] { a.Id, b.Id }).Apply();
            show.Status = ShowStatus.Completed; schedule.Status = ScheduleStatus.Completed;
        }
        private static string Id(int value) => "10000000-0000-0000-0000-" + value.ToString("D12");

        [TestCase(InjuryStatus.Moderate, WrestlerAvailability.Available, true)]
        [TestCase(InjuryStatus.Severe, WrestlerAvailability.Available, true)]
        [TestCase(InjuryStatus.Severe, WrestlerAvailability.Unavailable, false)]
        public void RecoveryDay_UsesRecoveredHealthForAdaptationWithoutChangingPreparedSource(
            InjuryStatus injury, WrestlerAvailability availability, bool progresses)
        {
            var save = CardPlacementBehaviorTests.Create(); var person = save.Wrestlers[0];
            WrestlerAdaptationService.InitializeSave(save, initialGame: true);
            person.Condition.Availability = availability;
            MatchHealth.ApplyActualInjury(person, injury, BodyRegion.Legs, save.CurrentDate, 2);
            person.Presentation.WrestlingStyleId = "style_002"; person.Adaptation.StyleId = "style_001";
            new TimeFlowService().AdvanceTo(save, save.CurrentDate.AddDays(1));
            Assert.That(person.Adaptation.StyleReadiness, Is.Zero);
            var before = JsonUtility.ToJson(person); var next = save.CurrentDate.AddDays(1);
            var prepared = WrestlerAdaptationService.PrepareDay(save, next);
            Assert.That(JsonUtility.ToJson(person), Is.EqualTo(before));
            Assert.That(prepared[person.Id].StyleReadiness > 0, Is.EqualTo(progresses));
            new TimeFlowService().AdvanceTo(save, next);
            Assert.That(person.Condition.InjuryStatus, Is.EqualTo(InjuryStatus.None));
            Assert.That(person.Condition.Availability, Is.EqualTo(availability));
            Assert.That(person.Adaptation.StyleReadiness > 0, Is.EqualTo(progresses));
        }
    }
}
