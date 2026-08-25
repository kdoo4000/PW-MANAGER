using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchEvaluatorTests
    {
        [Test]
        public void EvaluateTechnical_UsesConfirmedFormulaAndPreservesBookingResult()
        {
            var save = CreateSave("matchtype_001", 20f, 20f);
            var evaluator = new MatchEvaluator(new Rules(), new Rules(), () => "11111111111111111111111111111111");

            save.ShowEvents[0].PlannedDuration = 20;
            var result = evaluator.EvaluateTechnical(save, "event", 1f, 77);

            Assert.That(result.WinnerId, Is.EqualTo("wrestler_a"));
            Assert.That(result.FinishType, Is.EqualTo(MatchFinishType.Pinfall));
            Assert.That(result.TechnicalEvaluation.Performance, Is.InRange(19.5f, 20f));
            Assert.That(result.TechnicalEvaluation.Structure, Is.EqualTo(20f).Within(0.001f));
            Assert.That(result.TechnicalEvaluation.Duration, Is.Zero.Within(0.001f));
            Assert.That(result.FinalMatchQuality, Is.EqualTo(20f).Within(0.001f));
            Assert.That(result.ResultSeed, Is.EqualTo(77));
            Assert.That(result.WrestlerConditionChanges, Has.Count.EqualTo(2));
            Assert.That(result.WrestlerConditionChanges[0].SourceResultId, Is.EqualTo(result.Id));
            Assert.That(result.WrestlerConditionChanges[0].ConditionDelta, Is.EqualTo(-12f));
        }

        [Test]
        public void EvaluateTechnical_ShortHighQualityMatch_AppliesDurationMultiplierOnlyToBaseQuality()
        {
            var save = CreateSave("matchtype_001", 16f, 16f, 15);
            save.ShowEvents[0].PlannedDuration = 10;
            var evaluator = new MatchEvaluator(new Rules(), new Rules(), () => "11111111111111111111111111111111");

            var result = evaluator.EvaluateTechnical(save, "event", 2f, 1);

            Assert.That(result.TechnicalEvaluation.Structure, Is.EqualTo(16f).Within(0.001f));
            var baseQuality = result.TechnicalEvaluation.Performance * .70f + result.TechnicalEvaluation.Structure * .30f;
            Assert.That(result.TechnicalEvaluation.Duration, Is.EqualTo(baseQuality * -.15f).Within(0.001f));
            Assert.That(result.FinalMatchQuality, Is.EqualTo(baseQuality * .85f + 2f).Within(0.001f));
            Assert.That(result.WrestlerConditionChanges[0].ConditionDelta, Is.EqualTo(-6f));
        }

        [Test]
        public void EvaluateTechnical_OverStaminaCapacity_AppliesPerWrestlerPenalty()
        {
            var save = CreateSave("matchtype_001", 10f, 10f, 10);
            save.ShowEvents[0].PlannedDuration = 30;
            var evaluator = new MatchEvaluator(new Rules(), new Rules(), () => "11111111111111111111111111111111");

            var result = evaluator.EvaluateTechnical(save, "event", 0f, 1);

            Assert.That(result.TechnicalEvaluation.Performance, Is.InRange(6.5f, 7.5f));
        }

        [Test]
        public void EvaluateTechnical_SameInput_ProducesSameGameplayValues()
        {
            var save = CreateSave("matchtype_001", 14f, 12f);
            var evaluator = new MatchEvaluator(new Rules(), new Rules(), () => "11111111111111111111111111111111");

            save.ShowEvents[0].PlannedDuration = 20;
            var first = evaluator.EvaluateTechnical(save, "event", 0.5f, 99);
            var second = evaluator.EvaluateTechnical(save, "event", 0.5f, 99);

            Assert.That(second.FinalMatchQuality, Is.EqualTo(first.FinalMatchQuality));
            Assert.That(second.TechnicalEvaluation.Performance, Is.EqualTo(first.TechnicalEvaluation.Performance));
            Assert.That(second.WrestlerPerformances.Select(x => x.PerformanceVariance),
                Is.EqualTo(first.WrestlerPerformances.Select(x => x.PerformanceVariance)));
        }

        [Test]
        public void EvaluateTechnical_UnofficialTagMatch_UsesSideProfilesAndDefaultChemistry()
        {
            var save = CreateSave("matchtype_002", 14f, 12f);
            save.MatchPlans[0].ParticipantIds.Add("wrestler_c");
            save.MatchPlans[0].ParticipantIds.Add("wrestler_d");
            save.MatchPlans[0].LoserTargetId = "wrestler_c";
            save.Wrestlers.Add(Wrestler("wrestler_c", 13f, 20f));
            save.Wrestlers.Add(Wrestler("wrestler_d", 11f, 20f));
            var evaluator = new MatchEvaluator(new Rules(), new Rules());

            var result = evaluator.EvaluateTechnical(save, "event", 0f, 1);

            Assert.That(result.ParticipantProfiles, Has.Count.EqualTo(2));
            Assert.That(result.ParticipantProfiles.All(x => x.AppliedChemistry == 30f), Is.True);
            Assert.That(result.ParticipantProfiles.All(x => x.MemberIds.Count == 2), Is.True);
            Assert.That(result.WrestlerConditionChanges, Has.Count.EqualTo(4));
            Assert.That(result.WrestlerConditionChanges[0].ConditionDelta, Is.EqualTo(-19.2f).Within(.001f));
            Assert.That(result.IndirectWinnerIds, Is.EqualTo(new[] { "wrestler_b" }));
            Assert.That(result.IndirectLoserIds, Is.EqualTo(new[] { "wrestler_d" }));
        }

        [Test]
        public void EvaluateTechnical_OfficialTagTeam_UsesStoredChemistry()
        {
            var save = CreateTagSave(2);
            save.TagTeams.Add(new TagTeamState
            {
                Id = "tag_team_a", Status = TagTeamStatus.Active, Chemistry = 100f,
                MemberIds = { "wrestler_a", "wrestler_b" }
            });
            var evaluator = new MatchEvaluator(new Rules(), new Rules());

            var result = evaluator.EvaluateTechnical(save, "event", 0f, 9);

            Assert.That(result.ParticipantProfiles[0].RepresentedTagTeamId, Is.EqualTo("tag_team_a"));
            Assert.That(result.ParticipantProfiles[0].AppliedChemistry, Is.EqualTo(100f));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void EvaluateTechnical_EqualTagSides_UseCommonParticipantFormula(int membersPerTeam)
        {
            var save = CreateTagSave(membersPerTeam);
            var evaluator = new MatchEvaluator(new Rules(), new Rules());

            var result = evaluator.EvaluateTechnical(save, "event", 0f, 4);

            Assert.That(result.ParticipantProfiles, Has.Count.EqualTo(2));
            Assert.That(result.ParticipantProfiles.All(x => x.MemberIds.Count == membersPerTeam), Is.True);
            Assert.That(result.IndirectWinnerIds, Has.Count.EqualTo(membersPerTeam - 1));
            Assert.That(result.IndirectLoserIds, Has.Count.EqualTo(membersPerTeam - 1));
        }

        [Test]
        public void EvaluateTechnical_ReorderingIndividuals_DoesNotChangeWrestlerVarianceOrQuality()
        {
            var firstSave = CreateSave("matchtype_001", 14f, 12f);
            var secondSave = CreateSave("matchtype_001", 14f, 12f);
            secondSave.MatchPlans[0].ParticipantIds.Reverse();
            var evaluator = new MatchEvaluator(new Rules(), new Rules());

            var first = evaluator.EvaluateTechnical(firstSave, "event", 0f, 71);
            var second = evaluator.EvaluateTechnical(secondSave, "event", 0f, 71);

            Assert.That(second.FinalMatchQuality, Is.EqualTo(first.FinalMatchQuality).Within(.001f));
            foreach (var performance in first.WrestlerPerformances)
                Assert.That(second.WrestlerPerformances.Single(x => x.WrestlerId == performance.WrestlerId).PerformanceVariance,
                    Is.EqualTo(performance.PerformanceVariance));
        }

        [TestCase(2, -3f)]
        [TestCase(60, -30f)]
        public void EvaluateTechnical_ConditionCost_IsLimited(int duration, float expectedDelta)
        {
            var save = CreateSave("matchtype_001", 14f, 12f);
            save.ShowEvents[0].PlannedDuration = 65;
            var evaluator = new MatchEvaluator(new Rules(), new Rules());

            save.ShowEvents[0].PlannedDuration = duration;
            var result = evaluator.EvaluateTechnical(save, "event", 0f, 1);

            Assert.That(result.WrestlerConditionChanges[0].ConditionDelta, Is.EqualTo(expectedDelta));
        }

        [Test]
        public void EvaluateTechnical_GimmickMultiplier_IncreasesConditionCost()
        {
            var save = CreateSave("matchtype_001", 14f, 12f);
            save.MatchPlans[0].MatchGimmickId = "gimmick";
            save.ShowEvents[0].PlannedDuration = 20;
            var evaluator = new MatchEvaluator(new Rules(), new Rules());

            var result = evaluator.EvaluateTechnical(save, "event", 0f, 1);

            Assert.That(result.WrestlerConditionChanges[0].ConditionDelta, Is.EqualTo(-15f).Within(0.001f));
        }

        [TestCase(null, .5f)]
        [TestCase("trait_012", .25f)]
        [TestCase("trait_013", 1f)]
        public void EvaluateTechnical_PerWrestlerVariance_UsesTraitRange(string traitId, float expectedRange)
        {
            var save = CreateSave("matchtype_001", 10f, 10f);
            if (traitId != null) save.Wrestlers[0].Presentation.TraitIds.Add(traitId);
            save.ShowEvents[0].PlannedDuration = 20;
            var evaluator = new MatchEvaluator(new Rules(), new Rules());

            var result = evaluator.EvaluateTechnical(save, "event", 0f, 123);

            Assert.That(Math.Abs(result.WrestlerPerformances[0].PerformanceVariance), Is.LessThanOrEqualTo(expectedRange));
            Assert.That(result.ActualMatchDuration, Is.EqualTo(result.PlannedDuration));
            Assert.That(result.TechnicalEvaluation.RandomVariance, Is.Zero);
        }

        private static GameSave CreateSave(string matchTypeId, float firstAbility, float secondAbility, float stamina = 20f)
        {
            var save = new GameSave();
            save.Wrestlers.Add(Wrestler("wrestler_a", firstAbility, stamina));
            save.Wrestlers.Add(Wrestler("wrestler_b", secondAbility, stamina));
            save.Shows.Add(new ShowState { Id = "show", ShowVersion = 1, Status = ShowStatus.Confirmed });
            save.MatchPlans.Add(new MatchPlanState
            {
                Id = "match", MatchTypeId = matchTypeId,
                ParticipantIds = { "wrestler_a", "wrestler_b" },
                WinnerId = "wrestler_a", LoserTargetId = "wrestler_b", FinishType = MatchFinishType.Pinfall
            });
            save.ShowEvents.Add(new ShowEventState
            {
                Id = "event", ShowId = "show", EventType = ShowEventType.Match,
                DetailId = "match", PlannedDuration = 40
            });
            return save;
        }

        private static GameSave CreateTagSave(int membersPerTeam)
        {
            var save = CreateSave("matchtype_002", 14f, 12f);
            var total = membersPerTeam * 2;
            for (var index = 2; index < total; index++)
            {
                var id = $"wrestler_{(char)('a' + index)}";
                save.Wrestlers.Add(Wrestler(id, 10f + index, 20f));
                save.MatchPlans[0].ParticipantIds.Add(id);
            }
            save.MatchPlans[0].TeamCount = 2;
            save.MatchPlans[0].MembersPerTeam = membersPerTeam;
            save.MatchPlans[0].LoserTargetId = save.MatchPlans[0].ParticipantIds[membersPerTeam];
            return save;
        }

        private static WrestlerState Wrestler(string id, float ability, float stamina) => new()
        {
            Id = id,
            Presentation = new WrestlerPresentationState { WrestlingStyleId = "style" },
            Attributes = new WrestlerAttributesState
            {
                Brawling = ability, Power = ability, HighFlying = ability, Technical = ability,
                Selling = ability, RingPsychology = ability, Stamina = stamina
            }
        };

        private sealed class Rules : IMatchTypeRules, IWrestlingStyleRules
        {
            public bool TryGetParticipantRange(string matchTypeId, out int minimum, out int maximum)
            {
                minimum = matchTypeId == "matchtype_002" ? 4 : 2;
                maximum = matchTypeId == "matchtype_002" ? 8 : 6;
                return true;
            }

            public bool TryGetTeamRules(string matchTypeId, out int minimumTeamCount, out int maximumTeamCount,
                out int minimumMembersPerTeam, out int maximumMembersPerTeam)
            {
                minimumTeamCount = matchTypeId == "matchtype_002" ? 2 : 0;
                maximumTeamCount = matchTypeId == "matchtype_002" ? 4 : 0;
                minimumMembersPerTeam = matchTypeId == "matchtype_002" ? 2 : 0;
                maximumMembersPerTeam = matchTypeId == "matchtype_002" ? 4 : 0;
                return true;
            }

            public bool TryGetConditionCostMultiplier(string matchTypeId, out float multiplier)
            {
                multiplier = matchTypeId == "matchtype_002" ? .8f : 1f;
                return true;
            }

            public bool TryGetGimmickRules(string gimmickId, out MatchGimmickRules rules)
            {
                rules = new MatchGimmickRules(2, 6, new[] { "matchtype_001", "matchtype_002" }, 1.25f);
                return gimmickId == "gimmick";
            }

            public bool TryGetStyleWeights(string styleId, out WrestlingStyleWeights weights)
            {
                weights = new WrestlingStyleWeights(0.25f, 0.25f, 0.25f, 0.25f);
                return styleId == "style";
            }
        }
    }
}
