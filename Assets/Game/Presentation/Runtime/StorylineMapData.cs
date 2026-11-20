using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Presentation
{
    public enum StorylineMapRelationKind { Rivalry, Alliance, Incident }

    public sealed class StorylineMapRelation
    {
        public string FirstId, SecondId, Label, Background;
        public StorylineMapRelationKind Kind;
        public bool Friendly, Directed, Dashed, IsStoryOpposition, IsMembership;
        public StoryEventState Event;
    }

    public sealed class StorylineMapGroup
    {
        public string Id, Name;
        public StoryPartyType Type;
        public readonly List<string> MemberIds = new();
    }

    public sealed class StorylineMapCluster
    {
        public StorylineState Story;
        public readonly List<WrestlerState> First = new();
        public readonly List<WrestlerState> Second = new();
        public readonly List<StorylineMapRelation> Relations = new();
        public readonly List<ChampionshipState> Championships = new();
        public readonly List<StoryEventState> History = new();
        public string FirstParty, SecondParty;
        public bool MissingParticipants;
        public readonly HashSet<string> FirstPartyIds = new(StringComparer.Ordinal);
        public readonly HashSet<string> SecondPartyIds = new(StringComparer.Ordinal);
        public IEnumerable<WrestlerState> Wrestlers => First.Concat(Second);
    }

    // Presentation-only projection. A story's opposing parties are separate from lasting relationships.
    public sealed class StorylineMapData
    {
        public GameSave Save;
        public readonly List<StorylineMapCluster> Clusters = new();
        public readonly List<WrestlerState> Wrestlers = new();
        public readonly List<StorylineMapGroup> Groups = new();
        public readonly Dictionary<string, List<ChampionshipState>> ChampionTitles = new(StringComparer.Ordinal);

        public static StorylineMapData Build(GameSave save)
        {
            var data = new StorylineMapData { Save = save };
            if (save?.Promotion == null) return data;
            var wrestlers = (save.Wrestlers ?? new()).Where(x => !string.IsNullOrEmpty(x?.Id))
                .GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var titles = (save.Championships ?? new()).Where(x => x != null && x.PromotionId == save.Promotion.Id)
                .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            foreach (var title in titles.Where(x => !string.IsNullOrEmpty(x.HolderId)))
            {
                IEnumerable<string> holders = title.Type == ChampionshipType.Individual ? new[] { title.HolderId }
                    : title.Reigns?.LastOrDefault(x => x != null && x.HolderId == title.HolderId && !x.LostDate.HasValue)?.MemberIds
                      ?? save.TagTeams?.FirstOrDefault(x => x?.Id == title.HolderId)?.MemberIds ?? new List<string>();
                foreach (var id in holders.Where(x => x != null && wrestlers.ContainsKey(x)).Distinct())
                {
                    if (!data.ChampionTitles.TryGetValue(id, out var held)) data.ChampionTitles[id] = held = new();
                    held.Add(title);
                }
            }
            foreach (var story in (save.Storylines ?? new()).Where(x => x != null && x.Status == StorylineStatus.Active && x.PromotionId == save.Promotion.Id)
                         .OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var cluster = new StorylineMapCluster { Story = story };
                data.Clusters.Add(cluster);
                var used = new HashSet<string>(StringComparer.Ordinal);
                void AddMembers(IEnumerable<string> ids, List<WrestlerState> party)
                {
                    foreach (var id in (ids ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal))
                    {
                        if (string.IsNullOrEmpty(id) || !wrestlers.TryGetValue(id, out var wrestler)) { cluster.MissingParticipants = true; continue; }
                        if (used.Add(id)) party.Add(wrestler);
                    }
                }
                IEnumerable<string> Members(bool first)
                {
                    if (story.PartyType == StoryPartyType.Wrestler) return new[] { first ? story.FirstWrestlerId : story.SecondWrestlerId };
                    var id = first ? story.FirstGroupId : story.SecondGroupId;
                    var members = story.PartyType == StoryPartyType.TagTeam
                        ? save.TagTeams?.FirstOrDefault(x => x?.Id == id)?.MemberIds
                        : save.Stables?.FirstOrDefault(x => x?.Id == id)?.MemberIds;
                    if (members == null || members.Count == 0) cluster.MissingParticipants = true;
                    return members ?? new List<string>();
                }
                string PartyName(bool first)
                {
                    if (story.PartyType == StoryPartyType.Wrestler) return null;
                    var id = first ? story.FirstGroupId : story.SecondGroupId;
                    return (story.PartyType == StoryPartyType.TagTeam ? save.TagTeams?.FirstOrDefault(x => x?.Id == id)?.Name
                        : save.Stables?.FirstOrDefault(x => x?.Id == id)?.Name) ?? "그룹 정보 없음";
                }
                AddMembers(Members(true), cluster.First);
                AddMembers(Members(false), cluster.Second);
                var firstRepresentative = cluster.First.FirstOrDefault()?.Id;
                var secondRepresentative = cluster.Second.FirstOrDefault()?.Id;
                cluster.FirstPartyIds.UnionWith(cluster.First.Select(x => x.Id));
                cluster.SecondPartyIds.UnionWith(cluster.Second.Select(x => x.Id));
                cluster.FirstParty = story.PartyType == StoryPartyType.Wrestler ? null : PartyName(true);
                cluster.SecondParty = story.PartyType == StoryPartyType.Wrestler ? null : PartyName(false);
                var history = StorylineHistory.ForStory(save, story);
                cluster.History.AddRange(history);
                // Recorded actors can remain visible after their group's membership changes.
                foreach (var id in history.SelectMany(x => new[] { x.ActorId, x.TargetId }).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x, StringComparer.Ordinal))
                    if (!used.Contains(id)) AddMembers(new[] { id }, cluster.First.Count <= cluster.Second.Count ? cluster.First : cluster.Second);

                void Relation(string first, string second, string label, string background, StorylineMapRelationKind kind,
                    bool friendly = false, bool directed = false, bool dashed = false, StoryEventState beat = null, bool storyOpposition = false, bool membership = false)
                {
                    if (first == second || first == null || second == null || !used.Contains(first) || !used.Contains(second)) return;
                    cluster.Relations.Add(new StorylineMapRelation { FirstId = first, SecondId = second, Label = label,
                        Background = background, Kind = kind, Friendly = friendly, Directed = directed, Dashed = dashed, Event = beat,
                        IsStoryOpposition = storyOpposition, IsMembership = membership });
                }
                var friendlyStory = story.IsAutomatic && (save.StoryRelationships ?? new()).Any(x =>
                    x.Value > 0 && (x.FirstWrestlerId == firstRepresentative && x.SecondWrestlerId == secondRepresentative ||
                        x.FirstWrestlerId == secondRepresentative && x.SecondWrestlerId == firstRepresentative));
                if (firstRepresentative != null && secondRepresentative != null)
                    Relation(firstRepresentative, secondRepresentative,
                        story.IsAutomatic ? "스토리 관계" : story.PartyType == StoryPartyType.Wrestler ? "스토리 대립" : "그룹 대립",
                        story.IsAutomatic ? StorylineHistory.Origin(save, story) : story.PartyType == StoryPartyType.Wrestler ? "이 스토리라인에 등록된 양측의 대립입니다."
                            : "등록된 두 그룹의 대립입니다. 선은 각 그룹의 대표 카드 하나씩을 연결하며, 모든 멤버의 개인적 적대를 뜻하지 않습니다.",
                        friendlyStory ? StorylineMapRelationKind.Alliance : StorylineMapRelationKind.Rivalry, friendlyStory, storyOpposition: true);
                foreach (var pair in (save.StoryRelationships ?? new()).Where(x => x != null && x.Value != 0 && !float.IsNaN(x.Value))
                             .OrderBy(x => x.FirstWrestlerId, StringComparer.Ordinal).ThenBy(x => x.SecondWrestlerId, StringComparer.Ordinal))
                    Relation(pair.FirstWrestlerId, pair.SecondWrestlerId, pair.Value < 0 ? "지속 관계 · 적대" : "지속 관계 · 우호",
                        "저장된 선수 간 지속 관계입니다. 특정 이야기 안에서의 역할이나 최근 사건과 다를 수 있습니다.",
                        pair.Value < 0 ? StorylineMapRelationKind.Rivalry : StorylineMapRelationKind.Alliance, pair.Value > 0, dashed: true);

                void Group(IEnumerable<string> members, string name, string type)
                {
                    var visible = (members ?? Array.Empty<string>()).Where(x => x != null && used.Contains(x)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                    // A spanning chain shows shared membership without turning a stable into a clique.
                    for (var i = 1; i < visible.Count; i++)
                        Relation(visible[i - 1], visible[i], type + " · " + name, "현재 활동 중인 " + type + "의 소속 관계입니다. 이야기 속 대립과 별개로 표시합니다.",
                            StorylineMapRelationKind.Alliance, true, dashed: true, membership: true);
                }
                foreach (var team in (save.TagTeams ?? new()).Where(x => x != null && x.Status == TagTeamStatus.Active && x.PromotionId == save.Promotion.Id).OrderBy(x => x.Id, StringComparer.Ordinal))
                    Group(team.MemberIds, team.Name, "태그팀");
                foreach (var stable in (save.Stables ?? new()).Where(x => x != null && x.Status == TagTeamStatus.Active && x.PromotionId == save.Promotion.Id).OrderBy(x => x.Id, StringComparer.Ordinal))
                    Group(stable.MemberIds, stable.Name, "스테이블");
                var latestPairs = history.Where(x => x.Incident != StoryIncident.None && x.ActorId != null && x.TargetId != null)
                    .GroupBy(x => string.CompareOrdinal(x.ActorId, x.TargetId) < 0 ? (x.ActorId, x.TargetId) : (x.TargetId, x.ActorId))
                    .Select(x => x.OrderBy(e => e.Date.Year).ThenBy(e => e.Date.Month).ThenBy(e => e.Date.Day).Last())
                    .OrderBy(x => x.ActorId, StringComparer.Ordinal).ThenBy(x => x.TargetId, StringComparer.Ordinal);
                foreach (var beat in latestPairs)
                    Relation(beat.ActorId, beat.TargetId, "최근 사건 · " + IncidentName(beat.Incident),
                        "이 스토리라인의 두 선수 사이에 기록된 가장 최근 사건입니다. 화살표는 행동한 선수에서 대상 선수로 향합니다.",
                        StorylineMapRelationKind.Incident, beat.Incident is StoryIncident.Rescue or StoryIncident.Respect or StoryIncident.Reconciliation,
                        beat.Incident != StoryIncident.Competition, dashed: true, beat: beat);
                var sourceIds = history.Select(x => x.SourceResultId).ToHashSet();
                var matchPlanIds = save.MatchResults.Where(x => sourceIds.Contains(x.Id)).Select(x => x.MatchPlanId).ToHashSet();
                var titleIds = (save.MatchPlans ?? new()).Where(x => x != null && (x.StorylineId == story.Id || matchPlanIds.Contains(x.Id)) && !string.IsNullOrEmpty(x.ChampionshipId))
                    .Select(x => x.ChampionshipId).ToHashSet();
                cluster.Championships.AddRange(titles.Where(x => titleIds.Contains(x.Id)));
            }
            data.Wrestlers.AddRange(wrestlers.Values.Where(x => x.PromotionId == save.Promotion.Id && x.Roster.ActivityState != RosterActivityState.Released)
                .Concat(data.Clusters.SelectMany(x => x.Wrestlers)).GroupBy(x => x.Id).Select(x => x.First()).OrderBy(x => x.Id, StringComparer.Ordinal));
            var visibleIds = data.Wrestlers.Select(x => x.Id).ToHashSet();
            void AddGroup(string id, string name, StoryPartyType type, IEnumerable<string> members)
            {
                var group = new StorylineMapGroup { Id = id, Name = name, Type = type };
                group.MemberIds.AddRange((members ?? Array.Empty<string>()).Where(x => x != null && visibleIds.Contains(x)).Distinct().OrderBy(x => x, StringComparer.Ordinal));
                if (group.MemberIds.Count > 1) data.Groups.Add(group);
            }
            foreach (var stable in (save.Stables ?? new()).Where(x => x != null && x.Status == TagTeamStatus.Active && x.PromotionId == save.Promotion.Id).OrderBy(x => x.Id, StringComparer.Ordinal))
                AddGroup(stable.Id, stable.Name, StoryPartyType.Stable, stable.MemberIds);
            foreach (var team in (save.TagTeams ?? new()).Where(x => x != null && x.Status == TagTeamStatus.Active && x.PromotionId == save.Promotion.Id).OrderBy(x => x.Id, StringComparer.Ordinal))
                AddGroup(team.Id, team.Name, StoryPartyType.TagTeam, team.MemberIds);
            return data;
        }

        public static string IncidentName(StoryIncident incident) => incident switch
        {
            StoryIncident.Competition => "경쟁", StoryIncident.Challenge => "도전", StoryIncident.Taunt => "도발",
            StoryIncident.Attack => "공격", StoryIncident.Betrayal => "배신", StoryIncident.Rescue => "구출",
            StoryIncident.Respect => "존중", StoryIncident.Reconciliation => "화해", _ => "진행"
        };
    }
}
