using System;
using System.Collections.Generic;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class ShowExecutionServiceTests
    {
        [Test]
        public void Execute_ConfirmedMixedTimeline_CreatesOrderedResultAndCompletesShow()
        {
            var save = CreateSave();

            var result = Service().Execute(save, "show", 73);

            Assert.That(save.Shows[0].Status, Is.EqualTo(ShowStatus.Completed));
            Assert.That(result.TimelineResultIds, Is.EqualTo(new[] { save.PromoResults[0].Id, save.MatchResults[0].Id }));
            Assert.That(result.PromoResultIds, Is.EqualTo(new[] { save.PromoResults[0].Id }));
            Assert.That(result.MatchResultIds, Is.EqualTo(new[] { save.MatchResults[0].Id }));
            Assert.That(save.PromoResults[0].ShowEventId, Is.EqualTo("promo-event"));
            Assert.That(save.MatchResults[0].ShowEventId, Is.EqualTo("match-event"));
            Assert.That(ResultApplicationContext.Create(save, result.Id).ShowResult, Is.SameAs(result));
        }

        [Test]
        public void Execute_SameShowVersionAndSeed_ProducesSameEvaluationValues()
        {
            var first = CreateSave();
            var second = CreateSave();

            var firstResult = Service().Execute(first, "show", 73);
            var secondResult = Service().Execute(second, "show", 73);

            Assert.That(second.MatchResults[0].ResultSeed, Is.EqualTo(first.MatchResults[0].ResultSeed));
            Assert.That(second.MatchResults[0].FinalMatchQuality, Is.EqualTo(first.MatchResults[0].FinalMatchQuality));
            Assert.That(second.PromoResults[0].ResultSeed, Is.EqualTo(first.PromoResults[0].ResultSeed));
            Assert.That(second.PromoResults[0].PromoScore, Is.EqualTo(first.PromoResults[0].PromoScore));
            Assert.That(secondResult.TimelineResultIds.Count, Is.EqualTo(firstResult.TimelineResultIds.Count));
        }

        [Test]
        public void Execute_UnconfirmedShow_IsRejectedWithoutCreatingResults()
        {
            var save = CreateSave();
            save.Shows[0].Status = ShowStatus.Preparing;

            Assert.Throws<InvalidOperationException>(() => Service().Execute(save, "show", 73));

            Assert.That(save.ShowResults, Is.Empty);
            Assert.That(save.MatchResults, Is.Empty);
            Assert.That(save.PromoResults, Is.Empty);
        }

        [Test]
        public void Execute_InvalidPlan_IsRejectedWithoutCreatingResults()
        {
            var save = CreateSave();
            save.PromoPlans[0].ParticipantIds[0] = "missing";

            Assert.Throws<InvalidOperationException>(() => Service().Execute(save, "show", 73));

            Assert.That(save.ShowResults, Is.Empty);
            Assert.That(save.Shows[0].Status, Is.EqualTo(ShowStatus.Confirmed));
        }

        [Test]
        public void Execute_SameConfirmedVersionTwice_IsRejected()
        {
            var save = CreateSave();
            var service = Service();
            service.Execute(save, "show", 73);

            Assert.Throws<InvalidOperationException>(() => service.Execute(save, "show", 73));
            Assert.That(save.ShowResults, Has.Count.EqualTo(1));
        }

        private static ShowExecutionService Service()
        {
            var rules = new Rules();
            var nextId = 0;
            string CreateId() => $"result-{++nextId}";
            return new ShowExecutionService(
                new ShowPlanningService(rules, CreateId),
                new MatchEvaluator(rules, rules, CreateId),
                new PromoEvaluator(CreateId),
                CreateId);
        }

        private static GameSave CreateSave()
        {
            var save = new GameSave();
            save.Wrestlers.Add(Wrestler("a", 16f, 15f));
            save.Wrestlers.Add(Wrestler("b", 12f, 10f));
            save.Shows.Add(new ShowState
            {
                Id = "show", Name = "Opening Night", ShowVersion = 1, Status = ShowStatus.Confirmed,
                Date = new GameDate(2026, 6, 1), DurationLimit = 20, TimelineEventIds = { "promo-event", "match-event" }
            });
            save.Contracts.Add(ActiveContract("contract-a", "a"));
            save.Contracts.Add(ActiveContract("contract-b", "b"));
            save.PromoPlans.Add(new PromoPlanState
            {
                Id = "promo", Purpose = PromoPurpose.CharacterIntroduction, Presentation = PromoPresentation.InRingMic,
                ParticipantIds = { "a" }
            });
            save.MatchPlans.Add(new MatchPlanState
            {
                Id = "match", MatchTypeId = "singles", ParticipantIds = { "a", "b" },
                WinnerId = "a", LoserTargetId = "b", FinishType = MatchFinishType.Pinfall
            });
            save.ShowEvents.Add(new ShowEventState
            {
                Id = "promo-event", ShowId = "show", EventType = ShowEventType.Promo, DetailId = "promo", PlannedDuration = 10
            });
            save.ShowEvents.Add(new ShowEventState
            {
                Id = "match-event", ShowId = "show", EventType = ShowEventType.Match, DetailId = "match", PlannedDuration = 10
            });
            return save;
        }

        private static ContractState ActiveContract(string id, string wrestlerId) => new()
        {
            Id = id, PersonId = wrestlerId, Type = ContractType.Wrestler, Status = ContractStatus.Active,
            StartDate = new GameDate(2026, 1, 1), EndDate = new GameDate(2026, 12, 31)
        };

        private static WrestlerState Wrestler(string id, float matchAbility, float promoAbility) => new()
        {
            Id = id,
            Presentation = new WrestlerPresentationState { WrestlingStyleId = "style" },
            Attributes = new WrestlerAttributesState
            {
                Brawling = matchAbility, Power = matchAbility, HighFlying = matchAbility, Technical = matchAbility,
                Selling = matchAbility, RingPsychology = matchAbility, Stamina = 20f,
                Charisma = promoAbility, MicWork = promoAbility, Improvisation = promoAbility, Acting = promoAbility,
                FaceWork = promoAbility, HeelWork = promoAbility, Comedy = promoAbility
            }
        };

        private sealed class Rules : IMatchTypeRules, IWrestlingStyleRules
        {
            public bool TryGetParticipantRange(string matchTypeId, out int minimum, out int maximum)
            {
                minimum = 2; maximum = 6; return matchTypeId == "singles";
            }

            public bool TryGetTeamRules(string matchTypeId, out int minimumTeamCount, out int maximumTeamCount,
                out int minimumMembersPerTeam, out int maximumMembersPerTeam)
            {
                minimumTeamCount = maximumTeamCount = minimumMembersPerTeam = maximumMembersPerTeam = 0;
                return matchTypeId == "singles";
            }

            public bool TryGetConditionCostMultiplier(string matchTypeId, out float multiplier)
            {
                multiplier = 1f; return matchTypeId == "singles";
            }

            public bool TryGetGimmickRules(string gimmickId, out MatchGimmickRules rules)
            {
                rules = default; return false;
            }

            public bool TryGetStyleWeights(string styleId, out WrestlingStyleWeights weights)
            {
                weights = new WrestlingStyleWeights(.25f, .25f, .25f, .25f); return styleId == "style";
            }
        }
    }
}
