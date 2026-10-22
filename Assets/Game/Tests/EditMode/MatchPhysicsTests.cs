using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchPhysicsTests
    {
        [SetUp]
        public void LoadRuntimeMoveTable() => PWManager.Data.Loading.MoveTable.Initialize();

        [TestCase("move_314", MatchPosition.TopRope)]
        [TestCase("move_320", MatchPosition.Ropes)]
        [TestCase("move_322", MatchPosition.Ropes)]
        [TestCase("move_332", MatchPosition.Ropes)]
        public void RingsideExpansion_DivesUseTheirLaunchPositionAndLandOutside(string moveId, MatchPosition launch)
        {
            var s = RingsideState();
            var flyer = s.GetParticipant("b"); flyer.Position = launch;
            s.Exchange.ActionType = MatchActionType.Dive; s.Exchange.MoveId = moveId;
            var e = new MatchSimulator(31, accidentsEnabled: false).Step(s);
            Assert.That(e.Type, Is.EqualTo(MatchActionType.Dive));
            Assert.That(e.TechniqueId, Is.EqualTo(moveId));
            Assert.That(e.Before.Participants.Single(p => p.Id == "b").Position, Is.EqualTo(launch));
            Assert.That(e.After.Participants.All(p => p.Position == MatchPosition.Outside), Is.True);
            Assert.That(e.Motion.Last().Participants.All(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) >= 1.15f), Is.True);
            Assert.That(e.CauseEventIndex, Is.EqualTo(0));
            Assert.That(MatchEventNarration.Text(e, new MatchResultState(), id => id), Does.Contain(e.TechniqueName));
        }

        [Test]
        public void RingsideExpansion_PoolsSeparateOutsideTargetsAndGroundedPostures()
        {
            var s = RingsideState(); var a = s.GetParticipant("b"); var b = s.GetParticipant("a");
            var standing = MatchTechniques.Candidates(MatchActionType.Dive, a, b);
            Assert.That(standing.Select(x => x.Key), Is.SupersetOf(new[] { "outside-crossbody", "suicide-dive", "plancha" }));
            Assert.That(standing.All(x => !x.GroundedTarget), Is.True);
            b.IsDowned = true; b.Posture = MatchPosture.Supine;
            Assert.That(MatchTechniques.Candidates(MatchActionType.Dive, a, b).Select(x => x.Key), Does.Contain("outside-splash"));
            b.Position = MatchPosition.Center;
            Assert.That(MatchTechniques.Candidates(MatchActionType.Dive, a, b).Any(x => x.Rules.Situation == MoveSituation.DivingOutsideGrounded), Is.False);
            a.RealInjury = InjuryStatus.Moderate; a.RealInjuryRegion = BodyRegion.Legs; b.Position = MatchPosition.Outside;
            Assert.That(MatchTechniques.Candidates(MatchActionType.Dive, a, b), Is.Empty);
        }

        [Test]
        public void RingsideExpansion_TableBreakDamagesOpponentAndMatchContinues()
        {
            var s = RingsideState(); s.GetParticipant("b").Position = MatchPosition.Outside;
            s.Rules.Disqualification = s.Rules.CountOut = false;
            UnityEngine.JsonUtility.FromJsonOverwrite("{\"ImportantMatch\":true}", s.Rules);
            UnityEngine.JsonUtility.FromJsonOverwrite("{\"AnnounceTableEligible\":true,\"AnnounceTableReady\":true}", s);
            s.Exchange.ActionType = MatchActionType.ObjectSetup;
            var e = new MatchSimulator(2, accidentsEnabled: false).Step(s);
            Assert.That(e.Detail, Is.EqualTo("announce-table-break"));
            Assert.That(e.Result, Is.EqualTo(MatchActionResult.TableBroken));
            Assert.That(e.Impact, Is.GreaterThanOrEqualTo(15));
            Assert.That(s.GetParticipant("a").BodyDamage, Is.GreaterThan(0));
            Assert.That(s.IsFinished, Is.False);
            Assert.That(MatchEventNarration.Text(e, new MatchResultState(), id => id), Does.Contain("해설 테이블"));
            var sim = new MatchSimulator(31, accidentsEnabled: false);
            while (s.Exchange != null) sim.Step(s);
            Assert.That(s.Participants.All(MatchPhysics.InRing), Is.True);
        }

        [Test]
        public void RingsideExpansion_HeelHasMoreReasonToEscapeAndCannotRepeatImmediately()
        {
            var s = State(); s.ElapsedSeconds = 180; s.Opening.Completed = true;
            UnityEngine.JsonUtility.FromJsonOverwrite("{\"RingsideEligible\":true}", s);
            var action = A(MatchActionType.ExitRing); var a = s.GetParticipant("a");
            float face = ActionEvaluator.Score(s, action);
            a.Alignment = KayfabeAlignment.Heel; a.Attributes.HeelWork = 20;
            Assert.That(ActionEvaluator.Score(s, action), Is.GreaterThan(face));
            s.Events.Add(new MatchEngineEvent { Type = MatchActionType.ExitRing, PerformerId = "a", MatchTimeSeconds = 170 });
            Assert.That(MatchPhysics.Actions(s, a, s.GetParticipant("b")).Any(x => x.Type == MatchActionType.ExitRing), Is.False);
        }

        private static MatchEngineState RingsideState()
        {
            var s = State(); s.TargetDurationSeconds = 1200; s.ElapsedSeconds = 500;
            s.Phase = MatchPhase.Control; s.Opening.Initialized = s.Opening.Completed = true;
            var a = s.GetParticipant("a"); a.Position = MatchPosition.Outside; a.Alignment = KayfabeAlignment.Heel;
            s.GetParticipant("b").WrestlingStyleId = "style_004";
            s.FindMove = id => MatchTechniques.Find(id)?.Rules;
            s.Events.Add(new MatchEngineEvent { Type = MatchActionType.ExitRing, PerformerId = "a", ReceiverId = "b", MatchTimeSeconds = 497 });
            s.Exchange = new MatchExchange { Kind = ExchangeKind.Ringside, ActorId = "a", TargetId = "b", Id = 1, CauseIndex = 0, StartedAt = 497 };
            return s;
        }

        [Test]
        public void RingsideExpansion_IllegalLaunchCoverAndTableUseAreRejected()
        {
            var s = RingsideState(); var a = s.GetParticipant("b"); var b = s.GetParticipant("a");
            a.Position = MatchPosition.Center;
            Assert.Throws<InvalidOperationException>(() => MatchPhysics.Resolve(s,
                new MatchEngineAction { Type = MatchActionType.Dive, PerformerId = a.Id, ReceiverId = b.Id, MoveId = "move_322" }, new Random(1)));
            a.Position = MatchPosition.Outside; b.IsDowned = true;
            Assert.Throws<InvalidOperationException>(() => MatchPhysics.Resolve(s,
                new MatchEngineAction { Type = MatchActionType.Pin, PerformerId = a.Id, ReceiverId = b.Id }, new Random(1)));
            b.IsDowned = false;
            s.AnnounceTableEligible = s.AnnounceTableReady = s.Rules.ImportantMatch = true;
            Assert.That(MatchPhysics.CanMove(s, a, b, "move_330"), Is.False, "DQ protects the table.");
            s.Rules.Disqualification = false; s.Rules.CountOutSeconds = 30;
            Assert.That(MatchPhysics.CanMove(s, a, b, "move_330"), Is.True);
            s.AnnounceTableBroken = true;
            Assert.That(MatchPhysics.CanMove(s, a, b, "move_330"), Is.False);
        }

        [Test]
        public void RingsideExpansion_DiveFailureAndEvadingKeepBothWrestlersOutside()
        {
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 80; seed++)
            {
                var s = RingsideState(); s.GetParticipant("b").Position = MatchPosition.Ropes;
                s.Exchange.ActionType = MatchActionType.Dive; s.Exchange.MoveId = "move_322";
                var e = new MatchSimulator(seed, accidentsEnabled: false).Step(s); seen.Add(e.Detail);
                if (e.Detail is not ("outside-dive-evaded" or "outside-dive-missed")) continue;
                Assert.That(e.Impact, Is.Zero);
                Assert.That(e.After.Participants.Single(p => p.Id == "b").IsDowned, Is.True);
                Assert.That(e.After.Participants.Single(p => p.Id == "b").BodyDamage, Is.GreaterThan(0));
                Assert.That(e.Motion.Last().Participants.All(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) >= 1.15f), Is.True);
                Assert.That(e.Motion.Last().Participants.All(p => p.Height == -.4f), Is.True);
            }
            Assert.That(seen, Is.SupersetOf(new[] { "outside-dive", "outside-dive-evaded", "outside-dive-missed" }));
        }

        [Test]
        public void RingsideExpansion_WaitStallRecoveryAndTableInterruptionFollowTheirCause()
        {
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 80; seed++)
            {
                var s = RingsideState(); s.Rules.CountOut = false;
                s.GetParticipant("a").Attributes.HeelWork = 20;
                var sim = new MatchSimulator(seed, accidentsEnabled: false);
                for (int i = 0; i < 8 && s.Exchange != null; i++)
                {
                    var e = sim.Step(s); seen.Add(e.Detail ?? e.Type.ToString()); seen.Add(e.Type.ToString());
                    Assert.That(e.CauseEventIndex, Is.EqualTo(0));
                    Assert.That(MatchEventNarration.Text(e, new MatchResultState(), id => id), Is.Not.Empty);
                }
                var table = RingsideState(); table.GetParticipant("b").Position = MatchPosition.Outside;
                table.Rules.CountOut = table.Rules.Disqualification = false; table.Rules.ImportantMatch = true;
                table.AnnounceTableEligible = table.AnnounceTableReady = true;
                table.Exchange.ActionType = MatchActionType.ObjectSetup;
                var blocked = new MatchSimulator(seed, accidentsEnabled: false).Step(table); seen.Add(blocked.Detail);
                if (blocked.Detail == "announce-table-blocked")
                    Assert.That(blocked.Motion.SelectMany(f => f.Participants).All(p =>
                        Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) >= 1.15f && p.Height == -.4f), Is.True);
            }
            Assert.That(seen, Is.SupersetOf(new[] { "ringside-wait", "ringside-stall", "Recover", "announce-table-blocked", "announce-table-break" }));
        }

        [Test]
        public void RingsideExpansion_TableSetupIsAvailableWhenItsConditionsAreMet()
        {
            var s = RingsideState(); s.GetParticipant("b").Position = MatchPosition.Outside;
            s.Rules.CountOut = s.Rules.Disqualification = false; s.Rules.ImportantMatch = true;
            s.AnnounceTableEligible = true;
            var e = new MatchSimulator(31, accidentsEnabled: false).Step(s);
            Assert.That(e.Detail, Is.EqualTo("announce-table-setup"));
            Assert.That(s.AnnounceTableReady, Is.True);
            Assert.That(s.Exchange.ActionType, Is.EqualTo(MatchActionType.ObjectSetup));
        }

        [Test]
        public void RingsideExpansion_MultipleSeedsRespectRatesRulesBookingTimeAndReplay()
        {
            var report = new List<string> { "scenario,matches,escapes,dives,tables,eligibleTables,tableSetups,countResets" };
            for (int scenario = 0; scenario < 4; scenario++)
            {
                int escapes = 0, dives = 0, tables = 0, eligibleTables = 0, tableSetups = 0, countResets = 0;
                for (int seed = 0; seed < 200; seed++)
                {
                    MatchEngineState Run()
                    {
                        var a = P("a"); var b = P("b");
                        a.Alignment = scenario == 0 ? KayfabeAlignment.Face : KayfabeAlignment.Heel;
                        a.Attributes.HeelWork = 18; a.Attributes.Power = b.Attributes.Power = 18;
                        a.Attributes.HighFlying = b.Attributes.HighFlying = 18; b.WrestlingStyleId = "style_004";
                        if (scenario == 2) a.Attributes.Stamina = b.Attributes.Stamina = 18;
                        if (scenario == 3) { a.RealInjury = b.RealInjury = InjuryStatus.Moderate;
                            a.RealInjuryRegion = b.RealInjuryRegion = BodyRegion.Legs; }
                        return new MatchSimulator(seed, accidentsEnabled: false).Simulate(new[] { a, b }, 1200,
                            new MatchEngineBooking { WinnerId = "b", LoserId = "a" },
                            new MatchRuntimeRules { ImportantMatch = scenario == 2, Disqualification = scenario != 2, CountOut = scenario != 2 });
                    }
                    var s = Run();
                    Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True, $"{scenario}/{seed}: {s.FailureReason}");
                    Assert.That(s.WinnerId, Is.EqualTo("b")); Assert.That(s.ElapsedSeconds, Is.InRange(1200 * .95, 1200 * 1.05));
                    Assert.That(s.ActualFinish, Is.EqualTo(MatchFinishType.Pinfall));
                    var exits = s.Events.Where(e => e.Type == MatchActionType.ExitRing).ToList();
                    Assert.That(exits.Count, Is.LessThanOrEqualTo(s.Rules.ImportantMatch ? 2 : 1));
                    if (exits.Count == 2) Assert.That(exits[1].MatchTimeSeconds - exits[0].MatchTimeSeconds, Is.GreaterThanOrEqualTo(180));
                    if (exits.Count > 0) escapes++;
                    if (s.Events.Any(e => e.Detail is "outside-dive" or "outside-dive-evaded" or "outside-dive-missed")) dives++;
                    if (s.AnnounceTableBroken) tables++;
                    if (s.AnnounceTableEligible) eligibleTables++;
                    tableSetups += s.Events.Count(e => e.Detail == "announce-table-setup");
                    countResets += s.Events.Count(e => e.Detail == "ringside-count-reset");
                    Assert.That(s.Events.Count(e => e.Detail is "outside-run" or "outside-climb"), Is.LessThanOrEqualTo(1));
                    Assert.That(s.Events.Count(e => e.Detail == "announce-table-setup"), Is.LessThanOrEqualTo(1));
                    Assert.That(s.Events.Count(e => e.Detail == "announce-table-break"), Is.LessThanOrEqualTo(1));
                    int leftAt = -1;
                    foreach (var e in s.Events)
                    {
                        if (e.Type == MatchActionType.ExitRing) leftAt = e.MatchTimeSeconds;
                        if (e.Detail is "ringside-count-reset" or "ringside-resume") leftAt = e.MatchTimeSeconds;
                        if (e.Detail is "ringside-return" or "ringside-wait")
                        {
                            if (s.Rules.CountOut && leftAt >= 0)
                                Assert.That(e.MatchTimeSeconds + e.DurationSeconds - leftAt, Is.LessThanOrEqualTo(s.Rules.CountOutSeconds));
                            leftAt = -1;
                        }
                        if (e.Type is MatchActionType.Cover or MatchActionType.Pin)
                            Assert.That(e.Before.Participants.All(p => p.Position is not (MatchPosition.Outside or MatchPosition.Apron)), Is.True);
                        if (e.Detail != null && (e.Detail.StartsWith("outside-dive") || e.Detail == "announce-table-break"))
                        {
                            Assert.That(e.CauseEventIndex, Is.GreaterThanOrEqualTo(0));
                            Assert.That(e.Motion.Last().Participants.All(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) >= 1.15f), Is.True);
                        }
                        Assert.That(e.Motion.SelectMany(f => f.Participants).All(p => MatchPhysics.Finite(p.X) && MatchPhysics.Finite(p.Z) && MatchPhysics.Finite(p.Height)), Is.True);
                    }
                    if (seed < 5) Assert.That(UnityEngine.JsonUtility.ToJson(Run()), Is.EqualTo(UnityEngine.JsonUtility.ToJson(s)));
                }
                report.Add($"{scenario},200,{escapes},{dives},{tables},{eligibleTables},{tableSetups},{countResets}");
                System.IO.File.WriteAllLines("artifacts/ringside-expansion-20261009/rates.csv", report);
                Assert.That(escapes, Is.LessThan(120));
                Assert.That(dives, Is.LessThan(80));
                // An optional spot may be absent in this sample; setup and break have direct coverage.
                if (scenario == 2) Assert.That(tables, Is.InRange(0, 30));
                else Assert.That(tables, Is.Zero);
                if (scenario == 3) Assert.That(escapes + dives, Is.Zero);
            }
            System.IO.File.WriteAllLines("artifacts/ringside-expansion-20261009/rates.csv", report);
        }

        [Test]
        public void RingsideExpansion_RefereeCountsAndBriefReentryRestartsTheCountOnce()
        {
            var counted = new MatchSimulator(31, accidentsEnabled: false).Step(RingsideState());
            Assert.That(counted.Detail, Is.EqualTo("ringside-count"));
            Assert.That(counted.RefereeCountFrom, Is.EqualTo(1)); Assert.That(counted.RefereeCount, Is.EqualTo(3));
            Assert.That(MatchEventNarration.Text(counted, new MatchResultState(), id => id), Does.Contain("하나! 둘! 셋!"));
            int resets = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                var s = RingsideState(); s.GetParticipant("b").Position = MatchPosition.Outside;
                s.Exchange.StartedAt = 494; s.Exchange.RefereeCount = 5;
                var sim = new MatchSimulator(seed, accidentsEnabled: false); var e = sim.Step(s);
                if (e.Detail != "ringside-count-reset") continue;
                resets++;
                Assert.That(e.After.Participants.Single(p => p.Id == "b").Position, Is.EqualTo(MatchPosition.Center));
                Assert.That(e.After.Participants.Single(p => p.Id == "a").Position, Is.EqualTo(MatchPosition.Outside));
                Assert.That(s.Exchange.RefereeCount, Is.Zero); Assert.That(s.Exchange.CountOutResets, Is.EqualTo(1));
                var resumed = sim.Step(s);
                Assert.That(resumed.Detail, Is.EqualTo("ringside-resume"));
                Assert.That(s.Exchange.StartedAt, Is.EqualTo(resumed.MatchTimeSeconds));
                Assert.That(resumed.After.Participants.All(p => p.Position == MatchPosition.Outside), Is.True);
                while (s.Exchange != null) sim.Step(s);
                Assert.That(s.Events.Count(x => x.Detail == "ringside-count-reset"), Is.EqualTo(1));
                Assert.That(s.Participants.All(MatchPhysics.InRing), Is.True);
            }
            Assert.That(resets, Is.InRange(1, 39));
            var noCount = RingsideState(); noCount.Rules.CountOut = false;
            Assert.That(new MatchSimulator(31, accidentsEnabled: false).Step(noCount).Detail, Is.Not.EqualTo("ringside-count"));
        }

        [Test]
        public void RingsideExpansion_BookedCountOutCallsEveryNumberThroughTen()
        {
            var s = new MatchSimulator(31, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b", FinishType = MatchFinishType.CountOut });
            var calls = s.Events.Where(e => e.Detail == "count-out" || e.Result == MatchActionResult.CountOut)
                .Where(e => e.RefereeCountFrom > 0).ToList();
            Assert.That(calls.SelectMany(e => Enumerable.Range(e.RefereeCountFrom, e.RefereeCount - e.RefereeCountFrom + 1)),
                Is.EqualTo(Enumerable.Range(1, 10)));
            Assert.That(MatchEventNarration.Text(calls.Last(), new MatchResultState(), id => id), Does.Contain("열!"));
            Assert.That(s.WinnerId, Is.EqualTo("a")); Assert.That(s.ElapsedSeconds, Is.InRange(600 * .95, 600 * 1.05));
            Assert.That(s.ActualFinish, Is.EqualTo(MatchFinishType.CountOut));
        }
        [Test]
        public void GeneratedWrestlersProduceReviewableMatchLog()
        {
            var audit = System.Reflection.Assembly.Load("PWMANAGER.Editor").GetType("PWManager.Editor.MatchEngineAudit", true);
            audit.GetMethod("Run").Invoke(null, null);
        }

        [Test]
        public void MovementExchangesHaveSetupsCountersDamageAndLegalBookedFinishes()
        {
            var names = new[] { "IrishWhip", "CornerWhip", "RopeRun", "RunningAttack", "CornerAttack", "Evade", "Retreat" };
            foreach (var name in names)
                Assert.That(Enum.TryParse<MatchActionType>(name, out _), Is.True, $"Missing wrestling situation: {name}");
            var seen = new HashSet<string>();
            for (var seed = 0; seed < 40; seed++)
            {
                var s = new MatchSimulator(seed, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600,
                    new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
                Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True, $"Seed {seed}: {s.FailureReason}");
                Assert.That(s.WinnerId, Is.EqualTo("a"));
                Assert.That(s.ElapsedSeconds, Is.InRange(600 * .95, 600 * 1.05));
                foreach (var e in s.Events.Where(e => names.Contains(e.Type.ToString())))
                {
                    seen.Add(e.Type.ToString());
                    var actor = e.Before.Participants.Single(p => p.Id == e.PerformerId);
                    var target = e.Before.Participants.Single(p => p.Id == e.ReceiverId);
                    Assert.That(actor.IsDowned, Is.False, e.Type.ToString());
                    if (e.Type != MatchActionType.Retreat && e.Detail != "outside-run")
                    {
                        Assert.That(actor.Position, Is.Not.EqualTo(MatchPosition.Outside), $"{seed}/{e.MatchTimeSeconds}: {e.Type}/{e.Detail}, actor");
                        Assert.That(target.Position, Is.Not.EqualTo(MatchPosition.Outside), $"{seed}/{e.MatchTimeSeconds}: {e.Type}/{e.Detail}, target");
                    }
                    if (e.Type.ToString() is "RunningAttack" or "CornerAttack" or "Evade")
                    {
                        Assert.That(e.CauseEventIndex, Is.GreaterThanOrEqualTo(0), e.Type.ToString());
                        Assert.That(new[] { "IrishWhip", "CornerWhip", "RopeRun" }, Does.Contain(s.Events[e.CauseEventIndex].Type.ToString()));
                        Assert.That(string.IsNullOrWhiteSpace(MatchEventNarration.Text(e, new MatchResultState(), id => id)), Is.False);
                    }
                    if (e.Type.ToString() is "IrishWhip" or "CornerWhip")
                    {
                        Assert.That(target.IsDowned, Is.False);
                        Assert.That(e.Impact, Is.Zero);
                    }
                    if (e.Result is MatchActionResult.Escaped or MatchActionResult.Countered)
                        Assert.That(e.Impact, Is.Zero);
                    if (e.Impact > 0)
                    {
                        var after = e.After.Participants.Single(p => p.Id == e.ReceiverId);
                        Assert.That(after.HeadDamage + after.BodyDamage + after.ArmDamage + after.LegDamage,
                            Is.GreaterThan(target.HeadDamage + target.BodyDamage + target.ArmDamage + target.LegDamage));
                    }
                }
            }
            Assert.That(seen, Is.SupersetOf(names));
            var impaired = State(); impaired.ElapsedSeconds = 130; impaired.Opening.Completed = true;
            impaired.Participants[0].RealInjury = InjuryStatus.Moderate; impaired.Participants[0].RealInjuryRegion = BodyRegion.Arms;
            Assert.That(MatchPhysics.Actions(impaired, impaired.Participants[0], impaired.Participants[1])
                .Any(x => x.Type.ToString() is "IrishWhip" or "CornerWhip"), Is.False);
            impaired.Participants[0].RealInjuryRegion = BodyRegion.Legs;
            Assert.That(MatchPhysics.Actions(impaired, impaired.Participants[0], impaired.Participants[1])
                .Any(x => x.Type == MatchActionType.RopeRun), Is.False);
        }
        [Test]
        public void ExpandedWrestlingExchangesKeepTheirCausePostureDamageAndBooking()
        {
            var situations = new[] { "StandingHold", "PrepareAttack", "ExitRing", "Pursue", "RingsideAttack", "TopRopeAttack" };
            foreach (var situation in situations)
                Assert.That(Enum.TryParse<MatchActionType>(situation, out _), Is.True, situation);
            var seen = new HashSet<string>();
            var details = new HashSet<string>();
            for (var seed = 0; seed < 100; seed++)
            {
                var a = P("a"); var b = P("b");
                a.Alignment = KayfabeAlignment.Heel; a.Attributes.HeelWork = 18;
                a.Attributes.Stamina = b.Attributes.Stamina = 18;
                a.FinisherMoveIds.Add("grip-finish"); b.FinisherMoveIds.Add("grip-finish");
                a.TargetRegion = BodyRegion.Arms; b.TargetRegion = BodyRegion.Body;
                var s = new MatchSimulator(seed, accidentsEnabled: false, findMove: _ => new MatchMoveRules
                    { Name = "그립 피니셔", Kind = MoveKind.Grapple, TechnicalWeight = 1 })
                    .Simulate(new[] { a, b }, 900, new MatchEngineBooking { WinnerId = "a", LoserId = "b" },
                        new MatchRuntimeRules { CountOutSeconds = 30 });
                Assert.That(s.IsFinished && !s.IsAborted && !s.WasStoppedByExecution, Is.True, $"Seed {seed}: {s.FailureReason}");
                Assert.That(s.WinnerId, Is.EqualTo("a"));
                Assert.That(s.ElapsedSeconds, Is.InRange(900 * .95, 900 * 1.05));
                foreach (var e in s.Events)
                {
                    seen.Add(e.Type.ToString()); if (e.Detail != null) details.Add(e.Detail);
                    if (situations.Contains(e.Type.ToString()) || e.Detail is "retained-grip" or "transition" or "aerial-interrupted")
                    {
                        Assert.That(string.IsNullOrWhiteSpace(MatchEventNarration.Text(e, new MatchResultState(), id => id)), Is.False);
                        if (e.Type.ToString() is "RingsideAttack" or "TopRopeAttack" || e.Detail is "retained-grip" or "transition" or "aerial-interrupted")
                            Assert.That(e.CauseEventIndex, Is.GreaterThanOrEqualTo(0), e.Type + ":" + e.Detail);
                    }
                    if (e.Type.ToString() == "StandingHold" && e.Result == MatchActionResult.FullSuccess)
                        Assert.That(e.After.Participants.Single(p => p.Id == e.ReceiverId).IsDowned, Is.False);
                    if (e.Type.ToString() == "RingsideAttack")
                        Assert.That(e.Before.Participants.All(p => p.Position == MatchPosition.Outside), Is.True);
                    if (e.Type.ToString() is "RunningAttack" or "RingsideAttack" or "TopRopeAttack" or "StandingHold")
                    {
                        Assert.That(MatchTechniques.Find(e.TechniqueId), Is.Not.Null, e.Type.ToString());
                        Assert.That(MatchEventNarration.Text(e, new MatchResultState(), id => id), Does.Contain(e.TechniqueName));
                    }
                    if (e.Result is MatchActionResult.Countered or MatchActionResult.ExecutionFailure or MatchActionResult.Blocked)
                        Assert.That(e.Impact, Is.Zero);
                    if (e.Impact > 0 && e.ReceiverId != null)
                    {
                        var before = e.Before.Participants.Single(p => p.Id == e.ReceiverId);
                        var after = e.After.Participants.Single(p => p.Id == e.ReceiverId);
                        Assert.That(after.HeadDamage + after.BodyDamage + after.ArmDamage + after.LegDamage,
                            Is.GreaterThanOrEqualTo(before.HeadDamage + before.BodyDamage + before.ArmDamage + before.LegDamage));
                    }
                }
            }
            Assert.That(seen, Is.SupersetOf(situations));
            Assert.That(details, Is.SupersetOf(new[] { "retained-grip", "transition", "aerial-interrupted", "crowd-pause" }));
            Assert.That(MatchTechniques.Find("running-powerslam"), Is.Not.Null);
            Assert.That(MatchTechniques.Find("bearhug"), Is.Not.Null);
            Assert.That(MatchTechniques.Find("superplex"), Is.Not.Null);
            Assert.That(MatchTechniques.Find("diving-crossbody"), Is.Not.Null);
            var style = State(); var attacker = style.Participants[0]; var defender = style.Participants[1];
            attacker.Attributes.Power = 20; attacker.Attributes.Technical = 1;
            var styleRandom = new Random(41);
            int powerPicks = 0, technicalPicks = 0;
            for (int i = 0; i < 500; i++)
                if (MatchTechniques.Select(style, MatchActionType.Grapple, attacker, defender, styleRandom).Rules.PowerWeight > 0) powerPicks++;
            attacker.Attributes.Power = 1; attacker.Attributes.Technical = 20;
            for (int i = 0; i < 500; i++)
                if (MatchTechniques.Select(style, MatchActionType.Grapple, attacker, defender, styleRandom).Rules.PowerWeight > 0) technicalPicks++;
            Assert.That(powerPicks, Is.GreaterThan(350));
            Assert.That(technicalPicks, Is.LessThan(120));
            for (var seed = 0; seed < 20; seed++)
            {
                var flyer = P("a"); flyer.SignatureMoveIds.Add("aerial"); flyer.FinisherMoveIds.Add("aerial");
                var flight = new MatchSimulator(seed, accidentsEnabled: false, findMove: _ => new MatchMoveRules
                    { Name = "등록 공중기", Kind = MoveKind.Aerial, HighFlyingWeight = 1 })
                    .Simulate(new[] { flyer, P("b") }, 900, new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
                Assert.That(flight.IsFinished && !flight.IsAborted && !flight.WasStoppedByExecution, Is.True, $"Aerial seed {seed}: {flight.FailureReason}");
                Assert.That(flight.ElapsedSeconds, Is.InRange(900 * .95, 900 * 1.05));
                foreach (var attempt in flight.Events.Where(e => e.MoveId == "aerial" && e.Type is MatchActionType.Signature or MatchActionType.Finisher))
                    Assert.That(attempt.Before.Participants.Single(p => p.Id == attempt.ReceiverId).IsDowned, Is.True);
            }
        }
        private static MatchEngineParticipant P(string id, string side = null) => new()
        {
            Id = id, SideId = side ?? id,
            Attributes = new WrestlerAttributesState { Brawling = 12, Technical = 12, Power = 12, HighFlying = 12, Selling = 12, Stamina = 12, RingPsychology = 12 }
        };
        private static MatchEngineState State() => new() { Participants = new() { P("a"), P("b") }, TargetDurationSeconds = 600 };
        private static MatchEngineAction A(MatchActionType type) => new() { Type = type, PerformerId = "a", ReceiverId = "b" };

        [Test]
        public void GeneralTechniquesMatchPostureAndLimbTargetWithoutUsingSignatureCatalog()
        {
            var s = State(); var random = new Random(41);
            foreach (var region in new[] { BodyRegion.Head, BodyRegion.Body, BodyRegion.Arms, BodyRegion.Legs })
            foreach (var grounded in new[] { false, true })
            {
                s.Participants[0].TargetRegion = region; s.Participants[1].IsDowned = grounded;
                var technique = MatchTechniques.Select(s, MatchActionType.LimbAttack, s.Participants[0], s.Participants[1], random);
                Assert.That(technique.GroundedTarget, Is.EqualTo(grounded));
                Assert.That(technique.Rules.TargetRegion, Is.EqualTo(region));
            }
            s.Participants[1].IsDowned = true;
            var e = ActionResolver.Resolve(s, A(MatchActionType.GroundAttack), random);
            Assert.That(e.MoveId, Is.Null);
            Assert.That(e.TechniqueId, Is.Not.Empty);
            Assert.That(e.TechniqueName, Is.Not.Empty);
            var restored = UnityEngine.JsonUtility.FromJson<MatchEngineEvent>(UnityEngine.JsonUtility.ToJson(e));
            Assert.That(MatchEventNarration.Text(restored, new MatchResultState(), id => id), Does.Contain(e.TechniqueName));
            var signature = UnityEngine.JsonUtility.FromJson<MatchEngineEvent>(UnityEngine.JsonUtility.ToJson(
                new MatchEngineEvent { Type = MatchActionType.Signature, MoveId = "signature", PerformerId = "a", ReceiverId = "b" }));
            var match = new MatchResultState { MoveResults = new() { new MoveResultState { MoveId = "signature", MoveName = "등록 시그니처" } } };
            Assert.That(MatchEventNarration.Text(signature, match, id => id), Does.Contain("등록 시그니처"));
        }

        [Test]
        public void EveryGeneralAttackRecordsItsActualTechnique_AndSelectionVaries()
        {
            var s = new MatchSimulator(731, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 720,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            var attacks = s.Events.Where(e => e.Type is MatchActionType.BasicStrike or MatchActionType.HeavyStrike or
                MatchActionType.Grapple or MatchActionType.GroundAttack or MatchActionType.LimbAttack or
                MatchActionType.Submission or MatchActionType.Dive or MatchActionType.Finisher or MatchActionType.Counter).ToList();
            foreach (var e in attacks)
            {
                Assert.That(e.MoveId, Is.Null);
                var technique = MatchTechniques.Find(e.TechniqueId);
                Assert.That(technique, Is.Not.Null, e.Type.ToString());
                Assert.That(technique.GroundedTarget, Is.EqualTo(e.Before.Participants.Single(p => p.Id == e.ReceiverId).IsDowned));
                Assert.That(MatchEventNarration.Text(e, new MatchResultState(), id => id), Does.Contain(e.TechniqueName));
            }
            Assert.That(attacks.Select(e => e.TechniqueId).Distinct().Count(), Is.GreaterThan(6));
        }

        [Test]
        public void RegisteredFinisherNeedsItsSetupAndCannotBeRepeatedImmediately()
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            s.FindMove = _ => new MatchMoveRules { Kind = MoveKind.Strike, BrawlingWeight = 1 };
            a.FinisherMoveIds.Add("strike"); a.MatchControl = 100; b.IsDowned = true;
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type == MatchActionType.Finisher), Is.False);
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type == MatchActionType.Lift), Is.True);
            b.IsDowned = false; s.BookingSequence = true;
            var action = A(MatchActionType.Finisher); action.MoveId = "strike";
            s.Events.Add(ActionResolver.Resolve(s, action, new Random(1)));
            Assert.That(a.MatchControl, Is.LessThan(100));
            b.IsDowned = false; a.MatchControl = 100;
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type == MatchActionType.Finisher), Is.False);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void MultipleFallsLeaveTimeForTheRemainingFinish(bool elimination, bool ironMan)
        {
            for (var seed = 0; seed < 30; seed++)
            {
                var participants = elimination ? new[] { P("a"), P("b"), P("c") } : new[] { P("a"), P("b") };
                var s = new MatchSimulator(seed, accidentsEnabled: false).Simulate(participants, 600,
                    new MatchEngineBooking { WinnerId = "a", LoserId = "b" },
                    new MatchRuntimeRules { Elimination = elimination, IronMan = ironMan, FallsToWin = elimination || ironMan ? 1 : 2 });
                Assert.That(s.IsFinished, Is.True, $"Seed {seed}: {s.FailureReason}");
                Assert.That(s.WinnerId, Is.EqualTo("a"));
                Assert.That(s.ElapsedSeconds, Is.InRange(600 * .95, 600 * 1.05));
            }
        }

        [Test]
        public void ActualLegInjuryPreventsAerialFinisherWithoutPreventingALegalPinFinish()
        {
            var a = P("a"); a.RealInjury = InjuryStatus.Moderate; a.RealInjuryRegion = BodyRegion.Legs; a.FinisherMoveIds.Add("dive");
            var s = new MatchSimulator(3, accidentsEnabled: false, findMove: _ => new MatchMoveRules { Kind = MoveKind.Aerial, HighFlyingWeight = 1 })
                .Simulate(new[] { a, P("b") }, 600, new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            Assert.That(s.Events.Any(e => e.PerformerId == "a" && e.Type == MatchActionType.Finisher), Is.False);
            Assert.That(s.ActualFinish, Is.EqualTo(MatchFinishType.Pinfall));
        }

        [TestCase("elbow-drop")]
        [TestCase("fist-drop")]
        [TestCase("body-elbow")]
        [TestCase("leg-elbow")]
        [TestCase("head-knee")]
        [TestCase("arm-knee")]
        public void MatDropsRequireALyingTargetAndRoomAwayFromTheCorner(string key)
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            var move = MatchTechniques.Find(key); a.TargetRegion = move.Rules.TargetRegion;
            b.IsDowned = true; b.Posture = MatchPosture.Supine;
            Assert.That(MatchPhysics.CanMove(s, a, b, move.Id), Is.True);
            Assert.That(MatchTechniques.Candidates(move.Action, a, b), Has.Member(move));
            foreach (var posture in new[] { MatchPosture.Seated, MatchPosture.Kneeling })
            {
                b.Posture = posture;
                Assert.That(MatchPhysics.CanMove(s, a, b, move.Id), Is.False, posture.ToString());
                Assert.That(MatchTechniques.Candidates(move.Action, a, b), Has.No.Member(move));
            }
            b.Posture = MatchPosture.Supine; b.Position = MatchPosition.Corner; b.RingX = b.RingZ = .92f;
            Assert.That(MatchPhysics.CanMove(s, a, b, move.Id), Is.False);
            Assert.That(MatchTechniques.Candidates(move.Action, a, b), Has.No.Member(move));
            Assert.Throws<InvalidOperationException>(() => MatchPhysics.Resolve(s,
                new MatchEngineAction { Type = move.Action, PerformerId = a.Id, ReceiverId = b.Id, MoveId = move.Id }, new Random(2)));
            var before = MatchEngineSnapshot.Capture(s);
            var reposition = MatchPhysics.Resolve(s, A(MatchActionType.Reposition), new Random(2));
            MatchMovement.Apply(s, reposition, before);
            Assert.That(MatchPhysics.CanMove(s, a, b, move.Id), Is.True);
        }

        [TestCase(MatchPosition.Corner, .92f, .92f, true, true)]
        [TestCase(MatchPosition.Center, .83f, .15f, true, true)]
        [TestCase(MatchPosition.Center, 0f, 0f, true, false)]
        [TestCase(MatchPosition.Corner, .92f, .92f, false, false)]
        public void CoversUseActualRopeReachAndRespectNoRopeBreakRules(MatchPosition position, float x, float z, bool ropeBreak, bool expected)
        {
            var s = State(); var b = s.Participants[1]; s.Rules.RopeBreak = ropeBreak;
            s.SpatialInitialized = true; s.Opening.Initialized = true; s.Phase = MatchPhase.Control; s.ElapsedSeconds = 200;
            b.IsDowned = true; b.Posture = MatchPosture.Supine; b.Position = position; b.RingX = x; b.RingZ = z;
            s.Exchange = new MatchExchange { Id = 1, Kind = ExchangeKind.Cover, ActorId = "a", TargetId = "b", CauseIndex = -1 };
            var e = new MatchSimulator(2, accidentsEnabled: false).Step(s);
            Assert.That(e.Type == MatchActionType.RopeBreak, Is.EqualTo(expected));
            if (expected)
            {
                Assert.That(e.PerformerId, Is.EqualTo("b"));
                Assert.That(e.After.Participants.Single(p => p.Id == "b").Position, Is.EqualTo(MatchPosition.Ropes));
                Assert.That(e.After.Participants.All(p => p.Grip == MatchGrip.None), Is.True);
                Assert.That(s.Exchange, Is.Null);
                Assert.That(MatchPhysics.Actions(s, s.Participants[0], b).Any(a => a.Type == MatchActionType.Pin), Is.False);
            }
        }

        [Test]
        public void RingPsychologyFavorsMovingAnOpponentAwayFromTheRopesBeforeCovering()
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1]; s.ElapsedSeconds = 200;
            b.IsDowned = true; b.Posture = MatchPosture.Supine; b.RingX = .9f;
            b.Fatigue = 80; b.Stun = 25; b.LastImpact = 12; b.LastImpactTime = s.ElapsedSeconds;
            a.Attributes.RingPsychology = 0;
            float low = ActionEvaluator.Score(s, A(MatchActionType.Pin)) / ActionEvaluator.Score(s, A(MatchActionType.Reposition));
            a.Attributes.RingPsychology = 20;
            float high = ActionEvaluator.Score(s, A(MatchActionType.Pin)) / ActionEvaluator.Score(s, A(MatchActionType.Reposition));
            Assert.That(high, Is.LessThan(low));
        }

        [Test]
        public void RopeBreakRequiresMovingTheOpponentBeforeAnotherCoverOrHold()
        {
            var s = State(); var a = s.Participants[0]; var b = s.Participants[1];
            b.IsDowned = true; b.Position = MatchPosition.Ropes;
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type is MatchActionType.Pin or MatchActionType.Submission), Is.False);
            ActionResolver.Resolve(s, A(MatchActionType.Reposition), new Random(1));
            Assert.That(b.IsDowned, Is.True);
            Assert.That(MatchPhysics.Actions(s, a, b).Any(x => x.Type == MatchActionType.Submission), Is.True);
        }

        [Test]
        public void GroundAttackNeverStandsTheVictimUp_AndRecoveryIsExplicit()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var s = State(); s.Participants[1].IsDowned = true;
                ActionResolver.Resolve(s, A(MatchActionType.GroundAttack), new Random(seed));
                Assert.That(s.Participants[1].IsDowned, Is.True);
            }
        }

        [Test]
        public void StaminaChangesActualWorkCost_AndRestNeverHealsLimbs()
        {
            var low = State(); var high = State();
            low.Participants[0].Attributes.Stamina = 1; high.Participants[0].Attributes.Stamina = 20;
            ActionResolver.Resolve(low, A(MatchActionType.BasicStrike), new Random(4));
            ActionResolver.Resolve(high, A(MatchActionType.BasicStrike), new Random(4));
            Assert.That(high.Participants[0].Fatigue, Is.LessThan(low.Participants[0].Fatigue));
            high.Participants[0].LegDamage = 45;
            ActionResolver.Resolve(high, A(MatchActionType.Rest), new Random(4));
            Assert.That(high.Participants[0].LegDamage, Is.EqualTo(45));
        }

        [Test]
        public void SubmissionMoveStartsPersistentHold_AndRopesBreakIt()
        {
            var s = State(); s.BookingSequence = true; s.Participants[1].IsDowned = true;
            s.FindMove = _ => new MatchMoveRules { Kind = MoveKind.Submission, TechnicalWeight = 1, TargetRegion = BodyRegion.Legs };
            var action = A(MatchActionType.Finisher); action.MoveId = "ankle";
            ActionResolver.Resolve(s, action, new Random(2));
            Assert.That(s.Exchange.Kind, Is.EqualTo(ExchangeKind.Hold));
            Assert.That(s.Participants[1].LegDamage, Is.GreaterThan(0));
            s.Participants[1].Position = MatchPosition.Ropes;
            var e = new MatchSimulator(2, accidentsEnabled: false).Step(s);
            Assert.That(e.Type, Is.EqualTo(MatchActionType.RopeBreak));
            Assert.That(s.IsFinished, Is.False);
        }

        [Test]
        public void AerialMoveRequiresSetup_AndUsesFlyingNotPower()
        {
            var s = State(); s.Participants[1].IsDowned = true;
            s.FindMove = _ => new MatchMoveRules { Kind = MoveKind.Aerial, HighFlyingWeight = 1, ExecutionDifficulty = 15 };
            var action = A(MatchActionType.Signature); action.MoveId = "dive";
            var e = ActionResolver.Resolve(s, action, new Random(2));
            Assert.That(e.Type, Is.EqualTo(MatchActionType.Climb));
            Assert.That(s.Participants[0].Position, Is.EqualTo(MatchPosition.TopRope));
            s.Participants[0].RealInjury = InjuryStatus.Moderate; s.Participants[0].RealInjuryRegion = BodyRegion.Legs;
            Assert.That(MatchPhysics.CanMove(s, s.Participants[0], s.Participants[1], "dive"), Is.False);
        }

        [Test]
        public void RefereeAndRuleRestrictionsBlockIllegalCovers()
        {
            var s = State(); s.Participants[1].IsDowned = true; s.RefereeUnavailableUntil = 20;
            Assert.Throws<InvalidOperationException>(() => ActionResolver.Resolve(s, A(MatchActionType.Pin), new Random(2)));
            s.RefereeUnavailableUntil = 0; s.Rules.Pinfall = false;
            Assert.That(ActionGenerator.Generate(s, s.Participants[0], s.Participants[1]).Any(x => x.Type == MatchActionType.Pin), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TagAndTornadoMatchesKeepSidesAndBookedWinner(bool tornado)
        {
            for (var seed = 0; seed < 6; seed++)
            {
                var s = new MatchSimulator(seed, accidentsEnabled: false).Simulate(new[] { P("a", "one"), P("b", "one"), P("c", "two"), P("d", "two") }, 600,
                    new MatchEngineBooking { WinnerId = "b", LoserId = "d" }, new MatchRuntimeRules { Tag = true, Tornado = tornado });
                Assert.That(s.IsFinished, Is.True, s.FailureReason);
                Assert.That(s.WinnerId, Is.EqualTo("b"));
                foreach (var e in s.Events.Where(x => x.PerformerId != null && x.ReceiverId != null))
                    Assert.That(s.GetParticipant(e.PerformerId).SideId, Is.Not.EqualTo(s.GetParticipant(e.ReceiverId).SideId));
                if (!tornado) Assert.That(s.Events.Any(x => x.Type == MatchActionType.Tag), Is.True);
            }
        }

        [TestCase(MatchFinishType.ObjectRetrieval, MatchObjective.Retrieval)]
        [TestCase(MatchFinishType.Escape, MatchObjective.Escape)]
        [TestCase(MatchFinishType.TableBreak, MatchObjective.Table)]
        public void ObjectFinishHasPreparationAndProgress(MatchFinishType finish, MatchObjective objective)
        {
            var s = new MatchSimulator(17, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b", FinishType = finish },
                new MatchRuntimeRules { Objective = objective, Pinfall = false, Submission = false });
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            Assert.That(s.ActualFinish, Is.EqualTo(finish));
            Assert.That(s.Events.Any(x => x.Type == MatchActionType.ObjectSetup), Is.True);
            Assert.That(s.Events.Any(x => x.Type == MatchActionType.ObjectClimb), Is.True);
        }

        [Test]
        public void BestOfThreeCountsFallsWithoutResettingDamage()
        {
            var s = new MatchSimulator(17, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" }, new MatchRuntimeRules { FallsToWin = 2 });
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            Assert.That(s.GetParticipant("a").Falls, Is.EqualTo(2));
            Assert.That(s.Events.Any(x => x.Type == MatchActionType.Fall), Is.True);
            foreach (var e in s.Events.Where(x => x.Type == MatchActionType.Fall))
                Assert.That(e.After.Participants[1].BodyDamage, Is.GreaterThanOrEqualTo(e.Before.Participants[1].BodyDamage));
        }

        [Test]
        public void EliminationEndsOnlyWhenNoOpponentRemains()
        {
            var s = new MatchSimulator(18, accidentsEnabled: false).Simulate(new[] { P("a"), P("b"), P("c") }, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" }, new MatchRuntimeRules { Elimination = true });
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            Assert.That(s.GetOpponents("a"), Is.Empty);
        }

        [Test]
        public void IronManWinnerActuallyLeadsAtDeadline()
        {
            var s = new MatchSimulator(18, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" }, new MatchRuntimeRules { IronMan = true });
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            Assert.That(s.GetParticipant("a").Falls, Is.GreaterThan(s.GetParticipant("b").Falls));
            Assert.That(s.ElapsedSeconds, Is.InRange(600 * .95, 600 * 1.05));
        }

        [Test]
        public void PlannedInterferenceChangesEngineAndDisablesReferee()
        {
            var s = new MatchSimulator(18, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" }, spots: new[] {
                    new SpotResultState { SpotId = "spot_005", ActorId = "a", TargetId = "b", Phase = MatchSpotPhase.Middle, Result = SpotExecutionResult.Success } });
            var incident = s.Events.Single(x => x.Detail == "spot_005");
            Assert.That(incident.After.Participants.Single(x => x.Id == "b").BodyDamage,
                Is.GreaterThan(incident.Before.Participants.Single(x => x.Id == "b").BodyDamage));
            Assert.That(s.Events.Where(x => x.MatchTimeSeconds >= incident.MatchTimeSeconds && x.MatchTimeSeconds < incident.MatchTimeSeconds + 18)
                .Any(x => x.Type == MatchActionType.Cover), Is.False);
        }

        [Test]
        public void HighlightsKeepEverySelectedContinuationCause_AndLegacyVersionStaysZero()
        {
            var s = new MatchSimulator(8, accidentsEnabled: false).Simulate(new[] { P("a"), P("b") }, 600, new MatchEngineBooking { WinnerId = "a", LoserId = "b" });
            var result = new MatchResultState { EngineState = s, ActualMatchDuration = 10 };
            var beats = new MatchNarrationService().CreateBeats(new MatchPlanState(), result);
            var indices = beats.Where(x => x.BeatType == MatchBeatType.EngineAction).Select(x => x.EngineEventIndex).ToHashSet();
            foreach (var i in indices)
                if (s.Events[i].CauseEventIndex >= 0) Assert.That(indices.Contains(MatchEventNarration.CommentaryIndex(result, s.Events[i].CauseEventIndex)), Is.True);
            Assert.That(UnityEngine.JsonUtility.FromJson<MatchEngineState>("{}" ).EngineVersion, Is.Zero);
        }
    }
}
