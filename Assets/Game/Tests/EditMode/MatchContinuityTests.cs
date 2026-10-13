using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Loading;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchContinuityTests
    {
        [Test]
        public void EventsRecordSpatialMotionAndPostureForReplay()
        {
            MoveTable.Initialize();
            for (int seed = 0; seed < 8; seed++)
            {
                var a = Fighter("a"); var b = Fighter("b");
                var match = new MatchSimulator(seed).Simulate(new[] { a, b }, 600,
                    new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
                Assert.That(match.IsFinished && !match.IsAborted, Is.True, match.FailureReason);
                for (int i = 0; i < match.Events.Count; i++)
                {
                    var e = match.Events[i];
                    Assert.That(e.Motion, Is.Not.Null);
                    foreach (var p in e.After.Participants)
                    {
                        AssertPose(MatchMovement.Sample(e, p.Id, 1), p);
                        AssertPose(MatchMovement.Sample(e, p.Id, 0), e.Before.Participants.Single(x => x.Id == p.Id));
                        Assert.That(p.Posture != MatchPosture.Standing, Is.EqualTo(p.IsDowned));
                        if (i + 1 < match.Events.Count)
                            AssertPose(MatchMovement.Sample(match.Events[i + 1], p.Id, 0), p);
                        foreach (var frame in e.Motion)
                        {
                            var pose = frame.Participants.Single(x => x.Id == p.Id);
                            Assert.That(MatchPhysics.Finite(pose.X) && MatchPhysics.Finite(pose.Z) && MatchPhysics.Finite(pose.Height), Is.True);
                            Assert.That(Math.Max(Math.Abs(pose.X), Math.Abs(pose.Z)), Is.LessThanOrEqualTo(1.41f));
                        }
                    }
                }
                var restored = UnityEngine.JsonUtility.FromJson<MatchEngineState>(UnityEngine.JsonUtility.ToJson(match));
                AssertPose(MatchMovement.Sample(restored.Events.Last(), "a", 1), match.Events.Last().After.Participants.Single(x => x.Id == "a"));
            }
        }

        [Test]
        public void RecoveryPassesThroughSittingAndKneelingAndProneCoversRequireTurningOver()
        {
            var a = Fighter("a"); var b = Fighter("b");
            var state = new MatchEngineState { Participants = new() { a, b } };
            a.IsDowned = true; a.Posture = MatchPosture.Prone;
            foreach (var expected in new[] { MatchPosture.Seated, MatchPosture.Kneeling, MatchPosture.Standing })
            {
                ActionResolver.Resolve(state, new MatchEngineAction { Type = MatchActionType.Recover, PerformerId = "a", ReceiverId = "b" }, new Random(1));
                Assert.That(a.Posture, Is.EqualTo(expected));
            }
            b.IsDowned = true; b.Posture = MatchPosture.Prone;
            var pin = new MatchEngineAction { Type = MatchActionType.Pin, PerformerId = "a", ReceiverId = "b" };
            Assert.That(ActionResolver.Resolve(state, pin, new Random(1)).Type, Is.EqualTo(MatchActionType.TurnOver));
            Assert.That(b.Posture, Is.EqualTo(MatchPosture.Supine));
            Assert.That(ActionResolver.Resolve(state, pin, new Random(1)).Type, Is.EqualTo(MatchActionType.Cover));
        }

        [Test]
        public void MissedCornerChargeLeavesARearOpeningAndPostureSpecificPoolsStayLegal()
        {
            MoveTable.Initialize();
            var a = Fighter("a"); var b = Fighter("b");
            a.RingX = 0; a.RingZ = 0;
            b.RingX = .92f; b.RingZ = .92f; b.Position = MatchPosition.Corner;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            var before = MatchEngineSnapshot.Capture(state);
            a.Position = MatchPosition.Corner; b.Position = MatchPosition.Center;
            MatchMovement.Apply(state, new MatchEngineEvent { Type = MatchActionType.CornerAttack,
                Result = MatchActionResult.Escaped, PerformerId = "a", ReceiverId = "b", Detail = "missed-charge" }, before);
            Assert.That(MatchMovement.IsBehind(b, a), Is.True);
            Assert.That(MatchTechniques.Candidates(MatchActionType.Grapple, b, a).Any(x => x.Rules.Situation == MoveSituation.StandingRearGrapple), Is.True);
            a.IsDowned = true; a.Posture = MatchPosture.Kneeling;
            Assert.That(MatchTechniques.Candidates(MatchActionType.GroundAttack, b, a).Any(x => x.Key == "kneeling-ddt"), Is.True);
            Assert.That(MatchTechniques.Candidates(MatchActionType.GroundAttack, b, a).Any(x => x.Key == "seated-soccer-kick"), Is.False);
            a.Posture = MatchPosture.Seated;
            Assert.That(MatchTechniques.Candidates(MatchActionType.GroundAttack, b, a).Any(x => x.Key == "seated-soccer-kick"), Is.True);
        }

        private static MatchEngineParticipant Fighter(string id) => new()
        {
            Id = id, WrestlingStyleId = "style_003",
            Attributes = new PWManager.Domain.Models.WrestlerAttributesState { Technical = 15, Power = 12, Brawling = 12,
                HighFlying = 12, Selling = 12, Stamina = 12, RingPsychology = 12, RingImprovisation = 12, Charisma = 12, Acting = 12 }
        };

        [TestCase(.4f, 0f)]
        [TestCase(-.4f, 0f)]
        [TestCase(.3f, .3f)]
        public void WhipReboundAndCatchCarryVelocityAndReplayTheRopeCompression(float x, float z)
        {
            MoveTable.Initialize();
            var a = Fighter("a"); var b = Fighter("b"); b.RingX = x; b.RingZ = z;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            var whip = new MatchEngineEvent { Type = MatchActionType.IrishWhip, Result = MatchActionResult.FullSuccess,
                PerformerId = "a", ReceiverId = "b", DurationSeconds = 4 };
            Record(whip, () => b.Position = MatchPosition.Ropes);
            var release = whip.Motion[^2].Progress;
            Assert.That(Speed(whip, release + .01f), Is.LessThan(Speed(whip, .9f) * .1f));
            Assert.That(Speed(whip, .99f), Is.GreaterThan(.5f));
            var rebound = new MatchEngineEvent { Type = MatchActionType.RopeRun, Detail = "rebound",
                PerformerId = "b", ReceiverId = "a", DurationSeconds = 3 };
            Record(rebound, () => b.Position = MatchPosition.Center);
            Assert.That(Speed(rebound, .001f), Is.EqualTo(Speed(whip, .999f)).Within(.04f));
            var compressed = rebound.Motion[1];
            var start = rebound.Motion[0].Participants.Single(p => p.Id == "b");
            var peak = compressed.Participants.Single(p => p.Id == "b");
            Assert.That(Math.Max(Math.Abs(peak.X), Math.Abs(peak.Z)), Is.GreaterThan(Math.Max(Math.Abs(start.X), Math.Abs(start.Z))));
            Assert.That(Speed(rebound, compressed.Progress), Is.LessThan(.03f));
            var attack = new MatchEngineEvent { Type = MatchActionType.RunningAttack, Detail = "rebound-catch",
                TechniqueId = MatchTechniques.Find("running-clothesline").Id, PerformerId = "a", ReceiverId = "b", DurationSeconds = 4, Impact = 8 };
            Record(attack, () => { b.IsDowned = true; b.Posture = MatchPosture.Supine; });
            Assert.That(Speed(attack, .001f), Is.EqualTo(Speed(rebound, .999f)).Within(.04f));
            foreach (var e in state.Events)
                for (int i = 0; i <= 200; i++)
                {
                    var pose = MatchMovement.Sample(e, "b", i / 200f);
                    Assert.That(MatchPhysics.Finite(pose.X) && MatchPhysics.Finite(pose.Z), Is.True);
                    Assert.That(Math.Max(Math.Abs(pose.X), Math.Abs(pose.Z)), Is.LessThanOrEqualTo(.981f));
                }
            var restored = UnityEngine.JsonUtility.FromJson<MatchEngineState>(UnityEngine.JsonUtility.ToJson(state));
            foreach (var e in restored.Events)
                Assert.That(Speed(e, .1f), Is.EqualTo(Speed(state.Events[restored.Events.IndexOf(e)], .1f)).Within(.0001f));

            void Record(MatchEngineEvent e, Action resolve)
            {
                e.Before = MatchEngineSnapshot.Capture(state); resolve();
                MatchMovement.Apply(state, e, e.Before); e.After = MatchEngineSnapshot.Capture(state); state.Events.Add(e);
                AssertPose(MatchMovement.Sample(e, "b", 0), e.Before.Participants.Single(p => p.Id == "b"));
                AssertPose(MatchMovement.Sample(e, "b", 1), e.After.Participants.Single(p => p.Id == "b"));
            }
            float Speed(MatchEngineEvent e, float progress)
            {
                var left = MatchMovement.Sample(e, "b", Math.Max(0, progress - .0001f));
                var right = MatchMovement.Sample(e, "b", Math.Min(1, progress + .0001f));
                float dx = right.X - left.X, dz = right.Z - left.Z;
                return (float)Math.Sqrt(dx * dx + dz * dz) / (.0002f * e.MotionSeconds);
            }
        }

        [Test]
        public void AVoluntaryRopeRunReboundsBeforeAttackingAndDamageSlowsTheRun()
        {
            MoveTable.Initialize();
            float healthy = Sequence(0, 0), tired = Sequence(60, 50);
            Assert.That(tired, Is.GreaterThan(healthy));

            float Sequence(float fatigue, float legDamage)
            {
                var a = Fighter("a"); var b = Fighter("b"); a.Fatigue = fatigue; a.LegDamage = legDamage;
                var state = new MatchEngineState { Participants = new() { a, b } };
                MatchMovement.Initialize(state); var random = new Random(3);
                var before = MatchEngineSnapshot.Capture(state);
                var run = ActionResolver.Resolve(state, new MatchEngineAction { Type = MatchActionType.RopeRun,
                    PerformerId = "a", ReceiverId = "b" }, random);
                MatchMovement.Apply(state, run, before); state.Events.Add(run);
                before = MatchEngineSnapshot.Capture(state);
                var rebound = MatchPhysics.Next(state, random, null);
                Assert.That(rebound.Type, Is.EqualTo(MatchActionType.RopeRun));
                Assert.That(rebound.Detail, Is.EqualTo("rebound"));
                Assert.That(rebound.PerformerId, Is.EqualTo("a"));
                MatchMovement.Apply(state, rebound, before); state.Events.Add(rebound);
                before = MatchEngineSnapshot.Capture(state);
                var attack = MatchPhysics.Next(state, random, null);
                Assert.That(attack.Type, Is.EqualTo(MatchActionType.RunningAttack));
                MatchMovement.Apply(state, attack, before);
                if (attack.Result is MatchActionResult.FullSuccess or MatchActionResult.PartialSuccess)
                {
                    var first = attack.Motion[0].Participants.Single(p => p.Id == "a");
                    var next = attack.Motion[1].Participants.Single(p => p.Id == "a");
                    float dx = next.X - first.X, dz = next.Z - first.Z, previousDistance = 0;
                    for (int i = 1; i <= 100; i++)
                    {
                        var pose = MatchMovement.Sample(attack, "a", attack.Motion[1].Progress * i / 100);
                        float distance = (pose.X - first.X) * dx + (pose.Z - first.Z) * dz;
                        Assert.That(distance, Is.GreaterThanOrEqualTo(previousDistance - .00001f)); previousDistance = distance;
                    }
                }
                return run.MotionSeconds;
            }
        }

        [TestCase("snap-suplex", 0f, 0f, .5f, 0f)]
        [TestCase("rear-german", .6f, .6f, .85f, .85f)]
        [TestCase("vertical-suplex", -.85f, 0f, -.6f, 0f)]
        public void SuplexLiftsTheReceiverOverTheAttackerAndLandsBehind(string key, float ax, float az, float bx, float bz)
        {
            MoveTable.Initialize();
            var a = Fighter("a"); var b = Fighter("b");
            a.RingX = ax; a.RingZ = az; b.RingX = bx; b.RingZ = bz;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            var before = MatchEngineSnapshot.Capture(state);
            var e = new MatchEngineEvent { Type = MatchActionType.Grapple, Result = MatchActionResult.FullSuccess,
                PerformerId = "a", ReceiverId = "b", TechniqueId = key, Impact = 8 };
            b.IsDowned = true;
            MatchMovement.Apply(state, e, before);
            var contact = MatchMovement.Sample(e, "a", .3f);
            var lifted = MatchMovement.Sample(e, "b", .42f);
            var landedA = MatchMovement.Sample(e, "a", .65f);
            var landedB = MatchMovement.Sample(e, "b", .65f);
            Assert.That(lifted.Height, Is.GreaterThan(.5f));
            var dx = lifted.X - contact.X; var dz = lifted.Z - contact.Z;
            Assert.That(Math.Sqrt(dx * dx + dz * dz), Is.LessThan(.13));
            var forwardX = bx - ax; var forwardZ = bz - az;
            Assert.That((landedB.X - landedA.X) * forwardX + (landedB.Z - landedA.Z) * forwardZ, Is.LessThan(-.025f));
            Assert.That(landedB.Posture, Is.EqualTo(MatchPosture.Supine));
            Assert.That(e.Motion.All(f => f.Participants.All(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) <= 1.4f)), Is.True);
            AssertPose(MatchMovement.Sample(e, "a", 1), MatchEngineSnapshot.Capture(state).Participants[0]);
        }

        [Test]
        public void ACounteredSuplexDoesNotLiftOrThrowTheReceiver()
        {
            MoveTable.Initialize();
            var a = Fighter("a"); var b = Fighter("b");
            var state = new MatchEngineState { Participants = new() { a, b } };
            MatchMovement.Initialize(state);
            var before = MatchEngineSnapshot.Capture(state);
            var e = new MatchEngineEvent { Type = MatchActionType.Grapple, Result = MatchActionResult.Countered,
                PerformerId = "a", ReceiverId = "b", TechniqueId = "snap-suplex" };
            MatchMovement.Apply(state, e, before);
            Assert.That(e.Motion.All(f => f.Participants.Single(p => p.Id == "b").Height == 0), Is.True);
            Assert.That(b.RingX, Is.EqualTo(before.Participants[1].RingX));
            Assert.That(b.RingZ, Is.EqualTo(before.Participants[1].RingZ));
        }

        [TestCase(false, MatchActionResult.OneCount)]
        [TestCase(true, MatchActionResult.OneCount)]
        [TestCase(false, MatchActionResult.TwoCount)]
        [TestCase(true, MatchActionResult.TwoCount)]
        [TestCase(false, MatchActionResult.NearFall)]
        [TestCase(true, MatchActionResult.NearFall)]
        public void CoverStaysAboveTheReceiverThroughTheCountAndReleasesOnKickout(bool reverseRoles, MatchActionResult result)
        {
            var a = Fighter(reverseRoles ? "b" : "a"); var b = Fighter(reverseRoles ? "a" : "b");
            var state = new MatchEngineState { Participants = new() { reverseRoles ? b : a, reverseRoles ? a : b } };
            MatchMovement.Initialize(state);
            b.IsDowned = true; b.Posture = MatchPosture.Supine;
            var before = MatchEngineSnapshot.Capture(state);
            var cover = ActionResolver.Resolve(state, new MatchEngineAction { Type = MatchActionType.Pin,
                PerformerId = a.Id, ReceiverId = b.Id }, new Random(1));
            MatchMovement.Apply(state, cover, before);
            Assert.That(a.Height, Is.GreaterThan(b.Height));
            foreach (float progress in new[] { .3f, .5f, .65f, 1 })
                Assert.That(MatchMovement.Sample(cover, a.Id, progress).Height,
                    Is.EqualTo(MatchMovement.Sample(cover, b.Id, progress).Height + .06f).Within(.0001f));
            state.Exchange = null;
            before = MatchEngineSnapshot.Capture(state);
            var finish = new MatchEngineEvent { Type = MatchActionType.Finish, Result = MatchActionResult.Pinfall,
                PerformerId = a.Id, ReceiverId = b.Id };
            MatchMovement.Apply(state, finish, before);
            Assert.That(a.Height, Is.GreaterThan(b.Height));
            foreach (float progress in new[] { 0f, .3f, .5f, 1 })
                Assert.That(MatchMovement.Sample(finish, a.Id, progress).Height,
                    Is.EqualTo(MatchMovement.Sample(finish, b.Id, progress).Height + .06f).Within(.0001f));
            state.Exchange = null;
            before = MatchEngineSnapshot.Capture(state);
            var kickout = new MatchEngineEvent { Type = MatchActionType.Pin, Result = result,
                PerformerId = a.Id, ReceiverId = b.Id };
            MatchMovement.Apply(state, kickout, before);
            Assert.That(MatchMovement.Sample(kickout, a.Id, .5f).Height,
                Is.EqualTo(MatchMovement.Sample(kickout, b.Id, .5f).Height + .06f).Within(.0001f));
            Assert.That(a.Height, Is.EqualTo(b.Height));
            Assert.That(MatchMovement.Distance(a, b), Is.GreaterThan(.2f));
            var held = MatchMovement.Sample(kickout, a.Id, .65f);
            var pushed = MatchMovement.Sample(kickout, a.Id, .78f);
            var end = MatchMovement.Sample(kickout, a.Id, 1);
            Assert.That(held.X, Is.EqualTo(before.Participants.Single(p => p.Id == a.Id).RingX));
            Assert.That(held.Z, Is.EqualTo(before.Participants.Single(p => p.Id == a.Id).RingZ));
            float displacement = MathF.Sqrt(MathF.Pow(end.X - held.X, 2) + MathF.Pow(end.Z - held.Z, 2));
            float pushedDistance = MathF.Sqrt(MathF.Pow(pushed.X - held.X, 2) + MathF.Pow(pushed.Z - held.Z, 2));
            Assert.That(pushedDistance, Is.GreaterThan(displacement));
            Assert.That(pushedDistance, Is.LessThan(displacement + .03f));
            Assert.That(MatchMovement.Sample(kickout, b.Id, .78f).Height, Is.GreaterThan(b.Height));
            Assert.That(end.X, Is.EqualTo(a.RingX)); Assert.That(end.Z, Is.EqualTo(a.RingZ));
            Assert.That(end.Height, Is.EqualTo(a.Height));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepositionActuallyMovesTheExchangeInwardWithARecordedGrip(bool grounded)
        {
            var a = Fighter("a"); var b = Fighter("b");
            a.RingX = -.7f; b.RingX = -.92f; b.Position = MatchPosition.Ropes;
            a.Position = MatchPosition.Corner; a.RingZ = .8f;
            b.IsDowned = grounded; b.Posture = grounded ? MatchPosture.Supine : MatchPosture.Standing;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            var before = MatchEngineSnapshot.Capture(state);
            var e = ActionResolver.Resolve(state, new MatchEngineAction { Type = MatchActionType.Reposition,
                PerformerId = "a", ReceiverId = "b" }, new Random(1));
            MatchMovement.Apply(state, e, before);
            Assert.That(Math.Max(Math.Abs(b.RingX), Math.Abs(b.RingZ)), Is.LessThan(.5f));
            Assert.That(b.IsDowned, Is.EqualTo(grounded));
            Assert.That(a.Position, Is.EqualTo(MatchPosition.Center));
            Assert.That(b.Position, Is.EqualTo(MatchPosition.Center));
            var contact = MatchMovement.Sample(e, "a", .3f);
            Assert.That(contact.GripTargetId, Is.EqualTo("b"));
            Assert.That(contact.Grip, Is.EqualTo(grounded ? MatchGrip.Leg : MatchGrip.Wrist));
            var middle = MatchMovement.Sample(e, "b", .5f);
            Assert.That(middle.X, Is.GreaterThan(before.Participants[1].RingX).And.LessThan(b.RingX));
            Assert.That(MatchMovement.Sample(e, "b", 0).X, Is.EqualTo(before.Participants[1].RingX));
        }

        [Test]
        public void WrestlersCanChooseToRepositionAtThePhysicalEdgeEvenWhenTheirRegionIsCenter()
        {
            var a = Fighter("a"); var b = Fighter("b");
            a.RingX = -.7f; b.RingX = -.84f; b.IsDowned = true;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            Assert.That(MatchPhysics.Actions(state, a, b).Any(x => x.Type == MatchActionType.Reposition), Is.True);
            b.RingX = -.3f;
            Assert.That(MatchPhysics.Actions(state, a, b).Any(x => x.Type == MatchActionType.Reposition), Is.False);
        }

        [Test]
        public void RingPsychologyRecognizesSpaceForRepositioningAtTheEdge()
        {
            var a = Fighter("a"); var b = Fighter("b"); b.RingX = -.84f;
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            var action = new MatchEngineAction { Type = MatchActionType.Reposition, PerformerId = "a", ReceiverId = "b" };
            a.Attributes.RingPsychology = 1; var low = ActionEvaluator.Score(state, action);
            a.Attributes.RingPsychology = 20;
            Assert.That(ActionEvaluator.Score(state, action), Is.GreaterThan(low * 2));
        }

        [Test]
        public void WhipRecordsAWristGripBeforeReleasingTheRunner()
        {
            var a = Fighter("a"); var b = Fighter("b");
            var state = new MatchEngineState { Participants = new() { a, b } };
            MatchMovement.Initialize(state);
            var before = MatchEngineSnapshot.Capture(state);
            var e = ActionResolver.Resolve(state, new MatchEngineAction { Type = MatchActionType.CornerWhip,
                PerformerId = "a", ReceiverId = "b" }, new Random(1));
            MatchMovement.Apply(state, e, before);
            var contact = e.Motion.Single(x => x.Progress == .3f).Participants;
            var holder = contact.Single(x => x.Grip == MatchGrip.Wrist);
            var runner = contact.Single(x => x.Id == holder.GripTargetId);
            var dx = holder.X - runner.X; var dz = holder.Z - runner.Z;
            Assert.That(Math.Sqrt(dx * dx + dz * dz), Is.LessThanOrEqualTo(.26));
            Assert.That(e.Motion.Last().Participants.All(x => x.Grip == MatchGrip.None), Is.True);
        }

        private static void AssertPose(MatchMotionPose pose, MatchParticipantSnapshot snapshot)
        {
            Assert.That(pose.X, Is.EqualTo(snapshot.RingX).Within(.00001f));
            Assert.That(pose.Z, Is.EqualTo(snapshot.RingZ).Within(.00001f));
            Assert.That(pose.Height, Is.EqualTo(snapshot.Height).Within(.00001f));
            Assert.That(pose.FacingX, Is.EqualTo(snapshot.FacingX).Within(.00001f));
            Assert.That(pose.FacingZ, Is.EqualTo(snapshot.FacingZ).Within(.00001f));
            Assert.That(pose.Posture, Is.EqualTo(snapshot.Posture));
            Assert.That(pose.Grip, Is.EqualTo(snapshot.Grip));
            Assert.That(pose.GripTargetId ?? "", Is.EqualTo(snapshot.GripTargetId ?? ""));
        }

        [Test]
        public void PsychologyRecognizesWhenToRestRatherThanAlwaysFavoringAttack()
        {
            var a = new MatchEngineParticipant { Id = "a", Fatigue = 85 };
            var b = new MatchEngineParticipant { Id = "b" };
            var state = new MatchEngineState { Participants = new() { a, b } };
            var rest = new MatchEngineAction { Type = MatchActionType.Rest, PerformerId = "a", ReceiverId = "b" };
            var attack = new MatchEngineAction { Type = MatchActionType.HeavyStrike, PerformerId = "a", ReceiverId = "b" };
            a.Attributes.RingPsychology = 1;
            var low = ActionEvaluator.Score(state, rest) / ActionEvaluator.Score(state, attack);
            a.Attributes.RingPsychology = 20;
            Assert.That(ActionEvaluator.Score(state, rest) / ActionEvaluator.Score(state, attack), Is.GreaterThan(low * 1.5f));
        }

        [Test]
        public void ADownedOpponentCanRecoverWhileTheOtherWrestlerTaunts()
        {
            var a = new MatchEngineParticipant { Id = "a" };
            var b = new MatchEngineParticipant { Id = "b", IsDowned = true, Fatigue = 30 };
            var state = new MatchEngineState { Participants = new() { a, b } };
            ActionResolver.Resolve(state, new MatchEngineAction { Type = MatchActionType.Taunt, PerformerId = "a", ReceiverId = "b" }, new Random(1));
            Assert.That(b.Fatigue, Is.LessThan(30));
        }

        [Test]
        public void RegisteredFinishersKeepTheirTechniqueNameInReplayCommentary()
        {
            var catalog = UnityEngine.Resources.Load<PWManager.Data.Catalogs.StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var registry = new StaticContentRegistry(catalog);
            var a = Fighter("a"); var b = Fighter("b");
            a.FinisherMoveIds.Add("move_020");
            var match = new MatchSimulator(13, findMove: registry.GetMoveRules).Simulate(new[] { a, b }, 120,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
            var finisher = match.Events.Last(e => e.Type == MatchActionType.Finisher && e.MoveId == "move_020");
            var name = registry.GetMoveRules("move_020").Name;
            Assert.That(finisher.TechniqueName, Is.EqualTo(name));
            Assert.That(MatchEventNarration.Text(finisher, new PWManager.Domain.Models.MatchResultState(), id => id), Does.Contain(name));
        }
    }
}
