using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchViewerFlowTests
    {
        private static MatchEngineState State() => new()
        {
            ElapsedSeconds = 500, TargetDurationSeconds = 600,
            Participants = new()
            {
                new() { Id = "a", SideId = "a", Attributes = new() { Technical = 12, RingPsychology = 12, Stamina = 12 }, TargetRegion = BodyRegion.Body },
                new() { Id = "b", SideId = "b", Attributes = new() { Technical = 12, RingPsychology = 12, Stamina = 12 }, IsDowned = true, Posture = MatchPosture.Supine }
            }
        };

        [SetUp] public void LoadMoves() => PWManager.Data.Loading.MoveTable.Initialize();

        [Test]
        public void OrdinaryOffenseCanLiftWithoutAPrimedFinisher()
        {
            var s = State(); s.Participants[0].MatchControl = 40;
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1])
                .Any(x => x.Type == MatchActionType.Lift), Is.True);
        }

        [Test]
        public void LiftContinuesIntoOffenseIncludingWhipsAndCanBeInterrupted()
        {
            bool whip = false, interrupted = false;
            foreach (int seed in Enumerable.Range(0, 100))
            {
                var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
                a.Attributes.Power = a.Attributes.Brawling = 12; s.Opening.Completed = true;
                s.FindMove = id => id == null ? throw new ArgumentNullException(nameof(id)) : MatchTechniques.Find(id)?.Rules;
                MatchPhysics.Resolve(s, new() { Type = MatchActionType.Lift, PerformerId = "a", ReceiverId = "b" }, new Random(seed));
                Assert.That(s.Exchange?.Kind, Is.EqualTo(ExchangeKind.Preparation));
                var next = new MatchSimulator(seed, accidentsEnabled: false).Step(s);
                Assert.That(next.Type, Is.Not.EqualTo(MatchActionType.Lift));
                Assert.That(next.Type, Is.Not.EqualTo(MatchActionType.TurnOver));
                whip |= next.Type is MatchActionType.IrishWhip or MatchActionType.CornerWhip;
                interrupted |= next.Result == MatchActionResult.Blocked;
            }
            Assert.That(whip, Is.True, "Lifting should be able to lead into rope or corner offense.");
            Assert.That(interrupted, Is.True, "A setup must leave an opening for the opponent.");
        }

        [TestCase(MatchPosition.Center)]
        [TestCase(MatchPosition.Ropes)]
        public void GeneralTurnOverHasAnExecutableAttackAndDoesNotChangeThePoseWhileChoosing(MatchPosition position)
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            b.Position = position;
            var choices = MatchPhysics.Actions(s, a, b).Where(x => x.Type == MatchActionType.TurnOver && x.MoveId != null).ToList();
            Assert.That(choices, Is.Not.Empty);
            Assert.That(b.Posture, Is.EqualTo(MatchPosture.Supine));
            foreach (var setup in choices)
            {
                var copy = UnityEngine.JsonUtility.FromJson<MatchEngineState>(UnityEngine.JsonUtility.ToJson(s));
                MatchPhysics.Resolve(copy, setup, new Random(1));
                Assert.That(copy.Exchange?.Kind, Is.EqualTo(ExchangeKind.Preparation));
                Assert.That(copy.Participants[1].Posture, Is.Not.EqualTo(MatchPosture.Supine));
                Assert.That(MatchPhysics.CanMove(copy, copy.Participants[0], copy.Participants[1], setup.MoveId), Is.True);
                var next = new MatchSimulator(1, accidentsEnabled: false).Step(copy);
                Assert.That(next.Type, Is.Not.EqualTo(MatchActionType.TurnOver));
            }
        }

        [Test]
        public void GeneralSetupsRespectInjuriesAndDoNotDivertAFreshFinisherCover()
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            a.RealInjury = InjuryStatus.Moderate; a.RealInjuryRegion = BodyRegion.Arms;
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type is MatchActionType.Lift or MatchActionType.TurnOver), Is.False);
            a.RealInjury = InjuryStatus.None;
            b.LastImpactWasFinisher = true; b.LastImpactTime = s.ElapsedSeconds;
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type is MatchActionType.Lift or MatchActionType.TurnOver), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SubmissionCanStartByDraggingTheOpponentAwayFromTheRopes(bool grounded)
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            b.Position = MatchPosition.Ropes; b.Stun = 35; b.IsDowned = grounded;
            b.Posture = grounded ? MatchPosture.Supine : MatchPosture.Standing;
            var holdType = grounded ? MatchActionType.Submission : MatchActionType.StandingHold;
            MatchMovement.Initialize(s);
            var drag = MatchPhysics.Actions(s, a, b).FirstOrDefault(x => x.Type == MatchActionType.Reposition && x.MoveId != null);
            Assert.That(drag, Is.Not.Null);
            var before = MatchEngineSnapshot.Capture(s);
            var e = MatchPhysics.Resolve(s, drag, new Random(1));
            MatchMovement.Apply(s, e, before);
            Assert.That(b.Position, Is.EqualTo(MatchPosition.Center));
            Assert.That(MatchMovement.CanReachRopes(b), Is.False);
            Assert.That(s.Exchange?.ActionType, Is.EqualTo(holdType));
            var next = new MatchSimulator(1, accidentsEnabled: false).Step(s);
            Assert.That(next.Type, Is.EqualTo(holdType));
            Assert.That(next.TechniqueId, Is.EqualTo(drag.MoveId));
        }

        [Test]
        public void CountOutTimingDoesNotIncludeAnUnusedAerialFinisherSetup()
        {
            var s = State(); s.TargetDurationSeconds = 900; s.ElapsedSeconds = 837; s.BookingSequence = true;
            s.Booking = new() { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.CountOut };
            s.Participants[0].FinisherMoveIds.Add("move_024"); s.Participants[0].Attributes.HighFlying = 20;
            s.FindMove = id => MatchTechniques.Find(id)?.Rules;
            var simulator = new MatchSimulator(1, accidentsEnabled: false);
            for (int i = 0; i < 50 && !s.IsFinished && !s.IsAborted; i++) simulator.Step(s);
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            Assert.That(s.ActualFinish, Is.EqualTo(MatchFinishType.CountOut));
            Assert.That(s.ElapsedSeconds, Is.GreaterThanOrEqualTo(s.EarliestFinishSeconds));
        }

        [TestCase(0f, 25f, MatchPosture.Standing)]
        [TestCase(20f, 25f, MatchPosture.Kneeling)]
        [TestCase(0f, 90f, MatchPosture.Kneeling)]
        public void SeatedRiseIsContinuousAndKeepsStunAndFatigueRestrictions(float stun, float fatigue, MatchPosture expected)
        {
            var s = State(); var a = s.Participants[0];
            a.IsDowned = true; a.Posture = MatchPosture.Seated; a.Stun = stun; a.Fatigue = fatigue;
            MatchMovement.Initialize(s);
            var before = MatchEngineSnapshot.Capture(s);
            var e = MatchPhysics.Resolve(s, new() { Type = MatchActionType.Recover, PerformerId = "a", ReceiverId = "b" }, new Random(1));
            MatchMovement.Apply(s, e, before);
            Assert.That(a.Posture, Is.EqualTo(expected));
            Assert.That(MatchMovement.Sample(e, "a", 0).Posture, Is.EqualTo(MatchPosture.Seated));
            Assert.That(MatchMovement.Sample(e, "a", 1).Posture, Is.EqualTo(expected));
        }

        [Test]
        public void ReadyGroundedWrestlerCanCoverAfterAnExchangeEnds()
        {
            var s = State(); var a = s.Participants[0];
            a.IsDowned = true; a.Posture = MatchPosture.Kneeling;
            Assert.That(MatchPhysics.Actions(s, a, s.Participants[1]).Any(e => e.Type == MatchActionType.Pin), Is.True);
            Assert.That(MatchPhysics.Actions(s, a, s.Participants[1]).Any(e => e.Type == MatchActionType.Submission), Is.False);
        }

        [Test]
        public void CompletedBodyWorkDoesNotFavorMoreGenericHoldsOverTheStrikeFinish()
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            a.PlannedFinisherMoveId = "move_007";
            s.FindMove = id => MatchTechniques.Find(id)?.Rules;
            var action = new MatchEngineAction { Type = MatchActionType.Submission, PerformerId = "a", ReceiverId = "b" };
            b.BodyDamage = 25; float working = ActionEvaluator.Score(s, action);
            b.BodyDamage = 90;
            Assert.That(ActionEvaluator.Score(s, action), Is.LessThan(working));
        }

        [Test]
        public void FinisherLandingContinuesWithCover()
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            a.IsDowned = true; a.Posture = MatchPosture.Seated;
            b.LastImpactWasFinisher = true; b.LastImpactTime = 495; b.LastImpact = 15;
            s.Exchange = new() { Kind = ExchangeKind.FollowUp, ActorId = a.Id, TargetId = b.Id };
            foreach (int seed in Enumerable.Range(0, 12))
            {
                var copy = UnityEngine.JsonUtility.FromJson<MatchEngineState>(UnityEngine.JsonUtility.ToJson(s));
                Assert.That(new MatchSimulator(seed, accidentsEnabled: false).Step(copy).Type, Is.EqualTo(MatchActionType.Cover));
            }
        }

        [Test]
        public void CommittedFinishDoesNotDivertAnOrdinaryFollowUpIntoAnotherHold()
        {
            var s = State(); s.ElapsedSeconds = 550; s.BookingSequence = true; s.SpatialInitialized = true;
            s.Participants[0].FinisherMoveIds.Add("move_007");
            s.Participants[0].Attributes.Power = 12;
            s.FindMove = id => MatchTechniques.Find(id)?.Rules;
            s.Booking = new() { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.Pinfall };
            s.Exchange = new() { Kind = ExchangeKind.FollowUp, ActorId = "a", TargetId = "b" };
            Assert.That(new MatchSimulator(1, accidentsEnabled: false).Step(s).Type, Is.EqualTo(MatchActionType.Lift));
        }

        [Test]
        public void RecoveryDoesNotEraseRepeatedOffense()
        {
            var s = State(); var action = new MatchEngineAction { Type = MatchActionType.Submission, PerformerId = "a", ReceiverId = "b" };
            float fresh = ActionEvaluator.Score(s, action);
            for (int i = 0; i < 3; i++) s.Events.Add(new() { Type = MatchActionType.Submission, PerformerId = "a", ReceiverId = "b", Impact = 2, IsPhysicalAttack = true });
            for (int i = 0; i < 6; i++) s.Events.Add(new() { Type = MatchActionType.Recover, PerformerId = "a" });
            Assert.That(ActionEvaluator.Score(s, action), Is.LessThan(fresh * .5f));
        }

        [Test]
        public void TrailingIronManWrestlerPrioritizesScoringOverRest()
        {
            var s = State(); s.Rules.IronMan = true; s.Participants[1].Falls = 2;
            var pin = new MatchEngineAction { Type = MatchActionType.Pin, PerformerId = "a", ReceiverId = "b" };
            var rest = new MatchEngineAction { Type = MatchActionType.Rest, PerformerId = "a", ReceiverId = "b" };
            float ordinaryPin = ActionEvaluator.Score(s, pin), ordinaryRest = ActionEvaluator.Score(s, rest);
            s.ElapsedSeconds = 570;
            Assert.That(ActionEvaluator.Score(s, pin), Is.GreaterThan(ordinaryPin));
            Assert.That(ActionEvaluator.Score(s, rest), Is.LessThan(ordinaryRest));
        }

        [Test]
        public void RepeatedOffenseLowersObservedStructure()
        {
            var repeated = State(); var varied = State();
            for (int i = 0; i < 12; i++)
            {
                repeated.Events.Add(new() { Type = MatchActionType.Submission, IsPhysicalAttack = true, TechniqueId = "same", PerformerId = "a", ReceiverId = "b", Impact = 2 });
                varied.Events.Add(new() { Type = MatchActionType.Submission, IsPhysicalAttack = true, TechniqueId = "move-" + i, PerformerId = "a", ReceiverId = "b", Impact = 2 });
            }
            Assert.That(MatchEvaluator.ObserveQuality(repeated).ObservedStructure, Is.LessThan(MatchEvaluator.ObserveQuality(varied).ObservedStructure));
        }

        [Test]
        public void IronManPointVictoryIsNotADraw()
        {
            var s = State(); s.Rules.IronMan = true;
            s.Participants[0].Falls = 2; s.Participants[1].Falls = 1;
            s.ElapsedSeconds = s.TargetDurationSeconds - 1;
            var e = new MatchSimulator(1, accidentsEnabled: false).Step(s);
            Assert.That(s.WinnerId, Is.EqualTo("a"));
            Assert.That(e.Result, Is.EqualTo(MatchActionResult.TimeLimitDecision));
        }

        [Test]
        public void RepeatedFailedFinishUsesAnAssignedAlternative()
        {
            var s = State(); s.ElapsedSeconds = 580;
            var a = s.Participants[0];
            a.Attributes.Power = a.Attributes.Brawling = a.Attributes.HighFlying = 12;
            a.FinisherMoveIds.Add("move_173"); a.SignatureMoveIds.Add("move_117");
            s.FindMove = id => MatchTechniques.Find(id)?.Rules;
            s.Booking = new() { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.Pinfall };
            for (int i = 0; i < 2; i++) s.Events.Add(new() { Type = MatchActionType.Finisher, MoveId = "move_173",
                PerformerId = "a", ReceiverId = "b", MatchTimeSeconds = 560 + i * 10, Result = MatchActionResult.ExecutionFailure });
            var simulator = new MatchSimulator(1, accidentsEnabled: false);
            for (int i = 0; i < 12 && !s.IsFinished; i++) simulator.Step(s);
            Assert.That(s.Events.Any(e => e.Type == MatchActionType.Signature && e.MoveId == "move_117"), Is.True);
        }

        [Test]
        public void IronManGroundHoldsDoNotKnockOutAWrestlerAndContinue()
        {
            for (int seed = 0; seed < 8; seed++)
            {
                var s = State(); s.Participants[1].HeadDamage = 90;
                var match = new MatchSimulator(seed, accidentsEnabled: false).Simulate(s.Participants, 300,
                    rules: new MatchRuntimeRules { IronMan = true });
                Assert.That(match.IsFinished && !match.IsAborted, Is.True, match.FailureReason);
                Assert.That(match.Events.Any(e => e.Type == MatchActionType.Fall && e.Result == MatchActionResult.PassedOut), Is.False);
            }
        }
    }
}
