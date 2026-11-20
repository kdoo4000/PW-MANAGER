using System;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class StoryManagementView
    {
        public static void OpenGroups(VisualElement host) => WrestlerGroupsView.Open(host);

        internal static VisualElement Tabs(bool management, Action showMap, Action showManagement)
        {
            var tabs = new VisualElement { name = "story-tabs" };
            tabs.AddToClassList("ops-tabs");
            tabs.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StorylineTabs"));
            foreach (var (text, key, active, action) in new[] {
                ("관계도", "map", !management, showMap), ("목록", "management", management, showManagement) })
            {
                var tab = new Button(() => action?.Invoke()) { text = text, name = "story-tab-" + key };
                tab.AddToClassList("ops-tab"); tab.EnableInClassList("ops-active", active);
                tab.SetEnabled(active || action != null); tabs.Add(tab);
            }
            return tabs;
        }

        public static void Open(VisualElement host, string wrestlerId = null, bool groupsOnly = false, string storyId = null, Action showMap = null)
        {
            if (groupsOnly) { WrestlerGroupsView.Open(host); return; }
            host.Q("story-management")?.RemoveFromHierarchy();
            var save = DashboardSession.ActiveSave;
            if (save == null) return;
            var selectedStory = !groupsOnly && wrestlerId == null
                ? save.Storylines.FirstOrDefault(x => x.Id == storyId)
                    ?? save.Storylines.Where(x => x.Status == StorylineStatus.Active)
                        .OrderByDescending(x => StorylineHistory.ForStory(save, x).LastOrDefault()?.Date ?? x.StartedDate).FirstOrDefault()
                    ?? save.Storylines.FirstOrDefault() : null;
            var root = new VisualElement { name = "story-management" };
            root.AddToClassList("story-management");
            root.EnableInClassList("story-browser", wrestlerId == null);
            root.RegisterCallback<GeometryChangedEvent>(evt => root.EnableInClassList("story-browser-narrow", evt.newRect.width < 900));
            if (showMap != null) root.AddToClassList("story-tabbed");
            if (wrestlerId != null) root.AddToClassList("story-profile");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StoryManagement"));
            host.Add(root);
            var header = new VisualElement(); header.AddToClassList("story-header"); root.Add(header);
            var title = new Label(groupsOnly ? "태그팀 · 스테이블" : wrestlerId != null ? "선수 관계" : "스토리라인");
            title.AddToClassList("story-title"); header.Add(title);
            if (!groupsOnly && wrestlerId == null)
            {
                if (showMap != null)
                {
                    root.Add(Tabs(true, showMap, () => { }));
                }
                var count = new Label($"진행 중 {save.Storylines.Count(x => x.Status == StorylineStatus.Active)}");
                count.AddToClassList("story-count"); header.Add(count);
            }
            if (wrestlerId != null)
            {
                var close = new Button(() =>
                {
                    root.RemoveFromHierarchy();
                    var overview = host.Q<VisualElement>(className: "wp-scroll");
                    if (overview != null) overview.style.display = DisplayStyle.Flex;
                    host.Q<Button>("wp-open-relationships")?.EnableInClassList("active", false);
                    host.Q<Button>("wp-open-overview")?.EnableInClassList("active", true);
                }) { text = "개요로 돌아가기" };
                close.AddToClassList("story-close"); header.Add(close);
            }
            var body = new VisualElement(); body.AddToClassList("story-body"); root.Add(body);
            var main = new ScrollView { name = "story-list" }; main.AddToClassList("story-main"); body.Add(main);
            var side = new ScrollView { name = "story-detail" }; side.AddToClassList("story-side");
            if (wrestlerId == null) body.Add(side);
            else main.AddToClassList("story-main-full");
            var storyList = new VisualElement();
            var listRows = new VisualElement();
            var groupList = new VisualElement();
            var stableList = new VisualElement();
            var relationList = new VisualElement();
            if (groupsOnly)
            {
                var teamHeading = new Label("태그팀 · 그룹 결성"); teamHeading.AddToClassList("story-section-heading"); main.Add(teamHeading);
                main.Add(groupList);
                var stableHeading = new Label("스테이블"); stableHeading.AddToClassList("story-section-heading"); side.Add(stableHeading);
                side.Add(stableList);
            }
            else if (wrestlerId != null)
            {
                var relationHeading = new Label("선수 관계"); relationHeading.AddToClassList("story-section-heading"); main.Add(relationHeading);
                main.Add(relationList);
            }
            else
            {
                var storyHeading = new Label("스토리라인 목록"); storyHeading.AddToClassList("story-section-heading"); main.Add(storyHeading);
                main.Add(listRows); side.Add(storyList);

            }

            var error = new Label { name = "story-error" }; error.AddToClassList("story-error"); root.Add(error);
            void Names(VisualElement parent, System.Collections.Generic.IEnumerable<string> ids)
            {
                var people = new VisualElement(); people.AddToClassList("story-people");
                foreach (var id in ids.Distinct())
                {
                    var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == id);
                    if (wrestler == null) continue;
                    var link = WrestlerProfileController.CreateLink(wrestler);
                    link.clicked += () => root.RemoveFromHierarchy(); people.Add(link);
                }
                if (people.childCount > 0) parent.Add(people);
            }
            VisualElement Card(string heading, VisualElement parent)
            {
                var card = new VisualElement(); card.AddToClassList("story-card"); parent.Add(card);
                var label = new Label(heading); label.AddToClassList("story-heading"); card.Add(label); return card;
            }
            void Poster(StorylineState story, VisualElement parent)
            {
                var poster = new VisualElement(); poster.AddToClassList("story-poster"); parent.Add(poster);
                var stage = new VisualElement(); stage.AddToClassList("story-poster-stage"); poster.Add(stage);
                void Party(bool first)
                {
                    var partySide = new VisualElement(); partySide.AddToClassList("story-poster-party"); stage.Add(partySide);
                    if (story.PartyType != StoryPartyType.Wrestler)
                    {
                        partySide.AddToClassList("story-poster-group-side");
                        var groupId = first ? story.FirstGroupId : story.SecondGroupId;
                        var groupName = story.PartyType == StoryPartyType.TagTeam
                            ? save.TagTeams.FirstOrDefault(x => x.Id == groupId)?.Name
                            : save.Stables.FirstOrDefault(x => x.Id == groupId)?.Name;
                        var group = new Label(groupName ?? "그룹"); group.AddToClassList("story-poster-group"); partySide.Add(group);
                    }
                    var members = StorylineService.PartyMembers(save, story, first);
                    if (story.PartyType != StoryPartyType.Wrestler)
                    {
                        var leader = story.PartyType == StoryPartyType.Stable ? save.Stables.FirstOrDefault(s => s.Id == (first ? story.FirstGroupId : story.SecondGroupId))?.LeaderId : null;
                        var groupPicture = new GroupPortraitView(members.Select(id => save.Wrestlers.FirstOrDefault(w => w.Id == id)), leaderId: leader);
                        groupPicture.AddToClassList("story-poster-group-portrait"); partySide.Add(groupPicture); return;
                    }
                    var portraits = new VisualElement(); portraits.AddToClassList("story-poster-portraits"); partySide.Add(portraits);
                    foreach (var id in members.Take(3))
                    {
                        var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == id);
                        if (wrestler == null) continue;
                        portraits.Add(WrestlerProfileController.CreatePortraitLink(wrestler, "story-portrait"));
                    }
                    if (members.Count > 3) partySide.Add(new Label($"외 {members.Count - 3}명"));
                }
                Party(true);
                var center = new VisualElement(); center.AddToClassList("story-poster-center"); stage.Add(center);
                var kind = new Label(story.IsAutomatic ? "선수 스토리" : story.PartyType == StoryPartyType.Wrestler ? "개인 대립" : story.PartyType == StoryPartyType.TagTeam ? "태그팀 대립" : "스테이블 대립");
                kind.AddToClassList("story-poster-kind"); center.Add(kind);
                var posterTitle = WrestlerNameText.Create(StorylineHistory.Title(save, story), save.Wrestlers); posterTitle.AddToClassList("story-poster-title"); center.Add(posterTitle);
                var latest = StorylineHistory.ForStory(save, story).LastOrDefault();
                if (latest != null)
                {
                    var latestText = new Label($"최근 전개 · {Incident(latest.Incident)}");
                    latestText.AddToClassList("story-poster-latest"); center.Add(latestText);
                }
                Party(false);
                var footer = new VisualElement(); footer.AddToClassList("story-poster-footer"); poster.Add(footer);
                var reaction = new VisualElement(); reaction.AddToClassList("story-reaction"); footer.Add(reaction);
                var recordedChanges = save.ShowResults.Where(x => StorylineHistory.IsConfirmed(save, x))
                    .SelectMany(x => x.StoryChanges ?? new()).Where(x => x.TargetId == story.Id && x.ValueKey == "관심도")
                    .TakeLast(3).ToList();
                var trend = recordedChanges.Count > 0 ? recordedChanges.Sum(x => x.Amount) :
                    StorylineHistory.ForStory(save, story).TakeLast(3).Sum(x => x.InterestChange);
                var change = trend > 0 ? $"▲ +{trend:0}" : trend < 0 ? $"▼ {trend:0}" : "—";
                var reactionLabel = new Label($"관중 반응 {story.Interest:0}  {change}");
                reactionLabel.AddToClassList("story-reaction-label"); reaction.Add(reactionLabel);
                var track = new VisualElement(); track.AddToClassList("story-reaction-track"); reaction.Add(track);
                var fill = new VisualElement(); fill.AddToClassList("story-reaction-fill");
                fill.style.width = Length.Percent(Mathf.Clamp(story.Interest, 0, 100)); track.Add(fill);
                var tension = new Label($"긴장도 {story.Tension:0}"); tension.AddToClassList("story-poster-tension"); footer.Add(tension);
                if (story.Fatigue >= 65)
                {
                    var fatigue = new Label($"피로도 {story.Fatigue:0}"); fatigue.AddToClassList("story-poster-fatigue"); footer.Add(fatigue);
                }
            }
            void RenderSelected()
            {
                storyList.Clear(); side.scrollOffset = Vector2.zero;
                foreach (var row in main.Query<Button>(className: "story-list-row").ToList())
                    row.EnableInClassList("story-selected", row.name == "story-select-" + selectedStory?.Id);
                if (selectedStory == null)
                {
                    var empty = new Label("표시할 스토리라인이 없습니다."); empty.AddToClassList("story-empty"); storyList.Add(empty); return;
                }
                Poster(selectedStory, storyList);
                var idleDays = StorylineService.DaysWithoutProgress(save, selectedStory);
                var stateText = new Label($"{(selectedStory.Status == StorylineStatus.Inactive ? "휴지 중" : selectedStory.Status == StorylineStatus.Completed ? "종결" : idleDays >= 28 ? "28일 이상 전개 없음" : "진행 중")} · 마지막 전개 이후 {idleDays}일");
                stateText.AddToClassList("story-list-meta"); storyList.Add(stateText);
                if (selectedStory.Status != StorylineStatus.Completed)
                {
                    var actions = new VisualElement(); actions.AddToClassList("story-people"); storyList.Add(actions);
                    void Change(StorylineStatus status)
                    {
                        var before = JsonUtility.ToJson(save);
                        try
                        {
                            StorylineService.ChangeStatus(save, selectedStory.Id, status); DashboardSession.PersistActive();
                            Open(host, storyId: selectedStory.Id, showMap: showMap);
                        }
                        catch (System.Exception exception) { JsonUtility.FromJsonOverwrite(before, save); error.text = exception.Message; }
                    }
                    var paused = selectedStory.Status == StorylineStatus.Inactive;
                    actions.Add(new Button(() => Change(paused ? StorylineStatus.Active : StorylineStatus.Inactive))
                        { name = "story-toggle-hiatus", text = paused ? "재개" : "휴지" });
                    actions.Add(new Button(() => Change(StorylineStatus.Completed)) { name = "story-complete", text = "종결" });
                    actions.SetEnabled(!DashboardSession.IsShowRunning);
                }
                var origin = Card("대립의 시작", storyList);
                origin.Add(WrestlerNameText.Create(StorylineHistory.Origin(save, selectedStory), save.Wrestlers));
                var timelineHeading = new Label("전개 기록"); timelineHeading.AddToClassList("story-section-heading"); storyList.Add(timelineHeading);
                if (StorylineHistory.ForStory(save, selectedStory).Count == 0)
                {
                    var empty = new Label("아직 진행된 사건이 없습니다."); empty.AddToClassList("story-empty"); storyList.Add(empty);
                }
                foreach (var beat in StorylineHistory.ForStory(save, selectedStory).AsEnumerable().Reverse())
                {
                    var eventRow = new VisualElement(); eventRow.AddToClassList("story-event"); storyList.Add(eventRow);
                    var eventTitle = new Label($"{(beat.EventType == StoryEventType.Match ? "경기" : "프로모")} · {Incident(beat.Incident)}");
                    eventTitle.AddToClassList("story-event-title"); eventRow.Add(eventTitle);
                    var eventDate = new Label($"{beat.Date.Year:D4}.{beat.Date.Month:D2}.{beat.Date.Day:D2}  관중 반응 {beat.InterestChange:+0;-0;0}");
                    eventDate.AddToClassList("story-event-date"); eventRow.Add(eventDate);
                    Names(eventRow, new[] { beat.ActorId, beat.TargetId });
                }
            }
            void ListRow(StorylineState story, VisualElement parent)
            {
                var row = new Button(() => { selectedStory = story; RenderSelected(); }) { name = "story-select-" + story.Id };
                row.AddToClassList("story-list-row"); parent.Add(row);
                var name = WrestlerNameText.Create(StorylineHistory.Title(save, story), save.Wrestlers); name.AddToClassList("story-list-name"); row.Add(name);
                var status = story.Status == StorylineStatus.Active ? (StorylineService.DaysWithoutProgress(save, story) >= 28 ? "전개 없음" : "진행 중") : story.Status == StorylineStatus.Completed ? "종결" : "휴지 중";
                var meta = new Label($"{status} · 관심 {story.Interest:0} · 긴장도 {story.Tension:0}"); meta.AddToClassList("story-list-meta"); row.Add(meta);
            }
            if (wrestlerId == null)
            {
                var activeStories = save.Storylines.Where(x => x.Status == StorylineStatus.Active).ToList();
                foreach (var story in activeStories.Where(s => StorylineService.DaysWithoutProgress(save, s) < 28)) ListRow(story, listRows);
                var dormant = activeStories.Where(s => StorylineService.DaysWithoutProgress(save, s) >= 28).ToList();
                if (dormant.Count > 0)
                {
                    var heading = new Label($"28일 이상 전개 없음 · {dormant.Count}개"); heading.AddToClassList("story-section-heading"); listRows.Add(heading);
                    foreach (var story in dormant) ListRow(story, listRows);
                }
                if (activeStories.Count == 0)
                {
                    var empty = new Label("진행 중인 스토리라인이 없습니다."); empty.AddToClassList("story-empty"); listRows.Add(empty);
                }
                var completed = save.Storylines.Where(x => x.Status != StorylineStatus.Active).ToList();
                if (completed.Count > 0)
                {
                    var archive = new Foldout { text = $"지난 스토리 {completed.Count}", value = selectedStory?.Status != StorylineStatus.Active };
                    archive.AddToClassList("story-archive"); listRows.Add(archive);
                    foreach (var story in completed) ListRow(story, archive);
                }
                RenderSelected();
            }
            if (wrestlerId != null) RelationshipHistory(relationList, save, wrestlerId);
        }

        private static void RelationshipHistory(VisualElement parent, GameSave save, string fixedFirst = null)
        {
            var heading = new Label("관계 이력"); heading.AddToClassList("story-section-heading"); parent.Add(heading);
            var wrestlers = save.Wrestlers.Where(x => x != null && save.IsWrestlerDiscovered(x.Id)).ToList();
            if (wrestlers.Count < 2) { parent.Add(new Label("이력을 조회할 두 선수가 필요합니다.")); return; }
            var names = wrestlers.Select((x, i) => $"{i + 1}. {x.Identity.RingName}").ToList();
            var first = new DropdownField("선수", names, Math.Max(0, wrestlers.FindIndex(x => x.Id == fixedFirst))) { name = "history-first" };
            first.SetEnabled(fixedFirst == null); parent.Add(first);
            var second = new DropdownField("상대", names, first.index == 0 ? 1 : 0) { name = "history-second" }; parent.Add(second);
            var content = new VisualElement { name = "relationship-history" }; parent.Add(content);
            void Refresh() => PairHistory(content, save, wrestlers[first.index].Id, wrestlers[second.index].Id);
            first.RegisterValueChangedCallback(_ => Refresh()); second.RegisterValueChangedCallback(_ => Refresh()); Refresh();
        }

        internal static void PairHistory(VisualElement content, GameSave save, string firstId, string secondId)
        {
            content.Clear();
            var a = save.Wrestlers.FirstOrDefault(x => x.Id == firstId);
            var b = save.Wrestlers.FirstOrDefault(x => x.Id == secondId);
            if (a == null || b == null) { content.Add(new Label("선수 기록을 찾을 수 없습니다.")); return; }
            if (a.Id == b.Id) { content.Add(new Label("서로 다른 선수를 선택하세요.")); return; }
            var people = new VisualElement(); people.AddToClassList("story-people"); content.Add(people);
            people.Add(WrestlerProfileController.CreateLink(a)); people.Add(WrestlerProfileController.CreateLink(b));
            var value = save.StoryRelationships.FirstOrDefault(x => x.FirstWrestlerId == a.Id && x.SecondWrestlerId == b.Id ||
                x.FirstWrestlerId == b.Id && x.SecondWrestlerId == a.Id)?.Value ?? 0;
            content.Add(new Label($"우호도 {value:+0;-0;0}"));
            var stories = save.Storylines.Where(x => x.Status == StorylineStatus.Active && StorylineService.CanProgress(save, x) &&
                StorylineService.IncludesBothParties(save, x, new[] { firstId, secondId })).ToList();
            if (stories.Count > 0)
            {
                var heading = new Label("진행 중인 스토리"); heading.AddToClassList("story-section-heading"); content.Add(heading);
                foreach (var story in stories) content.Add(WrestlerNameText.Create(StorylineHistory.Title(save, story), save.Wrestlers));
            }
            var events = StorylineHistory.ForPair(save, a.Id, b.Id).AsEnumerable().Reverse().ToList();
            if (events.Count == 0) { content.Add(new Label("함께한 경기나 사건이 아직 없습니다.")); return; }
            var position = 0;
            var more = new Button { text = "이전 이력 더 보기" };
            void Append()
            {
                more.RemoveFromHierarchy();
                foreach (var beat in events.Skip(position).Take(30))
                {
                    var row = new VisualElement(); row.AddToClassList("story-event"); content.Add(row);
                    row.Add(new Label($"{beat.Date.Year:D4}.{beat.Date.Month:D2}.{beat.Date.Day:D2}"));
                    row.Add(WrestlerNameText.Create(StorylineHistory.Describe(save, beat), save.Wrestlers));
                    if (beat.Incident == StoryIncident.Competition)
                    {
                        var match = save.MatchResults.FirstOrDefault(x => x.Id == beat.SourceResultId);
                        var side = match?.ParticipantProfiles?.FirstOrDefault(x => x.MemberIds.Contains(a.Id));
                        if (side != null) row.Add(new Label(side.MemberIds.Contains(b.Id) ? "같은 편으로 출전" : "상대편으로 출전"));
                    }
                }
                position += 30;
                if (position < events.Count) content.Add(more);
            }
            more.clicked += Append; Append();
        }

        private static string Incident(StoryIncident incident) => incident switch
        {
            StoryIncident.Competition => "경쟁", StoryIncident.Challenge => "도전", StoryIncident.Taunt => "도발",
            StoryIncident.Attack => "공격", StoryIncident.Betrayal => "배신", StoryIncident.Rescue => "구출",
            StoryIncident.Respect => "존중", StoryIncident.Reconciliation => "화해", _ => "진행"
        };
    }
}
