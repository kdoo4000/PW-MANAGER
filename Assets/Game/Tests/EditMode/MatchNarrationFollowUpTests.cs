using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchNarrationFollowUpTests
    {
        [SetUp]
        public void LoadMoves() => PWManager.Data.Loading.MoveTable.Initialize();

        private static MatchEngineState Grounded(MatchPosture posture = MatchPosture.Seated)
        {
            var attributes = new WrestlerAttributesState { Technical = 12, Power = 12, HighFlying = 12, Stamina = 12, RingPsychology = 12 };
            return new MatchEngineState
            {
                TargetDurationSeconds = 600, ElapsedSeconds = 200, Phase = MatchPhase.Control,
                Participants = new()
                {
                    new() { Id = "a", SideId = "a", Attributes = attributes, IsDowned = true, Posture = posture, Fatigue = 25, TargetRegion = BodyRegion.Body },
                    new() { Id = "b", SideId = "b", Attributes = attributes, IsDowned = true, Posture = MatchPosture.Supine, Stun = 22 }
                },
                Exchange = new MatchExchange { Kind = ExchangeKind.FollowUp, ActorId = "a", TargetId = "b", ActionType = MatchActionType.Pin, Id = 1 }
            };
        }

        [TestCase(MatchPosture.Seated)]
        [TestCase(MatchPosture.Kneeling)]
        [TestCase(MatchPosture.Supine)]
        public void ReadyLandingAllowsCoverAndGroundHoldWithoutStanding(MatchPosture posture)
        {
            var s = Grounded(posture); var a = s.Participants[0]; var b = s.Participants[1];
            var actions = MatchPhysics.Actions(s, a, b);
            Assert.That(actions.Any(x => x.Type == MatchActionType.Pin), Is.True);
            Assert.That(actions.Any(x => x.Type == MatchActionType.Submission), Is.True);
            Assert.That(actions.All(x => x.Type is MatchActionType.Pin or MatchActionType.Submission or MatchActionType.Recover), Is.True);
            var cover = new MatchSimulator(1, accidentsEnabled: false).Step(s);
            Assert.That(cover.Type, Is.EqualTo(MatchActionType.Cover));
            Assert.That(cover.Before.Participants[0].Posture, Is.EqualTo(posture));
            Assert.That(cover.Motion.First().Participants[0].Posture, Is.EqualTo(posture));
        }

        [TestCase("move_141")]
        [TestCase("body-scissors")]
        public void SeatedBodyScissorsStartsFromLandingAndKeepsInjuryChecks(string moveId)
        {
            var s = Grounded(); var a = s.Participants[0]; var b = s.Participants[1];
            Assert.That(MatchTechniques.Candidates(MatchActionType.Submission, a, b).Select(x => x.Key), Does.Contain("body-scissors"));
            a.RealInjury = InjuryStatus.Moderate; a.RealInjuryRegion = BodyRegion.Arms;
            Assert.That(MatchTechniques.Candidates(MatchActionType.Submission, a, b), Is.Empty);
            a.RealInjury = InjuryStatus.None;
            s.Exchange = null;
            var before = MatchEngineSnapshot.Capture(s);
            var e = ActionResolver.Resolve(s, new MatchEngineAction { Type = MatchActionType.Submission, PerformerId = "a", ReceiverId = "b", MoveId = moveId }, new Random(1));
            MatchMovement.Apply(s, e, before);
            Assert.That(e.Type, Is.EqualTo(MatchActionType.Submission));
            Assert.That(e.Result, Is.EqualTo(MatchActionResult.FullSuccess));
            Assert.That(e.Motion.First().Participants[0].Posture, Is.EqualTo(MatchPosture.Seated));
            Assert.That(s.Exchange.Kind, Is.EqualTo(ExchangeKind.Hold));
        }

        [TestCase(12, 25, InjuryStatus.None)]
        [TestCase(0, 80, InjuryStatus.None)]
        [TestCase(0, 25, InjuryStatus.Moderate)]
        public void IncapacitatedLandingStillRequiresRecovery(float stun, float fatigue, InjuryStatus injury)
        {
            var s = Grounded(); var a = s.Participants[0];
            a.Stun = stun; a.Fatigue = fatigue; a.RealInjury = injury;
            Assert.That(MatchPhysics.Actions(s, a, s.Participants[1]).Select(x => x.Type), Is.EqualTo(new[] { MatchActionType.Recover }));
            Assert.That(new MatchSimulator(1, accidentsEnabled: false).Step(s).Type, Is.EqualTo(MatchActionType.Recover));
        }

        [Test]
        public void GroundedContinuationRespectsRopesRefereeAndMatchRules()
        {
            var s = Grounded(); var a = s.Participants[0]; var b = s.Participants[1];
            s.Rules.Pinfall = s.Rules.Submission = false;
            Assert.That(MatchPhysics.Actions(s, a, b).Select(x => x.Type), Is.EqualTo(new[] { MatchActionType.Recover }));
            s.Rules.Pinfall = s.Rules.Submission = true; b.Position = MatchPosition.Ropes;
            Assert.That(MatchPhysics.Actions(s, a, b).Select(x => x.Type), Is.EqualTo(new[] { MatchActionType.Recover }));
            b.Position = MatchPosition.Center; s.RefereeUnavailableUntil = 210;
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type == MatchActionType.Pin), Is.False);
        }

        [Test]
        public void CommittedFinishAllowsCoverWithoutStartingAFreshGroundHold()
        {
            var s = Grounded(); s.BookingSequence = true;
            var actions = MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]);
            Assert.That(actions.Any(x => x.Type == MatchActionType.Pin), Is.True);
            Assert.That(actions.Any(x => x.Type == MatchActionType.Submission), Is.False);
        }

        [Test]
        public void DownedDefenderCannotBorrowTheAttackersFollowUpButCanActAfterItEnds()
        {
            var s = Grounded();
            s.Exchange.ActorId = "b"; s.Exchange.TargetId = "a";
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Select(x => x.Type), Is.EqualTo(new[] { MatchActionType.Recover }));
            s.Exchange = null;
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Any(x => x.Type == MatchActionType.Pin), Is.True);
        }

        [TestCase("chain-wristlock", "손목")]
        [TestCase("chain-hammerlock", "등 뒤")]
        [TestCase("chain-waistlock", "허리")]
        [TestCase("chain-headlock", "옆구리")]
        [TestCase("chain-rollthrough", "구르며")]
        [TestCase("chain-switch", "뒤로")]
        [TestCase("chain-headlock-takedown", "매트")]
        [TestCase("chain-waistlock-takedown", "매트")]
        public void ChainNarrationDescribesControlAndReversal(string key, string cue)
        {
            var move = MatchTechniques.Find(key);
            var e = new MatchEngineEvent { Type = MatchActionType.Chain, Result = MatchActionResult.Held, TechniqueId = move.Id, TechniqueName = move.Rules.Name, PerformerId = "a", ReceiverId = "b" };
            var text = MatchEventNarration.Text(e, new(), id => id);
            Assert.That(text, Does.Contain(cue));
            Assert.That(text, Does.Contain(move.Rules.Name));
            Assert.That(text, Does.Not.Contain("연결합니다"));
        }

        [TestCase("move_141", "압박")]
        [TestCase("move_334", "뛰어")]
        [TestCase("move_113", "몸통")]
        public void AttackNarrationUsesTheActualMoveFamily(string id, string cue)
        {
            var move = MatchTechniques.Find(id);
            var e = new MatchEngineEvent { Type = move.Action, Result = MatchActionResult.FullSuccess, TechniqueId = id, TechniqueName = move.Rules.Name, TargetRegion = move.Rules.TargetRegion, PerformerId = "a", ReceiverId = "b" };
            var text = MatchEventNarration.Text(e, new(), name => name);
            Assert.That(text, Does.Contain(cue));
            Assert.That(text, Does.Contain(move.Rules.Name));
            Assert.That(text, Does.Not.Contain("연결합니다"));
        }
    }
}
