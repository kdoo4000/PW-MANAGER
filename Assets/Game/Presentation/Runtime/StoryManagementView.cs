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
        public static void OpenGroups(VisualElement host) => Open(host, null, true);

        public static void Open(VisualElement host, string wrestlerId = null, bool groupsOnly = false, string storyId = null)
        {
            host.Q("story-management")?.RemoveFromHierarchy();
            var save = DashboardSession.ActiveSave;
            if (save == null) return;
            var selectedStory = !groupsOnly && wrestlerId == null
                ? save.Storylines.FirstOrDefault(x => x.Id == storyId) : null;
            var root = new VisualElement { name = "story-management" };
            root.AddToClassList("story-management");
            if (wrestlerId != null) root.AddToClassList("story-profile");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StoryManagement"));
            host.Add(root);
            var header = new VisualElement(); header.AddToClassList("story-header"); root.Add(header);
            var title = new Label(groupsOnly ? "태그팀 · 스테이블" : wrestlerId != null ? "선수 관계" : "스토리라인");
            title.AddToClassList("story-title"); header.Add(title);
            if (!groupsOnly && wrestlerId == null)
            {
                var count = new Label($"진행 중 {save.Storylines.Count(x => x.Status == StorylineStatus.Active)}");
                count.AddToClassList("story-count"); header.Add(count);
                if (selectedStory != null)
                {
                    var back = new Button(() => Open(host)) { text = "대립 목록" };
                    back.AddToClassList("story-close"); header.Add(back);
                }
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
            var main = new ScrollView(); main.AddToClassList("story-main"); body.Add(main);
            var side = new ScrollView(); side.AddToClassList("story-side");
            if (wrestlerId == null && selectedStory == null) body.Add(side);
            else main.AddToClassList("story-main-full");
            var storyList = new VisualElement();
            var creationList = new VisualElement();
            var candidateList = new VisualElement();
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
                var storyHeading = new Label(selectedStory == null ? "진행 중인 스토리" : "대립 전개"); storyHeading.AddToClassList("story-section-heading"); main.Add(storyHeading);
                main.Add(storyList);
                if (selectedStory == null)
                {
                    var candidateHeading = new Label("스토리 후보"); candidateHeading.AddToClassList("story-section-heading"); side.Add(candidateHeading);
                    side.Add(candidateList);
                    var creation = new Foldout { text = "새 스토리 만들기", value = false };
                    creation.AddToClassList("story-creation"); side.Add(creation);
                    creation.Add(creationList);
                    side.Add(groupList);
                }
            }
            var error = new Label { name = "story-error" }; error.AddToClassList("story-error"); root.Add(error);
            void Change(Action action)
            {
                var before = JsonUtility.ToJson(save);
                try { action(); DashboardSession.PersistActive(); Open(host, wrestlerId, groupsOnly, storyId); }
                catch (Exception exception)
                {
                    JsonUtility.FromJsonOverwrite(before, save);
                    Open(host, wrestlerId, groupsOnly, storyId);
                    host.Q<Label>("story-error").text = exception.Message;
                    host.Q<Label>("story-error").AddToClassList("visible");
                }
            }
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
            void Poster(StorylineState story, VisualElement parent, bool detail)
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
                    var portraits = new VisualElement(); portraits.AddToClassList("story-poster-portraits"); partySide.Add(portraits);
                    var members = StorylineService.PartyMembers(save, story, first);
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
                var kind = new Label(story.PartyType == StoryPartyType.Wrestler ? "개인 대립" : story.PartyType == StoryPartyType.TagTeam ? "태그팀 대립" : "스테이블 대립");
                kind.AddToClassList("story-poster-kind"); center.Add(kind);
                var posterTitle = new Label(story.Name); posterTitle.AddToClassList("story-poster-title"); center.Add(posterTitle);
                var versus = new Label("VS"); versus.AddToClassList("story-poster-vs"); center.Add(versus);
                var latest = story.History?.LastOrDefault();
                if (latest != null)
                {
                    var latestText = new Label($"최근 전개 · {Incident(latest.Incident)}");
                    latestText.AddToClassList("story-poster-latest"); center.Add(latestText);
                }
                Party(false);
                var footer = new VisualElement(); footer.AddToClassList("story-poster-footer"); poster.Add(footer);
                var reaction = new VisualElement(); reaction.AddToClassList("story-reaction"); footer.Add(reaction);
                var trend = (story.History ?? new()).TakeLast(3).Sum(x => x.InterestChange);
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
                if (!detail)
                {
                    var open = new Button(() => Open(host, storyId: story.Id)) { text = "전개 보기" };
                    open.AddToClassList("story-poster-open"); footer.Add(open);
                }
            }
            if (wrestlerId == null)
            {
            var wrestlers = save.Wrestlers.Where(x => x.PromotionId == save.Promotion.Id).ToList();
            if (groupsOnly)
            {
            Func<string[]> MemberSelection(VisualElement parent, System.Collections.Generic.IEnumerable<string> initial)
            {
                var selected = initial.ToHashSet();
                var foldout = new Foldout { text = "소속 선수 선택", value = false }; parent.Add(foldout);
                var toggles = new System.Collections.Generic.List<(string id, Toggle toggle)>();
                foreach (var wrestler in wrestlers)
                {
                    var row = new VisualElement(); row.AddToClassList("story-member"); foldout.Add(row);
                    var toggle = new Toggle { value = selected.Contains(wrestler.Id), tooltip = "소속 선택" }; row.Add(toggle);
                    Names(row, new[] { wrestler.Id });
                    toggles.Add((wrestler.Id, toggle));
                }
                return () => toggles.Where(x => x.toggle.value).Select(x => x.id).ToArray();
            }
            if (wrestlers.Count >= 2)
            {
                var card = Card("새 태그팀", groupList);
                var name = new TextField("이름"); card.Add(name);
                var members = MemberSelection(card, Array.Empty<string>());
                card.Add(new Button(() => Change(() => WrestlerGroupService.CreateTagTeam(save, name.value, members()))) { text = "태그팀 결성" });
            }
            if (wrestlers.Count >= 3)
            {
                var card = Card("새 스테이블", stableList);
                var name = new TextField("이름"); card.Add(name);
                var members = MemberSelection(card, Array.Empty<string>());
                var leader = new DropdownField("리더", wrestlers.Select((x, i) => $"{i + 1}. {x.Identity.RingName}").ToList(), 0); card.Add(leader);
                card.Add(new Button(() => Change(() => WrestlerGroupService.CreateStable(save, name.value, members(), wrestlers[leader.index].Id))) { text = "스테이블 결성" });
            }
            foreach (var team in save.TagTeams.Where(x => x.Status == TagTeamStatus.Active))
            {
                var card = Card(string.IsNullOrWhiteSpace(team.Name) ? "태그팀" : team.Name, groupList);
                Names(card, team.MemberIds);
                card.Add(new Label($"호흡 {team.Chemistry:0}"));
                var members = MemberSelection(card, team.MemberIds);
                card.Add(new Button(() => Change(() => WrestlerGroupService.SetTagTeamMembers(save, team.Id, members()))) { text = "태그팀 소속 저장" });
                var confirm = new Toggle("해체 확인"); card.Add(confirm);
                card.Add(new Button(() => { if (confirm.value) Change(() => WrestlerGroupService.Disband(save, team.Id, false)); }) { text = "태그팀 해체" });
            }
            foreach (var stable in save.Stables.Where(x => x.Status == TagTeamStatus.Active))
            {
                var card = Card(stable.Name, stableList);
                Names(card, stable.MemberIds);
                card.Add(new Label($"호흡 {stable.Chemistry:0} · 리더")); Names(card, new[] { stable.LeaderId });
                var members = MemberSelection(card, stable.MemberIds);
                var leader = new DropdownField("리더", wrestlers.Select((x, i) => $"{i + 1}. {x.Identity.RingName}").ToList(), Math.Max(0, wrestlers.FindIndex(x => x.Id == stable.LeaderId))); card.Add(leader);
                card.Add(new Button(() => Change(() => WrestlerGroupService.SetStableMembers(save, stable.Id, members(), wrestlers[leader.index].Id))) { text = "소속·리더 저장" });
                var confirm = new Toggle("해체 확인"); card.Add(confirm);
                card.Add(new Button(() => { if (confirm.value) Change(() => WrestlerGroupService.Disband(save, stable.Id, true)); }) { text = "스테이블 해체" });
            }
            }
            else
            {
            var creation = Card("대립 만들기", creationList);
            foreach (var type in new[] { StoryPartyType.TagTeam, StoryPartyType.Stable })
            {
                var ids = type == StoryPartyType.TagTeam ? save.TagTeams.Where(x => x.Status == TagTeamStatus.Active).Select(x => x.Id).ToList() : save.Stables.Where(x => x.Status == TagTeamStatus.Active).Select(x => x.Id).ToList();
                if (ids.Count < 2) continue;
                if (groupList.childCount == 0)
                {
                    var heading = new Label("그룹 대립"); heading.AddToClassList("story-section-heading"); groupList.Add(heading);
                }
                var names = ids.Select((id, i) => $"{i + 1}. " + (type == StoryPartyType.TagTeam ? save.TagTeams.First(x => x.Id == id).Name : save.Stables.First(x => x.Id == id).Name)).ToList();
                var card = Card(type == StoryPartyType.TagTeam ? "태그팀 대립" : "스테이블 대립", groupList);
                var name = new TextField("스토리 이름"); card.Add(name);
                var first = new DropdownField("첫 그룹", names, 0); card.Add(first);
                var second = new DropdownField("둘째 그룹", names, 1); card.Add(second);
                card.Add(new Button(() => Change(() => new StorylineService().CreateGroupStory(save, name.value, type, ids[first.index], ids[second.index]))) { text = "대립 생성" });
            }
            if (wrestlers.Count >= 2)
            {
                var choices = wrestlers.Select((x, i) => $"{i + 1}. {x.Identity.RingName}").ToList();
                var name = new TextField("이름"); creation.Add(name);
                var first = new DropdownField("첫 선수", choices, 0); creation.Add(first);
                var second = new DropdownField("둘째 선수", choices, 1); creation.Add(second);
                creation.Add(new Button(() => Change(() => new StorylineService().Create(save, name.value, wrestlers[first.index].Id, wrestlers[second.index].Id))) { text = "스토리 생성" });
            }
            }
            }
            if (!groupsOnly && wrestlerId == null && selectedStory == null)
            {
            var candidates = save.StoryCandidates.Where(x => x.Status == StoryCandidateStatus.Pending).ToList();
            if (candidates.Count == 0)
            {
                var empty = new Label("아직 스토리 후보가 없습니다."); empty.AddToClassList("story-empty"); candidateList.Add(empty);
            }
            foreach (var candidate in candidates)
            {
                var card = Card("스토리 후보 · " + Incident(candidate.Event.Incident), candidateList);
                Names(card, new[] { candidate.Event.ActorId, candidate.Event.TargetId });
                var name = new TextField("새 스토리 이름"); card.Add(name);
                card.Add(new Button(() => Change(() => new StorylineService().AdoptCandidate(save, candidate.Id, name.value))) { text = "새 스토리로 채택" });
                var eligible = save.Storylines.Where(x => x.Status == StorylineStatus.Active && StorylineService.IncludesBothParties(save, x, new[] { candidate.Event.ActorId, candidate.Event.TargetId })).ToList();
                if (eligible.Count > 0)
                {
                    var existing = new DropdownField("기존 스토리", eligible.Select((x, i) => $"{i + 1}. {x.Name}").ToList(), 0); card.Add(existing);
                    card.Add(new Button(() => Change(() => new StorylineService().AdoptCandidate(save, candidate.Id, null, eligible[existing.index].Id))) { text = "기존 스토리에 연결" });
                }
            }
            var activeStories = save.Storylines.Where(x => x.Status == StorylineStatus.Active).ToList();
            if (activeStories.Count == 0)
            {
                var empty = new Label("진행 중인 대립이 없습니다."); empty.AddToClassList("story-empty"); storyList.Add(empty);
            }
            foreach (var story in activeStories)
                Poster(story, storyList, false);
            var completed = save.Storylines.Where(x => x.Status == StorylineStatus.Completed).ToList();
            if (completed.Count > 0)
            {
                var archive = new Foldout { text = $"종료된 대립 {completed.Count}", value = false };
                archive.AddToClassList("story-archive"); storyList.Add(archive);
                foreach (var story in completed) Poster(story, archive, false);
            }
            }
            if (selectedStory != null)
            {
                Poster(selectedStory, storyList, true);
                var context = Card("대립 맥락", storyList);
                var contextField = new TextField { value = selectedStory.Context, multiline = true };
                contextField.AddToClassList("story-context"); context.Add(contextField);
                context.Add(new Button(() => Change(() => selectedStory.Context = contextField.value)) { text = "맥락 저장" });
                var timelineHeading = new Label("전개 기록"); timelineHeading.AddToClassList("story-section-heading"); storyList.Add(timelineHeading);
                if (selectedStory.History == null || selectedStory.History.Count == 0)
                {
                    var empty = new Label("아직 진행된 사건이 없습니다."); empty.AddToClassList("story-empty"); storyList.Add(empty);
                }
                foreach (var beat in (selectedStory.History ?? new()).AsEnumerable().Reverse())
                {
                    var eventRow = new VisualElement(); eventRow.AddToClassList("story-event"); storyList.Add(eventRow);
                    var eventTitle = new Label($"{(beat.EventType == StoryEventType.Match ? "경기" : "프로모")} · {Incident(beat.Incident)}");
                    eventTitle.AddToClassList("story-event-title"); eventRow.Add(eventTitle);
                    var eventDate = new Label($"{beat.Date.Year:D4}.{beat.Date.Month:D2}.{beat.Date.Day:D2}  관중 반응 {beat.InterestChange:+0;-0;0}");
                    eventDate.AddToClassList("story-event-date"); eventRow.Add(eventDate);
                    Names(eventRow, new[] { beat.ActorId, beat.TargetId });
                }
            }
            if (wrestlerId != null)
            {
            var relations = relationList;
            var visibleRelations = save.StoryRelationships.Where(x =>
                x.FirstWrestlerId == wrestlerId || x.SecondWrestlerId == wrestlerId).OrderBy(x => x.Value).ToList();
            if (visibleRelations.Count == 0)
            {
                var empty = new Label("아직 기록된 관계 변화가 없습니다."); empty.AddToClassList("story-empty"); relations.Add(empty);
            }
            foreach (var pair in visibleRelations)
            {
                var row = new VisualElement(); row.AddToClassList("story-relation"); relations.Add(row);
                Names(row, new[] { pair.FirstWrestlerId, pair.SecondWrestlerId });
                var value = new Label($"관계 {pair.Value:+0;-0;0}"); value.AddToClassList("story-relation-value"); row.Add(value);
            }
            }
        }
        private static string Incident(StoryIncident incident) => incident switch
        {
            StoryIncident.Competition => "경쟁", StoryIncident.Challenge => "도전", StoryIncident.Taunt => "도발",
            StoryIncident.Attack => "공격", StoryIncident.Betrayal => "배신", StoryIncident.Rescue => "구출",
            StoryIncident.Respect => "존중", StoryIncident.Reconciliation => "화해", _ => "진행"
        };
    }
}
