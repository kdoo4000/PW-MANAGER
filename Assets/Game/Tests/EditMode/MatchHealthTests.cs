using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchHealthTests
    {
        private static MatchEngineParticipant P(string id) => new() { Id = id, SideId = id,
            Attributes = new WrestlerAttributesState { Brawling = 15, Power = 15, Technical = 15,
                HighFlying = 15, Selling = 15, Stamina = 18, RingPsychology = 18, RingImprovisation = 12 } };

        private static MatchEngineState State() => new() { EngineVersion = 4, Participants = new() { P("a"), P("b") },
            TargetDurationSeconds = 300, ElapsedSeconds = 290, AccidentsEnabled = false,
            Booking = new() { WinnerId = "a", LoserId = "b" } };

        [Test]
        public void PreviewConditionDoesNotChangeOriginal()
        {
            var wrestler = new WrestlerState();
            var preview = wrestler.CopyForGroupSimulation();
            preview.Condition.Condition = 20;
            Assert.That(wrestler.Condition.Condition, Is.EqualTo(100));
        }

        [Test]
        public void KayfabeLegDamageDoesNotPhysicallyPreventAerialTechnique()
        {
            var actor = new MatchEngineParticipant { LegDamage = 100,
                Attributes = new WrestlerAttributesState { HighFlying = 15 } };
            Assert.That(MatchTechniques.CanExecute(new MatchMoveRules { Kind = MoveKind.Aerial }, actor), Is.True);
        }

        [Test]
        public void SevereInjuryStopsBeforeAnyQueuedCoverOrSpot()
        {
            var state = State();
            state.Exchange = new MatchExchange { Kind = ExchangeKind.Cover, ActorId = "a", TargetId = "b", Booked = true };
            MatchHealth.ApplyInjury(state, state.Participants[1], InjuryStatus.Severe, BodyRegion.Head);
            var e = new MatchSimulator(2).Step(state);
            Assert.That(e.Type, Is.EqualTo(MatchActionType.MedicalStop));
            Assert.That(state.IsFinished, Is.True);
            Assert.That(state.WinnerId, Is.Null);
            Assert.That(state.Events, Has.Count.EqualTo(1));
            Assert.That(e.Motion, Has.Count.GreaterThanOrEqualTo(2));
            var result = new MatchResultState { EngineState = state, WasStoppedByInjury = true,
                SpotResults = new() { new() { Phase = MatchSpotPhase.PostMatch, ScriptedMoment = "예약된 경기 후 공격" } } };
            var lines = new MatchNarrationService().Narrate(new MatchPlanState { ClosingSpot = "예약된 클로징 공격" }, result, id => id);
            Assert.That(lines.Any(line => line.Text.Contains("예약된")), Is.False);
        }

        [Test]
        public void NoSafeLegalRescueReturnsExplicitFailureWithoutFakingWinner()
        {
            var state = State();
            MatchHealth.ApplyInjury(state, state.Participants[0], InjuryStatus.Moderate, BodyRegion.Head);
            new MatchSimulator(2).Step(state);
            Assert.That(state.IsAborted, Is.True);
            Assert.That(state.IsFinished, Is.False);
            Assert.That(state.WinnerId, Is.Null);
            Assert.That(state.Events.Any(e => e.IsPhysicalAttack), Is.False);
        }

        [Test]
        public void OutsiderSpotDoesNotRequireParticipantDecisionEvidence()
        {
            var state = State();
            state.ScheduledSpots.Add(new() { SpotId = "outsider", ActorId = "c", TargetId = "b", Phase = MatchSpotPhase.Middle });
            var e = new MatchSimulator(2).Step(state);
            Assert.That(e.Type, Is.EqualTo(MatchActionType.Spot));
            Assert.That(e.PerformerId, Is.EqualTo("c"));
            Assert.That(e.HasDecisionEvidence, Is.False);
        }

        [TestCase(260)]
        [TestCase(313)]
        public void ReadyBookedCoverFinishesInsideWindow(int elapsed)
        {
            var state = State(); state.ElapsedSeconds = elapsed;
            state.Participants[1].IsDowned = true; state.Participants[1].Posture = MatchPosture.Supine;
            state.Exchange = new() { Kind = ExchangeKind.Cover, ActorId = "a", TargetId = "b", Booked = true };
            var simulator = new MatchSimulator(2);
            for (int i = 0; i < 20 && !state.IsFinished && !state.IsAborted; i++) simulator.Step(state);
            Assert.That(state.IsFinished, Is.True, state.FailureReason);
            Assert.That(state.ElapsedSeconds, Is.InRange(285, 315));
            Assert.That(state.Participants[0].Falls, Is.EqualTo(1));
        }

        [Test]
        public void DamagedLimbCounterNeedsPainContinuity()
        {
            MatchEngineEvent Evidence(bool priorSelling)
            {
                var state = State(); state.Participants[0].LegDamage = 50; state.Participants[0].BodyDamage = 80;
                state.Participants[0].Attributes.Selling = 3;
                if (priorSelling) state.Events.Add(new() { SellingWrestlerId = "a", SellingRegion = BodyRegion.Legs, SellingProtectedRegion = true });
                var e = new MatchEngineEvent { PerformerId = "a", ReceiverId = "b", Type = MatchActionType.RunningAttack, IsPhysicalAttack = true };
                MatchHealth.RecordEvidence(state, e, MatchEngineSnapshot.Capture(state)); return e;
            }
            var coherent = Evidence(true); var broken = Evidence(false);
            Assert.That(broken.SellingContradiction, Is.True);
            Assert.That(broken.SellingRegion, Is.EqualTo(BodyRegion.Legs));
            Assert.That(coherent.SellingContradiction, Is.False);
            Assert.That(coherent.SellingQuality, Is.GreaterThan(broken.SellingQuality));
        }

        [TestCase(3, 11)]
        [TestCase(18, 3)]
        public void RescuePreparationUsesBothWrestlersImprovisation(int ability, int seconds)
        {
            var state = State(); foreach (var p in state.Participants) p.Attributes.RingImprovisation = ability;
            MatchHealth.ApplyInjury(state, state.Participants[1], InjuryStatus.Moderate, BodyRegion.Arms);
            var e = new MatchSimulator(2).Step(state);
            Assert.That(e.IsRescuePreparation, Is.True);
            Assert.That(e.DurationSeconds, Is.EqualTo(seconds));
            Assert.That(e.ExecutionQuality, Is.EqualTo(.5f + ability * .025f).Within(.001));
        }

        [Test]
        public void MinorInjuryKeepsQueuedScheduledFinish()
        {
            var state = State(); state.Participants[1].IsDowned = true;
            state.Exchange = new() { Kind = ExchangeKind.Cover, ActorId = "a", TargetId = "b", Booked = true };
            MatchHealth.ApplyInjury(state, state.Participants[1], InjuryStatus.Minor, BodyRegion.Head);
            var e = new MatchSimulator(2).Step(state);
            Assert.That(e.Type, Is.EqualTo(MatchActionType.Finish));
            Assert.That(state.RescueRequired, Is.False);
            Assert.That(state.WinnerId, Is.EqualTo("a"));
        }

        [Test]
        public void TableRescuePreservesAllowedObjectiveAndUsesSafeAlternative()
        {
            var state = State(); state.Rules = new() { Objective = MatchObjective.Table, Pinfall = false, Submission = false };
            state.Booking.FinishType = MatchFinishType.TableBreak;
            state.Participants[0].FinisherMoveId = "old"; state.Participants[0].SignatureMoveId = "safe";
            state.FindMove = id => new() { Kind = MoveKind.Strike, BrawlingWeight = 1,
                TargetRegion = id == "old" ? BodyRegion.Arms : BodyRegion.Body, Name = id };
            MatchHealth.ApplyInjury(state, state.Participants[1], InjuryStatus.Moderate, BodyRegion.Arms);
            var simulator = new MatchSimulator(2);
            for (int i = 0; i < 50 && !state.IsFinished && !state.IsAborted; i++) simulator.Step(state);
            Assert.That(state.IsFinished, Is.True, state.FailureReason);
            Assert.That(state.ActualFinish, Is.EqualTo(MatchFinishType.TableBreak));
            Assert.That(state.Events.Any(e => e.IsPhysicalAttack && e.MoveId == "safe"), Is.True);
        }

        [Test]
        public void ModerateReceiverInjuryReplacesOriginalFinishAndPreservesWinner()
        {
            var state = State();
            state.Participants[0].FinisherMoveId = "old";
            state.Participants[0].PlannedFinisherMoveId = "old";
            state.Participants[0].SignatureMoveId = "safe";
            state.FindMove = id => new MatchMoveRules { Kind = MoveKind.Strike, BrawlingWeight = 1,
                TargetRegion = id == "old" ? BodyRegion.Body : BodyRegion.Legs, Name = id };
            MatchHealth.ApplyInjury(state, state.Participants[1], InjuryStatus.Moderate, BodyRegion.Body);
            var simulator = new MatchSimulator(2);
            for (int i = 0; i < 40 && !state.IsFinished && !state.IsAborted; i++) simulator.Step(state);
            Assert.That(state.IsFinished, Is.True, state.FailureReason);
            Assert.That(state.WinnerId, Is.EqualTo("a"));
            Assert.That(state.Events.Any(e => e.MoveId == "old" && e.IsPhysicalAttack), Is.False);
            Assert.That(state.Events.Any(e => e.MoveId == "safe" && e.IsPhysicalAttack), Is.True);
        }

        [Test]
        public void IronManLeadDoesNotEndMatchBeforeExactTime()
        {
            var state = new MatchSimulator(3, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 300,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" }, new MatchRuntimeRules { IronMan = true });
            Assert.That(state.IsFinished, Is.True, state.FailureReason);
            Assert.That(state.ElapsedSeconds, Is.EqualTo(300));
            Assert.That(state.WinnerId, Is.EqualTo("a"));
        }

        [Test]
        public void TimeAndStaminaCorrectionsAreContinuous()
        {
            var duration = typeof(MatchEvaluator).GetMethod("GetDurationMultiplier", BindingFlags.Static | BindingFlags.NonPublic);
            var overtime = typeof(MatchEvaluator).GetMethod("GetStaminaPenalty", BindingFlags.Static | BindingFlags.NonPublic);
            float Factor(float minutes) => (float)duration.Invoke(null, new object[] { 9f,
                Convert.ChangeType(minutes, duration.GetParameters()[1].ParameterType) });
            Assert.That(Math.Abs(Factor(29) - Factor(30)) * 20, Is.LessThan(2));
            foreach (var minute in new[] { 10f, 29f })
                Assert.That(Math.Abs(Factor(minute) - Factor(minute + 1f / 60)) * 9, Is.LessThanOrEqualTo(.02));
            foreach (float boundary in new[] { 0f, 5f, 10f, 15f, 20f })
            {
                var left = (float)overtime.Invoke(null, new object[] { boundary - 1f / 60 });
                var right = (float)overtime.Invoke(null, new object[] { boundary + 1f / 60 });
                var center = (float)overtime.Invoke(null, new object[] { boundary });
                Assert.That(Math.Abs(left - center), Is.LessThanOrEqualTo(.02));
                Assert.That(Math.Abs(right - center), Is.LessThanOrEqualTo(.02));
            }
        }

        [Test]
        public void StoryAndMedicalInjuriesSaveAndRecoverIndependently()
        {
            var wrestler = new WrestlerState { Id = "a" };
            wrestler.Condition.Availability = WrestlerAvailability.MatchUnavailable;
            var save = new GameSave { Wrestlers = new() { wrestler } };
            var result = new MatchResultState { Id = "r", ParticipantProfiles = new() { new() { MemberIds = new() { "a" } } },
                InjuryChanges = new() { new() { SourceResultId = "r", WrestlerId = "a", Severity = InjuryStatus.Minor,
                Region = BodyRegion.Arms, RecoveryDays = 7 }, new() { SourceResultId = "r", WrestlerId = "a", IsKayfabe = true,
                Severity = InjuryStatus.Severe, Region = BodyRegion.Legs, RecoveryDays = 21, Absent = true } } };
            var start = new GameDate(2026, 10, 9);
            MatchHealth.ApplyResult(save, result, start);
            var restored = UnityEngine.JsonUtility.FromJson<WrestlerState>(UnityEngine.JsonUtility.ToJson(wrestler));
            MatchHealth.Recover(restored, start.AddDays(7));
            Assert.That(restored.Condition.InjuryStatus, Is.EqualTo(InjuryStatus.None));
            Assert.That(restored.Condition.Availability, Is.EqualTo(WrestlerAvailability.MatchUnavailable));
            Assert.That(restored.KayfabeInjury.Severity, Is.EqualTo(InjuryStatus.Severe));
            MatchHealth.Recover(restored, start.AddDays(21));
            Assert.That(restored.KayfabeInjury.Severity, Is.EqualTo(InjuryStatus.None));
        }

        [Test]
        public void StoryInjuryCancelsLaterMatchWithoutChangingOriginalUntilApply()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.MatchPlans[0].StoryInjuries.Add(new() { WrestlerId = "b", Region = BodyRegion.Arms,
                Severity = InjuryStatus.Moderate, Days = 14, Absent = true });
            save.MatchPlans.Add(new() { Id = "second", MatchTypeId = "singles", ParticipantIds = new() { "a", "b" },
                WinnerId = "a", LoserTargetId = "b" });
            save.ShowEvents.Add(new() { Id = "second-event", ShowId = "show", EventType = ShowEventType.Match,
                DetailId = "second", PlannedDuration = 10 });
            save.Shows[0].TimelineEventIds.Add("second-event"); save.Shows[0].DurationLimit = 30;
            var show = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 1000, 100);
            var first = save.MatchResults.First(); var cancelled = save.MatchResults.Last();
            Assert.That(first.CancellationReason, Is.Null);
            Assert.That(cancelled.CancellationReason, Does.Contain("각본상"));
            Assert.That(cancelled.ActualMatchDuration, Is.Zero);
            Assert.That(cancelled.CriticReview, Is.Null);
            Assert.That(save.Wrestlers.All(w => w.Condition.Condition == 100 && w.KayfabeInjury.Severity == InjuryStatus.None), Is.True);
            save.Shows[0].Status = ShowStatus.ResultsReviewed;
            new ShowResultApplicationService().Apply(save, show.Id);
            Assert.That(save.Wrestlers.Single(w => w.Id == "b").KayfabeInjury.Absent, Is.True);
            Assert.That(save.Wrestlers.All(w => w.Status.OfficialMatchCount == 1), Is.True);
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, show.Id));
        }

        [Test]
        public void InvalidMedicalResultIsRejectedBeforeAnySavedStateChanges()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var show = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 1000, 100);
            var match = save.MatchResults.Single();
            show.TimelineResultIds.Clear();
            match.InjuryChanges.Add(new() { SourceResultId = "other-result", WrestlerId = "a",
                Severity = InjuryStatus.Severe, Region = BodyRegion.Head, RecoveryDays = 60 });
            save.Shows[0].Status = ShowStatus.ResultsReviewed;
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, show.Id));
            Assert.That(save.Transactions, Is.Empty);
            Assert.That(save.Wrestlers.All(w => w.Condition.Condition == 100 && w.Condition.InjuryStatus == InjuryStatus.None), Is.True);
        }

        [Test]
        public void SellingDoesNotEnterControlledAccidentInputs()
        {
            int accidents = 0;
            for (int seed = 0; seed < 500; seed++)
            {
                var low = State(); var high = State(); low.AccidentsEnabled = high.AccidentsEnabled = true;
                low.Participants[1].Attributes.Selling = 3; high.Participants[1].Attributes.Selling = 18;
                MatchEngineEvent E() => new() { PerformerId = "a", ReceiverId = "b", Type = MatchActionType.Dive,
                    IsPhysicalAttack = true, PhysicalExecutionFailed = true, TargetRegion = BodyRegion.Body, ContactDifficulty = 12 };
                MatchHealth.Observe(low, E(), new Random(seed)); MatchHealth.Observe(high, E(), new Random(seed));
                Assert.That(high.Participants.Select(p => p.RealInjury), Is.EqualTo(low.Participants.Select(p => p.RealInjury)));
                if (low.Participants.Any(p => p.InjuredThisMatch)) accidents++;
            }
            Assert.That(accidents, Is.GreaterThan(0));
        }

        private sealed class SafetyRolls : Random
        {
            private readonly System.Collections.Generic.Queue<double> values;
            public SafetyRolls(params double[] rolls) => values = new(rolls);
            public override double NextDouble() => values.Dequeue();
        }

        [TestCase(.7999, InjuryStatus.Minor)]
        [TestCase(.8, InjuryStatus.Moderate)]
        [TestCase(.9799, InjuryStatus.Moderate)]
        [TestCase(.98, InjuryStatus.Severe)]
        public void FailedRiskyExecutionUsesActualSeverityThresholds(double roll, InjuryStatus severity)
        {
            var state = State(); state.AccidentsEnabled = true;
            var e = new MatchEngineEvent { PerformerId = "a", ReceiverId = "b", Type = MatchActionType.Dive,
                IsPhysicalAttack = true, PhysicalExecutionFailed = true, TargetRegion = BodyRegion.Legs };
            MatchHealth.Observe(state, e, new SafetyRolls(0, .009, roll));
            Assert.That(state.Participants[0].RealInjury, Is.EqualTo(severity));
            Assert.That(state.Participants[0].RealInjuryRegion, Is.EqualTo(BodyRegion.Body));
            Assert.That(state.WasStoppedByInjury, Is.EqualTo(severity == InjuryStatus.Severe));
            Assert.That(state.RescueRequired, Is.EqualTo(severity == InjuryStatus.Moderate));
        }

        [Test]
        public void SafeLimbTargetingDoesNotConvertStoryDamageIntoActualInjury()
        {
            var state = State(); state.AccidentsEnabled = true; state.Participants[1].LegDamage = 100;
            var e = new MatchEngineEvent { PerformerId = "a", ReceiverId = "b", Type = MatchActionType.LimbAttack,
                IsPhysicalAttack = true, TargetRegion = BodyRegion.Legs };
            MatchHealth.Observe(state, e, new SafetyRolls(0));
            Assert.That(state.Participants.All(p => p.RealInjury == InjuryStatus.None), Is.True);
        }

        [TestCase(.00009, true)]
        [TestCase(.00011, false)]
        public void SuccessfulRiskyTechniqueUsesRareAccidentThreshold(double probabilityRoll, bool injured)
        {
            var state = State(); state.AccidentsEnabled = true;
            var e = new MatchEngineEvent { PerformerId = "a", ReceiverId = "b", Type = MatchActionType.Dive, IsPhysicalAttack = true };
            MatchHealth.Observe(state, e, new SafetyRolls(0, probabilityRoll, 0));
            Assert.That(state.Participants[1].InjuredThisMatch, Is.EqualTo(injured));
            Assert.That(state.Participants[0].InjuredThisMatch, Is.False);
        }

        [Test]
        public void SensibleChoicesScoreAboveUnpreparedRepetitionAndHeatHasNoDirectBonus()
        {
            MatchEngineState Recorded(bool sensible) => new() { Events = new() {
                new() { Type = MatchActionType.Cover, HasDecisionEvidence = true, ValidCover = sensible },
                new() { Type = MatchActionType.Grapple, HasDecisionEvidence = true, IsPhysicalAttack = true,
                    ContextualChoice = sensible, SellingQuality = .8f, ExecutionQuality = .8f } } };
            var good = Recorded(true); var bad = Recorded(false);
            var goodQuality = MatchEvaluator.ObserveQuality(good);
            Assert.That(goodQuality.ObservedDecisions, Is.GreaterThan(MatchEvaluator.ObserveQuality(bad).ObservedDecisions));
            good.CrowdHeat = 100;
            Assert.That(MatchEvaluator.ObserveQuality(good).ObservedDecisions, Is.EqualTo(goodQuality.ObservedDecisions));
        }
    }
}
