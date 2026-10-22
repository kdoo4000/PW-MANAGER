using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;

namespace PWManager.Tests
{
    public sealed class MatchFlowNarrationTests
    {
        [SetUp]
        public void LoadMoves() => PWManager.Data.Loading.MoveTable.Initialize();

        private static MatchEngineState State() => new()
        {
            TargetDurationSeconds = 600, Phase = MatchPhase.Opening,
            Opening = new() { Initialized = true, Style = MatchOpeningStyle.Probe },
            Participants = new()
            {
                new() { Id = "a", SideId = "a", Attributes = Attributes(), TargetRegion = BodyRegion.Body },
                new() { Id = "b", SideId = "b", Attributes = Attributes(), TargetRegion = BodyRegion.Body }
            }
        };

        private static WrestlerAttributesState Attributes() => new()
            { Brawling = 12, Technical = 12, Power = 12, HighFlying = 12, Stamina = 12, Selling = 12, RingPsychology = 12 };

        [Test]
        public void FeelingOutStartsWithHandFightingRatherThanAFreeKneeStrike()
        {
            var s = State(); s.ElapsedSeconds = 5;
            s.Events.Add(new() { Type = MatchActionType.Probe });
            var actions = MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]);
            Assert.That(actions, Is.Not.Empty);
            Assert.That(actions.All(x => x.Type is MatchActionType.Probe or MatchActionType.TieUp), Is.True);
        }

        [TestCase(MatchOpeningStyle.Ambush)]
        [TestCase(MatchOpeningStyle.Clash)]
        [TestCase(MatchOpeningStyle.Dominance)]
        public void AggressiveOpeningDoesNotOfferAnUnmotivatedFormalTieUp(MatchOpeningStyle style)
        {
            var s = State(); s.Opening.Style = style;
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Any(x => x.Type is MatchActionType.Probe or MatchActionType.TieUp), Is.False);
        }

        [Test]
        public void ContactDoesNotResetIntoFeelingOutEvenWhileThePhaseIsOpening()
        {
            var s = State(); s.ElapsedSeconds = 15;
            s.Events.Add(new() { Type = MatchActionType.Counter, Impact = 3, Result = MatchActionResult.FullSuccess });
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Any(x => x.Type is MatchActionType.Probe or MatchActionType.TieUp), Is.False);
        }

        [TestCase("move_182", MatchActionType.RunningAttack)]
        [TestCase("move_160", MatchActionType.GroundAttack)]
        public void LowImpactLandingKeepsTheAttackersFollowUp(string moveId, MatchActionType type)
        {
            var s = State(); s.Phase = MatchPhase.Control; s.Opening.Completed = true; s.ElapsedSeconds = 94;
            var a = s.Participants[0]; var b = s.Participants[1];
            b.IsDowned = true; b.Posture = MatchPosture.Supine; b.Stun = 5; b.ActionReadiness = 36; b.MatchControl = 55;
            a.ActionReadiness = 18; a.MatchControl = 40;
            MatchMovement.Initialize(s);
            var before = MatchEngineSnapshot.Capture(s);
            var e = MatchPhysics.Resolve(s, new() { Type = type, MoveId = moveId, PerformerId = "a", ReceiverId = "b", IsContinuation = true }, new Random(1));
            e.Before = before;
            MatchMovement.Apply(s, e, before);
            e.After = MatchEngineSnapshot.Capture(s); s.Events.Add(e); s.ElapsedSeconds += e.DurationSeconds;
            Assert.That(e.Impact, Is.InRange(1, 5));
            Assert.That(a.IsDowned, Is.True);
            Assert.That(s.Exchange?.Kind, Is.EqualTo(ExchangeKind.FollowUp));
            var next = new MatchSimulator(2, accidentsEnabled: false).Step(s);
            Assert.That(next.PerformerId, Is.EqualTo("a"));
            Assert.That(next.CauseEventIndex, Is.EqualTo(0));
            Assert.That(next.Type is MatchActionType.Cover or MatchActionType.Submission or MatchActionType.Recover or MatchActionType.Rest, Is.True);
        }

        private sealed class FailedExecution : Random
        {
            public override double NextDouble() => .9999;
        }

        [TestCase("move_182", MatchActionType.RunningAttack)]
        [TestCase("move_160", MatchActionType.GroundAttack)]
        public void FailedDropDoesNotKeepTheAttackersOffense(string moveId, MatchActionType type)
        {
            var s = State(); s.Participants[1].IsDowned = true; s.Participants[1].Posture = MatchPosture.Supine;
            var e = MatchPhysics.Resolve(s, new() { Type = type, MoveId = moveId, PerformerId = "a", ReceiverId = "b", IsContinuation = true }, new FailedExecution());
            Assert.That(e.Result, Is.EqualTo(MatchActionResult.ExecutionFailure));
            Assert.That(s.Exchange?.Kind, Is.Not.EqualTo(ExchangeKind.FollowUp));
            Assert.That(s.Participants[1].BodyDamage, Is.Zero);
        }

        private static MatchEngineState WaitingOnTopRope(string moveId = null)
        {
            var s = State(); s.Phase = MatchPhase.Control; s.Opening.Completed = true; s.ElapsedSeconds = 80;
            s.Participants[0].Position = MatchPosition.TopRope;
            s.Participants[0].WrestlingStyleId = "style_007";
            s.Participants[1].IsDowned = true; s.Participants[1].Posture = MatchPosture.Seated;
            s.Exchange = new() { Id = 8, ActorId = "a", TargetId = "b", Kind = ExchangeKind.FollowUp,
                MoveId = moveId, ActionType = moveId == null ? MatchActionType.Dive : MatchActionType.Signature };
            return s;
        }

        [TestCase(null)]
        [TestCase("move_172")]
        public void TopRopeSetupWaitsThroughRisingThenAttemptsAStandingDive(string moveId)
        {
            var s = WaitingOnTopRope(moveId);
            var setup = s.Exchange;
            var phase = new ControlMatchState();
            foreach (var posture in new[] { MatchPosture.Kneeling, MatchPosture.Standing })
            {
                var recovery = MatchPhysics.Next(s, new FailedExecution(), phase);
                Assert.That(recovery.Type, Is.EqualTo(MatchActionType.Recover));
                Assert.That(recovery.PerformerId, Is.EqualTo("b"));
                Assert.That(s.Participants[1].Posture, Is.EqualTo(posture));
                Assert.That(s.Participants[0].Position, Is.EqualTo(MatchPosition.TopRope));
                Assert.That(s.Exchange, Is.SameAs(setup));
                s.Events.Add(recovery); s.ElapsedSeconds += recovery.DurationSeconds;
            }
            var attempt = MatchPhysics.Next(s, new FailedExecution(), phase);
            Assert.That(attempt.Type, Is.EqualTo(moveId == null ? MatchActionType.Dive : MatchActionType.Signature));
            Assert.That(MatchTechniques.Find(attempt).Rules.Situation, Is.EqualTo(MoveSituation.DivingStanding));
            if (moveId != null) Assert.That(attempt.TechniqueId, Is.EqualTo(moveId));
        }

        [TestCase("move_018", false, false)]
        [TestCase(null, true, false)]
        [TestCase(null, false, true)]
        public void TopRopeWaitDoesNotForceAGroundedMoveOrAnUnavailableTarget(string moveId, bool outside, bool injured)
        {
            var s = WaitingOnTopRope(moveId);
            if (outside) s.Participants[1].Position = MatchPosition.Outside;
            if (injured) { s.Participants[1].RealInjury = InjuryStatus.Moderate; s.Participants[1].RealInjuryRegion = BodyRegion.Legs; }
            var e = MatchPhysics.Next(s, new FailedExecution(), new ControlMatchState());
            Assert.That(e.Detail, Is.EqualTo("aerial-setup-lost"));
            Assert.That(s.Exchange, Is.Null);
            Assert.That(s.Participants[1].Posture, Is.EqualTo(MatchPosture.Seated));
        }

        private static string Name(string id) => id == "a" ? "Isaac Dalton" : "Damon Fairchild";

        [Test]
        public void OrdinaryCommentaryUsesSurnamesButFinisherAttemptsUseFullNames()
        {
            var e = new MatchEngineEvent { Type = MatchActionType.BasicStrike, PerformerId = "a", ReceiverId = "b", TechniqueName = "포어암", Result = MatchActionResult.FullSuccess };
            var result = new MatchResultState { EngineState = State() };
            var normal = MatchEventNarration.Text(e, result, Name);
            Assert.That(normal, Does.Contain("Dalton").And.Contain("Fairchild"));
            Assert.That(normal, Does.Not.Contain("Isaac").And.Not.Contain("Damon"));
            e.Type = MatchActionType.Finisher;
            var decisive = MatchEventNarration.Text(e, result, Name);
            Assert.That(decisive, Does.Contain("Isaac Dalton").And.Contain("Damon Fairchild"));
        }

        [Test]
        public void CommentaryKeepsSameSurnameOpponentsDistinct()
        {
            var e = new MatchEngineEvent { Type = MatchActionType.BasicStrike, PerformerId = "a", ReceiverId = "b", Result = MatchActionResult.FullSuccess };
            var result = new MatchResultState { EngineState = State() };
            Assert.That(MatchEventNarration.Text(e, result, id => id == "a" ? "Owen Hart" : "Bret Hart"), Does.Contain("Owen Hart").And.Contain("Bret Hart"));
        }

        [Test]
        public void SurnameCommentaryStillLinksToTheCorrectWrestler()
        {
            var wrestlers = new[] { "a", "b" }.Select(id => new WrestlerState { Identity = new() { Id = id, RingName = Name(id) } }).ToArray();
            var text = WrestlerNameText.Format("Dalton, Fairchild의 움직임을 봅니다.", wrestlers);
            Assert.That(text, Does.Contain("<link=\"wrestler:a\"><u>Dalton</u></link>"));
            Assert.That(text, Does.Contain("<link=\"wrestler:b\"><u>Fairchild</u></link>"));
        }

        private static MatchResultState RopePair() => new()
        {
            EngineState = new() { EngineVersion = 2, Events = new()
            {
                new() { Type = MatchActionType.RopeRun, Detail = "run-up", PerformerId = "a", ReceiverId = "b", ExchangeId = 12, MatchTimeSeconds = 88, DurationSeconds = 3, Result = MatchActionResult.FullSuccess },
                new() { Type = MatchActionType.RopeRun, Detail = "rebound", PerformerId = "a", ReceiverId = "b", ExchangeId = 12, MatchTimeSeconds = 91, DurationSeconds = 3, Result = MatchActionResult.FullSuccess }
            } }
        };

        [Test]
        public void RunningToTheRopesAndReboundingShareOneCommentaryLine()
        {
            var result = RopePair();
            var line = MatchNarrationService.NarrateEngineEvent(result, 0, Name);
            Assert.That(line, Does.Contain("로프").And.Contain("반동").And.Contain("돌진"));
            Assert.That(MatchNarrationService.NarrateEngineEvent(result, 1, Name), Is.Empty);
            Assert.That(result.EngineState.Events, Has.Count.EqualTo(2));
            Assert.That(result.EngineState.Events.Sum(e => e.DurationSeconds), Is.EqualTo(6));
        }

        [Test]
        public void AnUnpairedWhipReboundStillHasItsOwnCommentary()
        {
            var result = RopePair(); result.EngineState.Events.RemoveAt(0);
            Assert.That(MatchNarrationService.NarrateEngineEvent(result, 0, Name), Is.Not.Empty);
        }

        [Test]
        public void InterleavedRecoveryStagesGiveEachWrestlerOneLine()
        {
            var result = new MatchResultState { EngineState = new() { EngineVersion = 2, Events = new()
            {
                new() { Type = MatchActionType.Recover, Detail = "seated", PerformerId = "b" },
                new() { Type = MatchActionType.Recover, Detail = "kneeling", PerformerId = "b" },
                new() { Type = MatchActionType.Recover, Detail = "seated", PerformerId = "a" },
                new() { Type = MatchActionType.Recover, Detail = "standing", PerformerId = "b" }
            } } };
            Assert.That(result.EngineState.Events.Count(e => MatchEventNarration.ShouldNarrate(e, result)), Is.EqualTo(2));
            Assert.That(MatchEventNarration.CommentaryIndex(result, 3), Is.Zero);
            result.EngineState.Events.Insert(3, new() { Type = MatchActionType.Reposition, PerformerId = "a", ReceiverId = "other" });
            Assert.That(MatchEventNarration.ShouldNarrate(result.EngineState.Events[4], result), Is.False);
            result.EngineState.Events.Insert(3, new() { Type = MatchActionType.BasicStrike, PerformerId = "a", ReceiverId = "b", Impact = 2 });
            Assert.That(MatchEventNarration.ShouldNarrate(result.EngineState.Events[5], result), Is.True);
        }

        [Test]
        public void TurningTheOpponentAndCoveringShareOneLineButTheCountStaysSeparate()
        {
            var result = new MatchResultState { EngineState = new() { EngineVersion = 2, Events = new()
            {
                new() { Type = MatchActionType.TurnOver, Detail = "pin-setup", PerformerId = "a", ReceiverId = "b", ExchangeId = 35, MatchTimeSeconds = 226, DurationSeconds = 3 },
                new() { Type = MatchActionType.Cover, PerformerId = "a", ReceiverId = "b", ExchangeId = 35, MatchTimeSeconds = 229, DurationSeconds = 2 },
                new() { Type = MatchActionType.Pin, PerformerId = "a", ReceiverId = "b", ExchangeId = 35, MatchTimeSeconds = 231, DurationSeconds = 1, Result = MatchActionResult.OneCount }
            } } };
            Assert.That(MatchNarrationService.NarrateEngineEvent(result, 0, Name), Does.Contain("몸을 돌려").And.Contain("커버"));
            Assert.That(MatchNarrationService.NarrateEngineEvent(result, 1, Name), Is.Empty);
            Assert.That(MatchNarrationService.NarrateEngineEvent(result, 2, Name), Does.Contain("하나"));
        }

        private static MatchEngineState AfterEasyKickout()
        {
            var s = State(); s.Phase = MatchPhase.Control; s.Opening.Completed = true; s.ElapsedSeconds = 240;
            s.Participants[1].IsDowned = true; s.Participants[1].Posture = MatchPosture.Seated;
            s.Events.Add(new() { Type = MatchActionType.Pin, Result = MatchActionResult.OneCount, PerformerId = "a", ReceiverId = "b", MatchTimeSeconds = 231, DurationSeconds = 1 });
            return s;
        }

        [Test]
        public void EasyKickoutDoesNotAllowTheSameCoverAfterOnlyRecoveryOrAnUnrelatedReposition()
        {
            var s = AfterEasyKickout();
            s.Events.Add(new() { Type = MatchActionType.Reposition, PerformerId = "a", ReceiverId = "other" });
            s.Events.Add(new() { Type = MatchActionType.Recover, PerformerId = "b", Detail = "seated" });
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Any(x => x.Type == MatchActionType.Pin), Is.False);
            s.Events.Add(new() { Type = MatchActionType.BasicStrike, PerformerId = "a", ReceiverId = "other", Impact = 2 });
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Any(x => x.Type == MatchActionType.Pin), Is.False);
            s.Events.Add(new() { Type = MatchActionType.BasicStrike, PerformerId = "a", ReceiverId = "b", Impact = 2 });
            Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Any(x => x.Type == MatchActionType.Pin), Is.True);
        }

        [Test]
        public void FinisherKickoutsHaveNoCountLimitAndFreshOffenseCanEarnAnotherCover()
        {
            var s = AfterEasyKickout();
            for (int i = 0; i < 6; i++)
            {
                s.Events.Add(new() { Type = MatchActionType.Finisher, PerformerId = "a", ReceiverId = "b", Impact = 15 });
                Assert.That(MatchPhysics.Actions(s, s.Participants[0], s.Participants[1]).Any(x => x.Type == MatchActionType.Pin), Is.True);
                s.Events.Add(new() { Type = MatchActionType.Pin, Result = MatchActionResult.NearFall, PerformerId = "a", ReceiverId = "b" });
            }
        }
    }
}
