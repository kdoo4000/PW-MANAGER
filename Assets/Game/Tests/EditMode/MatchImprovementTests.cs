using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class MatchImprovementTests
    {
        private static MatchEngineParticipant P(string id) => new() { Id = id, SideId = id,
            Attributes = new WrestlerAttributesState { Brawling = 12, Power = 12, Technical = 12,
                HighFlying = 12, Selling = 12, Stamina = 18, RingPsychology = 18, RingImprovisation = 12 } };

        [Test]
        public void PreparationBudgetDependsOnCurrentWorkInsteadOfFixedEightyFiveSeconds()
        {
            var a = P("a"); var b = P("b");
            var state = new MatchEngineState { Participants = new() { a, b }, TargetDurationSeconds = 300,
                Booking = new() { WinnerId = "a", LoserId = "b" } };
            int ready = MatchPhysics.BookedPreparationSeconds(state);
            Assert.That(ready, Is.LessThan(85));
            a.IsDowned = true; a.Posture = MatchPosture.Supine; a.Fatigue = 90;
            b.Position = MatchPosition.Corner; b.RingX = .92f;
            Assert.That(MatchPhysics.BookedPreparationSeconds(state), Is.GreaterThan(ready));
        }

        [Test]
        public void BookedAttackKeepsItsDurationAndCanFinishAfterTheTarget()
        {
            var a = P("a"); a.FinisherMoveId = "named-finish";
            var s = new MatchEngineState { Participants = new() { a, P("b") }, SpatialInitialized = true,
                TargetDurationSeconds = 300, ElapsedSeconds = 290,
                Booking = new() { WinnerId = "a", LoserId = "b" },
                FindMove = _ => new MatchMoveRules { Kind = MoveKind.Strike, Name = "Named finish" } };
            var simulator = new MatchSimulator(1, accidentsEnabled: false);
            var attack = simulator.Step(s);
            Assert.That(attack.Type, Is.EqualTo(MatchActionType.Finisher));
            Assert.That(attack.DurationSeconds, Is.EqualTo(7));
            Assert.That(simulator.Step(s).Type, Is.EqualTo(MatchActionType.Cover));
            var count = simulator.Step(s);
            Assert.That(count.DurationSeconds, Is.EqualTo(3));
            Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True);
            Assert.That(s.ElapsedSeconds, Is.EqualTo(302));
        }

        [TestCase(300)]
        [TestCase(900)]
        [TestCase(2700)]
        public void BookedMatchesFinishWithinFivePercentWithoutAllEndingAtTheSameSecond(int duration)
        {
            var times = new System.Collections.Generic.HashSet<int>();
            for (int seed = 0; seed < 20; seed++)
            {
                var s = new MatchSimulator(seed, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, duration,
                    new() { WinnerId = "a", LoserId = "b" });
                Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True, s.FailureReason);
                Assert.That(s.ElapsedSeconds, Is.InRange(duration * .95, duration * 1.05));
                Assert.That(s.Events.Last().DurationSeconds, Is.EqualTo(3));
                times.Add(s.ElapsedSeconds);
            }
            Assert.That(times.Count, Is.GreaterThan(1));
        }

        [TestCase("move_023")]
        [TestCase("legacy-move_023")]
        public void BridgingSuplexRetainsContactThroughCoverAndReleasesOnKickout(string moveId)
        {
            PWManager.Data.Loading.MoveTable.Initialize();
            var a = P("a"); var b = P("b");
            a.RingX = -.4f; a.FacingX = 1; b.RingX = .1f; b.FacingX = -1;
            var s = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true,
                TargetDurationSeconds = 600, BookingSequence = true,
                Booking = new() { WinnerId = "b", LoserId = "a" },
                FindMove = id => MatchTechniques.Find(id)?.Rules };
            var before = MatchEngineSnapshot.Capture(s);
            var hit = MatchPhysics.Resolve(s, new() { Type = MatchActionType.Finisher, MoveId = moveId,
                PerformerId = a.Id, ReceiverId = b.Id }, new System.Random(1));
            MatchMovement.Apply(s, hit, before);
            foreach (float progress in new[] { .65f, .8f, 1f })
            {
                var holder = MatchMovement.Sample(hit, "a", progress); var target = MatchMovement.Sample(hit, "b", progress);
                Assert.That(new Vector2(holder.X - target.X, holder.Z - target.Z).magnitude, Is.LessThanOrEqualTo(.2f));
                Assert.That(holder.Grip, Is.EqualTo(MatchGrip.Waist));
                Assert.That(holder.GripTargetId, Is.EqualTo("b"));
                Assert.That(holder.Posture, Is.EqualTo(MatchPosture.Supine));
            }
            s.ElapsedSeconds += hit.DurationSeconds; s.Events.Add(hit);
            var simulator = new MatchSimulator(1, accidentsEnabled: false);
            var cover = simulator.Step(s);
            Assert.That(cover.Type, Is.EqualTo(MatchActionType.Cover));
            foreach (float progress in new[] { 0f, .3f, .65f, 1f })
            {
                var holder = MatchMovement.Sample(cover, "a", progress);
                Assert.That(holder.X, Is.EqualTo(a.RingX).Within(.0001f));
                Assert.That(holder.Z, Is.EqualTo(a.RingZ).Within(.0001f));
                Assert.That(holder.Posture, Is.EqualTo(MatchPosture.Supine));
                Assert.That(holder.Grip, Is.EqualTo(MatchGrip.Waist));
            }
            var count = simulator.Step(s);
            Assert.That(count.Type, Is.EqualTo(MatchActionType.Pin));
            Assert.That(new[] { MatchActionResult.OneCount, MatchActionResult.TwoCount, MatchActionResult.NearFall }, Does.Contain(count.Result));
            Assert.That(a.Grip, Is.EqualTo(MatchGrip.None));
            Assert.That(MatchMovement.Distance(a, b), Is.GreaterThan(.25f));
        }

        [TestCase(180)]
        [TestCase(900)]
        public void BookedBridgingSuplexCountsWithoutStandingUpWithinTheTimeWindow(int duration)
        {
            PWManager.Data.Loading.MoveTable.Initialize();
            for (int seed = 0; seed < 3; seed++)
            {
                var a = P("a"); a.FinisherMoveId = "move_023";
                var s = new MatchSimulator(seed, accidentsEnabled: false, findMove: id => MatchTechniques.Find(id)?.Rules).Simulate(new[] { a, P("b") }, duration,
                    new() { WinnerId = "a", LoserId = "b" });
                Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True, s.FailureReason +
                    $" seed={seed}: " + string.Join(" | ", s.Events.TakeLast(20).Select(e => $"{e.MatchTimeSeconds}:{e.Type}/{e.Result}/{e.MoveId}/{e.Detail}")));
                Assert.That(s.ElapsedSeconds, Is.InRange(duration * .95, duration * 1.05));
                var hit = s.Events.FindLastIndex(e => e.TechniqueId == "move_023" && e.Impact > 0);
                Assert.That(hit, Is.GreaterThanOrEqualTo(0));
                Assert.That(s.Events.Last().Result, Is.EqualTo(MatchActionResult.Pinfall));
                if (s.Events[hit].Result == MatchActionResult.FullSuccess)
                {
                    Assert.That(s.Events[hit + 1].Type, Is.EqualTo(MatchActionType.Cover));
                    Assert.That(s.Events.Skip(hit + 1).Any(e => e.Type == MatchActionType.Recover && e.PerformerId == "a"), Is.False);
                }
                else Assert.That(s.Events[hit].Result, Is.EqualTo(MatchActionResult.ReceptionFailure),
                    "A failed reception must not manufacture a successful bridge.");
            }
        }

        [TestCase(false, 0)]
        [TestCase(true, 100)]
        public void BridgingSuplexReleasesInsteadOfForcingAnIllegalCover(bool pinfall, int refereeUnavailableUntil)
        {
            PWManager.Data.Loading.MoveTable.Initialize();
            var a = P("a"); var b = P("b");
            var s = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true,
                TargetDurationSeconds = 600, BookingSequence = true, Rules = new() { Pinfall = pinfall },
                RefereeUnavailableUntil = refereeUnavailableUntil, FindMove = id => MatchTechniques.Find(id)?.Rules };
            var before = MatchEngineSnapshot.Capture(s);
            var hit = MatchPhysics.Resolve(s, new() { Type = MatchActionType.Finisher, MoveId = "move_023",
                PerformerId = a.Id, ReceiverId = b.Id }, new System.Random(1));
            MatchMovement.Apply(s, hit, before); s.Events.Add(hit); s.ElapsedSeconds += hit.DurationSeconds;
            Assert.That(new MatchSimulator(1, accidentsEnabled: false).Step(s).Type, Is.EqualTo(MatchActionType.Recover));
            Assert.That(a.Grip, Is.EqualTo(MatchGrip.None));
        }

        [Test]
        public void AssignedTechniqueIdAndSavedAliasesPreserveTheirRules()
        {
            var a = P("a"); var b = P("b");
            var s = new MatchEngineState { Participants = new() { a, b }, BookingSequence = true,
                FindMove = _ => new MatchMoveRules { Kind = MoveKind.Strike, Name = "Named finish" } };
            var e = MatchPhysics.Resolve(s, new MatchEngineAction { Type = MatchActionType.Finisher,
                PerformerId = a.Id, ReceiverId = b.Id, MoveId = "named-finish" }, new System.Random(1));
            Assert.That(e.TechniqueId, Is.EqualTo("named-finish"));
            var move = MatchTechniques.All.First();
            foreach (var id in new[] { (string)null, "" })
            {
                e.TechniqueId = id; e.MoveId = move.Id;
                Assert.That(MatchTechniques.Find(e)?.Id, Is.EqualTo(move.Id));
                Assert.That(MatchTechniques.Find(JsonUtility.FromJson<MatchEngineEvent>(JsonUtility.ToJson(e)))?.Id, Is.EqualTo(move.Id));
            }
            Assert.That(MatchTechniques.Find(new MatchEngineEvent()), Is.Null);
            var alias = MatchTechniques.All.First(m => !string.IsNullOrEmpty(m.Key));
            Assert.That(MatchTechniques.Find(alias.Key)?.Id, Is.EqualTo(alias.Id));
        }

        [TestCase(MatchFinishType.Pinfall)]
        [TestCase(MatchFinishType.RollUp)]
        public void BookedFinishHasNoTimerRestAndARealThreeCount(MatchFinishType finish)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var s = new MatchSimulator(seed, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600,
                    new MatchEngineBooking { WinnerId = "a", LoserId = "b", FinishType = finish });
                Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True, s.FailureReason);
                Assert.That(s.ElapsedSeconds, Is.InRange(600 * .95, 600 * 1.05));
                Assert.That(s.Events.Any(e => e.Detail == "delayed-cover"), Is.False);
                Assert.That(s.Events.Last().DurationSeconds, Is.EqualTo(3));
                if (finish == MatchFinishType.RollUp)
                {
                    var entry = s.Events.LastOrDefault(e => e.Detail == "roll-up");
                    Assert.That(entry, Is.Not.Null);
                    Assert.That(entry.Motion.Any(f => f.Participants.Any(p => p.Id == "b" && p.Posture != MatchPosture.Supine)), Is.True);
                    Assert.That(entry.After.Participants.Single(p => p.Id == "b").Posture, Is.EqualTo(MatchPosture.Supine));
                }
            }
        }

        [TestCase(1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(1f, 1f)]
        public void CenterRepositionActuallyReachesUsableCenter(float x, float z)
        {
            var a = P("a"); var b = P("b"); a.RingX = -.4f; b.RingX = x * .9f; b.RingZ = z * .9f;
            b.Position = MatchPosition.Ropes;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            var before = MatchEngineSnapshot.Capture(state);
            var e = MatchPhysics.Resolve(state, new MatchEngineAction { Type = MatchActionType.Reposition,
                PerformerId = "a", ReceiverId = "b" }, new System.Random(1));
            MatchMovement.Apply(state, e, before);
            Assert.That(MatchMovement.DistanceFromCenter(b), Is.LessThanOrEqualTo(.12f));
            Assert.That(e.Motion.First().Participants.Single(p => p.Id == "b").X, Is.EqualTo(x * .9f));
        }

        [Test]
        public void BookedAerialFinishKeepsItsTopRopePreparation()
        {
            var a = P("a"); a.FinisherMoveIds.Add("flight");
            var s = new MatchSimulator(3, accidentsEnabled: false, findMove: _ => new MatchMoveRules { Kind = MoveKind.Aerial,
                Situation = MoveSituation.DivingStanding, EndPosture = MatchPosture.Supine }).Simulate(new[] { a, P("b") }, 300,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.Pinfall });
            Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True, s.FailureReason);
            Assert.That(s.Events.Last().DurationSeconds, Is.EqualTo(3));
        }

        [Test]
        public void TurnOverKeepsTheRequiredKneelingFinishPosture()
        {
            var a = P("a"); var b = P("b"); b.IsDowned = true; b.Posture = MatchPosture.Supine;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true,
                FindMove = _ => new MatchMoveRules { Situation = MoveSituation.GroundKneeling, Kind = MoveKind.Strike } };
            var before = MatchEngineSnapshot.Capture(state);
            var e = MatchPhysics.Resolve(state, new MatchEngineAction { Type = MatchActionType.TurnOver,
                PerformerId = "a", ReceiverId = "b", MoveId = "kneeling-kick" }, new System.Random(1));
            MatchMovement.Apply(state, e, before);
            Assert.That(b.Posture, Is.EqualTo(MatchPosture.Kneeling));
            Assert.That(MatchPhysics.CanMove(state, a, b, "kneeling-kick"), Is.True);
        }

        [Test]
        public void FinisherPlanPersistsAndChangesWhenItsRequiredLimbIsInjured()
        {
            var field = typeof(MatchEngineParticipant).GetField("PlannedFinisherMoveId");
            Assert.That(field, Is.Not.Null, "No persistent finisher goal.");
            var a = P("a"); var b = P("b"); a.FinisherMoveIds.AddRange(new[] { "flight", "arm-lock" });
            var s = new MatchEngineState { Participants = new() { a, b }, TargetDurationSeconds = 600,
                FindMove = id => new MatchMoveRules { Kind = id == "flight" ? MoveKind.Aerial : MoveKind.Submission,
                    TargetRegion = id == "flight" ? BodyRegion.Body : BodyRegion.Arms } };
            field.SetValue(a, "flight"); a.RealInjury = InjuryStatus.Moderate; a.RealInjuryRegion = BodyRegion.Legs;
            new MatchSimulator(1, accidentsEnabled: false).Step(s);
            Assert.That(field.GetValue(a), Is.EqualTo("arm-lock"));
            Assert.That(a.TargetRegion, Is.EqualTo(BodyRegion.Arms));
            b.IsDowned = false;
            new MatchSimulator(2, accidentsEnabled: false).Step(s);
            Assert.That(field.GetValue(a), Is.EqualTo("arm-lock"), "Posture temporarily unavailable should preserve the goal.");
        }

        [TestCase(MatchActionType.DoubleTeam)]
        [TestCase(MatchActionType.BreakUp)]
        public void ThirdParticipantApproachesTheExchangeAndReturnsWithoutTeleporting(MatchActionType type)
        {
            var a = P("a"); var b = P("b"); var helper = P("c");
            a.RingX = -.2f; b.RingX = .2f; helper.RingX = 1.12f; helper.RingZ = .7f;
            helper.Position = MatchPosition.Apron; helper.IsLegal = false;
            var state = new MatchEngineState { SpatialInitialized = true, Participants = new() { a, b, helper } };
            var before = MatchEngineSnapshot.Capture(state);
            var e = new MatchEngineEvent { Type = type, PerformerId = type == MatchActionType.DoubleTeam ? "a" : "c",
                ReceiverId = "b", PartnerId = type == MatchActionType.DoubleTeam ? "c" : null,
                Result = type == MatchActionType.DoubleTeam ? MatchActionResult.FullSuccess : MatchActionResult.Interrupted,
                Before = before, DurationSeconds = 4, Impact = type == MatchActionType.DoubleTeam ? 10 : 0 };
            MatchMovement.Apply(state, e, before);
            var start = MatchMovement.Sample(e, "c", 0); var contact = MatchMovement.Sample(e, "c", .65f);
            var receiver = MatchMovement.Sample(e, "b", .65f);
            Assert.That(Math.Abs(contact.X - receiver.X) + Math.Abs(contact.Z - receiver.Z), Is.LessThan(.5f));
            Assert.That(MatchMovement.Sample(e, "c", 1).X, Is.EqualTo(start.X));
            Assert.That(MatchMovement.Sample(e, "c", 1).Z, Is.EqualTo(start.Z));
        }

        [Test]
        public void CornerAtTheCenterUsesApproachDirectionAndMirrors()
        {
            var ends = new float[2, 2];
            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? 1 : -1;
                var a = P("a"); var b = P("b"); a.RingX = sign * .4f; a.RingZ = sign * .3f;
                var state = new MatchEngineState { SpatialInitialized = true, Participants = new() { a, b } };
                var e = new MatchEngineEvent { Type = MatchActionType.CornerWhip, PerformerId = "a", ReceiverId = "b",
                    Result = MatchActionResult.FullSuccess, DurationSeconds = 4, ExchangeId = 8 };
                MatchMovement.Apply(state, e, MatchEngineSnapshot.Capture(state));
                ends[i, 0] = b.RingX; ends[i, 1] = b.RingZ;
            }
            Assert.That(ends[0, 0], Is.EqualTo(-ends[1, 0]).Within(.0001f));
            Assert.That(ends[0, 1], Is.EqualTo(-ends[1, 1]).Within(.0001f));
        }

        [Test]
        public void DoubleDownRecoversIntoAlternatingPunchesAndAChoiceOfComeback()
        {
            Assert.That(Enum.TryParse<ExchangeKind>("DoubleDown", out var kind), Is.True);
            var a = P("a"); var b = P("b");
            a.Alignment = KayfabeAlignment.Face; b.Alignment = KayfabeAlignment.Heel; a.Attributes.FaceWork = 18;
            a.IsDowned = b.IsDowned = true; a.Posture = b.Posture = MatchPosture.Supine;
            a.Fatigue = b.Fatigue = 60;
            var state = new MatchEngineState { Participants = new() { a, b }, TargetDurationSeconds = 1800, ElapsedSeconds = 600,
                Exchange = new MatchExchange { Kind = kind, ActorId = "a", TargetId = "b", CauseIndex = -1 } };
            var simulator = new MatchSimulator(3, accidentsEnabled: false);
            for (int i = 0; i < 8; i++) simulator.Step(state);
            Assert.That(state.Events.First().Detail, Is.EqualTo("double-down"));
            Assert.That(state.Events.First().After.Participants.All(p => p.IsDowned), Is.True);
            var punches = state.Events.Where(e => e.Detail == "boo-yay").ToArray();
            Assert.That(punches.Length, Is.EqualTo(4));
            Assert.That(punches.Select(e => e.PerformerId), Is.EqualTo(new[] { "a", "b", "a", "b" }));
            Assert.That(punches.First().Before.Participants.All(p => p.IsDowned), Is.True);
            Assert.That(punches.Last().After.Participants.All(p => !p.IsDowned), Is.True);
            Assert.That(a.HeadDamage > 0 && b.HeadDamage > 0, Is.True);
            for (int i = 1; i < state.Events.Count; i++)
                Assert.That(JsonUtility.ToJson(state.Events[i].Before), Is.EqualTo(JsonUtility.ToJson(state.Events[i - 1].After)));
        }

        [Test]
        public void ActualArmInjuryCannotBeBypassedWithAnUnassignedSubmission()
        {
            var a = P("a"); var b = P("b"); a.RealInjury = InjuryStatus.Moderate; a.RealInjuryRegion = BodyRegion.Arms;
            b.IsDowned = true; b.Posture = MatchPosture.Supine;
            Assert.That(MatchTechniques.Candidates(MatchActionType.Submission, a, b), Is.Empty);
            var state = new MatchSimulator(1, accidentsEnabled: false).Simulate(new[] { a, b }, 300,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.Submission });
            Assert.That(state.WasStoppedByExecution, Is.True);
            Assert.That(state.Events.Any(e => e.PerformerId == a.Id && e.Type == MatchActionType.Submission && e.Impact > 0), Is.False);
        }

        [Test]
        public void TurningForAPinCommitsToCoverAndOtherTurnsDoNotClaimACover()
        {
            var a = P("a"); var b = P("b"); b.IsDowned = true; b.Posture = MatchPosture.Prone;
            var state = new MatchEngineState { Participants = new() { a, b }, TargetDurationSeconds = 600 };
            var turn = MatchPhysics.Resolve(state, new MatchEngineAction { Type = MatchActionType.Pin, PerformerId = "a", ReceiverId = "b" }, new System.Random(1));
            var next = new MatchSimulator(1, accidentsEnabled: false).Step(state);
            Assert.That(turn.Detail, Is.EqualTo("pin-setup"));
            Assert.That(next.Type, Is.EqualTo(MatchActionType.Cover));
            var general = new MatchEngineEvent { Type = MatchActionType.TurnOver, PerformerId = "a", ReceiverId = "b", After = MatchEngineSnapshot.Capture(state) };
            Assert.That(MatchEventNarration.Text(general, new MatchResultState(), id => id), Does.Not.Contain("커버"));
        }

        [Test]
        public void ReceptionNarrationUsesTheCorrectKoreanParticle()
        {
            var e = new MatchEngineEvent { Result = MatchActionResult.ReceptionFailure, Type = MatchActionType.Submission, TechniqueName = "암바" };
            Assert.That(MatchEventNarration.Text(e, new MatchResultState(), id => id), Does.Contain("암바를"));
            e.TechniqueName = "슬리퍼 홀드";
            Assert.That(MatchEventNarration.Text(e, new MatchResultState(), id => id), Does.Contain("슬리퍼 홀드를"));
        }

        [TestCase(ExchangeKind.Counter)]
        [TestCase(ExchangeKind.Transition)]
        public void InjuredCounterAndTransitionChooseAnExecutableAttack(ExchangeKind kind)
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var a = P("a"); var b = P("b"); a.ArmDamage = 95;
                var state = new MatchEngineState { Participants = new() { a, b }, TargetDurationSeconds = 600, ElapsedSeconds = 100,
                    Exchange = new MatchExchange { Kind = kind, ActorId = "a", TargetId = "b" } };
                Assert.DoesNotThrow(() => new MatchSimulator(seed, accidentsEnabled: false).Step(state));
            }
        }
        [Test]
        public void LinkedExchangeKeepsItsPhaseAcrossContinuations()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var state = new MatchEngineState { Participants = new() { P("a"), P("b") }, Phase = MatchPhase.Control,
                    ElapsedSeconds = 200, TargetDurationSeconds = 600,
                    Exchange = new() { Kind = ExchangeKind.Cover, ActorId = "a", TargetId = "b" } };
                new MatchStateMachine(new System.Random(seed)).Update(state);
                Assert.That(state.Phase, Is.EqualTo(MatchPhase.Control));
            }
        }

        [Test]
        public void EarlyFinisherDoesNotRandomlyEnterTheFinishPhase()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var state = new MatchEngineState { Participants = new() { P("a"), P("b") }, Phase = MatchPhase.Control,
                    ElapsedSeconds = 60, TargetDurationSeconds = 1800 };
                state.Events.Add(new() { Phase = MatchPhase.Control, Type = MatchActionType.Finisher, Impact = 18, IsMajorMoment = true });
                new MatchStateMachine(new System.Random(seed)).Update(state);
                Assert.That(state.Phase, Is.Not.EqualTo(MatchPhase.Finish));
            }
        }

        [Test]
        public void StandingHoldEscapeStartsACausalComebackInAShortMatch()
        {
            var a = P("a"); var b = P("b"); a.MatchControl = 85; b.MatchControl = 15;
            b.Alignment = KayfabeAlignment.Face;
            var state = new MatchEngineState { Participants = new() { a, b }, Phase = MatchPhase.Control,
                TargetDurationSeconds = 600, ElapsedSeconds = 200,
                Exchange = new() { Kind = ExchangeKind.Hold, ActorId = "a", TargetId = "b", Beats = 2 } };
            var e = new MatchSimulator(3, accidentsEnabled: false).Step(state);
            Assert.That(e.Result, Is.EqualTo(MatchActionResult.Escaped));
            Assert.That(state.Exchange?.Kind, Is.EqualTo(ExchangeKind.Comeback));
            Assert.That(state.Exchange.ActorId, Is.EqualTo("b"));
            Assert.That(state.Exchange.CauseIndex, Is.EqualTo(0));
            var next = new MatchSimulator(3, accidentsEnabled: false).Step(state);
            Assert.That(next.Phase, Is.EqualTo(MatchPhase.Comeback));
            Assert.That(next.Detail, Does.StartWith("comeback-"));
        }

        [Test]
        public void SaturatedLimbWorkYieldsToASubmissionAttempt()
        {
            var a = P("a"); var b = P("b"); a.TargetRegion = BodyRegion.Body; b.BodyDamage = 100;
            var state = new MatchEngineState { Participants = new() { a, b }, ElapsedSeconds = 300 };
            float Score(MatchActionType type) => ActionEvaluator.Score(state, new() { Type = type, PerformerId = "a", ReceiverId = "b" });
            Assert.That(Score(MatchActionType.LimbAttack), Is.LessThan(Score(MatchActionType.Submission)));
            float initial = Score(MatchActionType.LimbAttack);
            state.Events.Add(new() { Type = MatchActionType.LimbAttack, PerformerId = "a" });
            Assert.That(Score(MatchActionType.LimbAttack), Is.LessThan(initial * .7f));
        }

        [Test]
        public void RecoveryReflectsStunAndRestUsesElapsedTime()
        {
            MatchEngineEvent Recover(float stun, bool downed)
            {
                var a = P("a"); a.IsDowned = downed; a.Posture = downed ? MatchPosture.Supine : MatchPosture.Standing;
                a.Fatigue = 60; a.Stun = stun;
                var state = new MatchEngineState { Participants = new() { a, P("b") }, TargetDurationSeconds = 600 };
                var e = MatchPhysics.Resolve(state, new() { Type = MatchActionType.Recover, PerformerId = "a", ReceiverId = "b" }, new System.Random(1));
                e.FatigueChange = 60 - a.Fatigue; return e;
            }
            var light = Recover(0, true); var heavy = Recover(40, true); var rest = Recover(0, false);
            Assert.That(heavy.DurationSeconds, Is.GreaterThan(light.DurationSeconds));
            Assert.That(rest.FatigueChange, Is.GreaterThan(light.FatigueChange));
            Assert.That(light.FatigueChange, Is.LessThan(2));
        }

        [Test]
        public void StartingConditionPenaltySurvivesRecovery()
        {
            var a = P("a"); a.Fatigue = 80;
            var state = new MatchSimulator(7, accidentsEnabled: false).Simulate(new[] { a, P("b") }, 900,
                new() { WinnerId = "a", LoserId = "b" });
            Assert.That(state.IsFinished && !state.IsAborted && !state.WasStoppedByExecution, Is.True, state.FailureReason);
            Assert.That(a.Fatigue, Is.GreaterThanOrEqualTo(80));
        }

        [Test]
        public void PinBookingPlansTheCompatibleSignatureBeforeTheFinalMinute()
        {
            var a = P("a"); a.FinisherMoveId = "lock"; a.SignatureMoveId = "strike";
            var state = new MatchEngineState { Participants = new() { a, P("b") }, TargetDurationSeconds = 1800,
                Booking = new() { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.Pinfall },
                FindMove = id => new() { Kind = id == "lock" ? MoveKind.Submission : MoveKind.Strike,
                    TargetRegion = id == "lock" ? BodyRegion.Arms : BodyRegion.Head } };
            new MatchSimulator(1, accidentsEnabled: false).Step(state);
            Assert.That(a.PlannedFinisherMoveId, Is.EqualTo("strike"));
            Assert.That(a.TargetRegion, Is.EqualTo(BodyRegion.Head));
        }

        [Test]
        public void BookedMultiwayCoverHasClearedEveryAvailableSave()
        {
            var state = new MatchSimulator(7, accidentsEnabled: false).Simulate(new[] { P("a"), P("b"), P("c"), P("d") }, 600,
                new() { WinnerId = "a", LoserId = "b" });
            Assert.That(state.IsFinished && !state.IsAborted && !state.WasStoppedByExecution, Is.True, state.FailureReason);
            var cover = state.Events.Last(e => e.Type == MatchActionType.Cover);
            Assert.That(cover.Before.Participants.Where(p => p.Id != "a" && p.Id != "b")
                .All(p => p.IsDowned || p.IsEliminated || p.Stun >= 12 || p.Position == MatchPosition.Outside), Is.True);
        }

        [Test]
        public void UnopposedEarlyObjectAttemptDoesNotInventADefenderInterruption()
        {
            var a = P("a"); var b = P("b"); b.IsDowned = true; b.Posture = MatchPosture.Supine; b.Stun = 80;
            var state = new MatchEngineState { Participants = new() { a, b }, ElapsedSeconds = 100, TargetDurationSeconds = 600,
                Rules = new() { Objective = MatchObjective.Escape }, Booking = new() { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.Escape },
                Exchange = new() { Kind = ExchangeKind.Object, ActorId = "a", TargetId = "b", Beats = 2 } };
            var e = new MatchSimulator(1, accidentsEnabled: false).Step(state);
            Assert.That(e.Detail, Is.EqualTo("object-retreat"));
            Assert.That(MatchEventNarration.Text(e, new(), id => id), Does.Not.Contain("저지"));
            Assert.That(state.IsFinished, Is.False);
        }

    }
}
