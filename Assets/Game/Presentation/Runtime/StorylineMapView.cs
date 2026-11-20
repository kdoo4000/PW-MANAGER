using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class StorylineMapView
    {
        private readonly VisualElement root = new() { name = "storyline-map" };
        private readonly VisualElement body = new();
        private readonly VisualElement canvasHost = new();
        private readonly ScrollView detail = new() { name = "story-map-detail" };
        private readonly DropdownField stories = new() { name = "story-map-stories", label = "표시" };
        private readonly Action manage;
        private readonly DropdownField relationFilter = new("전개", new List<string> { "모든 전개", "도전 기록", "공격·배신 기록", "협력 전개 기록" }, 0) { name = "story-map-relations" };
        private StorylineMapData data;
        private StorylineMapCanvas canvas;
        private string storyId;

        public static VisualElement Open(VisualElement host, Action manage)
        {
            host.Q("storyline-map")?.RemoveFromHierarchy();
            var view = new StorylineMapView(manage);
            host.Add(view.root);
            return view.root;
        }

        private StorylineMapView(Action manage)
        {
            this.manage = manage;
            root.AddToClassList("sm-screen");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StorylineMap"));
            var header = new VisualElement(); header.AddToClassList("sm-header"); root.Add(header);
            var title = new Label("스토리라인"); title.AddToClassList("sm-title"); header.Add(title);
            stories.AddToClassList("sm-stories"); header.Add(stories);
            root.Add(StoryManagementView.Tabs(false, () => { }, this.manage));
            var toolbar = new VisualElement(); toolbar.AddToClassList("sm-toolbar"); root.Add(toolbar);
            relationFilter.AddToClassList("sm-relations-filter"); toolbar.Add(relationFilter);
            relationFilter.RegisterValueChangedCallback(_ => canvas?.SetIncidentFilter(relationFilter.index));
            var legend = new VisualElement(); legend.AddToClassList("sm-alignment-legend"); toolbar.Add(legend);
            foreach (var (text, alignment) in new[] { ("페이스", "face"), ("힐", "heel"), ("트위너", "tweener") })
            {
                var label = new Label(text); label.AddToClassList("sm-alignment-key"); label.AddToClassList("sm-" + alignment); legend.Add(label);
            }
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; toolbar.Add(spacer);
            toolbar.Add(ActionButton("축소", "story-map-zoom-out", () => canvas?.ZoomAt(canvas.contentRect.center, 1 / 1.2f)));
            toolbar.Add(ActionButton("확대", "story-map-zoom-in", () => canvas?.ZoomAt(canvas.contentRect.center, 1.2f)));
            toolbar.Add(ActionButton("전체 보기", "story-map-fit", () => canvas?.Fit()));
            body.AddToClassList("sm-body"); root.Add(body);
            canvasHost.AddToClassList("sm-canvas-host"); body.Add(canvasHost);
            detail.AddToClassList("sm-detail"); body.Add(detail);
            stories.RegisterValueChangedCallback(_ =>
            {
                var index = stories.index - 1;
                storyId = index >= 0 && index < data.Clusters.Count ? data.Clusters[index].Story.Id : null;
                canvas?.SetStory(storyId);
                if (index >= 0 && index < data.Clusters.Count) ShowDetail(data.Clusters[index], null, null);
                else ClearDetail();
            });
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                canvas?.ClearSelection(); evt.StopPropagation(); root.focusController?.IgnoreEvent(evt);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                root.EnableInClassList("sm-compact", evt.newRect.width < 1050);
                root.EnableInClassList("sm-narrow", evt.newRect.width < 760);
            });
            root.RegisterCallback<AttachToPanelEvent>(_ => DashboardSession.SaveChanged += Render);
            root.RegisterCallback<DetachFromPanelEvent>(_ => DashboardSession.SaveChanged -= Render);
            Render(DashboardSession.ActiveSave);
        }

        private static Button ActionButton(string text, string name, Action action)
        {
            var button = new Button(action) { text = text, name = name };
            button.AddToClassList("sm-action"); return button;
        }

        private void Render(GameSave save)
        {
            root.SetEnabled(!DashboardSession.IsShowRunning);
            canvasHost.Clear(); detail.Clear(); canvas = null;
            if (save?.Promotion == null)
            {
                stories.SetEnabled(false);
                Message(canvasHost, "불러온 게임이 없습니다.");
                return;
            }
            data = StorylineMapData.Build(save);
            var choices = new List<string> { "전체 선수" };
            choices.AddRange(data.Clusters.Select((x, i) => $"{i + 1}. {StorylineHistory.Title(save, x.Story)}"));
            stories.choices = choices;
            if (!data.Clusters.Any(x => x.Story.Id == storyId)) storyId = null;
            stories.SetValueWithoutNotify(choices[storyId == null ? 0 : data.Clusters.FindIndex(x => x.Story.Id == storyId) + 1]);
            stories.SetEnabled(data.Clusters.Count > 0);
            relationFilter.SetEnabled(data.Clusters.Count > 0);
            foreach (var name in new[] { "story-map-zoom-out", "story-map-zoom-in", "story-map-fit" })
                root.Q<Button>(name).SetEnabled(data.Wrestlers.Count > 0);
            if (data.Wrestlers.Count == 0)
            {
                Message(canvasHost, "표시할 선수가 없습니다.");
                var emptyAction = ActionButton("스토리 관리 열기", "story-map-empty-manage", () => manage?.Invoke());
                emptyAction.SetEnabled(manage != null); canvasHost.Add(emptyAction);
            }
            else
            {
                canvas = new StorylineMapCanvas(data, (cluster, wrestler, relation) =>
                {
                    storyId = cluster?.Story.Id;
                    stories.SetValueWithoutNotify(choices[cluster == null ? 0 : data.Clusters.IndexOf(cluster) + 1]);
                    ShowDetail(cluster, wrestler, relation);
                }, ClearDetail);
                canvasHost.Add(canvas);
                canvas.SetIncidentFilter(relationFilter.index);
                canvas.SetStory(storyId);
            }
            var selected = data.Clusters.FirstOrDefault(x => x.Story.Id == storyId);
            if (selected != null) ShowDetail(selected, null, null); else ClearDetail();
        }

        private static void Heading(VisualElement parent, string text)
        {
            var label = new Label(text); label.AddToClassList("sm-detail-heading"); parent.Add(label);
        }

        private static void Copy(VisualElement parent, string text, IEnumerable<WrestlerState> wrestlers = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var label = WrestlerNameText.Create(text, wrestlers);
            label.AddToClassList("sm-copy"); parent.Add(label);
        }

        private static void Message(VisualElement parent, string text)
        {
            var label = new Label(text); label.AddToClassList("sm-empty"); parent.Add(label);
        }

        private void ClearDetail()
        {
            detail.Clear();
            Heading(detail, "관계 살펴보기");
            if (data.Clusters.Count == 0) Copy(detail, "진행 중인 스토리라인이 없습니다.");
            Copy(detail, "초상화나 관계선을 선택하면 여기에서 확인할 수 있습니다.");
            Copy(detail, "선수 이름을 누르면 선수 페이지로 이동합니다.");
            Copy(detail, "빈 공간 드래그로 이동 · 휠로 확대·축소 · Esc로 선택 해제");
        }

        private void ShowDetail(StorylineMapCluster cluster, WrestlerState wrestler, StorylineMapRelation relation)
        {
            if (relation?.IsStoryOpposition == true) relation = null;
            detail.Clear();
            detail.scrollOffset = Vector2.zero;
            if (wrestler != null)
            {
                detail.Add(WrestlerProfileController.CreateLink(wrestler, "sm-detail-name"));
                if (data.ChampionTitles.TryGetValue(wrestler.Id, out var titles))
                {
                    Heading(detail, "보유 챔피언십");
                    foreach (var title in titles) detail.Add(ChampionshipProfileView.CreateLink(title));
                }
                Heading(detail, "참여 중인 스토리라인");
                var participations = data.Clusters.Where(x => x.Wrestlers.Any(w => w.Id == wrestler.Id)).ToList();
                foreach (var participation in participations)
                    StoryButton(participation);
                if (participations.Count == 0) Copy(detail, "참여 중인 스토리라인이 없습니다.");
                var groups = data.Groups.Where(x => x.MemberIds.Contains(wrestler.Id)).ToList();
                if (groups.Count > 0)
                {
                    Heading(detail, "소속 그룹");
                    foreach (var group in groups) Copy(detail, group.Name, data.Wrestlers);
                }
                if (cluster == null) return;
                Heading(detail, "선택한 이야기");
                foreach (var edge in cluster.Relations.Where(x => x.FirstId == wrestler.Id || x.SecondId == wrestler.Id))
                {
                    var card = new VisualElement(); card.AddToClassList("sm-detail-section"); detail.Add(card);
                    Copy(card, edge.Label, cluster.Wrestlers);
                    Names(card, cluster, edge.FirstId, edge.SecondId);
                    Copy(card, edge.Background);
                }
                if (!cluster.Relations.Any(x => x.FirstId == wrestler.Id || x.SecondId == wrestler.Id))
                    Copy(detail, "이 이야기에서 확인되는 선수 간 관계가 없습니다.");
                return;
            }
            if (relation != null)
            {
                Heading(detail, relation.Label);
                Names(detail, cluster, relation.FirstId, relation.SecondId);
                if (relation.Directed) Copy(detail, "행동을 시작한 선수에서 대상 선수로 향하는 관계입니다.");
                Copy(detail, relation.Background);
                if (relation.Event != null)
                    Copy(detail, "발생 날짜 · " + DashboardText.Date(relation.Event.Date));
                Heading(detail, "스토리라인"); StoryButton(cluster);
                Heading(detail, "대립의 시작");
                Copy(detail, StorylineHistory.Origin(data.Save, cluster.Story), cluster.Wrestlers);
                Heading(detail, "두 선수의 전체 사건");
                var history = StorylineHistory.ForPair(data.Save, relation.FirstId, relation.SecondId);
                History(cluster, history);
                return;
            }
            var storyTitle = WrestlerNameText.Create(StorylineHistory.Title(data.Save, cluster.Story), cluster.Wrestlers);
            storyTitle.AddToClassList("sm-detail-heading"); detail.Add(storyTitle);
            Copy(detail, "시작 · " + DashboardText.Date(cluster.Story.StartedDate));
            Heading(detail, "대립의 시작");
            Copy(detail, StorylineHistory.Origin(data.Save, cluster.Story), cluster.Wrestlers);
            if (cluster.Story.PartyType != StoryPartyType.Wrestler)
            {
                Heading(detail, "참여 그룹");
                Copy(detail, cluster.FirstParty + " · " + cluster.SecondParty, cluster.Wrestlers);
            }
            Heading(detail, "참여 선수");
            foreach (var participant in cluster.Wrestlers) detail.Add(WrestlerProfileController.CreateLink(participant, "sm-detail-link"));
            Heading(detail, "연결된 챔피언십");
            if (cluster.Championships.Count == 0) Copy(detail, "이 이야기와 연결된 타이틀전 기록이 없습니다.");
            foreach (var title in cluster.Championships) detail.Add(ChampionshipProfileView.CreateLink(title));
            if (cluster.MissingParticipants) Copy(detail, "일부 참가자 또는 그룹 정보를 찾을 수 없습니다.");
            Heading(detail, "사건 기록");
            History(cluster, StorylineHistory.ForStory(data.Save, cluster.Story));
        }

        private void StoryButton(StorylineMapCluster cluster)
        {
            var button = new Button(() =>
            {
                storyId = cluster.Story.Id;
                stories.SetValueWithoutNotify(stories.choices[data.Clusters.IndexOf(cluster) + 1]);
                canvas?.SetStory(storyId); ShowDetail(cluster, null, null);
            });
            button.AddToClassList("sm-story-link");
            button.Add(WrestlerNameText.Create(StorylineHistory.Title(data.Save, cluster.Story), cluster.Wrestlers));
            detail.Add(button);
        }

        private static void Names(VisualElement parent, StorylineMapCluster cluster, string first, string second, bool directed = false)
        {
            var people = new VisualElement(); people.AddToClassList("sm-people"); parent.Add(people);
            foreach (var id in new[] { first, second })
            {
                var wrestler = cluster.Wrestlers.FirstOrDefault(x => x.Id == id);
                if (wrestler == null) continue;
                if (directed && people.childCount > 0)
                {
                    var direction = new Label("→"); direction.AddToClassList("sm-event-direction"); people.Add(direction);
                }
                people.Add(WrestlerProfileController.CreateLink(wrestler, "sm-detail-link"));
            }
        }

        private void History(StorylineMapCluster cluster, IEnumerable<StoryEventState> history)
        {
            var events = history.Where(x => x != null).OrderByDescending(x => x.Date.Year).ThenByDescending(x => x.Date.Month).ThenByDescending(x => x.Date.Day).ToList();
            if (events.Count == 0) { Copy(detail, "기록된 사건이 없습니다."); return; }
            foreach (var beat in events)
            {
                var row = new VisualElement(); row.AddToClassList("sm-detail-section"); detail.Add(row);
                Copy(row, DashboardText.Date(beat.Date) + " · " + StorylineMapData.IncidentName(beat.Incident));
                Names(row, cluster, beat.ActorId, beat.TargetId, true);
            }
        }
    }
}
