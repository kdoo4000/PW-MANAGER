using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class StorylineService
    {
        private readonly Func<string> createId;
        public StorylineService(Func<string> createId = null) => this.createId = createId ?? EntityId.CreateRuntimeId;

        public static void UpgradeRelationships(GameSave save)
        {
            if (save.RelationshipSchemaVersion > 1) throw new InvalidOperationException("Unsupported relationship schema.");
            if (save.RelationshipSchemaVersion == 1) return;
            foreach (var pair in save.StoryRelationships ?? new())
            {
                if (pair == null) continue;
                if (pair.Hostility != 0) pair.Value = -pair.Hostility;
                pair.Hostility = 0;
            }
            foreach (var result in save.ShowResults ?? new())
            foreach (var change in result?.RelationshipChanges ?? new())
            {
                if (change == null) continue;
                if (change.HostilityChange != 0) change.ValueChange = -change.HostilityChange;
                change.HostilityChange = 0;
            }
            save.RelationshipSchemaVersion = 1;
        }

        public StorylineState Create(GameSave save, string name, string firstWrestlerId, string secondWrestlerId)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (save.Promotion == null || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Storyline requires a promotion and name.");
            ValidatePair(save, firstWrestlerId, secondWrestlerId);
            var id = createId();
            if (!EntityId.IsValidRuntimeId(id) || (save.Storylines ?? new()).Any(x => x?.Id == id))
                throw new InvalidOperationException("Storyline ID must be unique.");
            var storyline = new StorylineState
            {
                Id = id, PromotionId = save.Promotion.Id, Name = name.Trim(),
                FirstWrestlerId = firstWrestlerId, SecondWrestlerId = secondWrestlerId,
                // ponytail: initial 25-point baseline; replace with candidate snapshots when candidate adoption is implemented.
                Tension = 25f, Interest = 25f, StartedDate = save.CurrentDate
            };
            save.Storylines ??= new();
            save.Storylines.Add(storyline);
            return storyline;
        }

        public StorylineState CreateGroupStory(GameSave save, string name, StoryPartyType type, string firstId, string secondId)
        {
            if (type == StoryPartyType.Wrestler || firstId == secondId) throw new InvalidOperationException("서로 다른 두 그룹이 필요합니다.");
            var draft = new StorylineState { PartyType = type, FirstGroupId = firstId, SecondGroupId = secondId };
            var first = PartyMembers(save, draft, true, true);
            var second = PartyMembers(save, draft, false, true);
            if (first.Intersect(second).Any()) throw new InvalidOperationException("대립 양측의 소속 선수가 겹칠 수 없습니다.");
            var story = Create(save, name, first.First(), second.First());
            story.PartyType = type;
            story.FirstGroupId = firstId;
            story.SecondGroupId = secondId;
            return story;
        }

        public static IReadOnlyList<string> PartyMembers(GameSave save, StorylineState story, bool first, bool requireActive = false)
        {
            if (story.PartyType == StoryPartyType.Wrestler)
                return new[] { first ? story.FirstWrestlerId : story.SecondWrestlerId };
            var id = first ? story.FirstGroupId : story.SecondGroupId;
            if (story.PartyType == StoryPartyType.TagTeam)
            {
                var team = save.TagTeams.SingleOrDefault(x => x?.Id == id);
                if (team != null && (!requireActive || team.Status == TagTeamStatus.Active)) return team.MemberIds;
            }
            else if (story.PartyType == StoryPartyType.Stable)
            {
                var stable = save.Stables.SingleOrDefault(x => x?.Id == id);
                if (stable != null && (!requireActive || stable.Status == TagTeamStatus.Active)) return stable.MemberIds;
            }
            throw new InvalidOperationException("스토리의 그룹을 찾을 수 없거나 활동 중이 아닙니다.");
        }

        public static bool IncludesBothParties(GameSave save, StorylineState story, IEnumerable<string> participants)
        {
            if (participants == null) return false;
            var ids = participants.ToHashSet();
            return PartyMembers(save, story, true).Any(ids.Contains) && PartyMembers(save, story, false).Any(ids.Contains);
        }

        public static bool CanProgress(GameSave save, StorylineState story)
        {
            if (story.Status != StorylineStatus.Active) return false;
            try
            {
                var first = PartyMembers(save, story, true, true);
                var second = PartyMembers(save, story, false, true);
                return first.Count > 0 && second.Count > 0 && !first.Intersect(second).Any();
            }
            catch (InvalidOperationException) { return false; }
        }

        public static StoryIncident PromoIncident(PromoPlanState plan) => plan.Incident != StoryIncident.None ? plan.Incident :
            plan.Purpose switch
            {
                PromoPurpose.Rivalry => StoryIncident.Taunt,
                PromoPurpose.Challenge or PromoPurpose.MatchBuild => StoryIncident.Challenge,
                _ => StoryIncident.None
            };

        public static void ValidatePromoIncident(GameSave save, PromoPlanState plan)
        {
            if (plan.ParticipantIds == null || plan.ParticipantIds.Count == 0)
                throw new InvalidOperationException("프로모 출연 선수가 필요합니다.");
            if (!Enum.IsDefined(typeof(StoryIncident), plan.Incident)) throw new InvalidOperationException("Invalid promo incident.");
            var incident = PromoIncident(plan);
            if (incident == StoryIncident.None) return;
            var (actor, target) = PromoPair(save, plan);
            // Legacy solo rivalry promos need no relationship change until a target is selected.
            if (plan.Incident == StoryIncident.None && string.IsNullOrEmpty(target)) return;
            ValidatePair(save, actor, target);
            if (!plan.ParticipantIds.Contains(actor) ||
                !plan.ParticipantIds.Concat(plan.TargetIds ?? new()).Contains(target))
                throw new InvalidOperationException("Promo incident must reference its speaker and participant or target.");
        }

        private static (string Actor, string Target) PromoPair(GameSave save, PromoPlanState plan)
        {
            var actor = string.IsNullOrEmpty(plan.IncidentActorId) ? plan.ParticipantIds.FirstOrDefault() : plan.IncidentActorId;
            var target = plan.IncidentTargetId;
            if (string.IsNullOrEmpty(target)) target = (plan.TargetIds ?? new()).Concat(plan.ParticipantIds).FirstOrDefault(x => x != actor);
            return (actor, target);
        }

        // Called for one timeline event; source relationships remain untouched until result confirmation.
        public static void Evaluate(GameSave save, ShowResultState output,
            IReadOnlyCollection<MatchResultState> matches, IReadOnlyCollection<PromoResultState> promos)
        {
            var order = output.TimelineResultIds ?? new();
            var events = matches.Select(x => (Id: x.Id, Match: x, Promo: (PromoResultState)null))
                .Concat(promos.Select(x => (Id: x.Id, Match: (MatchResultState)null, Promo: x)))
                .OrderBy(x => order.IndexOf(x.Id)).ToList();
            foreach (var item in events)
            {
                if (item.Match != null)
                {
                    var result = item.Match;
                    var plan = save.MatchPlans.Single(x => x.Id == result.MatchPlanId);
                    void AddSpot(SpotResultState spot)
                    {
                        if (spot.WasSkipped) return;
                        // Spot outcome grades execution, not whether the scripted betrayal happened.
                        var incident = spot.SpotId is "spot_009" or "spot_016" ? StoryIncident.Betrayal :
                            spot.SpotId is "spot_001" or "spot_002" or "spot_003" or "spot_004" or "spot_007" or
                                "spot_010" or "spot_011" or "spot_012" or "spot_013" or "spot_014" or "spot_015"
                                ? StoryIncident.Attack : StoryIncident.None;
                        if (incident == StoryIncident.None) return;
                        var linked = (save.Storylines ?? new()).FirstOrDefault(x => x.Id == plan.StorylineId);
                        var inStory = linked != null && IncludesBothParties(save, linked, new[] { spot.ActorId, spot.TargetId });
                        Add(save, output, plan.StorylineId, inStory ? StoryAction.Progress : StoryAction.None,
                            result.Id, StoryEventType.Match, incident, spot.ActorId, spot.TargetId, 50f);
                    }
                    foreach (var spot in (result.SpotResults ?? new()).Where(x => x.Phase != MatchSpotPhase.PostMatch).OrderBy(x => x.Phase)) AddSpot(spot);
                    if (result.WasStoppedByExecution) continue;
                    Add(save, output, plan.StorylineId, plan.StoryAction, result.Id, StoryEventType.Match,
                        StoryIncident.Competition, result.FinishPerformerId ?? plan.WinnerId, result.LoserTargetId,
                        result.FinalMatchQuality * 5f);
                    foreach (var spot in (result.SpotResults ?? new()).Where(x => x.Phase == MatchSpotPhase.PostMatch)) AddSpot(spot);
                    // End only after post-match events have been evaluated.
                    if (plan.StoryAction == StoryAction.End)
                        output.StorylineChanges.Last(x => x.StorylineId == plan.StorylineId && x.SourceResultId == result.Id).Action = StoryAction.End;
                }
                else
                {
                    var result = item.Promo;
                    var plan = save.PromoPlans.Single(x => x.Id == result.PromoPlanId);
                    ValidatePromoIncident(save, plan);
                    var (actor, target) = PromoPair(save, plan);
                    Add(save, output, plan.StorylineId, plan.StoryAction, result.Id, StoryEventType.Promo,
                        PromoIncident(plan), actor, target, result.PromoScore);
                }
            }
        }

        private static void Add(GameSave save, ShowResultState output, string storyId, StoryAction action, string source,
            StoryEventType type, StoryIncident incident, string actor, string target, float quality)
        {
            if (!Finite(quality) || quality < 0 || quality > 100) throw new InvalidOperationException("Invalid story event quality.");
            var story = (save.Storylines ?? new()).SingleOrDefault(x => x?.Id == storyId);
            if (action != StoryAction.None && (story == null || story.Status != StorylineStatus.Active))
                throw new InvalidOperationException("Linked storyline must be active.");
            if (story != null && action != StoryAction.None)
            {
                if ((output.StorylineChanges ?? new()).Any(x => x.StorylineId == storyId && x.Action == StoryAction.End))
                    throw new InvalidOperationException("Cannot progress a storyline after its ending.");
                // ponytail: count same incident in the last three beats; tune this window after playtesting.
                var recent = (story.History ?? new()).Select(x => x.Incident).Concat(
                    output.StorylineChanges.Where(x => x.StorylineId == storyId).Select(x => x.Incident)).TakeLast(3);
                var repeats = incident == StoryIncident.None ? 0 : recent.Count(x => x == incident);
                var tension = incident switch
                {
                    StoryIncident.Attack => 15f, StoryIncident.Betrayal => 25f,
                    StoryIncident.Challenge => 6f, StoryIncident.Taunt => 8f,
                    StoryIncident.Rescue => -8f, StoryIncident.Respect => -4f, StoryIncident.Reconciliation => -20f, _ => 0f
                };
                if (tension > 0) tension /= 1 + repeats;
                output.StorylineChanges.Add(new StorylineResultChangeState
                {
                    StorylineId = storyId, SourceResultId = source, EventType = type,
                    Action = type == StoryEventType.Match ? StoryAction.Progress : action,
                    Incident = incident, ActorId = actor, TargetId = target,
                    TensionChange = tension, InterestChange = (quality - 50f) / 10f - repeats * 2f,
                    FatigueChange = repeats > 0 ? repeats * 3f : 0f
                });
            }
            var delta = incident switch
            {
                StoryIncident.Attack => -15f, StoryIncident.Betrayal => -35f, StoryIncident.Taunt => -4f,
                StoryIncident.Rescue => 20f, StoryIncident.Respect => 8f, StoryIncident.Reconciliation => 25f, _ => 0f
            };
            if (string.IsNullOrEmpty(storyId) && action == StoryAction.None &&
                incident is StoryIncident.Attack or StoryIncident.Betrayal or StoryIncident.Challenge or StoryIncident.Taunt &&
                !string.IsNullOrEmpty(actor) && !string.IsNullOrEmpty(target) && actor != target)
            {
                ValidatePair(save, actor, target);
                var candidateId = source + ":" + incident + ":" + actor + ":" + target;
                if (!output.StoryCandidates.Any(x => x.Id == candidateId))
                    output.StoryCandidates.Add(new StoryCandidateState
                    {
                        Id = candidateId, Quality = quality,
                        Event = new StoryEventState { SourceResultId = source, EventType = type, Incident = incident, ActorId = actor, TargetId = target }
                    });
            }
            if (delta == 0 || string.IsNullOrEmpty(actor) || string.IsNullOrEmpty(target)) return;
            ValidatePair(save, actor, target);
            output.RelationshipChanges.Add(new StoryRelationshipChangeState
            {
                FirstWrestlerId = actor, SecondWrestlerId = target, ValueChange = delta, SourceResultId = source
            });
        }

        public static void Validate(GameSave save, ShowResultState result)
        {
            ValidateState(save);
            var resultCandidateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in result.StoryCandidates ?? new())
            {
                if (candidate?.Event == null || string.IsNullOrWhiteSpace(candidate.Id) || !resultCandidateIds.Add(candidate.Id) ||
                    string.IsNullOrWhiteSpace(candidate.Event.SourceResultId) || !Finite(candidate.Quality) || candidate.Quality < 0 || candidate.Quality > 100 ||
                    !Enum.IsDefined(typeof(StoryIncident), candidate.Event.Incident))
                    throw new InvalidOperationException("Invalid story candidate.");
                ValidatePair(save, candidate.Event.ActorId, candidate.Event.TargetId);
                if (result.TimelineResultIds?.Count > 0 && !result.TimelineResultIds.Contains(candidate.Event.SourceResultId))
                    throw new InvalidOperationException("Story candidate references another show's result.");
            }
            var ended = new HashSet<string>();
            var sourceIds = new HashSet<string>(result.TimelineResultIds ?? new());
            foreach (var change in result.StorylineChanges ?? new())
            {
                if (change == null) throw new InvalidOperationException("Story change is missing.");
                if (sourceIds.Count > 0 && !sourceIds.Contains(change.SourceResultId))
                    throw new InvalidOperationException("Story change references another show's result.");
                var story = (save.Storylines ?? new()).SingleOrDefault(x => x?.Id == change.StorylineId);
                if (story == null || story.Status != StorylineStatus.Active || ended.Contains(story.Id))
                    throw new InvalidOperationException("Story change references a missing or completed storyline.");
                if (!CanProgress(save, story) && !result.StorylineChanges.Any(x => x != null &&
                    x.StorylineId == story.Id && x.SourceResultId == change.SourceResultId && x.Action == StoryAction.End))
                    throw new InvalidOperationException("해체되거나 소속이 겹치는 그룹의 대립은 종료만 할 수 있습니다.");
                if (string.IsNullOrWhiteSpace(change.SourceResultId) || change.Action == StoryAction.None ||
                    !Enum.IsDefined(typeof(StoryAction), change.Action) || !Enum.IsDefined(typeof(StoryIncident), change.Incident) ||
                    !Finite(change.TensionChange) || !Finite(change.InterestChange) || !Finite(change.FatigueChange))
                    throw new InvalidOperationException("Story change is invalid.");
                if ((story.History ?? new()).Any(x => x.SourceResultId == change.SourceResultId))
                    throw new InvalidOperationException("Story result was already applied.");
                if (change.Action == StoryAction.End) ended.Add(story.Id);
            }
            foreach (var change in result.RelationshipChanges ?? new())
            {
                if (change == null || !Finite(change.ValueChange) || string.IsNullOrWhiteSpace(change.SourceResultId))
                    throw new InvalidOperationException("Relationship change is invalid.");
                if (sourceIds.Count > 0 && !sourceIds.Contains(change.SourceResultId))
                    throw new InvalidOperationException("Relationship change references another show's result.");
                ValidatePair(save, change.FirstWrestlerId, change.SecondWrestlerId);
            }
        }

        public static void ValidateState(GameSave save)
        {
            var candidateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in save.StoryCandidates ?? new())
            {
                if (candidate?.Event == null || string.IsNullOrWhiteSpace(candidate.Id) || !candidateIds.Add(candidate.Id) ||
                    string.IsNullOrWhiteSpace(candidate.Event.SourceResultId) ||
                    !Enum.IsDefined(typeof(StoryCandidateStatus), candidate.Status) ||
                    !Enum.IsDefined(typeof(StoryEventType), candidate.Event.EventType) ||
                    !Enum.IsDefined(typeof(StoryIncident), candidate.Event.Incident) ||
                    !Finite(candidate.Quality) || candidate.Quality < 0 || candidate.Quality > 100)
                    throw new InvalidOperationException("Invalid saved story candidate.");
                ValidatePair(save, candidate.Event.ActorId, candidate.Event.TargetId);
                if (candidate.Status == StoryCandidateStatus.Adopted &&
                    !(save.Storylines ?? new()).Any(x => x?.Id == candidate.StorylineId &&
                        (x.History ?? new()).Any(e => e?.SourceResultId == candidate.Event.SourceResultId)))
                    throw new InvalidOperationException("Adopted candidate requires its recorded story event.");
            }
            foreach (var story in save.Storylines ?? new())
            {
                if (story == null || !Finite(story.Tension) || !Finite(story.Interest) || !Finite(story.Fatigue) ||
                    story.Tension < 0 || story.Tension > 100 || story.Interest < 0 || story.Interest > 100 ||
                    story.Fatigue < 0 || story.Fatigue > 100 || !Enum.IsDefined(typeof(StorylineStatus), story.Status))
                    throw new InvalidOperationException("Invalid storyline state.");
                ValidatePair(save, story.FirstWrestlerId, story.SecondWrestlerId);
                if (story.PartyType != StoryPartyType.Wrestler)
                {
                    var first = PartyMembers(save, story, true);
                    var second = PartyMembers(save, story, false);
                    if (story.FirstGroupId == story.SecondGroupId || first == null || second == null)
                        throw new InvalidOperationException("Invalid storyline parties.");
                }
            }
            var pairs = new HashSet<(string, string)>();
            foreach (var relation in save.StoryRelationships ?? new())
            {
                if (relation == null) throw new InvalidOperationException("Relationship is missing.");
                ValidatePair(save, relation.FirstWrestlerId, relation.SecondWrestlerId);
                var key = string.CompareOrdinal(relation.FirstWrestlerId, relation.SecondWrestlerId) < 0 ?
                    (relation.FirstWrestlerId, relation.SecondWrestlerId) : (relation.SecondWrestlerId, relation.FirstWrestlerId);
                if (!pairs.Add(key) || !Finite(relation.Value) || relation.Value < -100 || relation.Value > 100)
                    throw new InvalidOperationException("Relationship must be unique and between -100 and 100.");
            }
        }

        public static void Apply(GameSave save, ShowResultState result, GameDate date)
        {
            Validate(save, result);
            foreach (var change in result.StorylineChanges ?? new())
            {
                var story = save.Storylines.Single(x => x.Id == change.StorylineId);
                story.History ??= new();
                story.Tension = Clamp(story.Tension + change.TensionChange);
                story.Interest = Clamp(story.Interest + change.InterestChange);
                story.Fatigue = Clamp(story.Fatigue + change.FatigueChange);
                story.History.Add(new StoryEventState
                {
                    SourceResultId = change.SourceResultId, EventType = change.EventType, Date = date,
                    Incident = change.Incident, ActorId = change.ActorId, TargetId = change.TargetId,
                    TensionChange = change.TensionChange, InterestChange = change.InterestChange, FatigueChange = change.FatigueChange
                });
                if (change.Action == StoryAction.End) { story.Status = StorylineStatus.Completed; story.CompletedDate = date; }
            }
            foreach (var change in result.RelationshipChanges ?? new())
            {
                save.StoryRelationships ??= new();
                var relation = save.StoryRelationships.SingleOrDefault(x => Pair(x.FirstWrestlerId, x.SecondWrestlerId,
                    change.FirstWrestlerId, change.SecondWrestlerId));
                if (relation == null)
                {
                    var first = change.FirstWrestlerId; var second = change.SecondWrestlerId;
                    if (string.CompareOrdinal(first, second) > 0) (first, second) = (second, first);
                    relation = new StoryRelationshipState { FirstWrestlerId = first, SecondWrestlerId = second };
                    save.StoryRelationships.Add(relation);
                }
                relation.Value = Math.Max(-100f, Math.Min(100f, relation.Value + change.ValueChange));
            }
        }

        public StorylineState AdoptCandidate(GameSave save, string candidateId, string name, string existingStoryId = null)
        {
            ValidateState(save);
            var candidate = save.StoryCandidates.SingleOrDefault(x => x?.Id == candidateId && x.Status == StoryCandidateStatus.Pending)
                ?? throw new InvalidOperationException("채택할 수 있는 스토리 후보가 아닙니다.");
            var snapshot = candidate.Event ?? throw new InvalidOperationException("후보 사건이 없습니다.");
            if (string.IsNullOrWhiteSpace(snapshot.SourceResultId) || !Enum.IsDefined(typeof(StoryIncident), snapshot.Incident))
                throw new InvalidOperationException("후보 사건이 올바르지 않습니다.");
            ValidatePair(save, snapshot.ActorId, snapshot.TargetId);
            if (!Finite(candidate.Quality) || candidate.Quality < 0 || candidate.Quality > 100)
                throw new InvalidOperationException("후보 평가가 올바르지 않습니다.");
            StorylineState story;
            if (string.IsNullOrEmpty(existingStoryId)) story = Create(save, name, snapshot.ActorId, snapshot.TargetId);
            else
            {
                story = save.Storylines.SingleOrDefault(x => x?.Id == existingStoryId && x.Status == StorylineStatus.Active)
                    ?? throw new InvalidOperationException("진행 중인 스토리가 아닙니다.");
                if (!IncludesBothParties(save, story, new[] { snapshot.ActorId, snapshot.TargetId }) ||
                    (story.History ?? new()).Any(x => x.SourceResultId == snapshot.SourceResultId))
                    throw new InvalidOperationException("이 사건은 해당 스토리에 연결할 수 없습니다.");
            }
            var changes = new ShowResultState();
            Add(save, changes, story.Id, StoryAction.Progress, snapshot.SourceResultId, snapshot.EventType,
                snapshot.Incident, snapshot.ActorId, snapshot.TargetId, candidate.Quality);
            changes.RelationshipChanges.Clear(); // The show's relationship effect was already applied.
            Apply(save, changes, snapshot.Date);
            candidate.Status = StoryCandidateStatus.Adopted;
            candidate.StorylineId = story.Id;
            return story;
        }

        public static void ExpireCandidates(GameSave save)
        {
            foreach (var candidate in save.StoryCandidates ?? new())
                if (candidate?.Status == StoryCandidateStatus.Pending) candidate.Status = StoryCandidateStatus.Expired;
        }

        public static void StartShow(GameSave save, ShowState show)
        {
            if (show?.Status != ShowStatus.Confirmed || !(save.Shows ?? new()).Contains(show))
                throw new InvalidOperationException("확정된 쇼만 시작할 수 있습니다.");
            show.Status = ShowStatus.InProgress;
            ExpireCandidates(save);
        }

        private static void ValidatePair(GameSave save, string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second) || first == second ||
                save.Wrestlers.All(x => x?.Id != first) || save.Wrestlers.All(x => x?.Id != second))
                throw new InvalidOperationException("Relationship requires two different existing wrestlers.");
        }

        private static bool Pair(string a, string b, string c, string d) => a == c && b == d || a == d && b == c;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Clamp(float value) => Math.Max(0f, Math.Min(100f, value));
    }
}
