using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class WrestlerGroupsView
    {
        private sealed class State { public bool Stable, Creating, Editing; public string SelectedId; }
        public static void Open(VisualElement host)
        {
            var state = host.Q("story-management")?.userData as State ?? new State();
            state.Creating = state.Editing = false;
            Open(host, state);
        }

        private static void Open(VisualElement host, State state)
        {
            host.Q("story-management")?.RemoveFromHierarchy();
            var save = DashboardSession.ActiveSave; if (save?.Promotion == null) return;
            var groups = state.Stable
                ? save.Stables.Where(g => g.PromotionId == save.Promotion.Id && g.Status == TagTeamStatus.Active).Select(g => (g.Id, g.Name, g.Chemistry, Members: g.MemberIds, Leader: g.LeaderId)).ToList()
                : save.TagTeams.Where(g => g.PromotionId == save.Promotion.Id && g.Status == TagTeamStatus.Active).Select(g => (g.Id, g.Name, g.Chemistry, Members: g.MemberIds, Leader: (string)null)).ToList();
            if (!groups.Any(g => g.Id == state.SelectedId)) state.SelectedId = groups.FirstOrDefault().Id;
            var selected = groups.FirstOrDefault(g => g.Id == state.SelectedId);
            var roster = save.Wrestlers.Where(w => w.PromotionId == save.Promotion.Id).OrderBy(WrestlerNameText.DisplayName).ToList();
            var root = OperationsView.Page(host, "story-management"); root.userData = state; ManagementVisuals.Style(root);
            root.EnableInClassList("mg-editor", state.Creating || state.Editing);
            var toolbar = new VisualElement(); toolbar.AddToClassList("ops-toolbar"); root.Add(toolbar);
            var tabs = new VisualElement(); tabs.AddToClassList("ops-tabs"); toolbar.Add(tabs);
            foreach (var stable in new[] { false, true })
            {
                var tab = new Button(() => { state.Stable = stable; state.SelectedId = null; state.Creating = state.Editing = false; Open(host, state); })
                    { text = stable ? "스테이블" : "태그팀", name = stable ? "groups-tab-stables" : "groups-tab-teams" };
                tab.AddToClassList("ops-tab"); tab.EnableInClassList("ops-active", state.Stable == stable); tabs.Add(tab);
            }
            OperationsView.Action(toolbar, state.Stable ? "새 스테이블" : "새 태그팀", "group-new", () => { state.Creating = true; state.Editing = false; Open(host, state); }, true).SetEnabled(!DashboardSession.IsShowRunning);
            var error = new Label { name = "story-error" }; error.AddToClassList("ops-status"); error.style.display = DisplayStyle.None; root.Add(error);
            var body = new VisualElement(); body.AddToClassList("ops-body"); root.Add(body);
            var listPane = new VisualElement(); listPane.AddToClassList("ops-list-pane"); body.Add(listPane);
            OperationsView.Heading(listPane, $"{(state.Stable ? "스테이블" : "태그팀")} {groups.Count}");
            var list = new ScrollView { name = "group-list" }; list.AddToClassList("ops-scroll"); listPane.Add(list);
            foreach (var group in groups)
            {
                var row = new VisualElement { name = "group-select-" + group.Id }; row.AddToClassList("mg-list-row");
                row.EnableInClassList("ops-selected", !state.Creating && selected.Id == group.Id); list.Add(row);
                var copy = new VisualElement(); copy.AddToClassList("mg-list-copy"); row.Add(copy);
                var groupName = new Label(group.Name); groupName.AddToClassList("mg-list-name"); copy.Add(groupName);
                var meta = new Label($"{group.Members.Count}명 · 호흡 {group.Chemistry:0}"); meta.AddToClassList("mg-caption"); copy.Add(meta);
                var faces = new GroupPortraitView(group.Members.Select(id => roster.FirstOrDefault(w => w.Id == id)), leaderId: group.Leader); faces.AddToClassList("group-portrait-thumbnail"); row.Insert(0, faces);
                OperationsView.Selectable(row, () => { state.SelectedId = group.Id; state.Creating = state.Editing = false; Open(host, state); });
            }
            if (groups.Count == 0) { var empty = new Label(state.Stable ? "활동 중인 스테이블이 없습니다." : "활동 중인 태그팀이 없습니다."); empty.AddToClassList("ops-empty"); list.Add(empty); }
            var detailPane = new VisualElement(); detailPane.AddToClassList("ops-detail-pane"); body.Add(detailPane);
            var detail = new ScrollView { name = "group-detail" }; detail.AddToClassList("ops-scroll"); detailPane.Add(detail);
            void Change(Action action, Action completed)
            {
                if (DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning) return;
                var before = JsonUtility.ToJson(save);
                try { action(); DashboardSession.PersistActive(); completed(); Open(host, state); }
                catch (Exception e) { JsonUtility.FromJsonOverwrite(before, save); error.text = e.Message; error.style.display = DisplayStyle.Flex; }
            }
            var kind = state.Stable ? "스테이블" : "태그팀";
            if (state.Creating || state.Editing && selected.Id != null)
            {
                var creating = state.Creating;
                var title = new Label(creating ? $"새 {kind} 결성" : $"{kind} 편집"); title.AddToClassList("ops-detail-title"); detail.Add(title);
                var layout = new VisualElement(); layout.AddToClassList("mg-editor-layout"); detail.Add(layout);
                var form = new VisualElement(); form.AddToClassList("ops-form"); form.AddToClassList("mg-editor-settings"); layout.Add(form);
                var name = new TextField("그룹 이름") { name = "group-name", value = creating ? "" : selected.Name }; form.Add(name);
                var members = new HashSet<string>(creating ? Enumerable.Empty<string>() : selected.Members);
                string leaderId = creating ? null : selected.Leader;
                var membersPane = new VisualElement(); membersPane.AddToClassList("mg-editor-members"); layout.Add(membersPane);
                var count = new Label { name = "group-selected-count" }; count.AddToClassList("ops-heading"); membersPane.Add(count);
                var picked = new VisualElement { name = "group-selected-members" }; picked.AddToClassList("mg-selected-grid"); membersPane.Add(picked);
                var search = new TextField("선수 검색") { name = "group-member-search" }; form.Add(search);
                OperationsView.Heading(form, "추가할 선수");
                var choices = new ScrollView { name = "group-member-options" }; choices.AddToClassList("mg-member-options"); form.Add(choices);
                var actions = new VisualElement(); actions.AddToClassList("ops-actions"); actions.AddToClassList("mg-footer"); detailPane.Add(actions);
                OperationsView.Action(actions, "취소", "group-cancel", () => { state.Creating = state.Editing = false; Open(host, state); });
                var apply = OperationsView.Action(actions, creating ? $"{kind} 결성" : "변경 저장", "group-save", () =>
                {
                    string createdId = null;
                    Change(() =>
                    {
                        if (creating) createdId = state.Stable ? WrestlerGroupService.CreateStable(save, name.value, members, leaderId).Id : WrestlerGroupService.CreateTagTeam(save, name.value, members).Id;
                        else
                        {
                            if (state.Stable) WrestlerGroupService.SetStableMembers(save, selected.Id, members, leaderId);
                            else WrestlerGroupService.SetTagTeamMembers(save, selected.Id, members);
                            WrestlerGroupService.Rename(save, selected.Id, state.Stable, name.value);
                        }
                    }, () => { if (createdId != null) state.SelectedId = createdId; state.Creating = state.Editing = false; });
                }, true);
                void RefreshValidity()
                {
                    count.text = state.Stable ? $"소속 선수 {members.Count}명 / 최소 3명" : $"소속 선수 {members.Count}명 / 2명";
                    apply.SetEnabled(!DashboardSession.IsShowRunning && !string.IsNullOrWhiteSpace(name.value) && (state.Stable ? members.Count >= 3 && members.Contains(leaderId) : members.Count == 2));
                }
                void RenderPicked()
                {
                    if (state.Stable && !members.Contains(leaderId)) leaderId = roster.FirstOrDefault(w => members.Contains(w.Id))?.Id;
                    picked.Clear();
                    foreach (var w in roster.Where(w => members.Contains(w.Id)))
                    {
                        var card = ManagementVisuals.Wrestler(w); picked.Add(card);
                        var controls = new VisualElement(); controls.AddToClassList("mg-member-actions"); card.Add(controls);
                        if (state.Stable)
                        {
                            var leader = OperationsView.Action(controls, leaderId == w.Id ? "리더" : "리더 지정", "group-leader-" + w.Id, () => { leaderId = w.Id; RenderPicked(); });
                            leader.EnableInClassList("mg-leader-selected", leaderId == w.Id);
                        }
                        OperationsView.Action(controls, "제외", "group-remove-" + w.Id, () => { members.Remove(w.Id); RenderChoices(); RenderPicked(); });
                    }
                    if (members.Count == 0) { var empty = new Label("왼쪽 목록에서 함께할 선수를 선택하세요."); empty.AddToClassList("ops-empty"); picked.Add(empty); }
                    RefreshValidity();
                }
                void RenderChoices()
                {
                    choices.Clear(); var query = search.value?.Trim() ?? "";
                    foreach (var w in roster.Where(w => !members.Contains(w.Id) && WrestlerNameText.DisplayName(w).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        var row = new VisualElement(); row.AddToClassList("mg-roster-row"); choices.Add(row);
                        var occupied = state.Stable ? save.Stables.Any(g => g.PromotionId == save.Promotion.Id && g.Status == TagTeamStatus.Active && g.Id != (creating ? null : selected.Id) && g.MemberIds.Contains(w.Id))
                            : save.TagTeams.Any(g => g.PromotionId == save.Promotion.Id && g.Status == TagTeamStatus.Active && g.Id != (creating ? null : selected.Id) && g.MemberIds.Contains(w.Id));
                        var toggle = new Toggle { name = "group-member-" + w.Id, tooltip = "소속 선수에 추가" }; toggle.SetEnabled(!occupied); row.Add(toggle);
                        row.Add(ManagementVisuals.Wrestler(w, true));
                        row.Add(WrestlerProfileController.CreateLink(w));
                        if (occupied) { var note = new Label($"다른 {kind} 소속"); note.AddToClassList("ops-meta"); row.Add(note); }
                        toggle.RegisterValueChangedCallback(evt => { if (!evt.newValue) return; members.Add(w.Id); RenderPicked(); RenderChoices(); });
                        if (!occupied) OperationsView.Selectable(row, () => toggle.value = true);
                    }
                    if (choices.childCount == 0) choices.Add(new Label("검색 결과가 없습니다."));
                }
                name.RegisterValueChangedCallback(_ => RefreshValidity()); search.RegisterValueChangedCallback(_ => RenderChoices());
                RenderChoices(); RenderPicked(); form.SetEnabled(!DashboardSession.IsShowRunning); return;
            }
            if (selected.Id == null) { var empty = new Label($"새 {kind}을 결성하거나 목록에서 그룹을 선택하세요."); empty.AddToClassList("ops-empty"); detail.Add(empty); return; }
            var header = new VisualElement(); header.AddToClassList("mg-header"); detail.Add(header);
            var label = new Label(selected.Name); label.AddToClassList("ops-detail-title"); header.Add(label);
            OperationsView.Action(header, "그룹 편집", "group-edit", () => { state.Editing = true; Open(host, state); }).SetEnabled(!DashboardSession.IsShowRunning);
            var stats = new VisualElement(); stats.AddToClassList("mg-stat-strip"); detail.Add(stats);
            ManagementVisuals.Stat(stats, "종류", kind); ManagementVisuals.Stat(stats, "소속 선수", $"{selected.Members.Count}명"); ManagementVisuals.Stat(stats, "호흡", selected.Chemistry.ToString("0"));
            var lineup = new VisualElement(); lineup.AddToClassList("mg-lineup"); detail.Add(lineup);
            var groupPortrait = new GroupPortraitView(selected.Members.Select(id => save.Wrestlers.FirstOrDefault(w => w.Id == id)), leaderId: selected.Leader); groupPortrait.AddToClassList("group-portrait-detail"); detail.Insert(detail.IndexOf(lineup), groupPortrait);
            var displayMembers = selected.Members.Select(id => save.Wrestlers.FirstOrDefault(w => w.Id == id)).Where(w => w != null).ToList();
            if (selected.Leader != null)
            {
                var rear = displayMembers.Where(w => w.Id != selected.Leader).ToList();
                displayMembers = rear.Where((w, i) => i % 2 == 0).Reverse().Concat(displayMembers.Where(w => w.Id == selected.Leader)).Concat(rear.Where((w, i) => i % 2 == 1)).ToList();
            }
            foreach (var w in displayMembers)
            {
                var card = new VisualElement(); card.AddToClassList("mg-member-name"); card.Add(WrestlerProfileController.CreateLink(w)); lineup.Add(card);
                if (w.Id == selected.Leader) { var leader = new Label("리더"); leader.AddToClassList("mg-leader-mark"); card.Add(leader); }
            }
            var confirmation = new VisualElement { name = "group-disband-confirmation" }; confirmation.AddToClassList("ops-confirm"); confirmation.style.display = DisplayStyle.None;
            OperationsView.Action(detail, $"{kind} 해체", "group-disband", () => confirmation.style.display = DisplayStyle.Flex).SetEnabled(!DashboardSession.IsShowRunning);
            detail.Add(confirmation); confirmation.Add(new Label($"{selected.Name}을 해체하시겠습니까?"));
            var confirmActions = new VisualElement(); confirmActions.AddToClassList("ops-actions"); confirmation.Add(confirmActions);
            OperationsView.Action(confirmActions, "취소", "group-disband-cancel", () => confirmation.style.display = DisplayStyle.None);
            var confirm = OperationsView.Action(confirmActions, "해체 확인", "group-disband-confirm", () => Change(() => WrestlerGroupService.Disband(save, selected.Id, state.Stable), () => { state.SelectedId = null; state.Editing = false; }));
            confirm.AddToClassList("ops-danger"); confirm.SetEnabled(!DashboardSession.IsShowRunning);
        }
    }
}
