using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchOpeningTests
    {
        private static MatchEngineParticipant P(string id, float status = 50) => new()
        {
            Id = id, SideId = id, StatusValue = status,
            Attributes = new WrestlerAttributesState { Technical = 15, Brawling = 12, Power = 12, Selling = 12, Stamina = 12, RingPsychology = 12 }
        };

        [Test]
        public void ProbeChainsAreNamedAndBookedFinishStillCompletes()
        {
            var s = new MatchSimulator(731).Simulate(new[] { P("a"), P("b") }, 720,
                new MatchEngineBooking { WinnerId = "a", LoserId = "b" }, new MatchRuntimeRules { OpeningStyle = MatchOpeningStyle.Probe });
            Assert.That(s.IsFinished, Is.True, s.FailureReason);
            Assert.That(s.ElapsedSeconds, Is.InRange(720 * .95, 720 * 1.05));
            Assert.That(s.Events.First().Type, Is.EqualTo(MatchActionType.Probe));
            var chains = s.Events.Where(e => e.Type == MatchActionType.Chain).ToList();
            Assert.That(chains, Is.Not.Empty);
            Assert.That(chains.All(e => !string.IsNullOrEmpty(e.TechniqueName) && e.ExchangeId > 0), Is.True);
            Assert.That(s.Events.TakeWhile(e => e.Phase == MatchPhase.Opening).Sum(e => e.DurationSeconds), Is.GreaterThanOrEqualTo(60));
        }

        [Test]
        public void HostilityAndStatusSelectDifferentOpenings()
        {
            int clashes = 0, dominance = 0, probes = 0;
            for (int seed = 0; seed < 50; seed++)
            {
                MatchEngineState State(float hostility, float gap) => new() {
                    Participants = new() { P("a", 50+gap/2), P("b", 50-gap/2) },
                    Rules = new MatchRuntimeRules { OpeningHostility = hostility }, TargetDurationSeconds = 720 };
                var normal = State(0, 0); MatchOpening.Initialize(normal, new Random(seed));
                var hostile = State(100, 0); MatchOpening.Initialize(hostile, new Random(seed));
                var uneven = State(0, 60); MatchOpening.Initialize(uneven, new Random(seed));
                if (normal.Opening.Style == MatchOpeningStyle.Probe) probes++;
                if (hostile.Opening.Style == MatchOpeningStyle.Clash) clashes++;
                if (uneven.Opening.Style == MatchOpeningStyle.Dominance) { dominance++; Assert.That(uneven.Opening.InitiatorId, Is.EqualTo("a")); }
            }
            Assert.That(probes, Is.GreaterThan(30));
            Assert.That(clashes, Is.GreaterThan(35));
            Assert.That(dominance, Is.GreaterThan(30));
        }

        [Test]
        public void AmbushCanAttemptRegisteredFinisherAtBell()
        {
            var attempts = 0;
            for (int seed = 0; seed < 8; seed++)
            {
                var a = P("a"); var b = P("b");
                a.FinisherMoveIds.Add("finish"); b.FinisherMoveIds.Add("finish");
                var s = new MatchSimulator(seed, findMove: id => new MatchMoveRules { Name = "래리어트", Kind = MoveKind.Strike, BrawlingWeight = 1 })
                    .Simulate(new[] { a, b }, 120, new MatchEngineBooking { WinnerId = "a", LoserId = "b" },
                        new MatchRuntimeRules { OpeningStyle = MatchOpeningStyle.Ambush });
                Assert.That(s.IsFinished, Is.True, s.FailureReason);
                Assert.That(s.ElapsedSeconds, Is.InRange(120 * .95, 120 * 1.05));
                if (s.Events[0].Type == MatchActionType.Finisher) { attempts++; Assert.That(s.Events[0].MatchTimeSeconds, Is.Zero); }
            }
            Assert.That(attempts, Is.GreaterThan(0));
        }
    }
}
