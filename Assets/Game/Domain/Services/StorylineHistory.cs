using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class StorylineHistory
    {
        public const int BackgroundDays = 90;
        private static readonly ConditionalWeakTable<GameSave, HistoryIndex> Indexes = new();

        private sealed class HistoryIndex
        {
            public int Shows, Matches, Promos, Applied, Legacy;
            public List<StoryEventState> Events;
            public Dictionary<string, StoryEventState> ById;
            public Dictionary<(string, string), StorylineResultChangeState> Changes;
            public readonly Dictionary<(string, string), List<StoryEventState>> Pairs = new();
        }

        public static void Invalidate(GameSave save) => Indexes.Remove(save);

        private static HistoryIndex Index(GameSave save)
        {
            var legacy = save.Storylines.Sum(x => x.History?.Count ?? 0);
            if (Indexes.TryGetValue(save, out var cached) && cached.Shows == save.ShowResults.Count &&
                cached.Matches == save.MatchResults.Count && cached.Promos == save.PromoResults.Count &&
                cached.Applied == save.ProcessedIds.Count && cached.Legacy == legacy) return cached;
            var events = ReadAll(save);
            var index = new HistoryIndex { Shows = save.ShowResults.Count, Matches = save.MatchResults.Count,
                Promos = save.PromoResults.Count, Applied = save.ProcessedIds.Count, Legacy = legacy,
                Events = events, ById = events.ToDictionary(Key),
                Changes = save.ShowResults.Where(x => IsConfirmed(save, x)).SelectMany(x => x.StorylineChanges ?? new())
                    .Where(x => !string.IsNullOrEmpty(x.EventId)).GroupBy(x => (x.StorylineId, x.EventId))
                    .ToDictionary(x => x.Key, x => x.First()) };
            Indexes.Remove(save); Indexes.Add(save, index);
            return index;
        }

        public static bool IsConfirmed(GameSave save, ShowResultState result) => result.StoryHistoryApplied ||
            save.Shows.Any(s => s.Id == result.ShowId && s.ShowVersion == result.ShowVersion && s.Status == ShowStatus.Completed);

        public static IReadOnlyList<StoryEventState> All(GameSave save) => Index(save).Events;

        private static List<StoryEventState> ReadAll(GameSave save)
        {
            var events = new List<StoryEventState>();
            var matches = save.MatchResults.ToDictionary(x => x.Id);
            var promos = save.PromoResults.ToDictionary(x => x.Id);
            foreach (var showResult in save.ShowResults.Where(x => x != null && IsConfirmed(save, x)))
            {
                events.AddRange(showResult.StoryEvents ?? new());
                var sources = (showResult.StoryEvents ?? new()).Select(x => x.SourceResultId).ToHashSet();
                var date = save.Shows.FirstOrDefault(x => x.Id == showResult.ShowId)?.Date ?? save.CurrentDate;
                foreach (var id in showResult.TimelineResultIds ?? new())
                {
                    if (sources.Contains(id)) continue;
                    if (matches.TryGetValue(id, out var match) && string.IsNullOrEmpty(match.CancellationReason))
                    {
                        var spots = match.SpotResults ?? new();
                        foreach (var spot in spots.Where(x => x.Phase != MatchSpotPhase.PostMatch).OrderBy(x => x.Phase)) AddSpot(spot);
                        if (!match.WasStoppedByExecution) events.Add(new StoryEventState { Id = id + ":match", SourceResultId = id, Date = date,
                            EventType = StoryEventType.Match, Incident = StoryIncident.Competition,
                            ActorId = match.FinishPerformerId ?? match.WinnerId, TargetId = match.LoserTargetId,
                            ParticipantIds = MatchMembers(save, match).ToList() });
                        foreach (var spot in spots.Where(x => x.Phase == MatchSpotPhase.PostMatch)) AddSpot(spot);
                        void AddSpot(SpotResultState spot)
                        {
                            var incident = StorylineService.SpotIncident(spot.SpotId);
                            if (spot.WasSkipped || incident == StoryIncident.None) return;
                            events.Add(new StoryEventState { Id = id + ":spot:" + spots.IndexOf(spot), SourceResultId = id,
                                Date = date, EventType = StoryEventType.Match, Incident = incident, ActorId = spot.ActorId, TargetId = spot.TargetId });
                        }
                    }
                    else if (promos.TryGetValue(id, out var promo) && string.IsNullOrEmpty(promo.CancellationReason))
                    {
                        var recorded = (showResult.StorylineChanges ?? new()).Where(x => x.SourceResultId == id).ToList();
                        if (recorded.Count > 0)
                        {
                            foreach (var change in recorded) events.Add(new StoryEventState { Id = id + ":promo", SourceResultId = id,
                                Date = date, EventType = StoryEventType.Promo, Incident = change.Incident,
                                ActorId = change.ActorId, TargetId = change.TargetId });
                            continue;
                        }
                        var plan = save.PromoPlans.FirstOrDefault(x => x.Id == promo.PromoPlanId);
                        if (plan == null) continue;
                        var pair = StorylineService.PromoPair(save, plan);
                        events.Add(new StoryEventState { Id = id + ":promo", SourceResultId = id, Date = date,
                            EventType = StoryEventType.Promo, Incident = StorylineService.PromoIncident(plan),
                            ActorId = pair.Actor, TargetId = pair.Target,
                            ParticipantIds = plan.ParticipantIds.Concat(plan.TargetIds ?? new()).Distinct().ToList() });
                    }
                }
            }
            var keys = events.Select(Key).ToHashSet();
            var meanings = events.Select(x => (x.SourceResultId, x.Incident, x.ActorId, x.TargetId)).ToHashSet();
            foreach (var beat in save.Storylines.SelectMany(x => x.History ?? new()).Where(x => x != null))
            {
                var meaning = (beat.SourceResultId, beat.Incident, beat.ActorId, beat.TargetId);
                if (keys.Contains(Key(beat)) || string.IsNullOrEmpty(beat.Id) && meanings.Contains(meaning)) continue;
                events.Add(beat); keys.Add(Key(beat)); meanings.Add(meaning);
            }
            return events.Where(x => x != null).GroupBy(Key).Select(x => x.First()).OrderBy(x => x.Date).ToList();
        }

        public static IEnumerable<string> MatchMembers(GameSave save, MatchResultState result)
        {
            var snapshot = (result.ParticipantProfiles ?? new()).SelectMany(x => x.MemberIds).Distinct().ToList();
            if (snapshot.Count > 0) return snapshot;
            var plan = save.MatchPlans.FirstOrDefault(x => x.Id == result.MatchPlanId);
            if (plan == null) return new[] { result.FinishPerformerId ?? result.WinnerId, result.LoserTargetId }.Where(x => x != null);
            return plan.Sides.Count > 0 ? plan.Sides.SelectMany(x => x.MemberIds).Distinct() : plan.ParticipantIds;
        }

        public static List<StoryEventState> ForPair(GameSave save, string first, string second)
        {
            var index = Index(save);
            var pair = string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);
            if (!index.Pairs.TryGetValue(pair, out var events))
                index.Pairs[pair] = events = index.Events.Where(x => Involves(x, first, second)).ToList();
            return new List<StoryEventState>(events);
        }

        public static bool Involves(StoryEventState beat, string first, string second) =>
            first != second && ((beat.ActorId == first && beat.TargetId == second || beat.ActorId == second && beat.TargetId == first) ||
                (beat.Incident is StoryIncident.Competition or StoryIncident.None &&
                 beat.ParticipantIds?.Contains(first) == true && beat.ParticipantIds.Contains(second)));

        public static List<StoryEventState> ForPairBefore(GameSave save, string first, string second, string sourceId)
        {
            var owner = save.ShowResults.FirstOrDefault(x => x.TimelineResultIds?.Contains(sourceId) == true);
            if (owner == null) return ForPair(save, first, second).Where(x => x.SourceResultId != sourceId).ToList();
            var sources = save.ShowResults.TakeWhile(x => x != owner).Where(x => IsConfirmed(save, x))
                .SelectMany(x => x.TimelineResultIds ?? new()).Concat(owner.TimelineResultIds.TakeWhile(x => x != sourceId)).ToHashSet();
            return All(save).Where(x => sources.Contains(x.SourceResultId)).Concat(
                    (owner.StoryEvents ?? new()).TakeWhile(x => x.SourceResultId != sourceId))
                .Where(x => Involves(x, first, second)).GroupBy(Key).Select(x => x.First()).OrderBy(x => x.Date).ToList();
        }

        public static List<StoryEventState> ForStory(GameSave save, StorylineState story)
        {
            var index = Index(save);
            var recorded = (story.EventIds ?? new()).Where(index.ById.ContainsKey).Select(id => index.ById[id]).Select(beat =>
            {
                if (!index.Changes.TryGetValue((story.Id, Key(beat)), out var change)) return beat;
                return new StoryEventState { Id = beat.Id, SourceResultId = beat.SourceResultId, Date = beat.Date,
                    EventType = beat.EventType, Incident = beat.Incident, ActorId = beat.ActorId, TargetId = beat.TargetId,
                    ParticipantIds = beat.ParticipantIds, TensionChange = change.TensionChange,
                    InterestChange = change.InterestChange, FatigueChange = change.FatigueChange };
            });
            return (story.History ?? new()).Concat(recorded)
                .GroupBy(Key).Select(x => x.First()).OrderBy(x => x.Date).ToList();
        }

        public static string Key(StoryEventState beat) => !string.IsNullOrEmpty(beat.Id) ? beat.Id :
            beat.EventType == StoryEventType.Promo ? beat.SourceResultId + ":promo" :
            beat.Incident == StoryIncident.Competition ? beat.SourceResultId + ":match" :
            $"{beat.SourceResultId}:{beat.Incident}:{beat.ActorId}:{beat.TargetId}";

        public static StorylineState FindActive(GameSave save, IEnumerable<string> participants, string preferredId = null)
        {
            var ids = participants.Where(x => !string.IsNullOrEmpty(x)).ToHashSet();
            return save.Storylines.Where(x => x.Status == StorylineStatus.Active &&
                    (x.PartyType == StoryPartyType.Wrestler || x.Id == preferredId) &&
                    StorylineService.CanProgress(save, x) && StorylineService.IncludesBothParties(save, x, ids))
                .OrderByDescending(x => x.Id == preferredId).ThenByDescending(x => x.LastEventDate)
                .ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        public static string Title(GameSave save, StorylineState story)
        {
            string Party(bool first)
            {
                if (story.PartyType == StoryPartyType.Wrestler)
                    return save.Wrestlers.FirstOrDefault(x => x.Id == (first ? story.FirstWrestlerId : story.SecondWrestlerId))?.Identity.RingName ?? "선수 정보 없음";
                var id = first ? story.FirstGroupId : story.SecondGroupId;
                return (story.PartyType == StoryPartyType.TagTeam
                    ? save.TagTeams.FirstOrDefault(x => x.Id == id)?.Name
                    : save.Stables.FirstOrDefault(x => x.Id == id)?.Name) ?? "그룹 정보 없음";
            }
            return Party(true) + " vs " + Party(false);
        }

        public static string Origin(GameSave save, StorylineState story)
        {
            var history = ForStory(save, story);
            // Automatic stories can include a prior match as background.
            var first = story.IsAutomatic ? history.FirstOrDefault(x => x.Date.CompareTo(story.StartedDate) >= 0 &&
                x.Incident is StoryIncident.Attack or StoryIncident.Betrayal or StoryIncident.Challenge or StoryIncident.Taunt or StoryIncident.Rescue) : history.FirstOrDefault();
            first ??= history.FirstOrDefault();
            if (first == null) return "촉발 사건 기록이 없습니다.";
            var showId = save.ShowResults.FirstOrDefault(x => IsConfirmed(save, x) &&
                (x.TimelineResultIds?.Contains(first.SourceResultId) == true || x.StoryEvents?.Any(e => Key(e) == Key(first)) == true))?.ShowId
                ?? save.MatchResults.FirstOrDefault(x => x.Id == first.SourceResultId)?.ShowId
                ?? save.PromoResults.FirstOrDefault(x => x.Id == first.SourceResultId)?.ShowId;
            var show = save.Shows.FirstOrDefault(x => x.Id == showId);
            var location = string.IsNullOrWhiteSpace(show?.Name) ? "" : show.Name + " · ";
            var segment = first.EventType == StoryEventType.Match ? "경기" : "프로모";
            return $"{first.Date.Year:D4}.{first.Date.Month:D2}.{first.Date.Day:D2} · {location}{segment} · {Describe(save, first)}";
        }

        public static string IncidentName(StoryIncident incident) => incident switch
        {
            StoryIncident.Competition => "경기", StoryIncident.Challenge => "도전", StoryIncident.Taunt => "도발",
            StoryIncident.Attack => "공격", StoryIncident.Betrayal => "배신", StoryIncident.Rescue => "구출",
            StoryIncident.Respect => "존중", StoryIncident.Reconciliation => "화해", _ => "공동 출연"
        };

        public static string Describe(GameSave save, StoryEventState beat)
        {
            string Name(string id) => save.Wrestlers.FirstOrDefault(x => x.Id == id)?.Identity.RingName ?? "선수";
            if (beat.Incident == StoryIncident.Competition)
            {
                var match = save.MatchResults.FirstOrDefault(x => x.Id == beat.SourceResultId);
                if (match != null && !string.IsNullOrEmpty(match.WinnerId) && !match.WasStoppedByExecution)
                    return $"{Name(match.WinnerId)} 승리 · {IncidentName(beat.Incident)}";
                return "함께 출전한 경기";
            }
            return string.IsNullOrEmpty(beat.TargetId) ? IncidentName(beat.Incident) :
                $"{Name(beat.ActorId)} · {Name(beat.TargetId)} · {IncidentName(beat.Incident)}";
        }
    }
}
