using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Loading;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class MatchMoveMotionTests
    {
        [SetUp] public void Setup() => MoveTable.Initialize();

        [Test]
        public void ExecutionStyleIsIndependentOfTheTargetsStartingSituation()
        {
            Assert.That(MatchTechniques.Find("uppercut").Rules.ExecutionStyle, Is.EqualTo(MoveExecutionStyle.Stationary));
            Assert.That(MatchTechniques.Find("move_004").Rules.ExecutionStyle, Is.EqualTo(MoveExecutionStyle.Running));
            Assert.That(MatchTechniques.Find("running-clothesline").Rules.ExecutionStyle, Is.EqualTo(MoveExecutionStyle.Running));
            Assert.That(MatchTechniques.Find("missile-dropkick").Rules.ExecutionStyle, Is.EqualTo(MoveExecutionStyle.Aerial));
            Assert.That(MatchTechniques.Find("move_006").Rules.Kind, Is.EqualTo(MoveKind.Grapple));
            string csv = Resources.Load<TextAsset>(MoveTable.ResourcePath).text;
            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var row = lines[1].Split(','); row[24] = "Unsupported"; lines[1] = string.Join(",", row);
            Assert.Throws<FormatException>(() => MoveTable.Parse(string.Join("\n", lines)));
        }

        [Test]
        public void CrossfaceParticipantsFaceTheSameDirectionDuringTheHold()
        {
            var e = Execute("crossface");
            foreach (float progress in new[] { .5f, .65f, 1f })
            {
                var a = Pose(e, "a", progress); var b = Pose(e, "b", progress);
                Assert.That(a.FacingX * b.FacingX + a.FacingZ * b.FacingZ, Is.GreaterThan(.999f));
                Assert.That(a.Posture, Is.EqualTo(MatchPosture.Prone));
                Assert.That(b.Posture, Is.EqualTo(MatchPosture.Prone));
            }
        }

        [Test]
        public void BodyScissorsKeepsBothWrestlersGroundedDuringTheHold()
        {
            var e = Execute("body-scissors");
            foreach (float progress in new[] { .5f, .65f, 1f })
            foreach (string id in new[] { "a", "b" })
                Assert.That(Pose(e, id, progress).Posture, Is.EqualTo(MatchPosture.Supine));
        }

        [Test]
        public void GroundHoldsRecordTheirOwnPosturesAndFigureFourFacesOpposite()
        {
            foreach (string key in new[] { "boston-crab", "single-leg-crab" })
                Assert.That(Pose(Execute(key), "b", 1).Posture, Is.EqualTo(MatchPosture.Prone));
            Assert.That(Pose(Execute("chinlock"), "b", 1).Posture, Is.EqualTo(MatchPosture.Seated));
            foreach (string key in new[] { "armbar", "figure-four", "heel-hook", "kneebar", "triangle-choke" })
                Assert.That(Pose(Execute(key), "a", 1).Posture, Is.EqualTo(MatchPosture.Supine));
            var e = Execute("figure-four"); var a = Pose(e, "a", .65f); var b = Pose(e, "b", .65f);
            Assert.That(a.FacingX * b.FacingX + a.FacingZ * b.FacingZ, Is.LessThan(-.999f));
            e = Execute("prone-camel-clutch");
            Assert.That(Pose(e, "a", 1).Height, Is.GreaterThan(Pose(e, "b", 1).Height));
            e = Execute("triangle-choke");
            Assert.That(Pose(e, "b", 1).Posture, Is.EqualTo(MatchPosture.Kneeling));
            Assert.That(Pose(e, "a", 1).Height, Is.LessThan(Pose(e, "b", 1).Height));
        }

        [Test]
        public void SuplexThrowsBehindWhileBodySlamThrowsForward()
        {
            var suplex = Execute("vertical-suplex"); var slam = Execute("body-slam");
            Assert.That(Pose(suplex, "b", .42f).Height, Is.GreaterThan(.6f));
            Assert.That(Pose(suplex, "b", .65f).X, Is.LessThan(Pose(suplex, "a", .65f).X - .3f));
            Assert.That(Pose(slam, "b", .65f).X, Is.GreaterThan(Pose(slam, "a", .65f).X));
        }

        [Test]
        public void FailedBridgeFallsFlatAndReleasesTheReceiver()
        {
            var e = Execute("move_023", MatchActionResult.ReceptionFailure);
            foreach (float progress in new[] { .65f, 1f })
            {
                var a = Pose(e, "a", progress); var b = Pose(e, "b", progress);
                Assert.That(new Vector2(a.X - b.X, a.Z - b.Z).magnitude, Is.GreaterThan(.3f));
                Assert.That(a.Height, Is.Zero);
                Assert.That(a.Grip, Is.EqualTo(MatchGrip.None));
            }
        }

        [Test]
        public void PowerbombDropsForwardWhilePiledriverAndBrainbusterDropClose()
        {
            var bomb = Execute("standing-powerbomb"); var piledriver = Execute("move_008"); var brainbuster = Execute("brainbuster");
            Assert.That(Pose(bomb, "b", .5f).Height, Is.GreaterThan(.7f));
            Assert.That(Pose(bomb, "b", .65f).X - Pose(bomb, "a", .65f).X, Is.GreaterThan(.4f));
            Assert.That(Math.Abs(Pose(piledriver, "b", .65f).X - Pose(piledriver, "a", .65f).X), Is.LessThan(.01f));
            Assert.That(Pose(brainbuster, "b", .5f).Height, Is.GreaterThan(Pose(piledriver, "b", .5f).Height + .3f));
        }

        [Test]
        public void SpearDrivesBothParticipantsForwardAndBellyToBellyThrowsSideways()
        {
            var spear = Execute("move_004"); var belly = Execute("belly-to-belly");
            Assert.That(Pose(spear, "a", .65f).X, Is.GreaterThan(spear.Before.Participants[0].RingX + .6f));
            Assert.That(Pose(spear, "b", .65f).X, Is.GreaterThan(spear.Before.Participants[1].RingX + .4f));
            Assert.That(Math.Abs(Pose(belly, "b", .65f).Z - Pose(belly, "a", .65f).Z), Is.GreaterThan(.5f));
        }

        [TestCase("move_004")]
        [TestCase("legacy-move_004")]
        public void SpearCarriesReceiverWithAttackerAfterContact(string key)
        {
            var spear = Execute(key);
            Assert.That(Pose(spear, "b", .42f).X, Is.EqualTo(Pose(spear, "b", 0).X).Within(.0001f));
            Assert.That(Pose(spear, "b", .5f).X - Pose(spear, "b", .42f).X, Is.GreaterThan(.1f));
            Assert.That(Pose(spear, "b", .65f).X - Pose(spear, "b", .6f).X, Is.LessThan(.13f));
            for (int i = 42; i <= 65; i++)
            {
                var actor = Pose(spear, "a", i / 100f); var target = Pose(spear, "b", i / 100f);
                Assert.That(target.X - actor.X, Is.InRange(.165f, .215f), "The receiver separated during flight");
                if (i >= 50) Assert.That(target.Height, Is.EqualTo(actor.Height).Within(.0001f));
            }
        }

        [Test]
        public void SpearKeepsVelocityAcrossRunBeatsWithoutOvershootingRecordedPositions()
        {
            var spear = Execute("move_004");
            foreach (float time in new[] { .42f, .5f, .6f })
            {
                float left = (Pose(spear, "a", time).X - Pose(spear, "a", time - .0001f).X) / .0001f;
                float right = (Pose(spear, "a", time + .0001f).X - Pose(spear, "a", time).X) / .0001f;
                Assert.That(left, Is.GreaterThan(.5f));
                Assert.That(right, Is.EqualTo(left).Within(.03f));
            }
            foreach (var id in new[] { "a", "b" })
                for (int i = 1; i < spear.Motion.Count; i++)
                {
                    var a = spear.Motion[i - 1]; var b = spear.Motion[i];
                    float start = a.Participants.Single(p => p.Id == id).X, end = b.Participants.Single(p => p.Id == id).X;
                    for (int j = 0; j <= 20; j++)
                        Assert.That(Pose(spear, id, a.Progress + (b.Progress - a.Progress) * j / 20f).X,
                            Is.InRange(Math.Min(start, end) - .00001f, Math.Max(start, end) + .00001f));
                }
        }

        [Test]
        public void MoonsaultShootingStarAnd450HaveDifferentFacingAndRotation()
        {
            var moon = Execute("move_014"); var star = Execute("move_015"); var splash = Execute("move_016");
            Assert.That(Pose(moon, "a", .34f).FacingX, Is.LessThan(0));
            Assert.That(Pose(star, "a", .34f).FacingX, Is.GreaterThan(0));
            Assert.That(Pose(star, "a", .6f).Tilt, Is.LessThan(0));
            Assert.That(Pose(splash, "a", .65f).Tilt, Is.EqualTo(450).Within(.001));
        }

        [Test]
        public void HalfTurnInterpolatesThroughAnAngleInsteadOfSnappingAtTheMidpoint()
        {
            var e = new MatchEngineEvent { Motion = new()
            {
                new() { Progress = 0, Participants = new() { new() { Id = "a", FacingX = 1 } } },
                new() { Progress = 1, Participants = new() { new() { Id = "a", FacingX = -1 } } }
            } };
            var halfway = Pose(e, "a", .5f);
            Assert.That(Math.Abs(halfway.FacingX), Is.LessThan(.001));
            Assert.That(Math.Abs(halfway.FacingZ), Is.GreaterThan(.999));
        }

        [Test]
        public void SubmissionsApproachGraduallyBeforeSettlingIntoTheirRecordedHold()
        {
            foreach (string key in new[] { "crossface", "move_012", "bearhug", "chain-headlock", "chain-wristlock", "chain-hammerlock", "chain-waistlock" })
            {
                var e = Execute(key);
                var start = Pose(e, "a", 0); var end = Pose(e, "a", 1);
                var direction = new Vector2(end.X - start.X, end.Z - start.Z);
                float previous = 0;
                for (int i = 1; i <= 50; i++)
                {
                    var pose = Pose(e, "a", i / 100f);
                    float distance = Vector2.Dot(new Vector2(pose.X - start.X, pose.Z - start.Z), direction.normalized);
                    Assert.That(distance, Is.GreaterThanOrEqualTo(previous - .00001f));
                    Assert.That(distance - previous, Is.LessThan(direction.magnitude * .045f));
                    previous = distance;
                }
                var middle = Pose(e, "a", .3f);
                Assert.That(new Vector2(middle.X - start.X, middle.Z - start.Z).magnitude,
                    Is.LessThan(direction.magnitude * .75f));
                Assert.That(Pose(e, "a", .5f).X, Is.EqualTo(end.X).Within(.0001f));
                Assert.That(Pose(e, "a", .5f).Z, Is.EqualTo(end.Z).Within(.0001f));
            }
        }

        [Test]
        public void MotionTableRejectsMissingTechniqueAndNonFiniteValues()
        {
            string csv = Resources.Load<TextAsset>("PWManagerRuntime/MatchMoveMotions").text;
            var moves = MoveTable.Load();
            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string missing = string.Join("\n", lines.Where(l => !l.StartsWith("move_004,")));
            Assert.Throws<FormatException>(() => MoveMotionTable.Parse(missing, moves));
            var invalid = lines.ToArray(); var row = invalid[1].Split(','); row[2] = "NaN"; invalid[1] = string.Join(",", row);
            Assert.Throws<FormatException>(() => MoveMotionTable.Parse(string.Join("\n", invalid), moves));
            invalid = lines.ToArray(); row = invalid[1].Split(','); row[12] = "Unknown"; invalid[1] = string.Join(",", row);
            Assert.Throws<FormatException>(() => MoveMotionTable.Parse(string.Join("\n", invalid), moves));
        }

        [TestCase("corner-clothesline")]
        [TestCase("corner-splash")]
        [TestCase("corner-knee")]
        [TestCase("running-clothesline")]
        [TestCase("shoulder-block")]
        [TestCase("legacy-move_004")]
        public void RunningStrikesCarryTheirChargeSpeedThroughContact(string key)
        {
            var e = Execute(key);
            float contact = MatchTechniques.Find(key).Id == "move_004" ? .42f : .65f;
            float speed = Speed(contact - .2f);
            Assert.That(speed, Is.GreaterThan(.1f));
            foreach (float offset in new[] { .15f, .1f, .05f, .01f, 0 })
                Assert.That(Speed(contact - offset), Is.GreaterThanOrEqualTo(speed * .95f), key + " slowed before contact");
            Assert.That(Speed(.999f), Is.LessThan(speed * .2f), key + " did not settle after contact");

            float Speed(float progress)
            {
                var left = Pose(e, "a", progress - .0001f); var right = Pose(e, "a", progress + .0001f);
                return new Vector2(right.X - left.X, right.Z - left.Z).magnitude / .0002f;
            }
        }

        private static MatchEngineEvent Execute(string key, MatchActionResult result = MatchActionResult.FullSuccess)
        {
            var move = MatchTechniques.Find(key);
            var a = new MatchEngineParticipant { Id = "a", RingX = -.4f, FacingX = 1 };
            var b = new MatchEngineParticipant { Id = "b", RingX = .1f, FacingX = -1 };
            var state = new MatchEngineState { Participants = new() { a, b }, SpatialInitialized = true };
            var e = new MatchEngineEvent { Type = move.Action, TechniqueId = move.Id, PerformerId = a.Id, ReceiverId = b.Id,
                Result = result, Impact = 8, Before = MatchEngineSnapshot.Capture(state) };
            b.IsDowned = true; b.Posture = move.Rules.EndPosture;
            MatchMovement.Apply(state, e, e.Before); e.After = MatchEngineSnapshot.Capture(state);
            foreach (var p in state.Participants)
            {
                var start = Pose(e, p.Id, 0); var end = Pose(e, p.Id, 1);
                Assert.That(end.X, Is.EqualTo(p.RingX)); Assert.That(end.Z, Is.EqualTo(p.RingZ));
                Assert.That(end.Height, Is.EqualTo(p.Height));
                Assert.That(start.X, Is.EqualTo(e.Before.Participants.Single(x => x.Id == p.Id).RingX));
            }
            return e;
        }
        private static MatchMotionPose Pose(MatchEngineEvent e, string id, float progress) => MatchMovement.Sample(e, id, progress);
    }
}
