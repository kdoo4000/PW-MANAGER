using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class WrestlerRelationshipsView
    {
        internal sealed class Relationship
        {
            public WrestlerState Wrestler;
            public float Affinity;
            public bool IsTagPartner;
            public readonly List<StorylineState> Stories = new();
            public readonly List<StoryEventState> History = new();
            public readonly List<string> Groups = new();
        }

        public static VisualElement Open(VisualElement host, GameSave save, string wrestlerId)
        {
            host.Q("wrestler-relationships")?.RemoveFromHierarchy();
            var root = new VisualElement { name = "wrestler-relationships" };
            root.AddToClassList("story-management"); root.AddToClassList("wr-screen"); root.AddToClassList("sm-screen");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StoryManagement"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StorylineMap"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/WrestlerRelationships"));
            host.Add(root);
            if (save == null || !save.Wrestlers.Any(x => x.Id == wrestlerId))
            {
                root.Add(new Label("선수의 관계 기록을 찾을 수 없습니다.")); return root;
            }

            var people = save.Wrestlers.Where(x => x != null && x.Id != wrestlerId && save.IsWrestlerDiscovered(x.Id)).ToDictionary(x => x.Id);
            var center = save.Wrestlers.First(x => x.Id == wrestlerId);
            var relationships = new Dictionary<string, Relationship>(StringComparer.Ordinal);
            Relationship Get(string id)
            {
                if (string.IsNullOrEmpty(id) || !people.TryGetValue(id, out var wrestler)) return null;
                if (!relationships.TryGetValue(id, out var value))
                    relationships[id] = value = new Relationship { Wrestler = wrestler };
                return value;
            }
            var contracted = save.OwnContracts.Where(x => x.Status == ContractStatus.Active && x.Type == ContractType.Wrestler)
                .Select(x => x.PersonId).ToHashSet();
            foreach (var other in people.Values.Where(x => x.Roster.ActivityState != RosterActivityState.Released &&
                         (!string.IsNullOrEmpty(center.PromotionId) ? x.PromotionId == center.PromotionId : contracted.Contains(x.Id))))
                Get(other.Id);
            foreach (var beat in StorylineHistory.All(save))
            {
                var ids = (beat.Incident is StoryIncident.Competition or StoryIncident.None ?
                        beat.ParticipantIds ?? new List<string>() : new List<string>())
                    .Concat(new[] { beat.ActorId, beat.TargetId }).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                if (!ids.Contains(wrestlerId)) continue;
                foreach (var id in ids.Where(x => x != wrestlerId)) Get(id)?.History.Add(beat);
            }
            foreach (var pair in save.StoryRelationships ?? new())
            {
                var other = pair.FirstWrestlerId == wrestlerId ? pair.SecondWrestlerId :
                    pair.SecondWrestlerId == wrestlerId ? pair.FirstWrestlerId : null;
                var value = Get(other); if (value != null) value.Affinity = pair.Value;
            }
            foreach (var story in save.Storylines.Where(x => StorylineService.CanProgress(save, x)))
            {
                var first = StorylineService.PartyMembers(save, story, true);
                var second = StorylineService.PartyMembers(save, story, false);
                if (!first.Contains(wrestlerId) && !second.Contains(wrestlerId)) continue;
                foreach (var id in first.Concat(second).Where(x => x != wrestlerId).Distinct())
                {
                    var relation = Get(id);
                    if (relation != null) relation.Stories.Add(story);
                }
            }
            var groups = save.TagTeams.Where(x => x.Status == TagTeamStatus.Active).Select(x => (x.MemberIds, x.Name, IsTagPartner: true))
                .Concat(save.Stables.Where(x => x.Status == TagTeamStatus.Active).Select(x => (x.MemberIds, x.Name, IsTagPartner: false)));
            foreach (var group in groups.Where(x => x.MemberIds.Contains(wrestlerId)))
                foreach (var id in group.MemberIds.Where(x => x != wrestlerId))
                {
                    var relation = Get(id);
                    if (relation == null) continue;
                    if (!relation.Groups.Contains(group.Name)) relation.Groups.Add(group.Name);
                    relation.IsTagPartner |= group.IsTagPartner;
                }
            var ordered = relationships.Values.Where(x => !float.IsNaN(x.Affinity) && !float.IsInfinity(x.Affinity)).OrderByDescending(x => x.History.LastOrDefault()?.Date ?? default)
                .ThenByDescending(x => Math.Abs(x.Affinity)).ThenBy(x => WrestlerNameText.DisplayName(x.Wrestler), StringComparer.Ordinal).ToList();
            var toolbar = new VisualElement(); toolbar.AddToClassList("wr-toolbar"); root.Add(toolbar);
            var search = new TextField("선수 검색") { name = "wr-search" }; toolbar.Add(search);
            var filter = new DropdownField("관계", new List<string> { "전체", "우호", "적대", "중립" }, 0) { name = "wr-filter" }; toolbar.Add(filter);
            var actions = new VisualElement(); actions.AddToClassList("wr-map-actions"); toolbar.Add(actions);
            var body = new VisualElement(); body.AddToClassList("wr-body"); root.Add(body);
            var mapHost = new VisualElement { name = "wr-map-host" }; mapHost.AddToClassList("wr-map-host"); body.Add(mapHost);
            var detail = new ScrollView { name = "wr-detail" }; detail.AddToClassList("wr-detail"); body.Add(detail);
            WrestlerRelationshipMapCanvas map = null;
            string selected = null;
            void Control(string text, string name, Action action)
            {
                var button = new Button(action) { text = text, name = name }; button.AddToClassList("sm-action"); actions.Add(button);
            }
            Control("축소", "wr-zoom-out", () => map?.ZoomAt(map.contentRect.center, 1 / 1.2f));
            Control("확대", "wr-zoom-in", () => map?.ZoomAt(map.contentRect.center, 1.2f));
            Control("전체 보기", "wr-fit", () => map?.Fit());

            void Select(string id)
            {
                selected = id; map?.SetSelection(id);
                foreach (var card in mapHost.Query<VisualElement>(className: "wr-neutral-node").ToList())
                    card.EnableInClassList("wr-selected", card.name == "wr-node-" + id);
                detail.Clear(); detail.scrollOffset = Vector2.zero;
                var relation = ordered.FirstOrDefault(x => x.Wrestler.Id == id);
                if (relation == null) return;
                var summary = new VisualElement(); summary.AddToClassList("wr-pair-summary"); detail.Add(summary);
                summary.Add(new Label(relation.Affinity > 0 ? "우호" : relation.Affinity < 0 ? "적대" : "중립") { name = "wr-pair-status" });
                summary.Add(new Label($"공동 경기 {relation.History.Count(x => x.Incident == StoryIncident.Competition)}회") { name = "wr-pair-matches" });
                foreach (var group in relation.Groups) summary.Add(WrestlerNameText.Create(group, save.Wrestlers));
                foreach (var story in relation.Stories.Where(x => !StorylineService.IncludesBothParties(save, x, new[] { wrestlerId, id })))
                    summary.Add(WrestlerNameText.Create(StorylineHistory.Title(save, story), save.Wrestlers));
                var history = new VisualElement { name = "wr-pair-history" }; detail.Add(history);
                StoryManagementView.PairHistory(history, save, wrestlerId, id);
            }

            void Render()
            {
                mapHost.Clear(); detail.Clear();
                var query = search.value?.Trim() ?? "";
                var visible = ordered.Where(x =>
                    (WrestlerNameText.DisplayName(x.Wrestler) + " " + x.Wrestler.Identity?.LegalName)
                        .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 && (filter.index switch {
                            1 => x.Affinity > 0, 2 => x.Affinity < 0, 3 => x.Affinity == 0, _ => true })).ToList();
                if (!visible.Any(x => x.Wrestler.Id == selected)) selected = visible.FirstOrDefault()?.Wrestler.Id;
                map = new WrestlerRelationshipMapCanvas(center, visible.Where(x => x.Affinity != 0 || x.Groups.Count > 0).ToList(), Select);
                mapHost.Add(map);
                var shelf = new VisualElement { name = "wr-neutral-shelf" }; shelf.AddToClassList("wr-neutral-shelf"); mapHost.Add(shelf);
                var neutralTitle = new Label("중립"); neutralTitle.AddToClassList("wr-neutral-heading"); shelf.Add(neutralTitle);
                var neutralList = new ScrollView(ScrollViewMode.Horizontal) { name = "wr-neutral-players" };
                neutralList.AddToClassList("wr-neutral-list"); shelf.Add(neutralList);
                var neutral = visible.Where(x => x.Affinity == 0 && x.Groups.Count == 0).ToList();
                foreach (var relation in neutral)
                {
                    var card = WrestlerRelationshipMapCanvas.CreateCard(relation.Wrestler, false, 0, () => Select(relation.Wrestler.Id));
                    card.AddToClassList("wr-neutral-node"); card.style.position = Position.Relative; neutralList.Add(card);
                }
                if (neutral.Count == 0) neutralList.Add(new Label("표시할 중립 선수가 없습니다."));
                if (visible.Count == 0)
                {
                    var empty = new Label(ordered.Count == 0 ? "표시할 선수가 없습니다." : "조건에 맞는 관계가 없습니다.");
                    empty.AddToClassList("wr-map-empty"); map.Add(empty);
                    detail.Add(new Label(ordered.Count == 0 ? "표시할 관계가 없습니다." : "검색어 또는 관계 필터를 바꾸면 다른 상대를 볼 수 있습니다."));
                    return;
                }
                Select(selected);
            }
            search.RegisterValueChangedCallback(_ => Render()); filter.RegisterValueChangedCallback(_ => Render());
            root.RegisterCallback<GeometryChangedEvent>(e => root.EnableInClassList("wr-narrow", e.newRect.width < 760));
            Render();
            return root;
        }
    }
}
