using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class LockerRoomView
    {
        private readonly VisualElement root = new() { name = "locker-room" };
        private readonly VisualElement board = new() { name = "locker-room-board" };
        private readonly ScrollView detail = new() { name = "locker-room-detail" };
        private readonly DropdownField filter = new("상세 관계", new List<string> { "교류 전체", "친분·갈등" }, 0);
        private GameSave save;
        private List<WrestlerState> people;
        private List<LockerRoomRelationshipState> relations;
        private string selectedPerson, first, second;

        public static VisualElement Open(VisualElement host)
        {
            host.Q("locker-room")?.RemoveFromHierarchy();
            var view = new LockerRoomView(); host.Add(view.root); return view.root;
        }

        private LockerRoomView()
        {
            root.AddToClassList("sm-screen");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StorylineMap"));
            var header = new VisualElement(); header.AddToClassList("sm-header"); root.Add(header);
            var title = new Label("라커룸"); title.AddToClassList("sm-title"); header.Add(title);
            filter.name = "locker-room-filter"; filter.AddToClassList("sm-relations-filter"); header.Add(filter);
            var body = new VisualElement(); body.AddToClassList("sm-body"); root.Add(body);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "locker-room-scroll" };
            scroll.AddToClassList("sm-canvas-host"); body.Add(scroll); scroll.Add(board);
            detail.AddToClassList("sm-detail"); body.Add(detail);
            filter.RegisterValueChangedCallback(_ => ShowDetail());
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                selectedPerson = first = second = null; ShowDetail(); e.StopPropagation();
            });
            root.RegisterCallback<GeometryChangedEvent>(e => root.EnableInClassList("sm-narrow", e.newRect.width < 760));
            root.RegisterCallback<AttachToPanelEvent>(_ => DashboardSession.SaveChanged += Render);
            root.RegisterCallback<DetachFromPanelEvent>(_ => DashboardSession.SaveChanged -= Render);
            Render(DashboardSession.ActiveSave);
        }

        private void Render(GameSave value)
        {
            save = value; board.Clear(); detail.Clear();
            people = (save?.Wrestlers ?? new()).Where(w => w != null && w.PromotionId == save?.Promotion?.Id &&
                w.Roster.ActivityState != RosterActivityState.Released && save.IsWrestlerDiscovered(w.Id)).OrderBy(w => w.Identity.RingName).ToList();
            var ids = people.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
            relations = (save?.LockerRoomRelationships ?? new()).Where(r => ids.Contains(r.FirstWrestlerId) && ids.Contains(r.SecondWrestlerId)).ToList();
            if (people.Count == 0) { Copy(board, "표시할 소속 선수가 없습니다."); return; }
            if (!ids.Contains(selectedPerson ?? "")) selectedPerson = null;
            if (!relations.Any(r => r.FirstWrestlerId == first && r.SecondWrestlerId == second && Visible(r)))
            {
                var closest = selectedPerson == null ? relations.Where(Visible).OrderByDescending(r => Math.Abs(r.Affinity)).FirstOrDefault() : null;
                first = closest?.FirstWrestlerId; second = closest?.SecondWrestlerId;
            }
            var remaining = new HashSet<string>(ids, StringComparer.Ordinal);
            var friends = relations.Where(r => r.Affinity >= 15)
                .SelectMany(r => new[] { (Id: r.FirstWrestlerId, Peer: r.SecondWrestlerId), (Id: r.SecondWrestlerId, Peer: r.FirstWrestlerId) })
                .ToLookup(pair => pair.Id, pair => pair.Peer, StringComparer.Ordinal);
            var groups = new List<List<string>>();
            foreach (var person in people)
            {
                if (!remaining.Remove(person.Id)) continue;
                var group = new List<string> { person.Id };
                for (var i = 0; i < group.Count; i++)
                    foreach (var peer in friends[group[i]])
                        if (remaining.Remove(peer)) group.Add(peer);
                groups.Add(group);
            }
            var independent = groups.Where(g => g.Count == 1).SelectMany(g => g).ToList();
            var grouped = groups.Where(g => g.Count > 1).OrderByDescending(g => g.Count).ToList();
            if (grouped.Count > 0)
            {
                var groupList = new VisualElement(); groupList.AddToClassList("locker-room-groups"); board.Add(groupList);
                for (var i = 0; i < grouped.Count; i++)
                    AddGroup(groupList, $"locker-room-group-{i + 1}", $"친분 그룹 {i + 1}", grouped[i]);
            }
            if (independent.Count > 0) AddGroup(board, "locker-room-ungrouped", "그 외 선수", independent);
            ShowDetail();
        }

        private void AddGroup(VisualElement host, string id, string title, List<string> members)
        {
            var group = new VisualElement { name = id }; group.AddToClassList("locker-room-group"); host.Add(group);
            Heading(group, $"{title} · {members.Count}명");
            var cards = new VisualElement(); cards.AddToClassList("locker-room-members"); group.Add(cards);
            var ids = members.ToHashSet(StringComparer.Ordinal);
            foreach (var w in people.Where(w => ids.Contains(w.Id)))
            {
                var card = new VisualElement { name = "locker-room-card-" + w.Id }; card.AddToClassList("sm-wrestler");
                var portrait = new Button(() =>
                {
                    selectedPerson = selectedPerson == w.Id ? null : w.Id;
                    first = second = null;
                    ShowDetail();
                }) { name = "locker-room-select-" + w.Id, tooltip = "동료와의 관계 보기" };
                portrait.AddToClassList("sm-portrait");
                var frame = WrestlerProfileController.CreatePortraitDisplayWithNameLink(w).Q<VisualElement>(className: "wrestler-portrait-frame");
                frame.RemoveFromHierarchy();
                portrait.Add(frame); card.Add(portrait); card.Add(WrestlerProfileController.CreateLink(w, "sm-name"));
                cards.Add(card);
            }
        }

        private bool Visible(LockerRoomRelationshipState r) => (filter.index == 0 || r.Affinity >= 15 || r.Affinity < -10) &&
            (selectedPerson == null || r.FirstWrestlerId == selectedPerson || r.SecondWrestlerId == selectedPerson);

        private void Select(LockerRoomRelationshipState r)
        { first = r.FirstWrestlerId; second = r.SecondWrestlerId; ShowDetail(); }

        private void ShowDetail()
        {
            detail.Clear(); detail.scrollOffset = Vector2.zero;
            var visibleRelations = relations.Where(Visible).OrderByDescending(r => Math.Abs(r.Affinity)).ToList();
            var selected = visibleRelations.FirstOrDefault(r => r.FirstWrestlerId == first && r.SecondWrestlerId == second);
            foreach (var card in board.Query<VisualElement>(className: "sm-wrestler").ToList())
                card.EnableInClassList("sm-selected", card.name == "locker-room-card-" + selectedPerson ||
                    selected != null && (card.name == "locker-room-card-" + first || card.name == "locker-room-card-" + second));
            if (selectedPerson != null) detail.Add(WrestlerProfileController.CreateLink(people.Single(w => w.Id == selectedPerson), "sm-detail-name"));
            if (selected != null)
            {
                foreach (var id in new[] { first, second }.Where(id => id != selectedPerson))
                    detail.Add(WrestlerProfileController.CreateLink(people.Single(w => w.Id == id), "sm-detail-link"));
                Copy(detail, $"{Strength(selected.Affinity)} · 친밀도 {selected.Affinity:+0.0;-0.0;0} · 교류 {selected.Interactions}회");
                Heading(detail, "최근 함께한 일");
                var events = save.LockerRoomEvents.Where(e => e.PromotionId == save.Promotion.Id &&
                    ((e.ActorId == first && e.PartnerId == second) || (e.ActorId == second && e.PartnerId == first))).TakeLast(20).Reverse().ToList();
                foreach (var e in events)
                {
                    Copy(detail, $"{DashboardText.Date(e.Date)} · {(e.WorkKind == LockerRoomWorkKind.Training ? "훈련" : save.Shows.FirstOrDefault(s => s.Id == e.ShowId)?.Name ?? "쇼")}");
                    Copy(detail, e.Description);
                    foreach (var id in new[] { e.ActorId, e.PartnerId, e.MediatorId }.Where(id => !string.IsNullOrEmpty(id)).Distinct())
                    {
                        var member = people.FirstOrDefault(w => w.Id == id);
                        if (member != null) detail.Add(WrestlerProfileController.CreateLink(member, "sm-detail-link"));
                    }
                    if (!string.IsNullOrEmpty(e.OriginBehaviorEventId))
                    {
                        var origin = save.WrestlerBehaviors.Where(b => b.PromotionId == save.Promotion.Id).SelectMany(b => b.Events).FirstOrDefault(b => b.Id == e.OriginBehaviorEventId);
                        if (origin != null) Copy(detail, "관련 면담 · " + origin.Description);
                    }
                }
                if (events.Count == 0) Copy(detail, "남아 있는 교류 기록이 없습니다.");
            }
            Heading(detail, selectedPerson == null ? "관계 선택" : "동료와의 관계");
            foreach (var r in visibleRelations)
            {
                var row = new Button(() => Select(r)); row.name = "locker-room-pair-" + r.FirstWrestlerId + "-" + r.SecondWrestlerId; row.AddToClassList("sm-story-link");
                var a = people.Single(w => w.Id == r.FirstWrestlerId); var b = people.Single(w => w.Id == r.SecondWrestlerId);
                row.Add(WrestlerNameText.Create($"{a.Identity.RingName} · {b.Identity.RingName}", people));
                row.Add(new Label($"{Strength(r.Affinity)} · {r.Affinity:+0.0;-0.0;0}")); detail.Add(row);
            }
            if (visibleRelations.Count == 0) Copy(detail, filter.index == 0 ? "아직 기록된 교류가 없습니다." : "아직 친분이나 갈등으로 발전한 관계가 없습니다.");
        }

        private static string Strength(float value) => value >= 15 ? "친분" : value < -10 ? "갈등" : "교류 중";
        private static void Heading(VisualElement host, string text) { var label = new Label(text); label.AddToClassList("sm-detail-heading"); host.Add(label); }
        private void Copy(VisualElement host, string text) { var label = WrestlerNameText.Create(text, people); label.AddToClassList("sm-copy"); host.Add(label); }
    }
}
