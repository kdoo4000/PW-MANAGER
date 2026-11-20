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
        private sealed class State { public string SelectedId, Tab = "overview"; public bool Creating, Editing, Initialized, Profile; }
        public static void Open(VisualElement host) => Open(host, new State());
        public static void Refresh(VisualElement host)
        {
            var state = (host?.Q("championship-profile") ?? host?.Q("championship-management"))?.userData as State;
            if (state != null && !state.Creating && !state.Editing) Open(host, state);
        }
        public static void OpenProfile(VisualElement host, string titleId)
        {
            if (DashboardSession.ActiveSave?.Championships.Any(t => t.Id == titleId && t.PromotionId == DashboardSession.ActiveSave.Promotion.Id) != true) { Open(host); return; }
            Open(host, new State { SelectedId = titleId, Profile = true, Initialized = true });
        }
        private static void Open(VisualElement host, State state)
        {
            host.Q("championship-management")?.RemoveFromHierarchy();
            host.Q("championship-profile")?.RemoveFromHierarchy();
            var save = DashboardSession.ActiveSave; if (save?.Promotion == null) return;
            var titles = save.Championships.Where(t => t.PromotionId == save.Promotion.Id).ToList();
            if (!state.Initialized && titles.Count == 0) state.Creating = true; state.Initialized = true;
            if (!titles.Any(t => t.Id == state.SelectedId)) state.SelectedId = titles.FirstOrDefault()?.Id;
            var selected = titles.FirstOrDefault(t => t.Id == state.SelectedId);
            var root = OperationsView.Page(host, state.Profile && !state.Creating && !state.Editing ? "championship-profile" : "championship-management"); root.userData = state; ManagementVisuals.Style(root);
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ChampionshipProfile"));
            root.EnableInClassList("cp-profile", state.Profile && !state.Creating && !state.Editing);
            root.EnableInClassList("mg-editor", state.Creating || state.Editing);
            var toolbar = new VisualElement(); toolbar.AddToClassList("ops-toolbar"); root.Add(toolbar);
            if (state.Profile) OperationsView.Action(toolbar, "챔피언십 목록", "championship-back-to-list", () =>
            {
                if (!DashboardController.OpenChampionshipGallery()) Open(host);
            });
            else OperationsView.Heading(toolbar, $"챔피언십 {titles.Count}개");
            OperationsView.Action(toolbar, "새 챔피언십", "championship-new", () => { state.Creating = true; state.Editing = false; Open(host, state); }, true).SetEnabled(!DashboardSession.IsShowRunning);
            var error = new Label { name = "championship-error" }; error.AddToClassList("ops-status"); error.style.display = DisplayStyle.None; root.Add(error);
            if (!state.Creating && !state.Editing && !state.Profile)
            {
                ChampionshipProfileView.Gallery(root, save, titles, id => { if (!DashboardController.OpenChampionshipProfile(id)) OpenProfile(host, id); });
                return;
            }
            var body = new VisualElement(); body.AddToClassList("ops-body"); root.Add(body);
            var detailPane = new VisualElement(); detailPane.AddToClassList("ops-detail-pane"); body.Add(detailPane);
            var detail = new ScrollView { name = "championship-detail" }; detail.AddToClassList("ops-scroll"); detailPane.Add(detail);
            void Change(Action change, Action completed)
            {
                if (DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning) return;
                var before = JsonUtility.ToJson(save);
                try
                {
                    change(); DashboardSession.PersistActive(); completed();
                    if (state.Profile && !state.Creating && !state.Editing && DashboardController.OpenChampionshipProfile(state.SelectedId)) return;
                    Open(host, state);
                }
                catch (Exception e) { JsonUtility.FromJsonOverwrite(before, save); error.text = e.Message; error.style.display = DisplayStyle.Flex; }
            }
            if (state.Creating || state.Editing && selected != null)
            {
                var creating = state.Creating;
                var draft = (creating ? BeltDesignRules.Default(ChampionshipType.Individual, false) : selected.BeltDesign ?? BeltDesignRules.Default(selected.Type, selected.IsWorldChampionship)).Copy();
                if (creating) draft.Abbreviation = new string(save.Promotion.GetDisplayAbbreviation().Take(BeltDesignRules.MaximumAbbreviationLength).ToArray());
                var titleLabel = new Label(creating ? "챔피언십 신설" : "챔피언십 편집"); titleLabel.AddToClassList("ops-detail-title"); detail.Add(titleLabel);
                var editorTabs = new VisualElement(); editorTabs.AddToClassList("ops-tabs"); detail.Add(editorTabs);
                var settingsPanel = new VisualElement { name = "championship-details-editor" }; detail.Add(settingsPanel);
                var designPanel = new VisualElement { name = "championship-belt-editor" }; designPanel.style.display = DisplayStyle.None; detail.Add(designPanel);
                foreach (var (key, text) in new[] { ("details", "기본 설정"), ("belt", "벨트 디자인") })
                {
                    var tab = new Button(() =>
                    {
                        settingsPanel.style.display = key == "details" ? DisplayStyle.Flex : DisplayStyle.None;
                        designPanel.style.display = key == "belt" ? DisplayStyle.Flex : DisplayStyle.None;
                        foreach (var id in new[] { "details", "belt" }) editorTabs.Q<Button>("championship-editor-tab-" + id).EnableInClassList("ops-active", id == key);
                    }) { text = text, name = "championship-editor-tab-" + key };
                    tab.AddToClassList("ops-tab"); tab.EnableInClassList("ops-active", key == "details"); editorTabs.Add(tab);
                }
                var layout = new VisualElement(); layout.AddToClassList("mg-editor-layout"); settingsPanel.Add(layout);
                var form = new VisualElement(); form.AddToClassList("ops-form"); form.AddToClassList("mg-editor-settings"); layout.Add(form);
                var preview = new VisualElement(); form.Add(preview);
                var name = new TextField(creating ? "이름" : "공식 명칭") { name = "championship-name", value = creating ? "" : selected.Name }; form.Add(name);
                var type = new DropdownField("종류", new List<string> { "개인", "태그팀" }, creating ? 0 : (int)selected.Type) { name = "championship-type" }; form.Add(type); type.SetEnabled(creating);
                var division = new DropdownField("디비전", new List<string> { "오픈", "남성부", "여성부" }, creating ? (int)ChampionshipDivision.Men : (int)selected.Division) { name = "championship-division" }; form.Add(division);
                var world = new Toggle("월드 챔피언십") { name = "championship-world", value = !creating && selected.IsWorldChampionship }; form.Add(world); world.SetEnabled(creating);
                var holder = new VisualElement(); holder.AddToClassList("ops-form"); holder.AddToClassList("mg-editor-members"); layout.Add(holder); string holderId = creating ? null : selected.HolderId;
                BeltCustomizerView designer = null;
                void RefreshBelt() { preview.Clear(); preview.Add(ManagementVisuals.Belt((ChampionshipType)type.index, world.value, "mg-belt-showcase", draft, name.value)); }
                void RefreshHolder()
                {
                    world.style.display = type.index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    if (type.index != 0) world.value = false;
                    var members = type.index == 0 ? new List<string> { holderId } : save.TagTeams.FirstOrDefault(t => t.Id == holderId)?.MemberIds;
                    if (!string.IsNullOrEmpty(holderId) && (members == null || members.Count == 0 || members.Any(id => !ChampionshipService.IsEligible((ChampionshipDivision)division.index, save.Wrestlers.FirstOrDefault(w => w.Id == id))))) holderId = null;
                    RefreshBelt(); holder.Clear(); HolderPicker(holder, save, (ChampionshipType)type.index, (ChampionshipDivision)division.index, holderId, id => { holderId = id; designer?.RefreshPreview(false); });
                }
                type.RegisterValueChangedCallback(_ => { holderId = null; RefreshHolder(); }); world.RegisterValueChangedCallback(_ => RefreshBelt()); RefreshHolder();
                division.RegisterValueChangedCallback(_ => { RefreshHolder(); designer?.RefreshPreview(false); });
                designer = new BeltCustomizerView(draft, () => name.value, RefreshBelt, () =>
                {
                    var member = type.index == 0 ? holderId : save.TagTeams.FirstOrDefault(t => t.Id == holderId)?.MemberIds.FirstOrDefault();
                    return save.Wrestlers.FirstOrDefault(w => w.Id == member) ?? save.Wrestlers.FirstOrDefault(w => w.PromotionId == save.Promotion.Id);
                }); designPanel.Add(designer); designer.SetEnabled(!DashboardSession.IsShowRunning);
                var actions = new VisualElement(); actions.AddToClassList("ops-actions"); actions.AddToClassList("mg-footer"); detailPane.Add(actions);
                OperationsView.Action(actions, "취소", "championship-cancel", () => { state.Creating = state.Editing = false; Open(host, state); });
                var saveButton = OperationsView.Action(actions, creating ? "챔피언십 생성" : "변경 저장", creating ? "championship-create" : "championship-save", () =>
                {
                    string createdId = null;
                    Change(() =>
                    {
                        BeltDesignRules.Validate(draft);
                        if (creating)
                        {
                            var created = ChampionshipService.Create(save, name.value, (ChampionshipType)type.index, world.value, holderId, (ChampionshipDivision)division.index);
                            ChampionshipService.SetBeltDesign(save, created, draft); createdId = created.Id;
                        }
                        else
                        {
                            var current = save.Championships.Single(t => t.Id == selected.Id);
                            ChampionshipService.Rename(save, current, name.value);
                            ChampionshipService.SetDivisionAndHolder(save, current, (ChampionshipDivision)division.index, holderId);
                            ChampionshipService.SetBeltDesign(save, current, draft);
                        }
                    }, () => { if (createdId != null) state.SelectedId = createdId; state.Creating = state.Editing = false; state.Profile = true; state.Tab = "overview"; });
                }, true);
                void CheckName() => saveButton.SetEnabled(!DashboardSession.IsShowRunning && !string.IsNullOrWhiteSpace(name.value));
                name.RegisterValueChangedCallback(_ => { CheckName(); RefreshBelt(); designer.RefreshPreview(false); }); CheckName(); form.SetEnabled(!DashboardSession.IsShowRunning);
                holder.SetEnabled(!DashboardSession.IsShowRunning);
                return;
            }
            if (selected == null) { var empty = new Label("새 챔피언십을 만들어 타이틀을 운영하세요."); empty.AddToClassList("ops-empty"); detail.Add(empty); return; }
            var header = new VisualElement(); header.AddToClassList("mg-header"); detail.Add(header);
            var titleHeading = new Label(selected.Name); titleHeading.AddToClassList("ops-detail-title"); header.Add(titleHeading);
            OperationsView.Action(header, "편집", "championship-edit", () => { state.Editing = true; Open(host, state); }).SetEnabled(!DashboardSession.IsShowRunning);
            var tabs = new VisualElement(); tabs.AddToClassList("ops-tabs"); detail.Add(tabs);
            foreach (var (key, text) in new[] { ("overview", "개요"), ("reigns", "역대 챔피언"), ("matches", "타이틀전 기록") })
            {
                var button = new Button(() => { state.Tab = key; Open(host, state); }) { text = text, name = "championship-tab-" + key };
                button.AddToClassList("ops-tab"); button.EnableInClassList("ops-active", state.Tab == key); tabs.Add(button);
            }
            if (state.Tab == "overview")
            {
                ChampionshipProfileView.Overview(detail, save, selected);
            }
            else if (state.Tab == "reigns")
            {
                ChampionshipProfileView.Reigns(detail, save, selected);
            }
            else
            {
                if (selected.MatchHistory.Count == 0) detail.Add(new Label("아직 타이틀전 기록이 없습니다."));
                foreach (var match in selected.MatchHistory.AsEnumerable().Reverse())
                {
                    var row = new VisualElement(); row.AddToClassList("ops-history-row"); detail.Add(row);
                    var outcome = match.WasStopped ? "경기 중단" : match.SuccessfulDefense ? "방어 성공" : match.ChampionBeforeId != match.ChampionAfterId ? "새 챔피언" : string.IsNullOrEmpty(match.ChampionAfterId) ? "공석 유지" : "타이틀 유지";
                    OperationsView.Heading(row, $"{Date(match.Date)} · {save.Shows.FirstOrDefault(s => s.Id == match.ShowId)?.Name ?? "쇼"} · {outcome}");
                    AddHolder(row, save, ChampionshipType.Individual, null, match.ParticipantIds);
                    OperationsView.AddValue(row, "결과", Finish(match.FinishType)); OperationsView.AddValue(row, "경기 평가", match.MatchQuality.ToString("0.0")); OperationsView.AddValue(row, "팬 만족도", match.FanSatisfaction.ToString("0.0"));
                    if (match.WinnerMemberIds.Count > 0) { OperationsView.Heading(row, "승리"); AddHolder(row, save, ChampionshipType.Individual, null, match.WinnerMemberIds); }
                    OperationsView.AddValue(row, "위상", $"{match.PrestigeBefore:0.0} → {match.PrestigeAfter:0.0} ({match.PrestigeAfter - match.PrestigeBefore:+0.0;-0.0;0.0})");
                }
            }
        }

        private static void HolderPicker(VisualElement parent, GameSave save, ChampionshipType type, ChampionshipDivision division, string selected, Action<string> select)
        {
            OperationsView.Heading(parent, "챔피언 지정");
            var search = new TextField("챔피언 검색") { name = "championship-holder-search" }; parent.Add(search);
            var options = new ScrollView(); options.AddToClassList("mg-member-options"); parent.Add(options);
            var grid = new VisualElement(); grid.AddToClassList("mg-picker-grid"); options.Add(grid);
            void Render()
            {
                grid.Clear(); var toggles = new List<Toggle>();
                void Option(string id, IEnumerable<string> members)
                {
                    var row = new VisualElement(); row.AddToClassList("mg-picker-card"); row.AddToClassList("story-member"); grid.Add(row);
                    var active = selected == id || string.IsNullOrEmpty(selected) && id == null; row.EnableInClassList("ops-selected", active);
                    if (id == null) OperationsView.Heading(row, "공석");
                    else
                    {
                        if (type == ChampionshipType.TagTeam) OperationsView.Heading(row, save.TagTeams.First(t => t.Id == id).Name);
                        if (type == ChampionshipType.TagTeam)
                        {
                            var faces = new GroupPortraitView(members.Select(member => save.Wrestlers.FirstOrDefault(w => w.Id == member))); faces.AddToClassList("group-portrait-picker"); row.Add(faces);
                            foreach (var w in save.Wrestlers.Where(w => members.Contains(w.Id))) row.Add(WrestlerProfileController.CreateLink(w));
                        }
                        else foreach (var w in save.Wrestlers.Where(w => members.Contains(w.Id))) row.Add(ManagementVisuals.Wrestler(w));
                    }
                    var toggle = new Toggle(id == null ? "공석으로 지정" : "챔피언 지정") { name = "championship-holder-" + (id ?? "vacant"), value = active }; toggles.Add(toggle); row.Add(toggle);
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (!evt.newValue) { toggle.SetValueWithoutNotify(true); return; }
                        selected = id; foreach (var other in toggles) { other.SetValueWithoutNotify(other == toggle); other.parent.EnableInClassList("ops-selected", other == toggle); } select(id);
                    });
                    OperationsView.Selectable(row, () => toggle.value = true);
                }
                Option(null, Array.Empty<string>());
                var query = search.value?.Trim() ?? "";
                if (type == ChampionshipType.Individual)
                    foreach (var w in save.Wrestlers.Where(w => w.PromotionId == save.Promotion.Id && w.Roster.ActivityState == RosterActivityState.Active && ChampionshipService.IsEligible(division, w) && WrestlerNameText.DisplayName(w).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)) Option(w.Id, new[] { w.Id });
                else
                    foreach (var team in save.TagTeams.Where(t => t.PromotionId == save.Promotion.Id && t.Status == TagTeamStatus.Active &&
                        t.MemberIds.Count >= 2 && t.MemberIds.All(id => save.Wrestlers.Any(w => w.Id == id && w.PromotionId == save.Promotion.Id && w.Roster.ActivityState == RosterActivityState.Active && ChampionshipService.IsEligible(division, w))) &&
                        (t.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || save.Wrestlers.Any(w => t.MemberIds.Contains(w.Id) && WrestlerNameText.DisplayName(w).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))) Option(team.Id, team.MemberIds);
            }
            search.RegisterValueChangedCallback(_ => Render()); Render();
        }

        private static void AddHolder(VisualElement parent, GameSave save, ChampionshipType type, string id, IEnumerable<string> members)
        {
            if (type == ChampionshipType.TagTeam) parent.Add(new Label(save.TagTeams.FirstOrDefault(x => x.Id == id)?.Name ?? "태그팀"));
            var people = new VisualElement(); people.AddToClassList("ops-people"); parent.Add(people);
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
