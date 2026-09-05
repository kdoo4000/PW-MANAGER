using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class MatchSpotEvaluator
    {
        public static IEnumerable<string> Validate(MatchPlanState match) => Validate(null, match);

        public static IEnumerable<string> Validate(GameSave save, MatchPlanState match)
        {
            var sides = (match.Sides ?? new List<MatchSideState>()).Where(x => x?.MemberIds != null).ToList();
            foreach (var spot in match.Spots ?? new List<PlannedSpotState>())
            {
                if (spot == null) { yield return "스팟 설정이 비어 있습니다."; continue; }
                var definition = SpecialMatchSpotCatalog.Find(spot.SpotId);
                var reason = SpecialMatchSpotCatalog.UnavailableReason(definition, match);
                if (reason != null) { yield return reason; continue; }
                if (!definition.Phases.Contains(spot.Phase)) yield return $"{definition.Name}: 사용할 수 있는 발생 구간을 선택하세요.";
                var actorSide = sides.FirstOrDefault(x => x.MemberIds.Contains(spot.ActorId));
                var targetSide = sides.FirstOrDefault(x => x.MemberIds.Contains(spot.TargetId));
                if (targetSide == null) yield return $"{definition.Name}: 피해 선수는 경기 참가자여야 합니다.";
                if (definition.ActorScope == SpotActorScope.Outsider &&
                    (actorSide != null || save?.Wrestlers?.Any(x => x?.Id == spot.ActorId) != true))
                    yield return $"{definition.Name}: 주체는 이 경기에 출전하지 않는 선수여야 합니다.";
                if (definition.ActorScope == SpotActorScope.Participant && actorSide == null)
                    yield return $"{definition.Name}: 주체는 경기 참가자여야 합니다.";
                if (definition.Relationship == SpotRelationship.Opponents && (actorSide == null || targetSide == null || actorSide == targetSide))
                    yield return $"{definition.Name}: 서로 다른 진영의 선수를 선택하세요.";
                if (definition.Relationship == SpotRelationship.Teammates && (actorSide == null || actorSide != targetSide || spot.ActorId == spot.TargetId))
                    yield return $"{definition.Name}: 같은 팀의 다른 선수를 선택하세요.";
                if (spot.ActorId == spot.TargetId || !string.IsNullOrEmpty(spot.PartnerId))
                    yield return $"{definition.Name}: 주체와 피해 선수를 올바르게 선택하세요.";
            }
        }

        public static int RecentUses(GameSave save, string spotId, GameDate date, string excludedEventId = null,
            IEnumerable<MatchResultState> pending = null)
        {
            return (save.MatchResults ?? new List<MatchResultState>()).Concat(pending ?? Array.Empty<MatchResultState>())
                .Where(x => x != null && x.ShowEventId != excludedEventId)
                .SelectMany(x => x.SpotResults ?? new List<SpotResultState>())
                .Count(x => x != null && x.SpotId == spotId && x.Date.CompareTo(date.AddDays(-56)) >= 0 && x.Date.CompareTo(date) <= 0);
        }

        public static List<SpotResultState> Evaluate(GameSave save, MatchPlanState match, GameDate date,
            string showEventId, int seed, IEnumerable<MatchResultState> pending = null)
        {
            var errors = Validate(save, match).ToList();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            var results = new List<SpotResultState>();
            // A separate random stream leaves the established match-performance rolls unchanged.
            var random = new Random(seed ^ 0x53504f54);
            foreach (var spot in (match.Spots ?? new List<PlannedSpotState>()).OrderBy(x => x.Phase))
            {
                var definition = SpecialMatchSpotCatalog.Find(spot.SpotId);
                WrestlerState Wrestler(string id) => save.Wrestlers.Single(x => x.Id == id);
                var actor = Wrestler(spot.ActorId);
                var target = Wrestler(spot.TargetId);
                WrestlerState partner = null;
                var execution = (RoleScore(actor, definition.Skill, false) * 2f + RoleScore(target, definition.Skill, true)
                    + (partner == null ? 0f : RoleScore(partner, definition.Skill, false))) / (partner == null ? 3f : 4f);
                var probabilities = Probabilities(execution, definition.Difficulty);
                var roll = random.NextDouble();
                var outcome = 0;
                while (outcome < 3 && roll >= probabilities[outcome]) { roll -= probabilities[outcome]; outcome++; }
                var initial = (SpotExecutionResult)outcome;
                // ponytail: recovery probability is average ring improvisation / 20 until a recovery formula is specified.
                var recovery = initial == SpotExecutionResult.Disaster && random.NextDouble() <
                    (actor.Attributes.RingImprovisation + target.Attributes.RingImprovisation + (partner?.Attributes.RingImprovisation ?? 0f)) / (partner == null ? 40f : 60f);
                var result = recovery ? SpotExecutionResult.Failure : initial;
                var repeat = RecentUses(save, spot.SpotId, date, showEventId, pending) + results.Count(x => x.SpotId == spot.SpotId);
                var variant = random.Next(definition.Variants.Count);
                string Name(WrestlerState wrestler) => string.IsNullOrWhiteSpace(wrestler.Identity.RingName) ? wrestler.Identity.LegalName : wrestler.Identity.RingName;
                results.Add(new SpotResultState
                {
                    SpotId = spot.SpotId, Phase = spot.Phase, ActorId = spot.ActorId, TargetId = spot.TargetId,
                    PartnerId = spot.PartnerId, Date = date, VariantIndex = variant,
                    ScriptedMoment = definition.Variants[variant].Replace("{actor}", Name(actor)).Replace("{target}", Name(target)).Replace("{partner}", partner == null ? string.Empty : Name(partner)),
                    EffectiveExecution = execution, InitialResult = initial, Result = result, Recovered = recovery,
                    RepeatCount = repeat, FinalSpotScore = Score(definition.BasePoint, result, repeat), ScriptedEffect = definition.Effect
                });
            }
            return results;
        }

        public static double[] Probabilities(float execution, float difficulty)
        {
            if (float.IsNaN(execution) || float.IsInfinity(execution) || execution < 0 || execution > 20 ||
                float.IsNaN(difficulty) || difficulty < 1 || difficulty > 20) throw new ArgumentOutOfRangeException();
            var margin = execution - difficulty + 1;
            double C(float boundary) => 1d / (1d + Math.Exp(-(boundary - margin) / 1.5d));
            var c1 = C(-4); var c2 = C(-1); var c3 = C(3);
            return new[] { c1, c2 - c1, c3 - c2, 1 - c3 };
        }

        public static float Score(float basePoint, SpotExecutionResult result, int repeat)
        {
            if (float.IsNaN(basePoint) || float.IsInfinity(basePoint) || basePoint < 0 || repeat < 0 ||
                !Enum.IsDefined(typeof(SpotExecutionResult), result)) throw new ArgumentOutOfRangeException();
            var multiplier = result switch { SpotExecutionResult.GreatSuccess => 1.5f, SpotExecutionResult.Success => 1f, SpotExecutionResult.Failure => -.5f, _ => -1.5f };
            var repetition = result >= SpotExecutionResult.Success ? Math.Max(-.5f, 1.1f - .2f * repeat) : Math.Min(1.5f, 1 + .1f * repeat);
            return basePoint * multiplier * repetition;
        }

        private static float RoleScore(WrestlerState wrestler, SpotSkill skill, bool receiving)
        {
            var a = wrestler.Attributes;
            var specialty = skill switch
            {
                SpotSkill.Technical => a.Technical, SpotSkill.Brawling => a.Brawling, SpotSkill.Power => a.Power,
                SpotSkill.HighFlying => a.HighFlying, SpotSkill.Specialty => a.SpecialtyMatches,
                SpotSkill.Comedy => a.Comedy, _ => a.RingPsychology
            };
            var other = receiving ? a.Selling : specialty;
            var condition = wrestler.Condition.Condition;
            if (float.IsNaN(a.SpotWork) || a.SpotWork < 1 || a.SpotWork > 20 ||
                float.IsNaN(other) || other < 1 || other > 20 || float.IsNaN(condition) || condition < 0 || condition > 100 ||
                float.IsNaN(a.RingImprovisation) || a.RingImprovisation < 1 || a.RingImprovisation > 20 ||
                !Enum.IsDefined(typeof(InjuryStatus), wrestler.Condition.InjuryStatus)) throw new InvalidOperationException("스팟 참여 선수의 능력치 또는 컨디션이 유효하지 않습니다.");
            // ponytail: initial balance is up to 5 condition points and 1/3/6 injury points; tune here after playtesting.
            var injury = wrestler.Condition.InjuryStatus switch { InjuryStatus.Minor => 1f, InjuryStatus.Moderate => 3f, InjuryStatus.Severe => 6f, _ => 0f };
            return Math.Max(0, (a.SpotWork * 2 + other) / 3 - (100 - condition) / 20 - injury);
        }

        public static string ResultText(SpotResultState result) =>
            result.Result switch { SpotExecutionResult.GreatSuccess => "대성공", SpotExecutionResult.Success => "성공", SpotExecutionResult.Failure when result.Recovered => "실패 · 흐름 복구", SpotExecutionResult.Failure => "실패", _ => "대실패" };
    }
}
