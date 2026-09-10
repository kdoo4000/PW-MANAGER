using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchEngineTests
    {
        [Test]
        public void EventSnapshots_PreserveActualBeforeAndAfter_IncludingFinishAndClamping()
        {
            var participants = new[] { Participant("a"), Participant("b") };
            participants[0].Fatigue = 99f;
            var state = new MatchSimulator(73).Simulate(participants, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
            Assert.That(state.Events[0].Before.Participants[0].Fatigue, Is.EqualTo(99f));
            for (var i = 0; i < state.Events.Count; i++)
            {
                var value = state.Events[i];
                Assert.That(value.After.Phase, Is.EqualTo(value.Phase));
                Assert.That(value.After.Participants.Count, Is.EqualTo(2));
                foreach (var wrestler in value.After.Participants)
                {
                    Assert.That(wrestler.Fatigue, Is.InRange(0f, 100f));
                    Assert.That(wrestler.MatchControl, Is.InRange(0f, 100f));
                    Assert.That(wrestler.ActionReadiness, Is.InRange(0f, 100f));
                }
                if (i > 0) Assert.That(UnityEngine.JsonUtility.ToJson(value.Before),
                    Is.EqualTo(UnityEngine.JsonUtility.ToJson(state.Events[i - 1].After)));
            }
            Assert.That(UnityEngine.JsonUtility.ToJson(state.Events.Last().After),
                Is.EqualTo(UnityEngine.JsonUtility.ToJson(MatchEngineSnapshot.Capture(state))));
            participants[0].Fatigue = 0;
            Assert.That(state.Events[0].Before.Participants[0].Fatigue, Is.EqualTo(99f));
        }

        [TestCase(MatchFinishType.Pinfall, MatchActionResult.Pinfall)]
        [TestCase(MatchFinishType.Submission, MatchActionResult.Submitted)]
        [TestCase(MatchFinishType.RollUp, MatchActionResult.RollUp)]
        [TestCase(MatchFinishType.Disqualification, MatchActionResult.Disqualification)]
        [TestCase(MatchFinishType.CountOut, MatchActionResult.CountOut)]
        [TestCase(MatchFinishType.Draw, MatchActionResult.Draw)]
        public void BookedSingles_OnlyEndsWithBookedFinish_AtAllottedTime(MatchFinishType finish, MatchActionResult outcome)
        {
            foreach (var seed in Enumerable.Range(0, 12))
            {
                MatchEngineState Run() => new MatchSimulator(seed).Simulate(new[] { Participant("a"), Participant("b") }, 600,
                    new MatchEngineBooking { WinnerId = "b", LoserId = "a", FinishType = finish });
                var state = Run();
                Assert.That(state.IsFinished, Is.True);
                Assert.That(state.IsAborted, Is.False);
                Assert.That(state.WinnerId, Is.EqualTo(finish == MatchFinishType.Draw ? null : "b"));
                Assert.That(state.ElapsedSeconds, Is.EqualTo(600));
                Assert.That(state.Events.Sum(x => x.DurationSeconds), Is.EqualTo(600));
                Assert.That(state.Events.All(x => x.DurationSeconds > 0), Is.True);
                Assert.That(state.Events.Last().Type, Is.EqualTo(MatchActionType.Finish));
                Assert.That(state.Events.Last().Result, Is.EqualTo(outcome));
                Assert.That(state.Events.Take(state.Events.Count - 1).Any(x => x.Result is MatchActionResult.Pinfall or MatchActionResult.Submitted), Is.False);
                for (var i = 1; i < state.Events.Count; i++)
                    Assert.That(state.Events[i].MatchTimeSeconds, Is.EqualTo(state.Events[i - 1].MatchTimeSeconds + state.Events[i - 1].DurationSeconds));
                Assert.That(Run().Events.Select(Key), Is.EqualTo(state.Events.Select(Key)));
            }
        }

        [Test]
        public void BookedSingles_RejectsInvalidBooking_AndDoesNotInventFinishAtEventLimit()
        {
            var participants = new[] { Participant("a"), Participant("b") };
            Assert.Throws<ArgumentException>(() => new MatchSimulator(1).Simulate(participants, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "a" }));
            Assert.Throws<ArgumentException>(() => new MatchSimulator(1).Simulate(participants, 600,
                new MatchEngineBooking { WinnerId = "missing", LoserId = "b" }));
            Assert.Throws<ArgumentException>(() => new MatchSimulator(1).Simulate(participants, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.Escape }));
            var result = new MatchSimulator(1, 0).Simulate(participants, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
            Assert.That(result.IsAborted, Is.True);
            Assert.That(result.WinnerId, Is.Null);
        }

        [Test]
        public void SameSeed_ReplaysSameMatch_AndTerminatesWithCompleteEvents()
        {
            var first = Simulate(73);
            var second = Simulate(73);
            Assert.That(first.IsFinished, Is.True);
            Assert.That(first.IsAborted, Is.False);
            Assert.That(first.Events.Count, Is.LessThanOrEqualTo(MatchSimulator.MaximumEvents));
            Assert.That(first.Events.Select(Key), Is.EqualTo(second.Events.Select(Key)));
            Assert.That(first.Events, Has.All.Matches<MatchEngineEvent>(x => x.PerformerId != null && x.ReceiverId != null && x.CommentaryKey != null));
            Assert.That(first.Events.Select(x => x.Phase).Distinct().Count(), Is.GreaterThan(1));
        }

        [Test]
        public void PinChance_IncreasesWithFatigue_AndFinisher()
        {
            var state = State();
            var performer = state.GetParticipant("a");
            var receiver = state.GetParticipant("b");
            receiver.Fatigue = 20f;
            var fresh = ActionResolver.PinfallChance(state, performer, receiver, 3f, false);
            receiver.Fatigue = 80f;
            var tired = ActionResolver.PinfallChance(state, performer, receiver, 3f, false);
            var finisher = ActionResolver.PinfallChance(state, performer, receiver, 18f, true);
            Assert.That(tired, Is.GreaterThan(fresh));
            Assert.That(finisher, Is.GreaterThan(tired));
        }

        [Test]
        public void RepeatedAction_LosesWeight()
        {
            var state = State();
            var action = new MatchEngineAction { Type = MatchActionType.BasicStrike, PerformerId = "a", ReceiverId = "b" };
            var initial = ActionEvaluator.Score(state, action);
            state.Events.Add(new MatchEngineEvent { Type = action.Type, PerformerId = "a" });
            Assert.That(ActionEvaluator.Score(state, action), Is.LessThan(initial));
        }

        [Test]
        public void PerformerSelection_UsesControlReadinessAndDownedPenalty()
        {
            var state = State();
            var high = state.GetParticipant("a");
            var low = state.GetParticipant("b");
            high.MatchControl = 90f;
            high.ActionReadiness = 90f;
            low.MatchControl = 10f;
            low.ActionReadiness = 30f;
            var standingScore = PerformerSelector.Score(state, high);
            var random = new Random(10);
            var highCount = Enumerable.Range(0, 500).Count(_ => PerformerSelector.Select(state, random).Id == high.Id);
            Assert.That(highCount, Is.GreaterThan(250));
            Assert.That(highCount, Is.LessThan(500), "Low-control participants must retain a comeback chance.");
            high.IsDowned = true;
            Assert.That(PerformerSelector.Score(state, high), Is.LessThan(standingScore - 40f));
        }

        [Test]
        public void ThreeWayMatch_UsesMultiplePerformersAndTargets()
        {
            var participants = new[] { Participant("a"), Participant("b"), Participant("c") };
            var state = new MatchSimulator(91).Simulate(participants, 480);
            Assert.That(state.IsFinished || state.IsAborted, Is.True);
            Assert.That(state.Events.Select(x => x.PerformerId).Distinct().Count(), Is.GreaterThan(1));

            var targetState = new MatchEngineState { Participants = participants.ToList(), Phase = MatchPhase.Neutral };
            var random = new Random(22);
            var targets = Enumerable.Range(0, 100).Select(_ => TargetSelector.Select(targetState, participants[0], random).Id).Distinct().ToList();
            Assert.That(targets, Is.EquivalentTo(new[] { "b", "c" }));
        }

        [Test]
        public void EventLimit_AbortsWithoutInventingWinner()
        {
            var result = new MatchSimulator(1, 0).Simulate(new[] { Participant("a"), Participant("b") }, 1);
            Assert.That(result.IsAborted, Is.True);
            Assert.That(result.IsFinished, Is.False);
            Assert.That(result.WinnerId, Is.Null);
        }

        [Test]
        public void StateMachine_AllowsNonAdjacentTransitionsAndReentry()
        {
            var decisiveOpening = State(MatchPhase.Opening);
            decisiveOpening.ElapsedSeconds = 60;
            decisiveOpening.Events.Add(new MatchEngineEvent { Phase = MatchPhase.Opening, Type = MatchActionType.Finisher, Impact = 18f, IsMajorMoment = true });
            Assert.That(CanTransition(MatchPhase.Opening, MatchPhase.Climax, decisiveOpening), Is.True);
            Assert.That(CanTransition(MatchPhase.Opening, MatchPhase.Finish, decisiveOpening), Is.True);

            var dramatic = State(MatchPhase.Climax);
            dramatic.ElapsedSeconds = 500;
            dramatic.Drama = 85f;
            dramatic.Events.Add(new MatchEngineEvent { Phase = MatchPhase.Climax, Result = MatchActionResult.NearFall, IsMajorMoment = true });
            Assert.That(CanTransition(MatchPhase.Climax, MatchPhase.BackAndForth, dramatic), Is.True);
            Assert.That(CanTransition(MatchPhase.BackAndForth, MatchPhase.Climax, dramatic), Is.True);
            Assert.That(CanTransition(MatchPhase.Climax, MatchPhase.Finish, dramatic), Is.True);
        }

        [Test]
        public void ComebackState_BoostsTrailingPerformerWithoutForcingResult()
        {
            var state = State(MatchPhase.Comeback);
            var trailing = state.GetParticipant("a");
            var leader = state.GetParticipant("b");
            trailing.MatchControl = 15f;
            leader.MatchControl = 85f;
            trailing.ActionReadiness = 80f;
            state.Events.Add(new MatchEngineEvent { Phase = MatchPhase.Control, PerformerId = leader.Id, ReceiverId = trailing.Id });
            var comeback = new ComebackMatchState();
            Assert.That(comeback.GetPerformerScoreModifier(state, trailing), Is.GreaterThan(comeback.GetPerformerScoreModifier(state, leader)));
            Assert.That(comeback.GetActionScoreModifier(state, new MatchEngineAction { Type = MatchActionType.HeavyStrike }), Is.GreaterThan(1f));
        }

        [Test]
        public void FinishState_CanHoldOrReturnToClimax()
        {
            var state = State(MatchPhase.Finish);
            state.ElapsedSeconds = 900;
            Assert.That(CanTransition(MatchPhase.Finish, MatchPhase.Finish, state), Is.True);
            Assert.That(CanTransition(MatchPhase.Finish, MatchPhase.Climax, state), Is.True);
        }

        [Test]
        public void StateMachine_IsAuthoritativeAndPhaseOwnsActionModifiers()
        {
            var state = State(MatchPhase.Opening);
            var machine = new MatchStateMachine(new Random(4));
            machine.Initialize(state, MatchPhase.Finish);
            state.Phase = MatchPhase.Opening;
            machine.Update(state);
            Assert.That(state.Phase, Is.EqualTo(machine.CurrentState.Phase));

            var pin = new MatchEngineAction { Type = MatchActionType.Pin, PerformerId = "a", ReceiverId = "b" };
            var basic = new MatchEngineAction { Type = MatchActionType.BasicStrike, PerformerId = "a", ReceiverId = "b" };
            var opening = new OpeningMatchState();
            Assert.That(ActionEvaluator.Score(state, pin, opening), Is.LessThan(ActionEvaluator.Score(state, pin)));
            Assert.That(ActionEvaluator.Score(state, basic, opening), Is.GreaterThan(ActionEvaluator.Score(state, basic)));
        }

        [Test]
        public void EqualMatches_DoNotReplayOneFixedPhaseSchedule()
        {
            var sequences = Enumerable.Range(0, 12).Select(PhaseSequence).ToList();
            Assert.That(sequences.Distinct().Count(), Is.GreaterThan(1));
            Assert.That(sequences.Any(x => x.Contains(nameof(MatchPhase.Neutral)) || x.Contains(nameof(MatchPhase.Control))), Is.True);
            Assert.That(sequences.Any(x => x.Contains(nameof(MatchPhase.Comeback)) || x.Contains(nameof(MatchPhase.BackAndForth)) || x.Contains(nameof(MatchPhase.Climax))), Is.True);
        }

        private static MatchEngineState Simulate(int seed) => new MatchSimulator(seed).Simulate(Participant("a"), Participant("b"), 480);
        private static MatchEngineState State(MatchPhase phase = MatchPhase.Climax) => new() { Participants = new List<MatchEngineParticipant> { Participant("a"), Participant("b") }, Phase = phase };
        private static bool CanTransition(MatchPhase source, MatchPhase target, MatchEngineState state)
        {
            for (var seed = 0; seed < 5000; seed++)
            {
                var copy = new MatchEngineState
                {
                    Participants = state.Participants, Events = state.Events, Phase = source, ElapsedSeconds = state.ElapsedSeconds,
                    TargetDurationSeconds = state.TargetDurationSeconds, Drama = state.Drama, Intensity = state.Intensity, CrowdHeat = state.CrowdHeat
                };
                var machine = new MatchStateMachine(new Random(seed));
                machine.Initialize(copy, source);
                machine.Update(copy);
                if (copy.Phase == target) return true;
            }
            return false;
        }
        private static string PhaseSequence(int seed)
        {
            var state = State(MatchPhase.Opening);
            var machine = new MatchStateMachine(new Random(seed));
            machine.Initialize(state);
            var phases = new List<MatchPhase>();
            for (var step = 0; step < 30; step++)
            {
                state.ElapsedSeconds += 25;
                machine.Update(state);
                phases.Add(state.Phase);
                state.Events.Add(new MatchEngineEvent { Phase = state.Phase, PerformerId = step % 2 == 0 ? "a" : "b", ReceiverId = step % 2 == 0 ? "b" : "a" });
            }
            return string.Join(",", phases);
        }
        private static string Key(MatchEngineEvent value) => $"{value.MatchTimeSeconds}:{value.Phase}:{value.PerformerId}:{value.ReceiverId}:{value.Type}:{value.Result}";
        private static MatchEngineParticipant Participant(string id) => new()
        {
            Id = id,
            Attributes = new WrestlerAttributesState { Brawling = 12f, Power = 12f, Technical = 12f, HighFlying = 12f, Selling = 12f, Stamina = 12f, RingPsychology = 12f }
        };
    }
}
