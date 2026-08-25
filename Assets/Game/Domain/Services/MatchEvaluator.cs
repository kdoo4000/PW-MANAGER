using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class MatchEvaluator
    {
        private const float ConditionCostPerMinute = 0.6f;
        private const float MinimumConditionCost = 3f;
        private const float MaximumConditionCost = 30f;
        private const float ConsistentVarianceRange = 0.25f;
        private const float DefaultVarianceRange = 0.50f;
        private const float WildCardVarianceRange = 1.00f;
        private readonly IMatchTypeRules matchTypeRules;
        private readonly IWrestlingStyleRules styleRules;
        private readonly Func<string> createId;

        public MatchEvaluator(IMatchTypeRules matchTypeRules, IWrestlingStyleRules styleRules, Func<string> createId = null)
        {
            this.matchTypeRules = matchTypeRules ?? throw new ArgumentNullException(nameof(matchTypeRules));
            this.styleRules = styleRules ?? throw new ArgumentNullException(nameof(styleRules));
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public MatchResultState EvaluateTechnical(GameSave save, string showEventId, float spotScore, int resultSeed)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            var showEvent = save.ShowEvents.SingleOrDefault(x => x?.Id == showEventId)
                ?? throw new ArgumentException("Show event was not found.", nameof(showEventId));
            if (showEvent.EventType != ShowEventType.Match)
                throw new ArgumentException("Show event is not a match.", nameof(showEventId));
            var actualMatchDuration = showEvent.PlannedDuration;
            var show = save.Shows.SingleOrDefault(x => x?.Id == showEvent.ShowId)
                ?? throw new InvalidOperationException("Match references a missing show.");
            if (show.ShowVersion < 1)
                throw new InvalidOperationException("Match evaluation requires a confirmed show version.");
            var match = save.MatchPlans.SingleOrDefault(x => x?.Id == showEvent.DetailId)
                ?? throw new InvalidOperationException("Match plan was not found.");
            var sides = ResolveSides(match);
            ValidateMatch(save, match, sides);

            if (!matchTypeRules.TryGetTeamRules(match.MatchTypeId, out var minimumTeamCount, out _, out _, out _))
                throw new InvalidOperationException("Match type was not found.");
            if (!matchTypeRules.TryGetConditionCostMultiplier(match.MatchTypeId, out var conditionCostMultiplier) || conditionCostMultiplier <= 0f)
                throw new InvalidOperationException("Match type condition cost multiplier is invalid.");
            var gimmickConditionCostMultiplier = 1f;
            if (!string.IsNullOrEmpty(match.MatchGimmickId))
            {
                if (!matchTypeRules.TryGetGimmickRules(match.MatchGimmickId, out var gimmickRules) ||
                    !gimmickRules.IsCompatible(match.MatchTypeId, sides.Sum(x => x.MemberIds.Count)))
                    throw new InvalidOperationException("Match gimmick is missing or incompatible.");
                gimmickConditionCostMultiplier = gimmickRules.ConditionCostMultiplier;
            }
            var participantIds = sides.SelectMany(x => x.MemberIds).ToList();
            var participants = participantIds.Select(id => save.Wrestlers.Single(x => x.Id == id)).ToList();
            var conditionDelta = -Clamp(actualMatchDuration * ConditionCostPerMinute * conditionCostMultiplier * gimmickConditionCostMultiplier,
                MinimumConditionCost, MaximumConditionCost);
            var performances = participants.Select(x => CalculatePerformance(x, actualMatchDuration, resultSeed)).ToList();
            var performanceByWrestler = performances.ToDictionary(x => x.WrestlerId, StringComparer.Ordinal);
            var profiles = sides.Select(x => CreateParticipantProfile(save, x, minimumTeamCount > 0, performanceByWrestler)).ToList();
            var routineExecution = profiles.Average(x => x.RoutineExecution);
            var averagePsychology = profiles.Average(x => x.RingPsychology);
            var highestPsychology = profiles.Max(x => x.RingPsychology);
            var structureScore = averagePsychology * 0.70f + highestPsychology * 0.30f;
            var baseQuality = routineExecution * 0.70f + structureScore * 0.30f;
            var durationMultiplier = GetDurationMultiplier(baseQuality, actualMatchDuration);
            var durationContribution = baseQuality * durationMultiplier - baseQuality;
            var finalQuality = Clamp(baseQuality * durationMultiplier + spotScore, 0f, 20f);

            var finishPerformerId = match.FinishType == MatchFinishType.Draw ? null :
                (string.IsNullOrEmpty(match.FinishPerformerId) ? match.WinnerId : match.FinishPerformerId);
            var winningSide = match.FinishType == MatchFinishType.Draw ? null :
                sides.Single(x => x.Id == ResolveWinningSideId(match, sides, finishPerformerId));
            var loserTargetId = match.FinishType == MatchFinishType.Draw ? null : match.LoserTargetId;
            var losingSide = string.IsNullOrEmpty(loserTargetId) ? null : sides.Single(x => x.MemberIds.Contains(loserTargetId));
            var result = new MatchResultState
            {
                Id = createId(),
                ShowId = show.Id,
                ShowVersion = show.ShowVersion,
                ShowEventId = showEvent.Id,
                MatchPlanId = match.Id,
                PlannedDuration = showEvent.PlannedDuration,
                ActualMatchDuration = actualMatchDuration,
                WinnerId = finishPerformerId,
                WinningSideId = winningSide?.Id,
                FinishPerformerId = finishPerformerId,
                LoserTargetId = loserTargetId,
                IndirectWinnerIds = winningSide?.MemberIds.Where(x => x != finishPerformerId).ToList() ?? new List<string>(),
                IndirectLoserIds = losingSide?.MemberIds.Where(x => x != loserTargetId).ToList() ?? new List<string>(),
                FinishType = match.FinishType,
                FinalMatchQuality = finalQuality,
                TechnicalEvaluation = new TechnicalEvaluationBreakdownState
                {
                    Performance = routineExecution,
                    Structure = structureScore,
                    Duration = durationContribution,
                    Spot = spotScore,
                    RandomVariance = 0f
                },
                WrestlerPerformances = performances,
                ParticipantProfiles = profiles,
                EvaluationReasons = new List<EvaluationReasonState>
                {
                    new() { Code = "match.performance", Contribution = routineExecution },
                    new() { Code = "match.structure", Contribution = structureScore },
                    new() { Code = "match.duration", Contribution = durationContribution },
                    new() { Code = "match.spot", Contribution = spotScore }
                },
                ResultSeed = resultSeed
            };
            foreach (var participant in participants)
                result.WrestlerConditionChanges.Add(new WrestlerConditionChangeData
                {
                    SourceResultId = result.Id,
                    WrestlerId = participant.Id,
                    ConditionDelta = conditionDelta,
                    ReasonCode = WrestlerConditionChangeReason.MatchParticipation
                });
            return result;
        }

        private void ValidateMatch(GameSave save, MatchPlanState match, IReadOnlyList<MatchSideState> sides)
        {
            if (!matchTypeRules.TryGetParticipantRange(match.MatchTypeId, out var minimum, out var maximum))
                throw new InvalidOperationException("Match type was not found.");
            var ids = sides.SelectMany(x => x.MemberIds ?? new List<string>()).ToList();
            if (ids.Count < minimum || ids.Count > maximum || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new InvalidOperationException("Match participant count is invalid.");
            if (!matchTypeRules.TryGetTeamRules(match.MatchTypeId, out var minimumTeamCount, out var maximumTeamCount,
                    out var minimumMembersPerTeam, out var maximumMembersPerTeam))
                throw new InvalidOperationException("Match type was not found.");
            if (minimumTeamCount > 0)
            {
                var membersPerTeam = sides.Count == 0 ? 0 : sides[0].MemberIds.Count;
                if (sides.Count < minimumTeamCount || sides.Count > maximumTeamCount ||
                    membersPerTeam < minimumMembersPerTeam || membersPerTeam > maximumMembersPerTeam ||
                    sides.Any(x => x?.MemberIds == null || x.MemberIds.Count != membersPerTeam))
                    throw new InvalidOperationException("Tag match sides are invalid.");
            }
            else if (sides.Count != ids.Count || sides.Any(x => x.MemberIds.Count != 1))
                throw new InvalidOperationException("Individual match sides are invalid.");
            var finishPerformerId = string.IsNullOrEmpty(match.FinishPerformerId) ? match.WinnerId : match.FinishPerformerId;
            if (match.FinishType != MatchFinishType.Draw && !ids.Contains(finishPerformerId))
                throw new InvalidOperationException("Match finish performer must be a participant.");
            if (match.FinishType != MatchFinishType.Draw && !string.IsNullOrEmpty(match.LoserTargetId) && !ids.Contains(match.LoserTargetId))
                throw new InvalidOperationException("Match loser target must be a participant.");
            foreach (var id in ids)
                if (save.Wrestlers.All(x => x?.Id != id))
                    throw new InvalidOperationException($"Match participant was not found: {id}");
        }

        private WrestlerMatchPerformanceState CalculatePerformance(WrestlerState wrestler, int duration, int resultSeed)
        {
            if (!styleRules.TryGetStyleWeights(wrestler.Presentation.WrestlingStyleId, out var weights))
                throw new InvalidOperationException($"Wrestling style was not found: {wrestler.Presentation.WrestlingStyleId}");
            var attributes = wrestler.Attributes;
            var stylePerformance = attributes.Brawling * weights.Brawling + attributes.Power * weights.Power +
                attributes.HighFlying * weights.HighFlying + attributes.Technical * weights.Technical;
            var individualRoutine = stylePerformance * 0.75f + attributes.Selling * 0.25f;
            var staminaCapacity = Clamp(2.5f * attributes.Stamina - 2.5f, 5f, 50f);
            var overtime = Math.Max(0f, duration - staminaCapacity);
            var staminaPenalty = GetStaminaPenalty(overtime);
            var varianceRange = GetPerformanceVarianceRange(wrestler);
            var random = new Random(CreateWrestlerSeed(resultSeed, wrestler.Id));
            var performanceVariance = (float)(random.NextDouble() + random.NextDouble() - 1d) * varianceRange;
            return new WrestlerMatchPerformanceState
            {
                WrestlerId = wrestler.Id,
                BaseRoutine = individualRoutine,
                PerformanceVariance = performanceVariance,
                StaminaPenalty = staminaPenalty,
                EffectiveRoutine = Clamp(individualRoutine + performanceVariance - staminaPenalty, 0f, 20f)
            };
        }

        private MatchParticipantProfileState CreateParticipantProfile(
            GameSave save, MatchSideState side, bool isTeamMatch,
            IReadOnlyDictionary<string, WrestlerMatchPerformanceState> performances)
        {
            var members = side.MemberIds.Select(id => save.Wrestlers.Single(x => x.Id == id)).ToList();
            var representedTagTeamId = side.RepresentedTagTeamId;
            var chemistry = 50f;
            if (isTeamMatch)
            {
                var officialTeam = ResolveOfficialTagTeam(save, side);
                if (members.Count == 2 && officialTeam != null)
                {
                    chemistry = officialTeam.Chemistry;
                    representedTagTeamId ??= officialTeam.Id;
                }
                else chemistry = 30f;
            }
            var chemistryBonus = (chemistry - 50f) / 50f;
            float Ability(Func<WrestlerAttributesState, float> select) =>
                Clamp(members.Average(x => select(x.Attributes)) + chemistryBonus, 1f, 20f);
            return new MatchParticipantProfileState
            {
                SideId = side.Id,
                MemberIds = new List<string>(side.MemberIds),
                RepresentedTagTeamId = representedTagTeamId,
                AppliedChemistry = chemistry,
                RoutineExecution = Clamp(members.Average(x => performances[x.Id].EffectiveRoutine) + chemistryBonus, 0f, 20f),
                RingPsychology = Ability(x => x.RingPsychology),
                RingImprovisation = Ability(x => x.RingImprovisation),
                Technical = Ability(x => x.Technical),
                Brawling = Ability(x => x.Brawling),
                Power = Ability(x => x.Power),
                HighFlying = Ability(x => x.HighFlying),
                SpotWork = Ability(x => x.SpotWork),
                SpecialtyMatches = Ability(x => x.SpecialtyMatches),
                Selling = Ability(x => x.Selling),
                Stamina = Ability(x => x.Stamina)
            };
        }

        private static TagTeamState ResolveOfficialTagTeam(GameSave save, MatchSideState side)
        {
            var activeTeams = (save.TagTeams ?? new List<TagTeamState>()).Where(x => x?.Status == TagTeamStatus.Active).ToList();
            if (!string.IsNullOrEmpty(side.RepresentedTagTeamId))
            {
                var represented = activeTeams.SingleOrDefault(x => x.Id == side.RepresentedTagTeamId)
                    ?? throw new InvalidOperationException("Represented tag team is missing or inactive.");
                if (represented.MemberIds.Any(x => !side.MemberIds.Contains(x)))
                    throw new InvalidOperationException("Represented tag team members must belong to the match side.");
                if (side.MemberIds.Count == 2 && !new HashSet<string>(represented.MemberIds).SetEquals(side.MemberIds))
                    throw new InvalidOperationException("A two-member side must exactly match its represented tag team.");
                return represented;
            }
            if (side.MemberIds.Count != 2) return null;
            var matches = activeTeams.Where(x => new HashSet<string>(x.MemberIds).SetEquals(side.MemberIds)).ToList();
            if (matches.Count > 1) throw new InvalidOperationException("Multiple active tag teams match the same side.");
            return matches.SingleOrDefault();
        }

        private List<MatchSideState> ResolveSides(MatchPlanState match)
        {
            if (match.Sides?.Count > 0)
                return match.Sides.Where(x => x != null).Select(x => new MatchSideState
                {
                    Id = x.Id,
                    MemberIds = new List<string>(x.MemberIds ?? new List<string>()),
                    RepresentedTagTeamId = x.RepresentedTagTeamId
                }).ToList();
            var participantIds = match.ParticipantIds ?? new List<string>();
            if (matchTypeRules.TryGetTeamRules(match.MatchTypeId, out var minimumTeamCount, out _, out _, out _) &&
                minimumTeamCount > 0)
            {
                var teamCount = match.TeamCount > 0 ? match.TeamCount : minimumTeamCount;
                var membersPerTeam = match.MembersPerTeam > 0 ? match.MembersPerTeam :
                    (teamCount > 0 && participantIds.Count % teamCount == 0 ? participantIds.Count / teamCount : 0);
                if (teamCount > 0 && membersPerTeam > 0 && teamCount * membersPerTeam == participantIds.Count)
                    return Enumerable.Range(0, teamCount).Select(index => new MatchSideState
                    {
                        Id = $"legacy-side-{index + 1}",
                        MemberIds = participantIds.Skip(index * membersPerTeam).Take(membersPerTeam).ToList()
                    }).ToList();
            }
            return participantIds.Select((id, index) => new MatchSideState
            {
                Id = $"legacy-side-{index + 1}", MemberIds = new List<string> { id }
            }).ToList();
        }

        private static string ResolveWinningSideId(MatchPlanState match, IReadOnlyList<MatchSideState> sides, string finishPerformerId)
        {
            if (!string.IsNullOrEmpty(match.WinningSideId))
            {
                var side = sides.SingleOrDefault(x => x.Id == match.WinningSideId)
                    ?? throw new InvalidOperationException("Winning side must belong to the match.");
                if (!side.MemberIds.Contains(finishPerformerId))
                    throw new InvalidOperationException("Finish performer must belong to the winning side.");
                return side.Id;
            }
            return sides.Single(x => x.MemberIds.Contains(finishPerformerId)).Id;
        }

        private static int CreateWrestlerSeed(int resultSeed, string wrestlerId)
        {
            unchecked
            {
                var hash = 2166136261u ^ (uint)resultSeed;
                foreach (var character in wrestlerId ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7fffffffu);
            }
        }

        private static float GetPerformanceVarianceRange(WrestlerState wrestler)
        {
            var traits = wrestler.Presentation?.TraitIds;
            if (traits?.Contains("trait_013") == true) return WildCardVarianceRange;
            if (traits?.Contains("trait_012") == true) return ConsistentVarianceRange;
            return DefaultVarianceRange;
        }

        private static float GetStaminaPenalty(float overtime)
        {
            if (overtime <= 0f) return 0f;
            if (overtime <= 5f) return 1f;
            if (overtime <= 10f) return 3f;
            if (overtime <= 15f) return 6f;
            return 10f;
        }

        private static float GetDurationMultiplier(float baseQuality, int duration)
        {
            if (duration <= 10)
            {
                if (baseQuality < 10f) return 1f;
                if (baseQuality < 13f) return 0.95f;
                if (baseQuality < 15f) return 0.90f;
                if (baseQuality < 17f) return 0.85f;
                return 0.75f;
            }
            if (duration < 30) return 1f;
            if (baseQuality < 7f) return 0.65f;
            if (baseQuality < 10f) return 0.75f;
            if (baseQuality < 13f) return 0.85f;
            if (baseQuality < 15f) return 0.95f;
            return 1f;
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
