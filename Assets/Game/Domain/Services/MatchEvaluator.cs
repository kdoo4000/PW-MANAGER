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
        private readonly Func<string, MatchMoveRules> findMove;
        private readonly bool accidentsEnabled;

        public MatchEvaluator(IMatchTypeRules matchTypeRules, IWrestlingStyleRules styleRules, Func<string> createId = null,
            Func<string, MatchMoveRules> findMove = null, bool accidentsEnabled = true)
        {
            this.matchTypeRules = matchTypeRules ?? throw new ArgumentNullException(nameof(matchTypeRules));
            this.styleRules = styleRules ?? throw new ArgumentNullException(nameof(styleRules));
            this.createId = createId ?? EntityId.CreateRuntimeId;
            this.findMove = findMove;
            this.accidentsEnabled = accidentsEnabled;
        }

        public MatchResultState EvaluateTechnical(GameSave save, string showEventId, float spotScore, int resultSeed,
            IEnumerable<MatchResultState> pendingResults = null)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (float.IsNaN(spotScore) || float.IsInfinity(spotScore)) throw new ArgumentOutOfRangeException(nameof(spotScore));
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
            var bookedParticipants = sides.SelectMany(s => s.MemberIds).Select(id => save.Wrestlers.Single(w => w.Id == id)).ToList();
            var unavailable = bookedParticipants.FirstOrDefault(w => w.Condition.Availability is WrestlerAvailability.MatchUnavailable or WrestlerAvailability.Unavailable ||
                w.KayfabeInjury?.Absent == true && w.KayfabeInjury.Severity != InjuryStatus.None);
            if (unavailable != null)
                return new MatchResultState { Id = createId(), ShowId = show.Id, ShowVersion = show.ShowVersion,
                    ShowEventId = showEvent.Id, MatchPlanId = match.Id, PlannedDuration = showEvent.PlannedDuration,
                    FinishType = MatchFinishType.Draw, CriticReview = null, ResultSeed = resultSeed,
                    CancellationReason = unavailable.Condition.Availability is WrestlerAvailability.MatchUnavailable or WrestlerAvailability.Unavailable
                        ? "실제 부상으로 경기 취소" : "각본상 부상으로 결장",
                    ParticipantProfiles = sides.Select(side => new MatchParticipantProfileState { SideId = side.Id, MemberIds = side.MemberIds }).ToList() };
            var spots = MatchSpotEvaluator.Evaluate(save, match, show.Date, showEventId, resultSeed, pendingResults);
            var endingSpot = spots.FirstOrDefault(x => x.ScriptedEffect == SpotScriptedEffect.MatchStopped || x.ScriptedEffect == SpotScriptedEffect.Disqualification);
            if (endingSpot?.ScriptedEffect == SpotScriptedEffect.MatchStopped && endingSpot.Phase == MatchSpotPhase.Entrance)
                actualMatchDuration = 0;

            if (!matchTypeRules.TryGetTeamRules(match.MatchTypeId, out var minimumTeamCount, out _, out _, out _))
                throw new InvalidOperationException("Match type was not found.");
            if (!matchTypeRules.TryGetConditionCostMultiplier(match.MatchTypeId, out var conditionCostMultiplier) ||
                float.IsNaN(conditionCostMultiplier) || float.IsInfinity(conditionCostMultiplier) || conditionCostMultiplier <= 0f)
                throw new InvalidOperationException("Match type condition cost multiplier is invalid.");
            var gimmickConditionCostMultiplier = 1f;
            if (!string.IsNullOrEmpty(match.MatchGimmickId))
            {
                if (!matchTypeRules.TryGetGimmickRules(match.MatchGimmickId, out var gimmickRules) ||
                    !gimmickRules.IsCompatible(match.MatchTypeId, sides.Sum(x => x.MemberIds.Count)))
                    throw new InvalidOperationException("Match gimmick is missing or incompatible.");
                gimmickConditionCostMultiplier = gimmickRules.ConditionCostMultiplier;
                if (float.IsNaN(gimmickConditionCostMultiplier) || float.IsInfinity(gimmickConditionCostMultiplier) || gimmickConditionCostMultiplier <= 0f)
                    throw new InvalidOperationException("Match gimmick condition cost multiplier is invalid.");
            }
            var participantIds = sides.SelectMany(x => x.MemberIds).ToList();
            var participants = participantIds.Select(id => save.Wrestlers.Single(x => x.Id == id)).ToList();
            var finishType = endingSpot?.ScriptedEffect == SpotScriptedEffect.MatchStopped ? MatchFinishType.Draw :
                endingSpot?.ScriptedEffect == SpotScriptedEffect.Disqualification ? MatchFinishType.Disqualification : match.FinishType;
            var finishPerformerId = endingSpot?.ScriptedEffect == SpotScriptedEffect.Disqualification ? endingSpot.TargetId :
                finishType == MatchFinishType.Draw ? null :
                (string.IsNullOrEmpty(match.FinishPerformerId) ? match.WinnerId : match.FinishPerformerId);
            var bookedWinningSide = finishType == MatchFinishType.Draw ? null : endingSpot?.ScriptedEffect == SpotScriptedEffect.Disqualification
                ? sides.Single(x => x.MemberIds.Contains(finishPerformerId))
                : sides.Single(x => x.Id == ResolveWinningSideId(match, sides, finishPerformerId));
            var loserTargetId = finishType == MatchFinishType.Draw ? null :
                endingSpot?.ScriptedEffect == SpotScriptedEffect.Disqualification
                    ? sides.First(x => x.Id != bookedWinningSide.Id).MemberIds.FirstOrDefault() : match.LoserTargetId;
            if (finishType != MatchFinishType.Draw && string.IsNullOrEmpty(loserTargetId))
                loserTargetId = sides.First(x => x.Id != bookedWinningSide.Id).MemberIds[0];

            MatchEngineState engineState = null;
            if (actualMatchDuration > 0)
            {
                MatchGimmickRules? gimmick = null;
                if (!string.IsNullOrEmpty(match.MatchGimmickId) && matchTypeRules.TryGetGimmickRules(match.MatchGimmickId, out var found)) gimmick = found;
                bool Allowed(MatchFinishType value) => MatchFinishRules.IsAllowed(value, minimumTeamCount > 0, participantIds.Count, sides.Count, gimmick);
                var runtimeRules = new MatchRuntimeRules
                {
                    Tag = minimumTeamCount > 0,
                    Tornado = minimumTeamCount > 0 && (match.Tornado || gimmick?.Tornado == true),
                    Elimination = match.Elimination || gimmick?.Elimination == true,
                    IronMan = match.IronMan || gimmick?.IronMan == true,
                    FallsToWin = gimmick?.TwoOutOfThree == true ? 2 : Math.Max(1, match.FallsToWin),
                    DominantSideId = match.DominantSideId,
                    OpeningStyle = match.OpeningStyle,
                    OpeningHostility = MatchOpening.Hostility(save, sides),
                    ImportantMatch = show.MainEventId == showEvent.Id || match.StoryAction == StoryAction.End,
                    Pinfall = Allowed(MatchFinishType.Pinfall),
                    Submission = Allowed(MatchFinishType.Submission),
                    Disqualification = Allowed(MatchFinishType.Disqualification),
                    CountOut = Allowed(MatchFinishType.CountOut),
                    RopeBreak = MatchFinishRules.Resolve(gimmick?.RopeBreakRule ?? MatchRuleOverride.Default, true),
                    CountOutSeconds = gimmick?.CountOutSeconds ?? 10,
                    Objective = Allowed(MatchFinishType.ObjectRetrieval) ? MatchObjective.Retrieval :
                        Allowed(MatchFinishType.TableBreak) ? MatchObjective.Table : Allowed(MatchFinishType.Escape) ? MatchObjective.Escape : MatchObjective.PinOrSubmission
                };
                engineState = new MatchSimulator(resultSeed, findMove: findMove, accidentsEnabled: accidentsEnabled).Simulate(participants.Select(x =>
                    new MatchEngineParticipant
                    {
                        Id = x.Id,
                        WrestlingStyleId = x.Presentation.WrestlingStyleId,
                        Alignment = x.Roster.Alignment,
                        SideId = sides.Single(side => side.MemberIds.Contains(x.Id)).Id,
                        StatusValue = x.Status.StatusValue,
                        AmbushTendency = x.Roster.Alignment == KayfabeAlignment.Heel ? x.Attributes.HeelWork / 20f : .05f,
                        Attributes = new WrestlerAttributesState
                        {
                            Brawling = x.Attributes.Brawling, Power = x.Attributes.Power, Technical = x.Attributes.Technical,
                            HighFlying = x.Attributes.HighFlying, Selling = x.Attributes.Selling,
                            Stamina = x.Attributes.Stamina, RingPsychology = x.Attributes.RingPsychology,
                            RingImprovisation = x.Attributes.RingImprovisation, Charisma = x.Attributes.Charisma,
                            Acting = x.Attributes.Acting, FaceWork = x.Attributes.FaceWork, HeelWork = x.Attributes.HeelWork
                        },
                        Fatigue = Math.Max(0f, Math.Min(100f, 100f - x.Condition.Condition)),
                        RealInjury = x.Condition.InjuryStatus, RealInjuryRegion = x.Condition.InjuryRegion,
                        HeadDamage = StoryDamage(x, BodyRegion.Head), BodyDamage = StoryDamage(x, BodyRegion.Body),
                        ArmDamage = StoryDamage(x, BodyRegion.Arms), LegDamage = StoryDamage(x, BodyRegion.Legs),
                        TargetRegion = x.Presentation.FinisherMoveIds?.Select(id => findMove?.Invoke(id)).FirstOrDefault(m => m != null)?.TargetRegion ??
                            (x.Attributes.Power > x.Attributes.Technical ? BodyRegion.Body : BodyRegion.Legs),
                        SignatureMoveIds = x.Presentation.SignatureMoveIds?.Where(id => findMove?.Invoke(id) != null).ToList() ?? new(),
                        FinisherMoveIds = x.Presentation.FinisherMoveIds?.Where(id => findMove?.Invoke(id) != null).ToList() ?? new()
                    }), checked(showEvent.PlannedDuration * 60), new MatchEngineBooking
                    {
                        WinnerId = finishPerformerId, LoserId = loserTargetId, FinishType = finishType
                    }, runtimeRules, spots);
                if (!engineState.IsFinished || engineState.IsAborted) throw new InvalidOperationException(engineState.FailureReason);
                if (engineState.WasStoppedByInjury || engineState.WasStoppedByExecution)
                {
                    foreach (var spot in spots.Where((spot, index) => !engineState.AppliedSpots.Contains(index)))
                    { spot.WasSkipped = true; spot.SkipReason = "경기 중단으로 미실행"; spot.FinalSpotScore = 0; }
                }
                endingSpot = spots.FirstOrDefault(x => !x.WasSkipped && x.ScriptedEffect != SpotScriptedEffect.None);
                finishPerformerId = engineState.WinnerId;
                finishType = engineState.ActualFinish;
                actualMatchDuration = (int)Math.Ceiling(engineState.ElapsedSeconds / 60d);
                var engineLoserId = engineState.Events.LastOrDefault()?.ReceiverId;
                loserTargetId = finishType == MatchFinishType.Draw ? null :
                    participantIds.Contains(engineLoserId) ? engineLoserId : loserTargetId;
            }

            if (engineState == null && endingSpot?.ScriptedEffect == SpotScriptedEffect.MatchStopped)
            {
                int stopIndex = spots.IndexOf(endingSpot);
                for (int i = 0; i < spots.Count; i++)
                {
                    var spot = spots[i];
                    if (i <= stopIndex) { spot.WasApplied = true; continue; }
                    spot.WasSkipped = true; spot.SkipReason = "경기 무산으로 미실행"; spot.FinalSpotScore = 0;
                }
            }
            var winningSide = finishType == MatchFinishType.Draw ? null : sides.Single(x => x.MemberIds.Contains(finishPerformerId));
            var losingSide = string.IsNullOrEmpty(loserTargetId) ? null : sides.Single(x => x.MemberIds.Contains(loserTargetId));
            var evaluationMinutes = engineState == null ? actualMatchDuration : engineState.ElapsedSeconds / 60f;
            var performances = participants.Select(x => CalculatePerformance(x, evaluationMinutes, resultSeed)).ToList();
            var performanceByWrestler = performances.ToDictionary(x => x.WrestlerId, StringComparer.Ordinal);
            var profiles = sides.Select(x => CreateParticipantProfile(save, x, minimumTeamCount > 0, performanceByWrestler)).ToList();
            var routineExecution = profiles.Average(x => x.RoutineExecution);
            var averagePsychology = profiles.Average(x => x.RingPsychology);
            var highestPsychology = profiles.Max(x => x.RingPsychology);
            var structureScore = averagePsychology * 0.70f + highestPsychology * 0.30f;
            var baseQuality = routineExecution * 0.70f + structureScore * 0.30f;
            var observed = ObserveQuality(engineState);
            var observedQuality = observed.ObservedExecution * .35f + observed.ObservedStructure * .25f +
                observed.ObservedDecisions * .25f + observed.ObservedSelling * .15f;
            var engineContribution = engineState == null ? 0 : (observedQuality - baseQuality) * .30f;
            baseQuality += engineContribution;
            var durationMultiplier = GetDurationMultiplier(baseQuality, evaluationMinutes);
            var durationContribution = baseQuality * durationMultiplier - baseQuality;
            spotScore += spots.Where(x => !x.WasSkipped).Sum(x => x.FinalSpotScore);
            var finalQuality = Clamp(baseQuality * durationMultiplier + spotScore, 0f, 20f);
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
                FinishType = finishType,
                WasStoppedBySpot = endingSpot?.ScriptedEffect == SpotScriptedEffect.MatchStopped,
                WasStoppedByInjury = engineState?.WasStoppedByInjury == true,
                WasStoppedByExecution = engineState?.WasStoppedByExecution == true,
                InterruptionReason = engineState?.WasStoppedByExecution == true ? engineState.FailureReason : null,
                PerformedMatchQuality = finalQuality,
                FinalMatchQuality = engineState?.WasStoppedByExecution == true ? 0 : finalQuality,
                CriticReview = engineState?.WasStoppedByExecution == true ? null : CriticReviewCalculator.Match(finalQuality),
                TechnicalEvaluation = new TechnicalEvaluationBreakdownState
                {
                    Performance = routineExecution,
                    Structure = structureScore,
                    Duration = durationContribution,
                    Spot = spotScore,
                    RandomVariance = 0f, Engine = engineContribution,
                    ObservedExecution = observed.ObservedExecution, ObservedStructure = observed.ObservedStructure,
                    ObservedDecisions = observed.ObservedDecisions, ObservedSelling = observed.ObservedSelling
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
                ResultSeed = resultSeed,
                EngineState = engineState
            };
            result.SpotResults = spots;
            if (engineState != null)
            {
                result.EvaluationReasons.Add(new EvaluationReasonState { Code = "match.engine.content", Contribution = engineContribution });
                foreach (var injured in engineState.Participants.Where(p => p.InjuredThisMatch))
                    result.InjuryChanges.Add(new MatchInjuryChangeState { SourceResultId = result.Id, WrestlerId = injured.Id,
                        Severity = injured.RealInjury, Region = injured.RealInjuryRegion, RecoveryDays = MatchHealth.RecoveryDays(injured.RealInjury) });
            }
            foreach (var planned in match.StoryInjuries ?? new())
            {
                if (planned == null || !participantIds.Contains(planned.WrestlerId) || planned.Severity == InjuryStatus.None ||
                    !Enum.IsDefined(typeof(InjuryStatus), planned.Severity) || !Enum.IsDefined(typeof(BodyRegion), planned.Region) || planned.Days < 1 || planned.Days > 365)
                    throw new InvalidOperationException("Invalid planned story injury.");
                if (actualMatchDuration > 0 && engineState?.WasStoppedByExecution != true && engineState?.WasStoppedByInjury != true)
                    result.InjuryChanges.Add(new MatchInjuryChangeState { SourceResultId = result.Id,
                    WrestlerId = planned.WrestlerId, IsKayfabe = true, Severity = planned.Severity,
                    Region = planned.Region, RecoveryDays = planned.Days, Absent = planned.Absent });
            }
            result.MoveResults = MatchMoveService.Evaluate(save, sides, result, findMove);
            foreach (var participant in participants)
                result.WrestlerConditionChanges.Add(new WrestlerConditionChangeData
                {
                    SourceResultId = result.Id,
                    WrestlerId = participant.Id,
                    ConditionDelta = -Clamp(Math.Min(result.ActualMatchDuration * .1f,
                        result.EngineState?.Participants.FirstOrDefault(x => x.Id == participant.Id)?.Exertion * .02f ?? 0) +
                        result.ActualMatchDuration * ConditionCostPerMinute * conditionCostMultiplier * gimmickConditionCostMultiplier,
                        MinimumConditionCost, MaximumConditionCost),
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
            if (match.FallsToWin < 1) throw new InvalidOperationException("Falls to win must be at least one.");
            matchTypeRules.TryGetGimmickRules(match.MatchGimmickId ?? "gimmick_000", out var format);
            if (format.TwoOutOfThree && sides.Count != 2) throw new InvalidOperationException("Two out of three falls requires exactly two sides.");
            if ((match.IronMan || format.IronMan) && (match.Elimination || format.Elimination)) throw new InvalidOperationException("Iron Man and elimination rules cannot be combined.");
            if ((match.Tornado || format.Tornado) && minimumTeamCount == 0) throw new InvalidOperationException("Tornado rules require a tag match.");
            if (!string.IsNullOrEmpty(match.DominantSideId) && sides.All(x => x.Id != match.DominantSideId))
                throw new InvalidOperationException("Dominant side must be one of the match sides.");
            var finishPerformerId = string.IsNullOrEmpty(match.FinishPerformerId) ? match.WinnerId : match.FinishPerformerId;
            if (match.FinishType != MatchFinishType.Draw && !ids.Contains(finishPerformerId))
                throw new InvalidOperationException("Match finish performer must be a participant.");
            if (match.FinishType != MatchFinishType.Draw && !string.IsNullOrEmpty(match.LoserTargetId) && !ids.Contains(match.LoserTargetId))
                throw new InvalidOperationException("Match loser target must be a participant.");
            foreach (var id in ids)
                if (save.Wrestlers.All(x => x?.Id != id))
                    throw new InvalidOperationException($"Match participant was not found: {id}");
        }

        private WrestlerMatchPerformanceState CalculatePerformance(WrestlerState wrestler, float duration, int resultSeed)
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
                else chemistry = (save.Stables ?? new List<StableState>()).SingleOrDefault(x =>
                    x?.Status == TagTeamStatus.Active && side.MemberIds.All(x.MemberIds.Contains))?.Chemistry ?? 30f;
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

        internal static TagTeamState ResolveOfficialTagTeam(GameSave save, MatchSideState side)
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
            return Interpolate(overtime, new[] { 0f, 5f, 10f, 15f, 20f }, new[] { 0f, 1f, 3f, 6f, 10f });
        }

        private static float GetDurationMultiplier(float baseQuality, float duration)
        {
            float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
            var shortFactor = Interpolate(baseQuality, new[] { 0f, 10f, 13f, 15f, 17f, 20f }, new[] { 1f, 1f, .95f, .90f, .85f, .75f });
            var longFactor = Interpolate(baseQuality, new[] { 0f, 7f, 10f, 13f, 15f, 20f }, new[] { .65f, .65f, .75f, .85f, .95f, 1f });
            return 1 - (1 - shortFactor) * (1 - Smooth((duration - 8) / 7)) - (1 - longFactor) * Smooth((duration - 28) / 4);
        }

        private static float Interpolate(float value, float[] x, float[] y)
        {
            if (value <= x[0]) return y[0];
            for (int i = 1; i < x.Length; i++)
                if (value < x[i]) return y[i - 1] + (y[i] - y[i - 1]) * (value - x[i - 1]) / (x[i] - x[i - 1]);
            return y[y.Length - 1];
        }

        private static float StoryDamage(WrestlerState wrestler, BodyRegion region) =>
            wrestler.KayfabeInjury?.Region == region ? (int)wrestler.KayfabeInjury.Severity * 25f : 0;

        public static TechnicalEvaluationBreakdownState ObserveQuality(MatchEngineState state)
        {
            var result = new TechnicalEvaluationBreakdownState { ObservedExecution = 10, ObservedStructure = 10,
                ObservedDecisions = 10, ObservedSelling = 10 };
            if (state == null) return result;
            var attacks = state.Events.Where(e => e.IsPhysicalAttack).ToList();
            if (attacks.Count > 0) result.ObservedExecution = attacks.Average(e => e.Result == MatchActionResult.Countered ? 1 : e.ExecutionQuality) * 20;
            var decisions = state.Events.Where(e => e.HasDecisionEvidence).ToList();
            var covers = decisions.Where(e => e.Type == MatchActionType.Cover || e.Type == MatchActionType.Pin && e.Result == MatchActionResult.RollUp).ToList();
            var choices = decisions.Where(e => e.IsPhysicalAttack && !e.IntentionalCombination).ToList();
            result.ObservedDecisions = ((covers.Count == 0 ? 10 : covers.Count(e => e.ValidCover) * 20f / covers.Count) +
                (choices.Count == 0 ? 10 : choices.Count(e => e.ContextualChoice) * 20f / choices.Count)) / 2;
            var selling = decisions.Where(e => e.SellingProtectedRegion || e.SellingContradiction || e.IsPhysicalAttack || e.Type == MatchActionType.Recover).ToList();
            if (selling.Count > 0) result.ObservedSelling = selling.Average(e => e.SellingQuality) * 20;
            var opportunities = decisions.Where(e => e.TargetCompleted && e.PreparationAvailable && !string.IsNullOrEmpty(e.GoalMoveId)).ToList();
            var transitions = opportunities.Select(e => decisions.Where(next => next.PerformerId == e.PerformerId &&
                next.MatchTimeSeconds > e.MatchTimeSeconds && next.GoalMoveId == e.GoalMoveId).Take(3).ToList())
                .Where(events => events.Count == 3 && !events.Any(e => e.Result == MatchActionResult.Countered || e.InjurySeverity != InjuryStatus.None)).ToList();
            var transitionQuality = transitions.Count == 0 ? 10 : transitions.Count(events => events.Any(e => e.IsPreparation || e.Type == MatchActionType.Finisher)) * 20f / transitions.Count;
            var links = decisions.Where(e => e.CauseEventIndex >= 0 && e.CauseEventIndex < state.Events.Count).ToList();
            var continuity = links.Count == 0 ? 10 : links.Count(e =>
                state.Events[e.CauseEventIndex].MatchTimeSeconds < e.MatchTimeSeconds &&
                state.Events[e.CauseEventIndex].ExchangeId == e.ExchangeId) * 20f / links.Count;
            var finishingAttacks = attacks.Where(e => e.Type == MatchActionType.Finisher && e.Result is MatchActionResult.FullSuccess or MatchActionResult.ReceptionFailure).ToList();
            var finish = state.WasStoppedByInjury || finishingAttacks.Count == 0 ? 10 : finishingAttacks.Count(e =>
                decisions.Any(p => p.PerformerId == e.PerformerId && p.MatchTimeSeconds <= e.MatchTimeSeconds &&
                    p.MatchTimeSeconds >= e.MatchTimeSeconds - 45 && p.IsPreparation && p.GoalMoveId == e.GoalMoveId)) * 20f / finishingAttacks.Count;
            result.ObservedStructure = (transitionQuality + continuity + finish) / 3;
            var history = new Dictionary<string, List<string>>();
            int repeated = 0, observed = 0;
            foreach (var attack in attacks.Where(e => !e.IntentionalCombination && !string.IsNullOrEmpty(e.TechniqueId)))
            {
                string key = attack.PerformerId + ":" + attack.ReceiverId;
                if (!history.TryGetValue(key, out var recent)) history[key] = recent = new List<string>();
                if (recent.Count(id => id == attack.TechniqueId) >= 2) repeated++;
                recent.Add(attack.TechniqueId);
                if (recent.Count > 4) recent.RemoveAt(0);
                observed++;
            }
            if (observed > 0 && state.Participants.Count == 2 && !state.Rules.Tag)
                result.ObservedStructure = Math.Max(0, result.ObservedStructure - repeated * 8f / observed);
            return result;
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }

    public static class CriticReviewCalculator
    {
        public const float NeutralScore = 50f;

        public static CriticReviewState Match(float finalMatchQuality, float storyScore = NeutralScore)
        {
            ValidateRange(finalMatchQuality, 0f, 20f, nameof(finalMatchQuality));
            ValidateRange(storyScore, 0f, 100f, nameof(storyScore));
            var technicalScore = finalMatchQuality * 5f;
            var storyModifier = (storyScore - NeutralScore) / 10f;
            return Create(Clamp(technicalScore + storyModifier, 0f, 105f),
                new EvaluationReasonState { Code = "critic.technical", Contribution = technicalScore },
                new EvaluationReasonState { Code = "critic.story", Contribution = storyModifier });
        }

        public static CriticReviewState Show(
            IReadOnlyCollection<CriticReviewState> matchReviews,
            float flowScore = NeutralScore,
            float storyScore = NeutralScore)
        {
            if (matchReviews == null || matchReviews.Count == 0) return null;
            ValidateRange(flowScore, 0f, 100f, nameof(flowScore));
            ValidateRange(storyScore, 0f, 100f, nameof(storyScore));
            var scores = matchReviews.Select(x => x?.FinalScore ?? throw new ArgumentException("Match review is required.", nameof(matchReviews))).ToList();
            foreach (var score in scores) ValidateRange(score, 0f, 105f, nameof(matchReviews));
            var matchAverage = scores.Average();
            var topAverage = scores.OrderByDescending(x => x).Take(3).Average();
            var finalScore = Clamp(matchAverage * .55f + topAverage * .20f + flowScore * .15f + storyScore * .10f, 0f, 105f);
            return Create(finalScore,
                new EvaluationReasonState { Code = "critic.show.matches", Contribution = matchAverage * .55f },
                new EvaluationReasonState { Code = "critic.show.top-matches", Contribution = topAverage * .20f },
                new EvaluationReasonState { Code = "critic.show.flow", Contribution = flowScore * .15f },
                new EvaluationReasonState { Code = "critic.show.story", Contribution = storyScore * .10f });
        }

        public static float Stars(float score)
        {
            ValidateRange(score, 0f, 105f, nameof(score));
            if (score >= 105f) return 7f;
            if (score >= 104f) return 6f;
            if (score >= 103f) return 5.75f;
            if (score >= 102f) return 5.5f;
            if (score >= 101f) return 5.25f;
            if (score >= 100f) return 5f;
            if (score >= 97f) return 4.75f;
            if (score >= 94f) return 4.5f;
            if (score >= 90f) return 4.25f;
            if (score >= 84f) return 4f;
            if (score >= 76f) return 3.75f;
            if (score >= 68f) return 3.5f;
            if (score >= 60f) return 3.25f;
            if (score >= 52f) return 3f;
            if (score >= 44f) return 2.75f;
            if (score >= 36f) return 2.5f;
            return Math.Max(.25f, (float)Math.Floor(score / 4f) * .25f + .25f);
        }

        private static CriticReviewState Create(float score, params EvaluationReasonState[] reasons) => new()
        {
            FinalScore = score,
            DisplayedStars = Stars(score),
            Reasons = new List<EvaluationReasonState>(reasons)
        };

        private static void ValidateRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name, $"Value must be finite and between {minimum} and {maximum}.");
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
