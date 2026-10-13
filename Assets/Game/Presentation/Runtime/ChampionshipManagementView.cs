using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class ChampionshipManagementView
    {
        public static void Open(VisualElement host)
        {
            host.Q("championship-management")?.RemoveFromHierarchy();
            var save = DashboardSession.ActiveSave;
            if (save?.Promotion == null) return;
            var root = new VisualElement { name = "championship-management" };
            root.AddToClassList("story-management");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/StoryManagement"));
            host.Add(root);
            var body = new VisualElement(); body.AddToClassList("story-body"); root.Add(body);
            var list = new ScrollView(); list.AddToClassList("story-main"); body.Add(list);
            var form = new ScrollView(); form.AddToClassList("story-side"); body.Add(form);
            var error = new Label { name = "championship-error" }; error.AddToClassList("story-error"); root.Add(error);
            void Change(Action change)
            {
                if (DashboardSession.IsShowRunning) return;
                var before = JsonUtility.ToJson(save);
                try { change(); DashboardSession.PersistActive(); Open(host); }
                catch (Exception exception)
                {
                    JsonUtility.FromJsonOverwrite(before, save);
                    Open(host);
                    var message = host.Q<Label>("championship-error");
                    message.text = exception.Message; message.AddToClassList("visible");
                }
            }
            var titles = (save.Championships ?? new()).Where(x => x.PromotionId == save.Promotion.Id).ToList();
            if (titles.Count == 0)
            {
                var empty = new Label("등록된 챔피언십이 없습니다."); empty.AddToClassList("story-empty"); list.Add(empty);
            }
            foreach (var title in titles)
            {
                var card = Card(list, title.Name);
                card.Add(new Label(title.Type == ChampionshipType.TagTeam ? "태그팀 챔피언십" :
                    title.IsWorldChampionship ? "개인 · 월드 챔피언십" : "개인 챔피언십"));
                var prestige = new Label($"위상 {ChampionshipService.Prestige(title):0.0} / 100");
                prestige.AddToClassList("story-relation-value"); card.Add(prestige);
                var current = title.Reigns.LastOrDefault(x => !x.LostDate.HasValue);
                if (current == null) card.Add(new Label("공석"));
                else
                {
                    AddHolder(card, save, title.Type, current.HolderId, current.MemberIds);
                    card.Add(new Label($"{Date(current.WonDate)}부터 · 방어 {current.Defenses}회"));
                }
                var assignment = new Foldout { text = "챔피언 지정 · 공석 처리", value = false }; card.Add(assignment);
                assignment.SetEnabled(!DashboardSession.IsShowRunning);
                string nextHolder = title.HolderId;
                HolderPicker(assignment, save, title.Type, nextHolder, id => nextHolder = id);
                assignment.Add(new Button(() => Change(() => ChampionshipService.SetHolder(save, title, nextHolder))) { text = "적용" });
                var history = new Foldout { text = $"역대 챔피언 · {title.Reigns.Count}회", value = false }; card.Add(history);
                foreach (var reign in title.Reigns.AsEnumerable().Reverse())
                {
                    var row = new VisualElement(); row.AddToClassList("story-event"); history.Add(row);
                    AddHolder(row, save, title.Type, reign.HolderId, reign.MemberIds);
                    row.Add(new Label($"{Date(reign.WonDate)} ~ {(reign.LostDate.HasValue ? Date(reign.LostDate.Value) : "현재")} · 방어 {reign.Defenses}회"));
                }
                var matches = new Foldout { text = $"타이틀전 기록 · {title.MatchHistory?.Count ?? 0}경기", value = false }; card.Add(matches);
                foreach (var match in (title.MatchHistory ?? new()).AsEnumerable().Reverse())
                {
                    var row = new VisualElement(); row.AddToClassList("story-event"); matches.Add(row);
                    var showName = save.Shows.FirstOrDefault(x => x.Id == match.ShowId)?.Name;
                    var outcome = match.WasStopped ? "경기 중단" : match.SuccessfulDefense ? "방어 성공" :
                        match.ChampionBeforeId != match.ChampionAfterId ? "새 챔피언" : string.IsNullOrEmpty(match.ChampionAfterId) ? "공석 유지" : "타이틀 유지";
                    row.Add(new Label($"{Date(match.Date)} · {showName ?? "쇼"} · {outcome}"));
                    AddHolder(row, save, ChampionshipType.Individual, null, match.ParticipantIds);
                    row.Add(new Label($"{Finish(match.FinishType)} · 경기 {match.MatchQuality:0.0} · 팬 만족도 {match.FanSatisfaction:0.0}"));
                    if (match.WinnerMemberIds.Count > 0)
                    {
                        row.Add(new Label("승리")); AddHolder(row, save, ChampionshipType.Individual, null, match.WinnerMemberIds);
                    }
                    row.Add(new Label($"위상 {match.PrestigeBefore:0.0} → {match.PrestigeAfter:0.0} ({match.PrestigeAfter - match.PrestigeBefore:+0.0;-0.0;0.0})"));
                }
            }
            var create = Card(form, "챔피언십 만들기");
            create.SetEnabled(!DashboardSession.IsShowRunning);
            var name = new TextField("챔피언십명") { name = "championship-name" }; create.Add(name);
            var type = new DropdownField("종류", new List<string> { "개인", "태그팀" }, 0) { name = "championship-type" }; create.Add(type);
            var world = new Toggle("월드 챔피언십") { name = "championship-world" }; create.Add(world);
            var holder = new VisualElement(); create.Add(holder);
            string holderId = null;
            void Refresh()
            {
                world.style.display = type.index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (type.index != 0) world.value = false;
                holderId = null; holder.Clear();
                HolderPicker(holder, save, (ChampionshipType)type.index, null, id => holderId = id);
            }
            type.RegisterValueChangedCallback(_ => Refresh()); Refresh();
            create.Add(new Button(() => Change(() => ChampionshipService.Create(save, name.value,
                (ChampionshipType)type.index, world.value, holderId))) { name = "championship-create", text = "생성" });
        }

        private static VisualElement Card(VisualElement parent, string text)
        {
            var card = new VisualElement(); card.AddToClassList("story-card"); parent.Add(card);
            var heading = new Label(text); heading.AddToClassList("story-heading"); card.Add(heading); return card;
        }

        private static void HolderPicker(VisualElement parent, GameSave save, ChampionshipType type,
            string selected, Action<string> select)
        {
            var heading = new Label("챔피언"); parent.Add(heading);
            var options = new ScrollView(); options.style.maxHeight = 260; parent.Add(options);
            var toggles = new List<Toggle>();
            void Option(string id, Action<VisualElement> content)
            {
                var row = new VisualElement(); row.AddToClassList("story-member"); options.Add(row);
                var toggle = new Toggle { value = selected == id || string.IsNullOrEmpty(selected) && id == null,
                    tooltip = id == null ? "공석으로 시작" : "챔피언으로 지정" };
                toggles.Add(toggle); row.Add(toggle); content(row);
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue) { toggle.SetValueWithoutNotify(true); return; }
                    foreach (var other in toggles) other.SetValueWithoutNotify(other == toggle);
                    select(id);
                });
            }
            Option(null, row => row.Add(new Label("공석")));
            if (type == ChampionshipType.Individual)
                foreach (var wrestler in save.Wrestlers.Where(x => x.PromotionId == save.Promotion.Id && x.Roster.ActivityState == RosterActivityState.Active))
                    Option(wrestler.Id, row => row.Add(WrestlerProfileController.CreateLink(wrestler)));
            else
                foreach (var team in save.TagTeams.Where(x => x.PromotionId == save.Promotion.Id && x.Status == TagTeamStatus.Active))
                    Option(team.Id, row => AddHolder(row, save, type, team.Id, team.MemberIds));
        }

        private static void AddHolder(VisualElement parent, GameSave save, ChampionshipType type, string id, IEnumerable<string> members)
        {
            if (type == ChampionshipType.TagTeam) parent.Add(new Label(save.TagTeams.FirstOrDefault(x => x.Id == id)?.Name ?? "태그팀"));
            var people = new VisualElement(); people.AddToClassList("story-people"); parent.Add(people);
            foreach (var member in members)
            {
                var wrestler = save.Wrestlers.FirstOrDefault(x => x.Id == member);
                if (wrestler != null) people.Add(WrestlerProfileController.CreateLink(wrestler));
            }
        }

        private static string Date(GameDate date) => $"{date.Year}.{date.Month:00}.{date.Day:00}";
        private static string Finish(MatchFinishType finish) => finish switch
        {
            MatchFinishType.Submission => "서브미션", MatchFinishType.RollUp => "롤업", MatchFinishType.Disqualification => "반칙",
            MatchFinishType.CountOut => "카운트아웃", MatchFinishType.Draw => "무승부", MatchFinishType.Escape => "탈출",
            MatchFinishType.ObjectRetrieval => "목표물 획득", MatchFinishType.TableBreak => "테이블 파괴", _ => "핀폴"
        };
    }
}
