using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class DashboardController : MonoBehaviour
    {
        private static DashboardController instance;
        private UIDocument document;
        private readonly List<SortCriterion> rosterSorts = new();
        private string rosterSearch = string.Empty;
        private RosterMode rosterMode = RosterMode.All;
        private GameSave boundSave;
        private string selectedShowEventId;
        private string selectedShowId;
        private StaticContentRegistry staticContent;
        private ShowPlanningService showPlanningService;
        private readonly HashSet<string> editorParticipantIds = new(StringComparer.Ordinal);
        private readonly List<string> editorParticipantSlots = new();
        private int selectedParticipantSlotIndex = -1;
        private string pendingParticipantId;
        private ShowEditorMode showEditorMode;
        private ShowEditSection showEditSection;
        private string editingShowEventId;
        private MatchRuleOption selectedMatchRule;
        private string selectedMatchGimmickId;
        private readonly List<NavigationEntry> navigationHistory = new();
        private int navigationIndex = -1;
        private bool applyingNavigation;
        private VisualElement saveLoadOverlay;
        private VisualElement saveLoadRows;
        private TextField manualSaveName;
        private Label saveLoadMessage;

        private void OnEnable()
        {
            instance = this;
            document = GetComponent<UIDocument>();
            ApplyIcons();
            document.rootVisualElement.RegisterCallback<ClickEvent>(OnClick);
            SetupRosterControls();
            SetupShowPlanningControls();
            SetupSaveLoadControls();
            DashboardSession.SaveChanged += Bind;
            Bind(DashboardSession.ActiveSave);
            navigationHistory.Clear();
            navigationIndex = -1;
            applyingNavigation = false;
            RecordNavigation(new NavigationEntry(DashboardPage.Home));
        }

        private void OnDisable()
        {
            DashboardSession.SaveChanged -= Bind;
            if (document != null && document.rootVisualElement != null)
                document.rootVisualElement.UnregisterCallback<ClickEvent>(OnClick);
            if (instance == this) instance = null;
        }

        private void SetupShowPlanningControls()
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            if (catalog != null)
            {
                staticContent = new StaticContentRegistry(catalog);
                showPlanningService = new ShowPlanningService(staticContent);
            }
            BindClick("show-add-match", AddEmptyMatch);
            BindClick("show-add-promo", AddEmptyPromo);
            BindClick("show-save-draft", SaveShowDraft);
            BindClick("show-confirm", ConfirmSelectedShow);
            BindClick("show-edit-rules", () => OpenSelectedEventEditor(ShowEditSection.Rules));
            BindClick("show-edit-spots", () => OpenSelectedEventEditor(ShowEditSection.Spots));
            BindClick("show-move-up", () => MoveSelectedEvent(-1));
            BindClick("show-move-down", () => MoveSelectedEvent(1));
            BindClick("show-remove-event", RemoveSelectedEvent);
            BindClick("show-editor-close", CloseShowEditor);
            BindClick("show-editor-cancel", CloseShowEditor);
            BindClick("show-editor-submit", SubmitShowEditor);
        }

        private void SetupSaveLoadControls()
        {
            saveLoadOverlay = document.rootVisualElement.Q<VisualElement>("dashboard-save-load-overlay");
            saveLoadRows = document.rootVisualElement.Q<VisualElement>("dashboard-save-load-rows");
            manualSaveName = document.rootVisualElement.Q<TextField>("manual-save-name");
            saveLoadMessage = document.rootVisualElement.Q<Label>("save-load-message");
            BindClick("nav-settings", OpenSaveLoadMenu);
            BindClick("dashboard-save-load-close", CloseSaveLoadMenu);
            BindClick("manual-save-submit", SaveManual);
        }

        private void OpenSaveLoadMenu()
        {
            if (saveLoadOverlay == null) return;
            saveLoadMessage.text = string.Empty;
            RenderSaveSlots();
            saveLoadOverlay.RemoveFromClassList("hidden");
        }

        private void CloseSaveLoadMenu() => saveLoadOverlay?.AddToClassList("hidden");

        private void SaveManual()
        {
            try
            {
                var slotName = manualSaveName?.value?.Trim();
                DashboardSession.SaveAs(slotName);
                saveLoadMessage.text = $"{slotName} 저장 완료";
                saveLoadMessage.RemoveFromClassList("error");
                RenderSaveSlots();
            }
            catch (Exception exception)
            {
                saveLoadMessage.text = $"저장 실패: {exception.Message}";
                saveLoadMessage.AddToClassList("error");
            }
        }

        private void RenderSaveSlots()
        {
            if (saveLoadRows == null) return;
            saveLoadRows.Clear();
            var slots = DashboardSession.ListSlots();
            foreach (var slot in slots)
            {
                var row = new Button(); row.AddToClassList("save-load-row");
                var copy = new VisualElement(); copy.AddToClassList("save-load-row-copy");
                var name = new Label(slot); name.AddToClassList("save-load-row-name"); copy.Add(name);
                var date = new Label(DashboardSession.GetLastWriteTime(slot).ToString("yyyy.MM.dd  HH:mm")); date.AddToClassList("save-load-row-date"); copy.Add(date);
                row.Add(copy);
                var action = new Label("불러오기"); action.AddToClassList("save-load-row-action"); row.Add(action);
                var captured = slot;
                row.clicked += () => LoadSlot(captured);
                saveLoadRows.Add(row);
            }
            if (slots.Count == 0) { var empty = new Label("저장된 게임이 없습니다"); empty.AddToClassList("save-load-empty"); saveLoadRows.Add(empty); }
        }

        private void LoadSlot(string slotName)
        {
            try { DashboardSession.Load(slotName); CloseSaveLoadMenu(); }
            catch (Exception exception) { saveLoadMessage.text = $"불러오기 실패: {exception.Message}"; saveLoadMessage.AddToClassList("error"); }
        }

        private void BindClick(string name, Action action)
        {
            var button = document.rootVisualElement.Q<Button>(name);
            if (button != null) button.clicked += action;
        }

        public static void ShowWrestlerShell(string wrestlerName, string wrestlerId = null)
        {
            if (instance == null) return;
            instance.ShowProfilePage();
            instance.Set("dashboard-page-title", $"{wrestlerName} / 개요");
            instance.RecordNavigation(new NavigationEntry(DashboardPage.WrestlerProfile, wrestlerId));
        }
        public static void ShowRosterShell() { instance?.ShowPage(true); }
        public static int HomeBadgeCount
        {
            get
            {
                var value = instance?.document?.rootVisualElement.Q<Label>("home-badge")?.text;
                return int.TryParse(value, out var count) ? count : 0;
            }
        }

        public void Bind(GameSave save)
        {
            boundSave = save;
            if (save?.Promotion == null) { ShowEmpty(); return; }
            var wrestlers = save.Wrestlers?.Where(x => x != null).ToList() ?? new List<WrestlerState>();
            var active = wrestlers.Where(x => x.Roster?.ActivityState == RosterActivityState.Active).ToList();
            var transactions = save.Transactions?.Where(x => x != null && !x.IsVoided).ToList() ?? new List<TransactionRecord>();
            var cash = save.Promotion.CalculateCurrentCash(transactions);
            var seasonStart = save.SeasonPolicy?.StartDate ?? save.CurrentDate;

            Set("current-date", Date(save.CurrentDate)); Set("top-cash", Money(cash)); Set("cash-value", Compact(cash));
            Set("cash-detail", $"시즌 순변동 {SignedMoney(transactions.Where(x => x.Date.CompareTo(seasonStart) >= 0).Sum(x => x.Amount))}");
            Set("prestige-value", save.Promotion.PromotionPrestige.ToString("N0")); Set("promotion-name", save.Promotion.Name); Set("promotion-abbreviation", save.Promotion.GetDisplayAbbreviation());
            Set("roster-value", active.Count.ToString()); Set("roster-detail", $"전체 {wrestlers.Count}명");
            var injuries = active.Count(x => x.Condition?.InjuryStatus != InjuryStatus.None);
            var conditionRisks = active.Count(x => x.Condition == null || x.Condition.Condition < 40f || x.Condition.Satisfaction < 30f);
            Set("risk-value", (injuries + conditionRisks).ToString()); Set("risk-detail", $"부상 {injuries} · 컨디션/만족도 {conditionRisks}");
            var staff = save.StaffDepartments?.Where(x => x != null).ToList() ?? new List<StaffDepartmentState>();
            Set("staff-value", staff.Count == 0 ? "—" : $"Lv {staff.Average(x => x.CurrentLevel):0.0}");
            Set("staff-detail", staff.Count == 0 ? "부서 데이터 없음" : $"{staff.Count}개 부서 · 총 {staff.Sum(x => x.CurrentLevel)}레벨");

            var schedule = save.Schedules?.Where(x => x != null && x.Status != ScheduleStatus.Cancelled && x.Date.CompareTo(save.CurrentDate) >= 0)
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).FirstOrDefault();
            var show = schedule == null ? null : save.Shows?.FirstOrDefault(x => x?.ScheduleId == schedule.Id);
            BindNextShow(save, schedule, show);
            var tasks = BuildTasks(save, schedule, show, active);
            BindTasks(tasks); BindFanReaction(active); BindStories(save); BindResult(save);
            Badge("home-badge", tasks.Count); Badge("show-badge", save.Shows?.Count(x => x != null && x.Status != ShowStatus.Completed) ?? 0); Badge("report-badge", save.ShowResults?.Count ?? 0);
            var notifications = document.rootVisualElement.Q<Button>("notifications"); if (notifications != null) notifications.text = $"알림  {tasks.Count}";
            RenderRoster();
            RenderShowSchedule();
            RenderShowPlanning();
        }

        private void RenderShowSchedule()
        {
            var host = document?.rootVisualElement?.Q<VisualElement>("show-schedule-rows");
            if (host == null) return;
            host.Clear();
            var shows = boundSave?.Shows?.Where(x => x != null)
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).ToList() ?? new List<ShowState>();
            var upcoming = boundSave == null ? 0 : shows.Count(x => x.Status != ShowStatus.Completed && x.Date.CompareTo(boundSave.CurrentDate) >= 0);
            Set("show-schedule-caption", shows.Count == 0 ? "생성된 쇼 일정이 없습니다" : $"{Date(shows.First().Date)}부터 {Date(shows.Last().Date)}까지");
            Set("show-schedule-upcoming", $"예정 {upcoming}");
            Set("show-schedule-completed", $"완료 {shows.Count(x => x.Status == ShowStatus.Completed)}");
            foreach (var show in shows)
            {
                var showEvents = boundSave.ShowEvents?.Where(x => x != null && x.ShowId == show.Id).ToList() ?? new List<ShowEventState>();
                var row = new Button { name = $"show-schedule-{show.Id}" }; row.AddToClassList("show-schedule-row");
                var date = new Label($"{show.Date.Month:D2}월 {show.Date.Day:D2}일\n{show.Date.Year}"); date.AddToClassList("show-schedule-date"); row.Add(date);
                var copy = new VisualElement(); copy.AddToClassList("show-schedule-copy");
                var name = new Label(string.IsNullOrWhiteSpace(show.Name) ? ShowName(show.ShowType) : show.Name); name.AddToClassList("show-schedule-name"); copy.Add(name);
                var meta = new Label($"{ShowType(show.ShowType)} · {ShowVenueName(show)} · {show.CalculatePlannedDuration(showEvents)}/{show.DurationLimit}분"); meta.AddToClassList("show-schedule-meta"); copy.Add(meta); row.Add(copy);
                AddShowScheduleMetric(row, "편성", $"{showEvents.Count}개 세그먼트");
                AddShowScheduleMetric(row, "예상 비용", Money(show.EstimatedCost));
                var status = new Label(ShowStatusText(show.Status)); status.AddToClassList("show-schedule-status"); status.EnableInClassList("open", show.Status != ShowStatus.Completed); row.Add(status);
                var open = new Label("상세 보기"); open.AddToClassList("show-schedule-open"); row.Add(open);
                var captured = show.Id; row.clicked += () => ShowPlanningPage(captured);
                host.Add(row);
            }
            if (shows.Count == 0) { var empty = new Label("표시할 쇼 일정이 없습니다"); empty.AddToClassList("show-empty-state"); host.Add(empty); }
        }

        private static void AddShowScheduleMetric(VisualElement row, string label, string value)
        {
            var metric = new VisualElement(); metric.AddToClassList("show-schedule-metric");
            var caption = new Label(label); caption.AddToClassList("show-schedule-metric-label"); metric.Add(caption);
            var content = new Label(value); content.AddToClassList("show-schedule-metric-value"); metric.Add(content); row.Add(metric);
        }

        private void SetupRosterControls()
        {
            var search = document.rootVisualElement.Q<TextField>("roster-search");
            if (search != null)
            {
                search.value = string.Empty;
                search.RegisterValueChangedCallback(evt => { rosterSearch = evt.newValue ?? string.Empty; RenderRoster(); });
            }
            foreach (var key in new[] { "name", "gender", "age", "height", "weight", "role", "style", "match", "promo", "status", "momentum", "condition", "satisfaction", "team", "salary" })
            {
                var captured = key;
                var button = document.rootVisualElement.Q<Button>($"sort-{key}");
                if (button != null) button.clicked += () => ChangeRosterSort(captured);
            }
            BindRosterTab("roster-tab-all", RosterMode.All);
            BindRosterTab("roster-tab-wrestlers", RosterMode.Wrestlers);
            BindRosterTab("roster-tab-managers", RosterMode.Managers);
        }

        private void BindRosterTab(string name, RosterMode mode) { var button = document.rootVisualElement.Q<Button>(name); if (button != null) button.clicked += () => { rosterMode = mode; RefreshRosterTabs(); RenderRoster(); }; }
        private void RefreshRosterTabs() { document.rootVisualElement.Q<Button>("roster-tab-all")?.EnableInClassList("roster-tab-active", rosterMode == RosterMode.All); document.rootVisualElement.Q<Button>("roster-tab-wrestlers")?.EnableInClassList("roster-tab-active", rosterMode == RosterMode.Wrestlers); document.rootVisualElement.Q<Button>("roster-tab-managers")?.EnableInClassList("roster-tab-active", rosterMode == RosterMode.Managers); }

        private void ShowPage(bool roster, bool recordNavigation = true)
        {
            var overview = document.rootVisualElement.Q<VisualElement>("overview-content");
            var rosterContent = document.rootVisualElement.Q<VisualElement>("roster-content");
            var profileContent = document.rootVisualElement.Q<VisualElement>("wrestler-profile-content");
            var scheduleContent = document.rootVisualElement.Q<VisualElement>("show-schedule-content");
            var showContent = document.rootVisualElement.Q<VisualElement>("show-planning-content");
            if (overview != null) overview.style.display = roster ? DisplayStyle.None : DisplayStyle.Flex;
            if (rosterContent != null) rosterContent.style.display = roster ? DisplayStyle.Flex : DisplayStyle.None;
            if (profileContent != null) profileContent.style.display = DisplayStyle.None;
            if (scheduleContent != null) scheduleContent.style.display = DisplayStyle.None;
            if (showContent != null) showContent.style.display = DisplayStyle.None;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", !roster);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", roster);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", false);
            Set("dashboard-page-title", roster ? "로스터 / 전체 선수" : "홈 / 개요");
            if (roster) RenderRoster();
            if (recordNavigation) RecordNavigation(new NavigationEntry(roster ? DashboardPage.Roster : DashboardPage.Home));
        }

        private void ShowSchedulePage(bool recordNavigation = true)
        {
            document.rootVisualElement.Q<VisualElement>("overview-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("roster-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("wrestler-profile-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-planning-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-schedule-content").style.display = DisplayStyle.Flex;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", true);
            Set("dashboard-page-title", "쇼 / 전체 일정");
            RenderShowSchedule();
            if (recordNavigation) RecordNavigation(new NavigationEntry(DashboardPage.ShowSchedule));
        }

        private void ShowPlanningPage(string showId = null, bool recordNavigation = true)
        {
            if (!string.IsNullOrEmpty(showId)) { selectedShowId = showId; selectedShowEventId = null; }
            document.rootVisualElement.Q<VisualElement>("overview-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("roster-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("wrestler-profile-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-schedule-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-planning-content").style.display = DisplayStyle.Flex;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", true);
            RenderShowPlanning();
            var show = NextPlanningShow();
            Set("dashboard-page-title", show == null ? "쇼 / 쇼 기획" : $"쇼 / {(string.IsNullOrWhiteSpace(show.Name) ? ShowName(show.ShowType) : show.Name)}");
            if (recordNavigation) RecordNavigation(new NavigationEntry(DashboardPage.ShowPlanning, selectedShowId));
        }

        private void ShowProfilePage()
        {
            var overview = document.rootVisualElement.Q<VisualElement>("overview-content");
            var rosterContent = document.rootVisualElement.Q<VisualElement>("roster-content");
            var profileContent = document.rootVisualElement.Q<VisualElement>("wrestler-profile-content");
            var scheduleContent = document.rootVisualElement.Q<VisualElement>("show-schedule-content");
            var showContent = document.rootVisualElement.Q<VisualElement>("show-planning-content");
            if (overview != null) overview.style.display = DisplayStyle.None;
            if (rosterContent != null) rosterContent.style.display = DisplayStyle.None;
            if (profileContent != null) profileContent.style.display = DisplayStyle.Flex;
            if (scheduleContent != null) scheduleContent.style.display = DisplayStyle.None;
            if (showContent != null) showContent.style.display = DisplayStyle.None;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", true);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", false);
        }

        private void RecordNavigation(NavigationEntry entry)
        {
            if (applyingNavigation || entry == null) return;
            if (navigationIndex >= 0 && navigationIndex < navigationHistory.Count && navigationHistory[navigationIndex].Matches(entry))
            {
                UpdateNavigationButtons();
                return;
            }
            if (navigationIndex < navigationHistory.Count - 1)
                navigationHistory.RemoveRange(navigationIndex + 1, navigationHistory.Count - navigationIndex - 1);
            navigationHistory.Add(entry);
            navigationIndex = navigationHistory.Count - 1;
            UpdateNavigationButtons();
        }

        private void NavigateBack()
        {
            if (navigationIndex <= 0) return;
            navigationIndex--;
            ApplyNavigation(navigationHistory[navigationIndex]);
        }

        private void NavigateForward()
        {
            if (navigationIndex < 0 || navigationIndex >= navigationHistory.Count - 1) return;
            navigationIndex++;
            ApplyNavigation(navigationHistory[navigationIndex]);
        }

        private void ApplyNavigation(NavigationEntry entry)
        {
            applyingNavigation = true;
            try
            {
                WrestlerProfileController.CloseOpenProfile(false);
                switch (entry.Page)
                {
                    case DashboardPage.Home: ShowPage(false, false); break;
                    case DashboardPage.Roster: ShowPage(true, false); break;
                    case DashboardPage.ShowSchedule: ShowSchedulePage(false); break;
                    case DashboardPage.ShowPlanning: ShowPlanningPage(entry.ContextId, false); break;
                    case DashboardPage.WrestlerProfile:
                    {
                        var wrestler = boundSave?.Wrestlers?.FirstOrDefault(x => x?.Id == entry.ContextId);
                        if (wrestler != null) WrestlerProfileController.Open(wrestler);
                        else ShowPage(true, false);
                        break;
                    }
                }
            }
            finally
            {
                applyingNavigation = false;
                UpdateNavigationButtons();
            }
        }

        private void UpdateNavigationButtons()
        {
            document?.rootVisualElement?.Q<Button>("dashboard-back")?.SetEnabled(navigationIndex > 0);
            document?.rootVisualElement?.Q<Button>("dashboard-forward")?.SetEnabled(navigationIndex >= 0 && navigationIndex < navigationHistory.Count - 1);
        }

        private void ChangeRosterSort(string key)
        {
            var existing = rosterSorts.FindIndex(x => x.Key == key);
            var ascending = existing < 0 || !rosterSorts[existing].Ascending;
            if (existing >= 0) rosterSorts.RemoveAt(existing);
            rosterSorts.Insert(0, new SortCriterion(key, ascending));
            UpdateSortHeaders();
            RenderRoster();
        }

        private void UpdateSortHeaders()
        {
            foreach (var key in new[] { "name", "gender", "age", "height", "weight", "role", "style", "match", "promo", "status", "momentum", "condition", "satisfaction", "team", "salary" })
            {
                var button = document.rootVisualElement.Q<Button>($"sort-{key}");
                if (button == null) continue;
                var criterion = rosterSorts.FirstOrDefault(x => x.Key == key);
                button.EnableInClassList("sorted", criterion.Key != null);
                button.text = HeaderText(key) + (criterion.Key == null ? string.Empty : criterion.Ascending ? "  ↑" : "  ↓");
            }
        }

        private void RenderRoster()
        {
            if (document == null) return;
            var rows = document.rootVisualElement.Q<VisualElement>("roster-rows");
            if (rows == null) return;
            rows.Clear();
            var all = boundSave?.Wrestlers?.Where(x => x?.Identity != null).ToList() ?? new List<WrestlerState>();
            var managers = boundSave?.Managers?.Count(x => x != null && x.ActivityState != RosterActivityState.Released) ?? 0;
            Set("roster-summary", $"총 {all.Count + managers}명 · 선수 {all.Count} · 전문 매니저 {managers}");
            if (rosterMode == RosterMode.Managers)
            {
                var empty = new Label(managers == 0 ? "등록된 전문 매니저가 없습니다" : $"전문 매니저 {managers}명 · 상세 명단 데이터 준비 중"); empty.AddToClassList("roster-empty"); rows.Add(empty); return;
            }
            IEnumerable<WrestlerState> filtered = all;
            if (!string.IsNullOrWhiteSpace(rosterSearch))
            {
                var query = rosterSearch.Trim();
                filtered = filtered.Where(x => DisplayName(x).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (x.Identity.LegalName ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var result = SortRoster(filtered).ToList();
            if (result.Count == 0) { var empty = new Label("표시할 선수가 없습니다"); empty.AddToClassList("roster-empty"); rows.Add(empty); return; }
            var contracts = boundSave?.Contracts ?? new List<ContractState>();
            var teams = boundSave?.TagTeams ?? new List<TagTeamState>();
            for (var i = 0; i < result.Count; i++)
            {
                var wrestler = result[i];
                var row = new VisualElement(); row.AddToClassList("roster-row"); if ((i & 1) == 1) row.AddToClassList("roster-row-alt");
                var nameLink = WrestlerProfileController.CreateLink(wrestler, "roster-name-link"); nameLink.AddToClassList("roster-cell"); nameLink.AddToClassList("roster-col-name"); row.Add(nameLink);
                AddCell(row, GenderText(wrestler.Identity.Gender), "roster-col-gender");
                AddCell(row, Age(wrestler).ToString(), "roster-col-age");
                AddCell(row, $"{wrestler.Identity.HeightCm} cm", "roster-col-height");
                AddCell(row, $"{wrestler.Identity.WeightKg} kg", "roster-col-weight");
                AddCell(row, AlignmentText(wrestler.Roster.Alignment), "roster-col-role", AlignmentClass(wrestler.Roster.Alignment));
                AddCell(row, StyleText(wrestler.Presentation?.WrestlingStyleId), "roster-col-style");
                AddGradeCell(row, WrestlerOverallCalculator.Match(wrestler), "roster-col-match");
                AddGradeCell(row, WrestlerOverallCalculator.Promo(wrestler), "roster-col-promo");
                AddCell(row, StatusText(wrestler), "roster-col-status", StatusClass(wrestler));
                AddMeterCell(row, wrestler.Momentum?.Momentum ?? 0f, "roster-col-momentum", MeterKind.Momentum);
                AddMeterCell(row, wrestler.Condition?.Condition ?? 100f, "roster-col-condition", MeterKind.Condition);
                AddMeterCell(row, wrestler.Condition?.Satisfaction ?? 0f, "roster-col-satisfaction", MeterKind.Satisfaction);
                AddCell(row, TeamText(wrestler, teams), "roster-col-team", "roster-cell-muted");
                var contract = contracts.FirstOrDefault(x => x != null && x.PersonId == wrestler.Id && (x.Status == ContractStatus.Active || x.Status == ContractStatus.Expiring));
                AddCell(row, contract == null ? "—" : Money(contract.MonthlySalary), "roster-col-salary");
                rows.Add(row);
            }
        }

        private IEnumerable<WrestlerState> SortRoster(IEnumerable<WrestlerState> source)
        {
            IOrderedEnumerable<WrestlerState> ordered = null;
            foreach (var criterion in rosterSorts)
            {
                var selector = SortSelector(criterion.Key);
                ordered = ordered == null ? (criterion.Ascending ? source.OrderBy(selector) : source.OrderByDescending(selector)) : (criterion.Ascending ? ordered.ThenBy(selector) : ordered.ThenByDescending(selector));
            }
            return (ordered ?? source.OrderBy(x => DisplayName(x))).ThenBy(x => DisplayName(x));
        }

        private Func<WrestlerState, IComparable> SortSelector(string key) => key switch
        {
            "gender" => x => x.Identity.Gender, "age" => x => Age(x), "height" => x => x.Identity.HeightCm, "weight" => x => x.Identity.WeightKg,
            "role" => x => AlignmentText(x.Roster.Alignment), "style" => x => StyleText(x.Presentation?.WrestlingStyleId), "match" => x => WrestlerOverallCalculator.Match(x),
            "promo" => x => WrestlerOverallCalculator.Promo(x), "status" => x => x.Status?.StatusValue ?? 0, "momentum" => x => x.Momentum?.Momentum ?? 0,
            "condition" => x => x.Condition?.Condition ?? 100f, "satisfaction" => x => x.Condition?.Satisfaction ?? 0,
            "team" => x => x.Roster?.ActiveTagTeamId ?? x.Roster?.ActiveStableId ?? string.Empty, "salary" => x => Salary(x), _ => x => DisplayName(x)
        };

        private long Salary(WrestlerState wrestler) => boundSave?.Contracts?.FirstOrDefault(x => x != null && x.PersonId == wrestler.Id && (x.Status == ContractStatus.Active || x.Status == ContractStatus.Expiring))?.MonthlySalary ?? 0;
        private int Age(WrestlerState wrestler) { var now = boundSave?.CurrentDate ?? default; var birth = wrestler.Identity.BirthDate; return Math.Max(0, now.Year - birth.Year - ((now.Month < birth.Month || now.Month == birth.Month && now.Day < birth.Day) ? 1 : 0)); }
        private static string DisplayName(WrestlerState x) => string.IsNullOrWhiteSpace(x?.Identity?.RingName) ? x?.Identity?.LegalName ?? "—" : x.Identity.RingName;
        private static void AddCell(VisualElement row, string text, string columnClass, string extraClass = null) { var cell = new Label(text ?? "—"); cell.AddToClassList("roster-cell"); cell.AddToClassList(columnClass); if (!string.IsNullOrEmpty(extraClass)) cell.AddToClassList(extraClass); row.Add(cell); }
        private static void AddGradeCell(VisualElement row, float value, string columnClass) { var grade = WrestlerOverallCalculator.Grade(value); var cell = new Label(grade); cell.AddToClassList("roster-cell"); cell.AddToClassList(columnClass); cell.AddToClassList("roster-grade"); cell.AddToClassList(GradeClass(grade)); row.Add(cell); }
        private static void AddMeterCell(VisualElement row, float value, string columnClass, MeterKind kind) { value = Mathf.Clamp(value, 0, 100); AddCell(row, $"{value:0}", columnClass, MeterClass(value, kind)); }
        private static string GradeClass(string grade) => grade switch { "SS" => "grade-ss", "S+" => "grade-sp", "S" => "grade-s", "A+" => "grade-ap", "A" => "grade-a", "B+" => "grade-bp", "B" => "grade-b", "C" => "grade-c", "D" => "grade-d", _ => "grade-e" };
        private static string MeterClass(float value, MeterKind kind) => kind switch { MeterKind.Momentum => value < 20 ? "meter-low" : value < 40 ? "meter-blue" : value < 50 ? "meter-cyan" : value < 80 ? "meter-orange" : "meter-gold", _ => value < 40 ? "meter-red" : value < 60 ? "meter-orange" : value < 80 ? "meter-yellow" : "meter-green" };
        private static string GenderText(WrestlerGender value) => value == WrestlerGender.Female ? "여성" : "남성";
        private static string AlignmentText(KayfabeAlignment value) => value switch { KayfabeAlignment.Face => "페이스", KayfabeAlignment.Heel => "힐", _ => "트위너" };
        private static string AlignmentClass(KayfabeAlignment value) => value switch { KayfabeAlignment.Face => "role-face", KayfabeAlignment.Heel => "role-heel", _ => "role-tweener" };
        private static string StyleText(string id) => id switch { "style_001" => "브롤러", "style_002" => "파워하우스", "style_003" => "테크니션", "style_004" => "하이플라이어", "style_005" => "루차 리브레", "style_006" => "자이언트", _ => "올라운더" };
        private static string StatusText(WrestlerState x) => x.Status?.IsRookie == true ? "신인" : (x.Status?.StatusValue ?? 0) >= 80 ? "메인이벤터" : (x.Status?.StatusValue ?? 0) >= 45 ? "미드카더" : "자버";
        private static string StatusClass(WrestlerState x) => x.Status?.IsRookie == true ? "status-rookie" : (x.Status?.StatusValue ?? 0) >= 80 ? "status-main" : (x.Status?.StatusValue ?? 0) >= 45 ? "status-mid" : "status-jobber";
        private static string TeamText(WrestlerState x, List<TagTeamState> teams) { if (string.IsNullOrEmpty(x.Roster?.ActiveTagTeamId) && string.IsNullOrEmpty(x.Roster?.ActiveStableId)) return "—"; var team = teams.FirstOrDefault(t => t?.Id == x.Roster.ActiveTagTeamId); return team == null ? x.Roster.ActiveStableId ?? "—" : $"태그팀 · {team.MemberIds.Count}인"; }
        private static string HeaderText(string key) => key switch { "name" => "이름", "gender" => "성별", "age" => "나이", "height" => "키", "weight" => "몸무게", "role" => "역할", "style" => "경기 스타일", "match" => "경기 능력", "promo" => "프로모 능력", "status" => "위상", "momentum" => "모멘텀", "condition" => "컨디션", "satisfaction" => "만족도", "team" => "태그팀 / 스테이블", _ => "급료" };

        private void BindNextShow(GameSave save, ScheduleState schedule, ShowState show)
        {
            if (schedule == null)
            {
                Set("next-show-caption", "다음 쇼 일정"); Set("top-next-show", "예정된 쇼 없음"); Set("next-show-date", "—");
                Set("next-show-name", "예정된 쇼가 없습니다"); Set("next-show-detail", "시즌 일정을 확인하세요"); Set("next-show-type", "—"); Set("next-show-status", "일정 없음");
                SetProgress(0); Set("card-count", "카드 0개"); Set("participant-count", "출연 0명"); Set("estimated-cost", "예상 비용 —"); return;
            }
            Set("next-show-caption", $"다음 쇼 일정 · {save.CurrentDate.DaysUntil(schedule.Date)}일 남음");
            Set("top-next-show", show?.Name ?? ShowName(schedule.ShowType)); Set("next-show-date", $"{schedule.Date.Month:D2}\n{schedule.Date.Day:D2}");
            Set("next-show-name", show?.Name ?? ShowName(schedule.ShowType)); Set("next-show-detail", $"{Date(schedule.Date)} · 부킹 마감 {Date(schedule.BookingDeadline)}");
            Set("next-show-type", ShowType(schedule.ShowType)); Set("next-show-status", show == null ? "카드 미생성" : ShowStatusText(show.Status));
            var events = show == null ? new List<ShowEventState>() : save.ShowEvents?.Where(x => x != null && x.ShowId == show.Id).ToList() ?? new List<ShowEventState>();
            var planned = show?.CalculatePlannedDuration(events) ?? 0; var limit = Math.Max(show?.DurationLimit ?? 0, 1);
            SetProgress(show == null ? 0 : Math.Min(100, Mathf.RoundToInt(planned * 100f / limit)));
            Set("card-count", $"카드 {events.Count}개 · {planned}/{show?.DurationLimit ?? 0}분"); Set("participant-count", $"출연 {ParticipantCount(save, events)}명");
            Set("estimated-cost", show == null ? "예상 비용 —" : $"예상 비용 {Compact(show.EstimatedCost)}");
        }

        private static List<TaskItem> BuildTasks(GameSave save, ScheduleState schedule, ShowState show, List<WrestlerState> wrestlers)
        {
            var items = new List<TaskItem>();
            if (schedule != null && show == null) items.Add(new TaskItem("긴급", "다음 쇼 카드가 생성되지 않았습니다", "쇼 기획"));
            else if (show != null && string.IsNullOrEmpty(show.MainEventId)) items.Add(new TaskItem("긴급", "메인 이벤트가 지정되지 않았습니다", "쇼 기획"));
            var contract = save.Contracts?.Where(x => x != null && (x.Status == ContractStatus.Active || x.Status == ContractStatus.Expiring))
                .Select(x => new { Value = x, Days = save.CurrentDate.DaysUntil(x.EndDate) }).Where(x => x.Days >= 0 && x.Days <= 30).OrderBy(x => x.Days).FirstOrDefault();
            if (contract != null) { var wrestler = wrestlers.FirstOrDefault(x => x.Id == contract.Value.PersonId); items.Add(new TaskItem("높음", "계약 만료 임박", $"{contract.Days}일 남음", wrestler)); }
            var injured = wrestlers.FirstOrDefault(x => x.Condition?.InjuryStatus != InjuryStatus.None);
            if (injured != null) items.Add(new TaskItem("높음", "부상 상태", injured.Condition.InjuryStatus.ToString(), injured));
            var risk = wrestlers.Where(x => x.Condition != null).OrderBy(x => x.Condition.Condition).FirstOrDefault(x => x.Condition.Condition < 40f);
            if (risk != null) items.Add(new TaskItem("관심", "컨디션 저하", $"{risk.Condition.Condition:0}", risk));
            if (items.Count == 0) items.Add(new TaskItem("정상", "현재 우선 처리할 업무가 없습니다", ""));
            return items.Take(4).ToList();
        }

        private void BindTasks(List<TaskItem> items)
        {
            for (var i = 0; i < 4; i++)
            {
                var row = document.rootVisualElement.Q<VisualElement>($"task-{i}"); if (row == null) continue;
                row.style.display = i < items.Count ? DisplayStyle.Flex : DisplayStyle.None; if (i >= items.Count) continue;
                row.Q<Button>("task-wrestler-link")?.RemoveFromHierarchy();
                if (items[i].Wrestler != null) { var link = WrestlerProfileController.CreateLink(items[i].Wrestler); link.name = "task-wrestler-link"; row.Insert(1, link); }
                Set($"task-{i}-type", items[i].Severity); Set($"task-{i}-text", items[i].Text); Set($"task-{i}-link", items[i].Context);
            }
        }

        private void BindFanReaction(List<WrestlerState> wrestlers)
        {
            var values = wrestlers.Where(x => x.FanReaction != null).ToList();
            if (values.Count == 0) { Set("fan-caption", "데이터 없음"); Set("mania-value", "—"); Set("light-value", "—"); Set("family-value", "—"); FanBars(0, 0, 0); return; }
            var mania = values.Average(x => x.FanReaction.ManiaFanReaction); var light = values.Average(x => x.FanReaction.LightFanReaction); var family = values.Average(x => x.FanReaction.FamilyFanReaction);
            Set("fan-caption", $"{values.Count}명 기준"); Set("mania-value", Signed(mania)); Set("light-value", Signed(light)); Set("family-value", Signed(family)); FanBars(mania, light, family);
        }

        private void RenderShowPlanning()
        {
            var rows = document?.rootVisualElement?.Q<VisualElement>("show-timeline-rows");
            var timelineScroll = document?.rootVisualElement?.Q<ScrollView>("show-timeline-scroll");
            var participants = document?.rootVisualElement?.Q<VisualElement>("show-detail-participants");
            if (rows == null || timelineScroll == null || participants == null) return;
            timelineScroll.style.display = DisplayStyle.Flex;
            rows.Clear();
            participants.Clear();

            var show = NextPlanningShow();
            RefreshShowActions(show);
            if (show == null)
            {
                selectedShowEventId = null;
                Set("show-plan-name", "예정된 쇼 없음");
                Set("show-plan-meta", "확정된 일정에 쇼가 생성되면 이곳에서 편성 내용을 확인할 수 있습니다");
                Set("show-plan-status", "—"); Set("show-plan-cost", "—"); Set("show-event-count", "0개 세그먼트");
                Set("show-duration-used", "0분 사용"); Set("show-duration-limit", "총 0분");
                Set("show-match-count", "0"); Set("show-promo-count", "0"); Set("show-participant-count", "0");
                Set("show-planning-notice", "편성할 쇼가 없습니다"); Width("show-duration-fill", 0);
                ClearShowDetail();
                rows.Add(new Label("예정된 쇼가 없습니다") { name = "show-empty" });
                rows.Q<Label>("show-empty")?.AddToClassList("show-empty-state");
                return;
            }

            var eventsById = (boundSave.ShowEvents ?? new List<ShowEventState>()).Where(x => x != null && x.ShowId == show.Id)
                .ToDictionary(x => x.Id, StringComparer.Ordinal);
            var ordered = (show.TimelineEventIds ?? new List<string>()).Where(eventsById.ContainsKey).Select(id => eventsById[id]).ToList();
            if (selectedShowEventId == null || ordered.All(x => x.Id != selectedShowEventId)) selectedShowEventId = ordered.FirstOrDefault()?.Id;

            Set("show-plan-name", string.IsNullOrWhiteSpace(show.Name) ? ShowName(show.ShowType) : show.Name);
            Set("show-plan-meta", $"{Date(show.Date)} · {ShowType(show.ShowType)} · {ShowVenueName(show)} · 제한 시간 {show.DurationLimit}분");
            Set("show-plan-status", ShowStatusText(show.Status)); Set("show-plan-cost", Money(show.EstimatedCost));
            Set("show-event-count", $"{ordered.Count}개 세그먼트");

            for (var index = 0; index < ordered.Count; index++)
            {
                var showEvent = ordered[index];
                var row = new VisualElement { name = $"show-event-{showEvent.Id}" }; row.AddToClassList("show-event-row");
                row.EnableInClassList("selected", showEvent.Id == selectedShowEventId);
                row.EnableInClassList("opening", index == 0);
                row.EnableInClassList("main-event", index == ordered.Count - 1);
                var handle = new Label("⋮⋮") { tooltip = "드래그하여 순서 변경" }; handle.AddToClassList("show-event-drag-handle"); row.Add(handle);
                var order = new Label((index + 1).ToString("00")); order.AddToClassList("show-event-order"); row.Add(order);
                var type = new Label(showEvent.EventType == ShowEventType.Match ? "경기" : "프로모"); type.AddToClassList("show-event-type");
                type.EnableInClassList("promo", showEvent.EventType == ShowEventType.Promo); row.Add(type);
                var copy = new VisualElement(); copy.AddToClassList("show-event-copy");
                var title = new Label(ShowEventTitle(showEvent)); title.AddToClassList("show-event-title"); copy.Add(title);
                var cast = new Label(string.Join(" · ", ShowEventParticipantIds(showEvent).Select(WrestlerName))); cast.AddToClassList("show-event-participants"); copy.Add(cast); row.Add(copy);
                var duration = new Label($"{showEvent.PlannedDuration}분"); duration.AddToClassList("show-event-duration"); row.Add(duration);
                var positionText = ordered.Count == 1 ? "오프닝 · 메인" : index == 0 ? "오프닝" : index == ordered.Count - 1 ? "메인 이벤트" : string.Empty;
                var position = new Label(positionText); position.AddToClassList("show-event-position-label"); row.Add(position);
                var captured = showEvent.Id; row.AddManipulator(new Clickable(() => { selectedShowEventId = captured; RenderShowPlanning(); }));
                handle.AddManipulator(new ShowTimelineDragManipulator(
                    row,
                    () => show.TimelineEventIds.IndexOf(captured),
                    TimelineDropIndex,
                    (from, to) => MoveEventByIndex(show, from, to)));
                rows.Add(row);
            }
            if (ordered.Count == 0) { var empty = new Label("아직 편성된 경기나 프로모가 없습니다"); empty.AddToClassList("show-empty-state"); rows.Add(empty); }

            var used = show.CalculatePlannedDuration(ordered);
            var matchCount = ordered.Count(x => x.EventType == ShowEventType.Match);
            var promoCount = ordered.Count - matchCount;
            var castCount = ordered.SelectMany(ShowEventParticipantIds).Distinct(StringComparer.Ordinal).Count();
            Set("show-duration-used", $"{used}분 사용"); Set("show-duration-limit", $"총 {show.DurationLimit}분");
            Set("show-match-count", matchCount.ToString()); Set("show-promo-count", promoCount.ToString()); Set("show-participant-count", castCount.ToString());
            var percent = show.DurationLimit <= 0 ? 0 : Math.Min(100f, used * 100f / show.DurationLimit); Width("show-duration-fill", percent);
            document.rootVisualElement.Q<VisualElement>("show-duration-fill")?.EnableInClassList("over", used > show.DurationLimit);
            Set("show-planning-notice", used == show.DurationLimit ? "제한 시간에 맞게 편성되었습니다" : used < show.DurationLimit ? $"남은 편성 시간 {show.DurationLimit - used}분" : $"제한 시간을 {used - show.DurationLimit}분 초과했습니다");
            RenderShowDetail(show, ordered);
        }

        private ShowState NextPlanningShow()
        {
            if (boundSave?.Shows == null) return null;
            var explicitlySelected = boundSave.Shows.FirstOrDefault(x => x != null && x.Id == selectedShowId);
            if (explicitlySelected != null) return explicitlySelected;
            var available = boundSave.Shows.Where(x => x != null && x.Status != ShowStatus.Completed)
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).ToList();
            var selected = available.FirstOrDefault(x => x.Id == selectedShowId);
            if (selected != null) return selected;
            selected = available.FirstOrDefault(x => x.Date.CompareTo(boundSave.CurrentDate) >= 0) ?? available.FirstOrDefault();
            selectedShowId = selected?.Id;
            return selected;
        }

        private void RefreshShowActions(ShowState selected)
        {
            foreach (var id in new[] { "show-add-match", "show-add-promo", "show-save-draft", "show-confirm" })
                document.rootVisualElement.Q<Button>(id)?.SetEnabled(selected != null && selected.Status != ShowStatus.Confirmed && selected.Status != ShowStatus.Completed);
        }

        private void AddEmptyMatch()
        {
            try
            {
                var show = NextPlanningShow() ?? throw new InvalidOperationException("편성할 쇼를 선택하세요.");
                var type = MatchTypes().FirstOrDefault(x => x.Id == "matchtype_001") ?? MatchTypes().FirstOrDefault()
                    ?? throw new InvalidOperationException("경기 유형 데이터를 불러오지 못했습니다.");
                var added = showPlanningService.AddMatch(boundSave, show.Id, new MatchPlanState
                {
                    MatchTypeId = type.Id,
                    MatchGimmickId = "gimmick_000",
                    TeamCount = 2,
                    MembersPerTeam = 1,
                    Sides = Enumerable.Range(0, 2).Select(_ => new MatchSideState
                    {
                        Id = PWManager.Domain.Identifiers.EntityId.CreateRuntimeId(),
                        MemberIds = new List<string> { null }
                    }).ToList(),
                    FinishType = MatchFinishType.Draw
                }, 20);
                selectedShowEventId = added.Id;
                DashboardSession.PersistActive();
                RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void AddEmptyPromo()
        {
            try
            {
                var show = NextPlanningShow() ?? throw new InvalidOperationException("편성할 쇼를 선택하세요.");
                var added = showPlanningService.AddPromo(boundSave, show.Id, new PromoPlanState
                {
                    Purpose = PromoPurpose.CharacterIntroduction,
                    Presentation = PromoPresentation.InRingMic
                }, 10);
                selectedShowEventId = added.Id;
                DashboardSession.PersistActive();
                RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void OpenShowEditor(ShowEditorMode mode)
        {
            var show = NextPlanningShow();
            if (show == null || showPlanningService == null || staticContent == null) return;
            showEditorMode = mode;
            showEditSection = ShowEditSection.Full;
            editingShowEventId = null;
            selectedMatchRule = null;
            selectedMatchGimmickId = null;
            editorParticipantIds.Clear();
            editorParticipantSlots.Clear();
            selectedParticipantSlotIndex = -1;
            pendingParticipantId = null;
            document.rootVisualElement.Q<Button>("show-editor-submit").text = "추가";
            var primary = document.rootVisualElement.Q<DropdownField>("show-editor-primary");
            var secondary = document.rootVisualElement.Q<DropdownField>("show-editor-secondary");
            var duration = document.rootVisualElement.Q<DropdownField>("show-editor-duration");
            duration.choices = Enumerable.Range(1, 12).Select(x => $"{x * 5}분").ToList();
            duration.index = mode == ShowEditorMode.Match ? 3 : 1;
            if (mode == ShowEditorMode.Match)
            {
                var types = MatchTypes();
                primary.label = "경기 유형"; primary.choices = types.Select(x => MatchTypeText(x.Id)).ToList(); primary.index = 0;
                secondary.label = "경기 결과"; secondary.choices = Enum.GetValues(typeof(MatchFinishType)).Cast<MatchFinishType>().Select(MatchFinishText).ToList(); secondary.index = 0;
                Set("show-editor-title", "경기 추가");
            }
            else
            {
                primary.label = "프로모 목적"; primary.choices = Enum.GetValues(typeof(PromoPurpose)).Cast<PromoPurpose>().Select(PromoPurposeText).ToList(); primary.index = 0;
                secondary.label = "진행 방식"; secondary.choices = Enum.GetValues(typeof(PromoPresentation)).Cast<PromoPresentation>().Select(PromoPresentationText).ToList(); secondary.index = 0;
                Set("show-editor-title", "프로모 추가");
            }
            Set("show-editor-message", string.Empty);
            RenderShowEditorRoster(show);
            ConfigureShowEditorSection();
            document.rootVisualElement.Q<VisualElement>("show-editor-overlay")?.RemoveFromClassList("hidden");
        }

        private void OpenSelectedEventEditor(ShowEditSection section)
        {
            var show = NextPlanningShow();
            var showEvent = boundSave?.ShowEvents?.FirstOrDefault(x => x?.Id == selectedShowEventId && x.ShowId == show?.Id);
            if (show == null || showEvent == null) return;
            OpenShowEditor(showEvent.EventType == ShowEventType.Match ? ShowEditorMode.Match : ShowEditorMode.Promo);
            showEditSection = section;
            editingShowEventId = showEvent.Id;
            editorParticipantIds.Clear();
            editorParticipantSlots.Clear();
            selectedParticipantSlotIndex = -1;
            var duration = document.rootVisualElement.Q<DropdownField>("show-editor-duration");
            duration.index = Math.Max(0, showEvent.PlannedDuration / 5 - 1);
            var primary = document.rootVisualElement.Q<DropdownField>("show-editor-primary");
            var secondary = document.rootVisualElement.Q<DropdownField>("show-editor-secondary");
            if (showEvent.EventType == ShowEventType.Match)
            {
                var match = boundSave.MatchPlans.First(x => x.Id == showEvent.DetailId);
                primary.index = Math.Max(0, MatchTypes().FindIndex(x => x.Id == match.MatchTypeId));
                secondary.index = (int)match.FinishType;
                InitializeMatchRuleSelection(match, showEvent);
                InitializeMatchParticipantSlots(match);
                SetSpotFields(match.OpeningSpot, match.MiddleSpot, match.ClosingSpot);
            }
            else
            {
                var promo = boundSave.PromoPlans.First(x => x.Id == showEvent.DetailId);
                foreach (var id in promo.ParticipantIds ?? new List<string>()) editorParticipantIds.Add(id);
                primary.index = (int)promo.Purpose;
                secondary.index = (int)promo.Presentation;
                SetSpotFields(promo.OpeningSpot, promo.MiddleSpot, promo.ClosingSpot);
            }
            Set("show-editor-title", section switch { ShowEditSection.Rules when showEvent.EventType == ShowEventType.Match => "경기 규칙 및 기믹", ShowEditSection.Rules => "규칙 편집", ShowEditSection.Participants => "참가자 편집", _ => "스팟 편집" });
            document.rootVisualElement.Q<Button>("show-editor-submit").text = "변경 저장";
            RenderShowEditorRoster(show);
            ConfigureShowEditorSection();
        }

        private void ConfigureShowEditorSection()
        {
            var fields = document.rootVisualElement.Q<VisualElement>(className: "show-editor-fields");
            var rosterHeading = document.rootVisualElement.Q<VisualElement>(className: "show-editor-roster-heading");
            var rosterScroll = document.rootVisualElement.Q<ScrollView>(className: "show-editor-roster-scroll");
            var spots = document.rootVisualElement.Q<VisualElement>("show-editor-spots");
            var matchRules = document.rootVisualElement.Q<VisualElement>("show-editor-match-rules");
            var showMatchRulePicker = showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Rules;
            var assignsSingleMatchSlot = showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Participants;
            var showRules = (showEditSection is ShowEditSection.Full or ShowEditSection.Rules) && !showMatchRulePicker;
            var showParticipants = showEditSection is ShowEditSection.Full or ShowEditSection.Participants;
            if (fields != null) fields.style.display = showRules ? DisplayStyle.Flex : DisplayStyle.None;
            if (matchRules != null) matchRules.style.display = showMatchRulePicker ? DisplayStyle.Flex : DisplayStyle.None;
            if (rosterHeading != null) rosterHeading.style.display = showParticipants ? DisplayStyle.Flex : DisplayStyle.None;
            if (rosterScroll != null) rosterScroll.style.display = showParticipants ? DisplayStyle.Flex : DisplayStyle.None;
            if (spots != null) spots.style.display = showEditSection == ShowEditSection.Spots ? DisplayStyle.Flex : DisplayStyle.None;
            var submit = document.rootVisualElement.Q<Button>("show-editor-submit");
            if (submit != null)
            {
                submit.style.display = DisplayStyle.Flex;
                if (assignsSingleMatchSlot) submit.text = "결정";
            }
            var cancel = document.rootVisualElement.Q<Button>("show-editor-cancel");
            if (cancel != null) cancel.text = "취소";
            if (showMatchRulePicker) RenderMatchRulePicker();
        }

        private void InitializeMatchRuleSelection(MatchPlanState match, ShowEventState showEvent)
        {
            var participantCount = ShowEventParticipantIds(showEvent).Distinct(StringComparer.Ordinal).Count();
            var options = MatchRuleOptions();
            var teamCount = match.TeamCount > 0 ? match.TeamCount : match.Sides?.Count ?? 0;
            var membersPerTeam = match.MembersPerTeam > 0 ? match.MembersPerTeam : match.Sides?.FirstOrDefault()?.MemberIds?.Count ?? 0;
            if (match.MatchTypeId == "matchtype_001") { teamCount = Math.Max(2, participantCount); membersPerTeam = 1; }
            selectedMatchRule = options.FirstOrDefault(x => x.MatchTypeId == match.MatchTypeId && x.TeamCount == teamCount && x.MembersPerTeam == membersPerTeam)
                ?? options.FirstOrDefault(x => x.ParticipantCount == participantCount)
                ?? options.FirstOrDefault();
            selectedMatchGimmickId = match.MatchGimmickId;
        }

        private void RenderMatchRulePicker()
        {
            var ruleHost = document.rootVisualElement.Q<VisualElement>("show-editor-rule-options");
            var gimmickHost = document.rootVisualElement.Q<VisualElement>("show-editor-gimmick-options");
            if (ruleHost == null || gimmickHost == null) return;
            ruleHost.Clear();
            gimmickHost.Clear();
            foreach (var option in MatchRuleOptions())
            {
                var button = new Button { text = option.Label };
                button.AddToClassList("show-editor-rule-option");
                button.EnableInClassList("selected", selectedMatchRule?.Key == option.Key);
                button.clicked += () =>
                {
                    selectedMatchRule = option;
                    if (!CompatibleGimmicks(option).Any(x => x.Id == selectedMatchGimmickId))
                        selectedMatchGimmickId = CompatibleGimmicks(option).FirstOrDefault()?.Id;
                    RenderMatchRulePicker();
                };
                ruleHost.Add(button);
            }
            if (selectedMatchRule == null) return;
            var gimmicks = CompatibleGimmicks(selectedMatchRule).ToList();
            foreach (var gimmick in gimmicks)
            {
                var button = new Button { text = MatchGimmickText(gimmick) };
                button.AddToClassList("show-editor-rule-option");
                button.EnableInClassList("selected", gimmick.Id == selectedMatchGimmickId);
                button.clicked += () => { selectedMatchGimmickId = gimmick.Id; RenderMatchRulePicker(); };
                gimmickHost.Add(button);
            }
            if (gimmicks.Count == 0)
            {
                var empty = new Label("사용 가능한 기믹이 없습니다");
                empty.AddToClassList("show-editor-rule-empty");
                gimmickHost.Add(empty);
            }
        }

        private void SetSpotFields(string opening, string middle, string closing)
        {
            document.rootVisualElement.Q<TextField>("show-editor-opening-spot").value = opening ?? string.Empty;
            document.rootVisualElement.Q<TextField>("show-editor-middle-spot").value = middle ?? string.Empty;
            document.rootVisualElement.Q<TextField>("show-editor-closing-spot").value = closing ?? string.Empty;
        }

        private void RenderShowEditorRoster(ShowState show)
        {
            var host = document.rootVisualElement.Q<VisualElement>("show-editor-roster");
            if (host == null) return;
            host.Clear();
            var usesMatchSlots = showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Participants;
            var wrestlers = (boundSave.Wrestlers ?? new List<WrestlerState>()).Where(x => x != null &&
                x.Roster?.ActivityState == RosterActivityState.Active && boundSave.Contracts.Any(c => c != null && c.PersonId == x.Id &&
                (c.Status == ContractStatus.Active || c.Status == ContractStatus.Expiring) && c.StartDate.CompareTo(show.Date) <= 0 && c.EndDate.CompareTo(show.Date) >= 0))
                .OrderBy(DisplayName).ToList();
            foreach (var wrestler in wrestlers)
            {
                var button = CreateWrestlerSelectionCard(wrestler);
                button.EnableInClassList("selected", usesMatchSlots
                    ? pendingParticipantId == wrestler.Id
                    : editorParticipantIds.Contains(wrestler.Id));
                var id = wrestler.Id;
                button.clicked += () =>
                {
                    if (usesMatchSlots) { pendingParticipantId = id; RenderShowEditorRoster(show); }
                    else { if (!editorParticipantIds.Remove(id)) editorParticipantIds.Add(id); RenderShowEditorRoster(show); }
                };
                host.Add(button);
            }
            Set("show-editor-selected-count", usesMatchSlots
                ? $"{editorParticipantSlots.Count(x => !string.IsNullOrEmpty(x))}/{editorParticipantSlots.Count}명 배정"
                : $"{editorParticipantIds.Count}명 선택");
        }

        private static Button CreateWrestlerSelectionCard(WrestlerState wrestler)
        {
            var card = new Button();
            card.AddToClassList("show-editor-wrestler");
            var portrait = new VisualElement();
            portrait.AddToClassList("show-editor-wrestler-portrait");
            var portraitPath = wrestler.Presentation?.PortraitResourcePath;
            var texture = string.IsNullOrWhiteSpace(portraitPath) ? null : Resources.Load<Texture2D>(portraitPath);
            if (texture != null)
            {
                var image = new Image { image = texture, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                image.AddToClassList("show-editor-wrestler-image");
                portrait.Add(image);
            }
            else
            {
                var parts = DisplayName(wrestler).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var initials = new Label(string.Concat(parts.Take(2).Select(x => x[0])).ToUpperInvariant());
                initials.AddToClassList("show-editor-wrestler-initials");
                portrait.Add(initials);
            }
            var info = new VisualElement();
            info.AddToClassList("show-editor-wrestler-info");
            var nameLine = new VisualElement(); nameLine.AddToClassList("show-editor-wrestler-line");
            var name = WrestlerProfileController.CreateLink(wrestler, "show-editor-wrestler-name"); nameLine.Add(name);
            var identity = new VisualElement(); identity.AddToClassList("show-editor-wrestler-line"); identity.AddToClassList("show-editor-wrestler-identity");
            var gender = new Label(GenderText(wrestler.Identity.Gender)); gender.AddToClassList("show-editor-wrestler-tag"); identity.Add(gender);
            var style = new Label(StyleText(wrestler.Presentation?.WrestlingStyleId)); style.AddToClassList("show-editor-wrestler-tag"); identity.Add(style);
            var ratings = new VisualElement(); ratings.AddToClassList("show-editor-wrestler-line"); ratings.AddToClassList("show-editor-wrestler-ratings");
            var matchGrade = WrestlerOverallCalculator.Grade(WrestlerOverallCalculator.Match(wrestler));
            AddSelectionMetric(ratings, "경기", matchGrade, GradeClass(matchGrade));
            var promoGrade = WrestlerOverallCalculator.Grade(WrestlerOverallCalculator.Promo(wrestler));
            AddSelectionMetric(ratings, "프로모", promoGrade, GradeClass(promoGrade));
            var state = new VisualElement(); state.AddToClassList("show-editor-wrestler-line"); state.AddToClassList("show-editor-wrestler-state");
            var momentumValue = wrestler.Momentum?.Momentum ?? 0;
            AddSelectionMetric(state, "모멘텀", $"{momentumValue:0}", MeterClass(momentumValue, MeterKind.Momentum));
            var conditionValue = wrestler.Condition?.Condition ?? 100;
            AddSelectionMetric(state, "컨디션", $"{conditionValue:0}", MeterClass(conditionValue, MeterKind.Condition));
            var satisfactionValue = wrestler.Condition?.Satisfaction ?? 0;
            AddSelectionMetric(state, "만족도", $"{satisfactionValue:0}", MeterClass(satisfactionValue, MeterKind.Satisfaction));
            info.Add(nameLine); info.Add(identity); info.Add(ratings); info.Add(state);
            card.Add(portrait); card.Add(info);
            return card;
        }

        private static void AddSelectionMetric(VisualElement row, string title, string value, string valueClass)
        {
            var metric = new VisualElement(); metric.AddToClassList("show-editor-metric");
            var label = new Label(title); label.AddToClassList("show-editor-metric-label");
            var result = new Label(value); result.AddToClassList("show-editor-metric-value"); result.AddToClassList(valueClass);
            metric.Add(label); metric.Add(result); row.Add(metric);
        }

        private void InitializeMatchParticipantSlots(MatchPlanState match)
        {
            editorParticipantSlots.Clear();
            selectedParticipantSlotIndex = -1;
            var required = match.TeamCount > 0 && match.MembersPerTeam > 0 ? match.TeamCount * match.MembersPerTeam : 0;
            if (required <= 0) return;
            for (var teamIndex = 0; teamIndex < match.TeamCount; teamIndex++)
            for (var memberIndex = 0; memberIndex < match.MembersPerTeam; memberIndex++)
            {
                var members = match.Sides != null && teamIndex < match.Sides.Count ? match.Sides[teamIndex]?.MemberIds : null;
                editorParticipantSlots.Add(members != null && memberIndex < members.Count && !string.IsNullOrWhiteSpace(members[memberIndex]) ? members[memberIndex] : null);
            }
            if (editorParticipantSlots.All(string.IsNullOrEmpty))
            {
                var legacy = (match.ParticipantIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Take(required).ToList();
                for (var index = 0; index < legacy.Count; index++) editorParticipantSlots[index] = legacy[index];
            }
            selectedParticipantSlotIndex = editorParticipantSlots.FindIndex(string.IsNullOrEmpty);
            if (selectedParticipantSlotIndex < 0 && editorParticipantSlots.Count > 0) selectedParticipantSlotIndex = 0;
            pendingParticipantId = selectedParticipantSlotIndex >= 0 ? editorParticipantSlots[selectedParticipantSlotIndex] : null;
        }

        private void AssignWrestlerToSelectedSlot(string wrestlerId, ShowState show)
        {
            try
            {
                if (selectedParticipantSlotIndex < 0 || selectedParticipantSlotIndex >= editorParticipantSlots.Count)
                    throw new InvalidOperationException("배치할 참가자 슬롯을 먼저 선택하세요.");
                var showEvent = boundSave.ShowEvents.FirstOrDefault(x => x?.Id == editingShowEventId && x.ShowId == show?.Id)
                    ?? throw new InvalidOperationException("편집할 경기를 찾을 수 없습니다.");
                var match = boundSave.MatchPlans.FirstOrDefault(x => x?.Id == showEvent.DetailId)
                    ?? throw new InvalidOperationException("경기 세부 정보를 찾을 수 없습니다.");
                var previousSlot = editorParticipantSlots.FindIndex(x => string.Equals(x, wrestlerId, StringComparison.Ordinal));
                if (previousSlot >= 0 && previousSlot != selectedParticipantSlotIndex) editorParticipantSlots[previousSlot] = null;
                editorParticipantSlots[selectedParticipantSlotIndex] = wrestlerId;
                match.Sides = Enumerable.Range(0, match.TeamCount).Select(teamIndex => new MatchSideState
                {
                    Id = match.Sides != null && teamIndex < match.Sides.Count && !string.IsNullOrEmpty(match.Sides[teamIndex]?.Id)
                        ? match.Sides[teamIndex].Id
                        : PWManager.Domain.Identifiers.EntityId.CreateRuntimeId(),
                    MemberIds = editorParticipantSlots.Skip(teamIndex * match.MembersPerTeam).Take(match.MembersPerTeam).ToList()
                }).ToList();
                match.ParticipantIds.Clear();
                match.WinningSideId = null;
                match.FinishPerformerId = null;
                match.LoserTargetId = null;
                DashboardSession.PersistActive();
                CloseShowEditor();
                RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-editor-message", exception.Message); }
        }

        private void CloseShowEditor() => document.rootVisualElement.Q<VisualElement>("show-editor-overlay")?.AddToClassList("hidden");

        private void SubmitShowEditor()
        {
            try
            {
                var show = NextPlanningShow() ?? throw new InvalidOperationException("편성할 쇼를 선택하세요.");
                if (!string.IsNullOrEmpty(editingShowEventId) && showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Participants)
                {
                    if (string.IsNullOrEmpty(pendingParticipantId)) throw new InvalidOperationException("배정할 선수를 선택하세요.");
                    AssignWrestlerToSelectedSlot(pendingParticipantId, show);
                    return;
                }
                var primary = document.rootVisualElement.Q<DropdownField>("show-editor-primary");
                var secondary = document.rootVisualElement.Q<DropdownField>("show-editor-secondary");
                var durationField = document.rootVisualElement.Q<DropdownField>("show-editor-duration");
                var duration = (durationField.index + 1) * 5;
                if (!string.IsNullOrEmpty(editingShowEventId) && showEditSection != ShowEditSection.Full)
                {
                    SubmitSelectedShowSection(show, primary, secondary, duration);
                    DashboardSession.PersistActive();
                    CloseShowEditor();
                    RenderShowPlanning();
                    return;
                }
                if (showEditorMode == ShowEditorMode.Match)
                {
                    var types = MatchTypes();
                    if (primary.index < 0 || primary.index >= types.Count) throw new InvalidOperationException("경기 유형을 선택하세요.");
                    var type = types[primary.index];
                    var participants = editorParticipantIds.ToList();
                    if (participants.Count < type.MinimumParticipants || participants.Count > type.MaximumParticipants)
                        throw new InvalidOperationException($"이 경기에는 {type.MinimumParticipants}~{type.MaximumParticipants}명이 필요합니다.");
                    var sides = BuildMatchSides(type, participants);
                    var finish = (MatchFinishType)Math.Max(0, secondary.index);
                    var winning = finish == MatchFinishType.Draw ? null : sides[0];
                    var loser = winning == null ? null : sides.Skip(1).SelectMany(x => x.MemberIds).FirstOrDefault();
                    var plan = new MatchPlanState
                    {
                        MatchTypeId = type.Id, MatchGimmickId = "gimmick_000", Sides = sides,
                        FinishType = finish, WinningSideId = winning?.Id, FinishPerformerId = winning?.MemberIds.FirstOrDefault(), LoserTargetId = loser
                    };
                    if (string.IsNullOrEmpty(editingShowEventId))
                    {
                        var added = showPlanningService.AddMatch(boundSave, show.Id, plan, duration);
                        selectedShowEventId = added.Id;
                    }
                    else showPlanningService.UpdateMatch(boundSave, show.Id, editingShowEventId, plan, duration);
                }
                else
                {
                    if (editorParticipantIds.Count == 0) throw new InvalidOperationException("프로모 출연 선수를 한 명 이상 선택하세요.");
                    var purpose = (PromoPurpose)Math.Max(0, primary.index);
                    var plan = new PromoPlanState
                    {
                        Purpose = purpose, Presentation = (PromoPresentation)Math.Max(0, secondary.index),
                        ParticipantIds = editorParticipantIds.ToList(), SponsorRequirementId = purpose == PromoPurpose.SponsorAdvertisement ? "sponsor_requirement_runtime" : null
                    };
                    if (string.IsNullOrEmpty(editingShowEventId))
                    {
                        var added = showPlanningService.AddPromo(boundSave, show.Id, plan, duration);
                        selectedShowEventId = added.Id;
                    }
                    else
                    {
                        var showEvent = boundSave.ShowEvents.First(x => x.Id == editingShowEventId && x.ShowId == show.Id);
                        plan.Id = showEvent.DetailId;
                        var index = boundSave.PromoPlans.FindIndex(x => x?.Id == plan.Id);
                        if (index < 0) throw new InvalidOperationException("프로모 세부 정보를 찾을 수 없습니다.");
                        boundSave.PromoPlans[index] = plan;
                        showPlanningService.SetEventDuration(boundSave, show.Id, showEvent.Id, duration);
                    }
                }
                DashboardSession.PersistActive();
                CloseShowEditor();
                RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-editor-message", exception.Message); }
        }

        private void SubmitSelectedShowSection(ShowState show, DropdownField primary, DropdownField secondary, int duration)
        {
            var showEvent = boundSave.ShowEvents.FirstOrDefault(x => x?.Id == editingShowEventId && x.ShowId == show.Id)
                ?? throw new InvalidOperationException("편집할 항목을 찾을 수 없습니다.");
            if (showEvent.EventType == ShowEventType.Match)
            {
                var match = boundSave.MatchPlans.FirstOrDefault(x => x?.Id == showEvent.DetailId)
                    ?? throw new InvalidOperationException("경기 세부 정보를 찾을 수 없습니다.");
                if (showEditSection == ShowEditSection.Rules)
                {
                    var rule = selectedMatchRule ?? throw new InvalidOperationException("경기 규칙을 선택하세요.");
                    var type = MatchTypes().FirstOrDefault(x => x.Id == rule.MatchTypeId)
                        ?? throw new InvalidOperationException("선택한 경기 규칙을 찾을 수 없습니다.");
                    if (string.IsNullOrWhiteSpace(selectedMatchGimmickId)) throw new InvalidOperationException("경기 기믹을 선택하세요.");
                    if (!CompatibleGimmicks(rule).Any(x => x.Id == selectedMatchGimmickId))
                        throw new InvalidOperationException("선택한 규칙에서는 해당 기믹을 사용할 수 없습니다.");
                    var formatChanged = match.MatchTypeId != type.Id || match.TeamCount != rule.TeamCount || match.MembersPerTeam != rule.MembersPerTeam;
                    match.MatchTypeId = type.Id;
                    match.MatchGimmickId = selectedMatchGimmickId;
                    match.TeamCount = rule.TeamCount;
                    match.MembersPerTeam = rule.MembersPerTeam;
                    if (formatChanged)
                    {
                        match.Sides = Enumerable.Range(0, rule.TeamCount).Select(_ => new MatchSideState
                        {
                            Id = PWManager.Domain.Identifiers.EntityId.CreateRuntimeId(),
                            MemberIds = new List<string>()
                        }).ToList();
                        match.ParticipantIds.Clear();
                        match.WinningSideId = null;
                        match.FinishPerformerId = null;
                        match.LoserTargetId = null;
                    }
                }
                else if (showEditSection == ShowEditSection.Participants)
                {
                    var requiredParticipants = match.TeamCount > 0 && match.MembersPerTeam > 0 ? match.TeamCount * match.MembersPerTeam : 0;
                    if (requiredParticipants <= 0) throw new InvalidOperationException("먼저 경기 규칙과 기믹을 확정하세요.");
                    if (editorParticipantSlots.Count != requiredParticipants || editorParticipantSlots.Any(string.IsNullOrEmpty))
                        throw new InvalidOperationException($"{requiredParticipants}개의 참가자 슬롯을 모두 채우세요.");
                    var participants = editorParticipantSlots.ToList();
                    match.Sides = BuildConfiguredMatchSides(participants, match.TeamCount, match.MembersPerTeam);
                    match.ParticipantIds.Clear();
                    match.WinningSideId = null;
                    match.FinishPerformerId = null;
                    match.LoserTargetId = null;
                }
                else SaveSpots(match);
            }
            else
            {
                var promo = boundSave.PromoPlans.FirstOrDefault(x => x?.Id == showEvent.DetailId)
                    ?? throw new InvalidOperationException("프로모 세부 정보를 찾을 수 없습니다.");
                if (showEditSection == ShowEditSection.Rules)
                {
                    promo.Purpose = (PromoPurpose)Math.Max(0, primary.index);
                    promo.Presentation = (PromoPresentation)Math.Max(0, secondary.index);
                    promo.SponsorRequirementId = promo.Purpose == PromoPurpose.SponsorAdvertisement ? "sponsor_requirement_runtime" : null;
                    showPlanningService.SetEventDuration(boundSave, show.Id, showEvent.Id, duration);
                }
                else if (showEditSection == ShowEditSection.Participants)
                {
                    if (editorParticipantIds.Count == 0) throw new InvalidOperationException("프로모 출연 선수를 한 명 이상 선택하세요.");
                    promo.ParticipantIds = editorParticipantIds.ToList();
                }
                else SaveSpots(promo);
            }
        }

        private void SaveSpots(MatchPlanState plan)
        {
            plan.OpeningSpot = document.rootVisualElement.Q<TextField>("show-editor-opening-spot").value?.Trim();
            plan.MiddleSpot = document.rootVisualElement.Q<TextField>("show-editor-middle-spot").value?.Trim();
            plan.ClosingSpot = document.rootVisualElement.Q<TextField>("show-editor-closing-spot").value?.Trim();
        }

        private void SaveSpots(PromoPlanState plan)
        {
            plan.OpeningSpot = document.rootVisualElement.Q<TextField>("show-editor-opening-spot").value?.Trim();
            plan.MiddleSpot = document.rootVisualElement.Q<TextField>("show-editor-middle-spot").value?.Trim();
            plan.ClosingSpot = document.rootVisualElement.Q<TextField>("show-editor-closing-spot").value?.Trim();
        }

        private List<MatchTypeDefinition> MatchTypes() => staticContent?.MatchTypes.Values.OrderBy(x => x.Id).ToList() ?? new List<MatchTypeDefinition>();

        private List<MatchRuleOption> MatchRuleOptions()
        {
            var options = new List<MatchRuleOption>();
            foreach (var type in MatchTypes())
            {
                if (type.MinimumTeamCount > 0)
                {
                    for (var teamCount = type.MinimumTeamCount; teamCount <= type.MaximumTeamCount; teamCount++)
                    for (var members = type.MinimumMembersPerTeam; members <= type.MaximumMembersPerTeam; members++)
                    {
                        var total = teamCount * members;
                        if (total < type.MinimumParticipants || total > type.MaximumParticipants) continue;
                        options.Add(new MatchRuleOption(type.Id, teamCount, members, string.Join(" vs ", Enumerable.Repeat(members.ToString(), teamCount))));
                    }
                }
                else
                {
                    for (var count = type.MinimumParticipants; count <= type.MaximumParticipants; count++)
                        options.Add(new MatchRuleOption(type.Id, count, 1, count == 2 ? "1 vs 1" : $"{count} way"));
                }
            }
            return options.OrderBy(x => x.ParticipantCount).ThenBy(x => x.TeamCount).ThenBy(x => x.MembersPerTeam).ToList();
        }

        private IEnumerable<MatchGimmickDefinition> CompatibleGimmicks(MatchRuleOption rule)
        {
            if (rule == null || staticContent == null) return Enumerable.Empty<MatchGimmickDefinition>();
            return staticContent.MatchGimmicks.Values
                .Where(x => x != null && x.MinimumParticipants <= rule.ParticipantCount && x.MaximumParticipants >= rule.ParticipantCount &&
                    (x.CompatibleMatchTypeIds == null || x.CompatibleMatchTypeIds.Count == 0 || x.CompatibleMatchTypeIds.Contains(rule.MatchTypeId)))
                .OrderBy(x => x.Id);
        }

        private static List<MatchSideState> BuildConfiguredMatchSides(List<string> participants, int teamCount, int membersPerTeam)
        {
            if (teamCount <= 0 || membersPerTeam <= 0 || participants.Count != teamCount * membersPerTeam)
                throw new InvalidOperationException("선택한 경기 규칙과 참가자 구성이 맞지 않습니다.");
            return Enumerable.Range(0, teamCount)
                .Select(index => new MatchSideState
                {
                    Id = PWManager.Domain.Identifiers.EntityId.CreateRuntimeId(),
                    MemberIds = participants.Skip(index * membersPerTeam).Take(membersPerTeam).ToList()
                }).ToList();
        }

        private static List<MatchSideState> BuildMatchSides(MatchTypeDefinition type, List<string> participants)
        {
            var sides = new List<MatchSideState>();
            if (type.MinimumTeamCount > 0)
            {
                var teamCount = type.MinimumTeamCount;
                if (participants.Count % teamCount != 0) throw new InvalidOperationException($"선수 수는 {teamCount}개 팀으로 동일하게 나뉘어야 합니다.");
                var perTeam = participants.Count / teamCount;
                if (perTeam < type.MinimumMembersPerTeam || perTeam > type.MaximumMembersPerTeam)
                    throw new InvalidOperationException($"각 팀은 {type.MinimumMembersPerTeam}~{type.MaximumMembersPerTeam}명이어야 합니다.");
                for (var i = 0; i < teamCount; i++) sides.Add(new MatchSideState { Id = PWManager.Domain.Identifiers.EntityId.CreateRuntimeId(), MemberIds = participants.Skip(i * perTeam).Take(perTeam).ToList() });
            }
            else foreach (var id in participants) sides.Add(new MatchSideState { Id = PWManager.Domain.Identifiers.EntityId.CreateRuntimeId(), MemberIds = new List<string> { id } });
            return sides;
        }

        private int TimelineDropIndex(Vector2 pointerPosition)
        {
            var rows = document.rootVisualElement.Q<VisualElement>("show-timeline-rows");
            if (rows == null || rows.childCount == 0) return -1;
            var localPointer = rows.WorldToLocal(pointerPosition);
            for (var i = 0; i < rows.childCount; i++)
                if (localPointer.y < rows[i].layout.center.y) return i;
            return rows.childCount - 1;
        }

        private void MoveEventByIndex(ShowState show, int from, int to)
        {
            try
            {
                showPlanningService.MoveEvent(boundSave, show.Id, from, to);
                DashboardSession.PersistActive();
                RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void MoveSelectedEvent(int delta)
        {
            try
            {
                var show = NextPlanningShow(); if (show == null || string.IsNullOrEmpty(selectedShowEventId)) return;
                var oldIndex = show.TimelineEventIds.IndexOf(selectedShowEventId); var newIndex = oldIndex + delta;
                if (oldIndex < 0 || newIndex < 0 || newIndex >= show.TimelineEventIds.Count) return;
                showPlanningService.MoveEvent(boundSave, show.Id, oldIndex, newIndex); DashboardSession.PersistActive(); RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void RemoveSelectedEvent()
        {
            try
            {
                var show = NextPlanningShow(); if (show == null || string.IsNullOrEmpty(selectedShowEventId)) return;
                showPlanningService.RemoveEvent(boundSave, show.Id, selectedShowEventId); selectedShowEventId = null; DashboardSession.PersistActive(); RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void ConfirmSelectedShow()
        {
            try
            {
                var show = NextPlanningShow() ?? throw new InvalidOperationException("편성할 쇼를 선택하세요.");
                showPlanningService.Confirm(boundSave, show.Id); DashboardSession.PersistActive(); RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void SaveShowDraft()
        {
            try { DashboardSession.PersistActive(); Set("show-planning-notice", "현재 편성 내용을 저장했습니다"); }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private string ShowVenueName(ShowState show)
        {
            var contract = boundSave?.VenueContracts?.FirstOrDefault(x => x?.Id == show?.VenueContractId);
            if (contract == null) return "경기장 미정";
            return staticContent?.Venues.TryGetValue(contract.VenueId, out var venue) == true ? venue.DisplayName : contract.VenueId;
        }

        private void RenderShowDetail(ShowState show, List<ShowEventState> ordered)
        {
            var host = document.rootVisualElement.Q<VisualElement>("show-detail-participants");
            host.Clear();
            host.RemoveFromClassList("matchup");
            host.RemoveFromClassList("multi-side");
            host.RemoveFromClassList("duel");
            host.RemoveFromClassList("team-match");
            host.RemoveFromClassList("promo-cast");
            var showEvent = ordered.FirstOrDefault(x => x.Id == selectedShowEventId);
            if (showEvent == null) { ClearShowDetail(); return; }
            SetShowDetailActionsEnabled(true);
            var isPromo = showEvent.EventType == ShowEventType.Promo;
            var kind = document.rootVisualElement.Q<Label>("show-detail-kind"); kind.text = isPromo ? "프로모" : "경기"; kind.EnableInClassList("promo", isPromo);
            Set("show-detail-title", ShowEventTitle(showEvent));
            Set("show-detail-subtitle", ShowEventDescription(showEvent));
            Set("show-detail-rules-summary", ShowEventDescription(showEvent));
            Set("show-detail-spots-summary", ShowSpotSummary(showEvent));
            if (isPromo) RenderPromoCast(host, showEvent);
            else RenderMatchup(host, showEvent);
            if (host.childCount == 0) { var empty = new Label("배정된 선수가 없습니다"); empty.AddToClassList("show-panel-caption"); host.Add(empty); }
            var index = ordered.IndexOf(showEvent);
            var position = show.MainEventId == showEvent.Id ? "메인 이벤트" : show.OpeningEventId == showEvent.Id ? "오프닝" : $"{index + 1}번째 세그먼트";
            Set("show-detail-duration", $"{showEvent.PlannedDuration}분"); Set("show-detail-position", position);
        }

        private void RenderMatchup(VisualElement host, ShowEventState showEvent)
        {
            var match = boundSave.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            if (match == null) return;
            var sides = new List<List<string>>();
            if (match.TeamCount > 0 && match.MembersPerTeam > 0)
            {
                for (var teamIndex = 0; teamIndex < match.TeamCount; teamIndex++)
                {
                    var assigned = match.Sides != null && teamIndex < match.Sides.Count
                        ? match.Sides[teamIndex]?.MemberIds ?? new List<string>()
                        : new List<string>();
                    var side = new List<string>();
                    for (var memberIndex = 0; memberIndex < match.MembersPerTeam; memberIndex++)
                        side.Add(memberIndex < assigned.Count ? assigned[memberIndex] : null);
                    sides.Add(side);
                }
            }
            else
            {
                sides = (match.Sides ?? new List<MatchSideState>())
                    .Where(x => x?.MemberIds != null && x.MemberIds.Count > 0)
                    .Select(x => x.MemberIds.Distinct(StringComparer.Ordinal).ToList())
                    .ToList();
                if (sides.Count < 2)
                    sides = (match.ParticipantIds ?? new List<string>()).Distinct(StringComparer.Ordinal).Select(x => new List<string> { x }).ToList();
            }
            if (sides.Count == 0) return;

            host.AddToClassList("matchup");
            if (sides.Count == 2 && sides.All(x => x.Count == 1)) host.AddToClassList("duel");
            if (sides.Any(x => x.Count > 1)) host.AddToClassList("team-match");
            if (sides.Count > 3) host.AddToClassList("multi-side");
            var portraitCards = new List<TrapezoidPortraitCard>();
            var slotIndex = 0;
            for (var sideIndex = 0; sideIndex < sides.Count; sideIndex++)
            {
                for (var memberIndex = 0; memberIndex < sides[sideIndex].Count; memberIndex++)
                {
                    var portraitCard = AddParticipantPortrait(host, sides[sideIndex][memberIndex], slotIndex, true);
                    portraitCards.Add(portraitCard);
                    if (sideIndex > 0 && memberIndex == 0)
                    {
                        var versus = new Label("VS") { pickingMode = PickingMode.Ignore };
                        versus.AddToClassList("show-match-versus");
                        portraitCard.Add(versus);
                    }
                    slotIndex++;
                }
            }
            if (portraitCards.Count > 0)
            {
                portraitCards[0].IsFirst = true;
                portraitCards[portraitCards.Count - 1].IsLast = true;
            }
        }

        private void RenderPromoCast(VisualElement host, ShowEventState showEvent)
        {
            host.AddToClassList("promo-cast");
            TrapezoidPortraitCard firstCard = null;
            TrapezoidPortraitCard lastCard = null;
            foreach (var wrestlerId in ShowEventParticipantIds(showEvent).Distinct(StringComparer.Ordinal))
            {
                var card = AddParticipantPortrait(host, wrestlerId, -1, true);
                firstCard ??= card;
                lastCard = card ?? lastCard;
            }
            if (firstCard != null) firstCard.IsFirst = true;
            if (lastCard != null) lastCard.IsLast = true;
        }

        private TrapezoidPortraitCard AddParticipantPortrait(VisualElement host, string wrestlerId, int slotIndex = -1, bool editParticipantsOnCardClick = false)
        {
            var wrestler = boundSave.Wrestlers?.FirstOrDefault(x => x?.Id == wrestlerId);
            var card = new TrapezoidPortraitCard();
            if (wrestler == null)
            {
                card.AddToClassList("empty");
                var empty = new Label("+ 선수 배정") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList("show-participant-empty-label");
                card.Add(empty);
            }
            else card.Add(WrestlerProfileController.CreatePortraitDisplayWithNameLink(wrestler, "show-participant-portrait"));
            card.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (editParticipantsOnCardClick)
                {
                    OpenSelectedEventEditor(ShowEditSection.Participants);
                    if (showEditorMode == ShowEditorMode.Match && slotIndex >= 0 && slotIndex < editorParticipantSlots.Count)
                    {
                        selectedParticipantSlotIndex = slotIndex;
                        pendingParticipantId = editorParticipantSlots[slotIndex];
                        RenderShowEditorRoster(NextPlanningShow());
                    }
                }
                evt.StopPropagation();
            });
            host.Add(card);
            return card;
        }

        private void ClearShowDetail()
        {
            var kind = document.rootVisualElement.Q<Label>("show-detail-kind"); if (kind != null) { kind.text = "—"; kind.EnableInClassList("promo", false); }
            Set("show-detail-title", "타임라인에서 항목을 선택하세요"); Set("show-detail-subtitle", "경기 또는 프로모의 구성 정보를 확인할 수 있습니다");
            Set("show-detail-rules-summary", "항목을 선택하세요");
            Set("show-detail-spots-summary", "설정된 스팟 없음");
            Set("show-detail-duration", "—"); Set("show-detail-position", "—");
            document.rootVisualElement.Q<VisualElement>("show-detail-participants")?.Clear();
            SetShowDetailActionsEnabled(false);
        }

        private void SetShowDetailActionsEnabled(bool enabled)
        {
            foreach (var id in new[] { "show-edit-rules", "show-edit-participants", "show-edit-spots", "show-move-up", "show-move-down", "show-remove-event" })
                document.rootVisualElement.Q<VisualElement>(id)?.SetEnabled(enabled);
        }

        private string ShowSpotSummary(ShowEventState showEvent)
        {
            string opening;
            string middle;
            string closing;
            if (showEvent.EventType == ShowEventType.Match)
            {
                var plan = boundSave.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
                opening = plan?.OpeningSpot; middle = plan?.MiddleSpot; closing = plan?.ClosingSpot;
            }
            else
            {
                var plan = boundSave.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
                opening = plan?.OpeningSpot; middle = plan?.MiddleSpot; closing = plan?.ClosingSpot;
            }
            var entries = new List<string>();
            if (!string.IsNullOrWhiteSpace(opening)) entries.Add("오프닝");
            if (!string.IsNullOrWhiteSpace(middle)) entries.Add("중반");
            if (!string.IsNullOrWhiteSpace(closing)) entries.Add("마무리");
            return entries.Count == 0 ? "설정된 스팟 없음" : $"{string.Join(" · ", entries)} 스팟 설정됨";
        }

        private string ShowEventTitle(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Promo)
            {
                var promo = boundSave.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
                return promo == null ? "프로모 정보 없음" : promo.ParticipantIds == null || promo.ParticipantIds.Count == 0 ? "내용 미정 프로모" : PromoPurposeText(promo.Purpose);
            }
            var match = boundSave.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            if (match == null) return "경기 정보 없음";
            var sides = (match.Sides ?? new List<MatchSideState>()).Where(x => x != null && x.MemberIds != null && x.MemberIds.Any(id => !string.IsNullOrWhiteSpace(id)))
                .Select(x => string.Join(" & ", x.MemberIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(WrestlerName))).ToList();
            if (sides.Count > 1) return string.Join("  vs  ", sides);
            var names = (match.ParticipantIds ?? new List<string>()).Select(WrestlerName).ToList();
            return names.Count > 0 ? string.Join("  vs  ", names) : "참가자 미정 경기";
        }

        private IEnumerable<string> ShowEventParticipantIds(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Promo)
                return boundSave.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId)?.ParticipantIds ?? new List<string>();
            var match = boundSave.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            if (match == null) return Enumerable.Empty<string>();
            var sideIds = (match.Sides ?? new List<MatchSideState>()).Where(x => x?.MemberIds != null).SelectMany(x => x.MemberIds).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return sideIds.Count > 0 ? sideIds : match.ParticipantIds ?? new List<string>();
        }

        private string ShowEventDescription(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Match)
            {
                var match = boundSave.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
                if (match == null) return "경기 세부 데이터가 없습니다";
                var rule = MatchRuleOptions().FirstOrDefault(x => x.MatchTypeId == match.MatchTypeId && x.TeamCount == match.TeamCount && x.MembersPerTeam == match.MembersPerTeam);
                var ruleLabel = rule?.Label ?? MatchTypeText(match.MatchTypeId);
                var gimmick = staticContent?.MatchGimmicks.Values.FirstOrDefault(x => x.Id == match.MatchGimmickId);
                return $"{ruleLabel} · {MatchGimmickText(gimmick)}";
            }
            var promo = boundSave.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            return promo == null ? "프로모 세부 데이터가 없습니다" : $"{PromoPresentationText(promo.Presentation)} · {PromoPurposeText(promo.Purpose)}";
        }

        private string WrestlerName(string id) => DisplayName(boundSave?.Wrestlers?.FirstOrDefault(x => x?.Id == id));
        private static string MatchTypeText(string id) => id switch { "matchtype_001" => "개인전", "matchtype_002" => "태그팀 경기", _ => "경기" };
        private static string MatchGimmickText(MatchGimmickDefinition gimmick) => gimmick?.Id switch { "gimmick_000" => "기본 경기", "gimmick_001" => "스틸 케이지", "gimmick_002" => "래더 매치", "gimmick_003" => "테이블 매치", "gimmick_004" => "하드코어", _ => gimmick?.DisplayName ?? "기믹 미정" };
        private static string MatchFinishText(MatchFinishType value) => value switch { MatchFinishType.Submission => "서브미션", MatchFinishType.RollUp => "롤업", MatchFinishType.Disqualification => "반칙", MatchFinishType.CountOut => "카운트아웃", MatchFinishType.Draw => "무승부", _ => "핀폴" };
        private static string PromoPurposeText(PromoPurpose value) => value switch { PromoPurpose.CharacterIntroduction => "캐릭터 소개", PromoPurpose.ChampionStatement => "챔피언 선언", PromoPurpose.Rivalry => "라이벌리 전개", PromoPurpose.AlignmentChange => "성향 전환", PromoPurpose.TeamFormation => "팀 결성", PromoPurpose.TeamBreakup => "팀 해체", PromoPurpose.Challenge => "도전 선언", PromoPurpose.MatchBuild => "경기 빌드업", PromoPurpose.SponsorAdvertisement => "스폰서 광고", _ => "프로모" };
        private static string PromoPresentationText(PromoPresentation value) => value switch { PromoPresentation.InRingMic => "링 위 마이크", PromoPresentation.Interview => "인터뷰", PromoPresentation.BackstageConversation => "백스테이지 대화", PromoPresentation.InterruptionAttack => "난입 공격", PromoPresentation.RescueBetrayal => "구출·배신", PromoPresentation.VideoPackage => "비디오 패키지", _ => "프로모" };

        private void BindStories(GameSave save)
        {
            var changes = (save.ShowResults ?? new List<ShowResultState>()).Where(x => x != null).SelectMany(x => x.StoryChanges ?? new List<ResultChangeState>()).TakeLast(3).Reverse().ToList();
            for (var i = 0; i < 3; i++)
            {
                var row = document.rootVisualElement.Q<VisualElement>($"story-{i}"); if (row == null) continue;
                row.style.display = i < changes.Count || (i == 0 && changes.Count == 0) ? DisplayStyle.Flex : DisplayStyle.None;
                if (i < changes.Count) { Set($"story-{i}-title", string.IsNullOrWhiteSpace(changes[i].ValueKey) ? "스토리 변화" : changes[i].ValueKey); Set($"story-{i}-detail", $"{Signed(changes[i].Amount)} · {changes[i].Reason}"); }
                else if (i == 0) { Set("story-0-title", "데이터 없음"); Set("story-0-detail", "스토리 결과가 생성되면 표시됩니다"); }
            }
        }

        private void BindResult(GameSave save)
        {
            var result = save.ShowResults?.LastOrDefault(x => x != null);
            if (result == null) { Set("result-name", "결과 데이터 없음"); Set("result-finance", "완료된 쇼 결과가 생성되면 표시됩니다"); SetResultEmpty(); return; }
            var show = save.Shows?.FirstOrDefault(x => x?.Id == result.ShowId);
            var matches = (save.MatchResults ?? new List<MatchResultState>()).Where(x => x != null && result.MatchResultIds.Contains(x.Id)).ToList();
            var promos = (save.PromoResults ?? new List<PromoResultState>()).Where(x => x != null && result.PromoResultIds.Contains(x.Id)).ToList();
            var reactions = matches.Select(x => x.FanReaction).Concat(promos.Select(x => x.FanReaction)).Where(x => x != null).ToList();
            Set("result-name", show?.Name ?? "완료된 쇼"); Set("result-finance", $"수익 {Compact(result.FinancialSettlement.Revenue)} · 비용 {Compact(result.FinancialSettlement.Cost)} · 순손익 {SignedMoney(result.FinancialSettlement.NetIncome)}");
            Set("show-score", $"{result.ShowEvaluation.Score:0.0}"); Set("match-score", matches.Count == 0 ? "—" : $"{matches.Average(x => x.FinalMatchQuality):0.00}");
            Set("result-mania", reactions.Count == 0 ? "—" : Signed(reactions.Average(x => x.Mania))); Set("result-light", reactions.Count == 0 ? "—" : Signed(reactions.Average(x => x.Light))); Set("result-family", reactions.Count == 0 ? "—" : Signed(reactions.Average(x => x.Family)));
            var change = result.WrestlerChanges?.Concat(result.StoryChanges ?? new List<ResultChangeState>()).FirstOrDefault(); Set("result-change", change == null ? "기록된 변화 없음" : $"{change.ValueKey} {Signed(change.Amount)} · {change.Reason}");
        }

        private void ShowEmpty()
        {
            foreach (var id in new[] { "current-date", "top-cash", "top-next-show", "cash-value", "prestige-value", "roster-value", "risk-value", "staff-value" }) Set(id, "—");
            Set("next-show-caption", "다음 쇼 일정"); Set("next-show-date", "—"); Set("next-show-name", "활성 저장 데이터가 없습니다"); Set("next-show-detail", "새 게임 또는 저장 데이터를 연결하세요"); Set("next-show-type", "—"); Set("next-show-status", "저장 없음");
            Set("cash-detail", "활성 저장 없음"); Set("promotion-name", "—"); Set("promotion-abbreviation", "—"); Set("roster-detail", "전체 —"); Set("risk-detail", "부상 — · 컨디션 —"); Set("staff-detail", "부서 데이터 없음");
            SetProgress(0); Set("card-count", "카드 —"); Set("participant-count", "출연 —"); Set("estimated-cost", "예상 비용 —");
            BindTasks(new List<TaskItem> { new("—", "활성 저장 데이터 없음", "") }); BindFanReaction(new List<WrestlerState>()); BindStories(new GameSave());
            Set("result-name", "결과 데이터 없음"); Set("result-finance", "완료된 쇼 결과가 생성되면 표시됩니다"); SetResultEmpty();
            Badge("home-badge", 0); Badge("show-badge", 0); Badge("report-badge", 0); var button = document.rootVisualElement.Q<Button>("notifications"); if (button != null) button.text = "알림  0";
            RenderRoster();
            RenderShowSchedule();
        }

        private void SetResultEmpty() { foreach (var id in new[] { "show-score", "match-score", "result-mania", "result-light", "result-family" }) Set(id, "—"); Set("result-change", "데이터 없음"); }
        private void SetProgress(int value) { Set("preparation-value", $"준비도 {value}%"); var bar = document.rootVisualElement.Q<VisualElement>(className: "progress-fill"); if (bar != null) bar.style.width = Length.Percent(value); }
        private void FanBars(float mania, float light, float family) { var v = new[] { Math.Max(0, mania + 100), Math.Max(0, light + 100), Math.Max(0, family + 100) }; var total = Math.Max(v.Sum(), 1); Width("mania-bar", (float)(v[0] / total * 100)); Width("light-bar", (float)(v[1] / total * 100)); Width("family-bar", (float)(v[2] / total * 100)); }
        private void Width(string id, float value) { var e = document.rootVisualElement.Q<VisualElement>(id); if (e != null) e.style.width = Length.Percent(value); }
        private void Badge(string id, int count) { var e = document.rootVisualElement.Q<Label>(id); if (e == null) return; e.text = count.ToString(); e.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None; }
        private void Set(string id, string value) { var label = document.rootVisualElement.Q<Label>(id); if (label != null) label.text = value ?? "—"; }
        private void ApplyIcons()
        {
            foreach (var name in new[] { "home", "roster", "teams", "locker", "scout", "show", "story", "title", "tournament", "schedule", "staff", "company", "finance", "report", "world", "history", "settings" })
            {
                var icon = document.rootVisualElement.Q<VisualElement>($"icon-{name}");
                if (icon == null) continue;
                icon.Clear();
                icon.AddToClassList($"nav-icon-{name}");
            }
        }
        private static int ParticipantCount(GameSave save, List<ShowEventState> events) { var ids = new HashSet<string>(StringComparer.Ordinal); foreach (var e in events) if (e.EventType == ShowEventType.Match) { var m = save.MatchPlans?.FirstOrDefault(x => x?.Id == e.DetailId); if (m != null) { foreach (var s in m.Sides ?? new List<MatchSideState>()) foreach (var id in s.MemberIds ?? new List<string>()) if (!string.IsNullOrWhiteSpace(id)) ids.Add(id); foreach (var id in m.ParticipantIds ?? new List<string>()) if (!string.IsNullOrWhiteSpace(id)) ids.Add(id); } } else { var p = save.PromoPlans?.FirstOrDefault(x => x?.Id == e.DetailId); if (p != null) foreach (var id in p.ParticipantIds ?? new List<string>()) if (!string.IsNullOrWhiteSpace(id)) ids.Add(id); } return ids.Count; }
        private static string Date(GameDate d) => $"{d.Year}년 {d.Month}월 {d.Day}일";
        private static string Money(long v) => $"${v:N0}";
        private static string Compact(long v) => Math.Abs(v) >= 1_000_000 ? $"${v / 1_000_000f:0.0}M" : $"${v:N0}";
        private static string SignedMoney(long v) => $"{(v >= 0 ? "+" : "−")}{Compact(Math.Abs(v))}";
        private static string Signed(float v) => $"{(v >= 0 ? "+" : "")}{v:0.0}";
        private static string ShowName(ScheduledShowType v) => v switch { ScheduledShowType.Regular => "다음 정규 쇼", ScheduledShowType.PpvRegular => "다음 PPV", ScheduledShowType.PpvMajor => "다음 메이저 PPV", ScheduledShowType.PpvSignature => "다음 시그니처 PPV", _ => "다음 쇼" };
        private static string ShowType(ScheduledShowType v) => v switch { ScheduledShowType.Regular => "정규 쇼", ScheduledShowType.PpvRegular => "PPV", ScheduledShowType.PpvMajor => "메이저 PPV", ScheduledShowType.PpvSignature => "시그니처 PPV", _ => v.ToString() };
        private static string ShowStatusText(ShowStatus v) => v switch { ShowStatus.Draft => "초안", ShowStatus.Preparing => "준비 중", ShowStatus.Review => "검토 중", ShowStatus.Confirmed => "확정", ShowStatus.InProgress => "진행 중", ShowStatus.Completed => "완료", _ => v.ToString() };
        private void OnClick(ClickEvent evt)
        {
            var target = evt.target as VisualElement;
            var button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
            if (button == null) return;
            if (button.name == "dashboard-back") { NavigateBack(); evt.StopPropagation(); return; }
            if (button.name == "dashboard-forward") { NavigateForward(); evt.StopPropagation(); return; }
            if (button.name == "nav-home") { WrestlerProfileController.CloseOpenProfile(false); ShowPage(false); evt.StopPropagation(); return; }
            if (button.name == "nav-roster") { WrestlerProfileController.CloseOpenProfile(false); ShowPage(true); evt.StopPropagation(); return; }
            if (button.name == "nav-show") { WrestlerProfileController.CloseOpenProfile(false); ShowSchedulePage(); evt.StopPropagation(); return; }
            Debug.Log($"Dashboard action selected: {button.name}");
        }
        private enum RosterMode { All, Wrestlers, Managers }
        private enum ShowEditorMode { Match, Promo }
        private enum ShowEditSection { Full, Rules, Participants, Spots }
        private enum MeterKind { Momentum, Condition, Satisfaction }
        private enum DashboardPage { Home, Roster, WrestlerProfile, ShowSchedule, ShowPlanning }
        private sealed class NavigationEntry
        {
            public readonly DashboardPage Page;
            public readonly string ContextId;
            public NavigationEntry(DashboardPage page, string contextId = null) { Page = page; ContextId = contextId; }
            public bool Matches(NavigationEntry other) => other != null && Page == other.Page && string.Equals(ContextId, other.ContextId, StringComparison.Ordinal);
        }
        private sealed class MatchRuleOption
        {
            public readonly string MatchTypeId;
            public readonly int TeamCount;
            public readonly int MembersPerTeam;
            public readonly string Label;
            public int ParticipantCount => TeamCount * MembersPerTeam;
            public string Key => $"{MatchTypeId}:{TeamCount}:{MembersPerTeam}";
            public MatchRuleOption(string matchTypeId, int teamCount, int membersPerTeam, string label)
            {
                MatchTypeId = matchTypeId;
                TeamCount = teamCount;
                MembersPerTeam = membersPerTeam;
                Label = label;
            }
        }
        private readonly struct SortCriterion { public readonly string Key; public readonly bool Ascending; public SortCriterion(string key, bool ascending) { Key = key; Ascending = ascending; } }
        private readonly struct TaskItem { public readonly string Severity, Text, Context; public readonly WrestlerState Wrestler; public TaskItem(string severity, string text, string context, WrestlerState wrestler = null) { Severity = severity; Text = text; Context = context; Wrestler = wrestler; } }
    }

}
