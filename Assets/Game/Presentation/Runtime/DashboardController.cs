using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LLMUnity;
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
        private DashboardRosterController rosterController;
        private GameSave boundSave;
        private ShowPlanningController showPlanningController;
        private readonly List<NavigationEntry> navigationHistory = new();
        private int navigationIndex = -1;
        private bool applyingNavigation;
        private VisualElement saveLoadOverlay;
        private VisualElement saveLoadRows;
        private TextField manualSaveName;
        private Label saveLoadMessage;
        private DashboardInboxController inboxController;
        private ShowSimulationPlayer simulationPlayer;
        private VisualElement profileReturnOverlay;
        private bool advancingShow;
        private bool showTestMode;
        private DashboardScheduleController scheduleController;

        public void OpenShowTest(string showId, Action reset)
        {
            showTestMode = true;
            var root = document.rootVisualElement;
            root.Q("dashboard").AddToClassList("show-test");
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ShowTest"));
            var toolbar = root.Q(className: "topbar");
            void AddAction(string text, Action action)
            {
                var button = new Button(() => { if (!advancingShow) action(); }) { text = text };
                button.AddToClassList("small-button");
                toolbar.Insert(toolbar.childCount - 1, button);
            }
            AddAction("로스터", () => ShowPage(true));
            AddAction("쇼 편성", () => ShowPlanningPage(showId));
            AddAction("결과", () => ShowPage(false));
            AddAction("새 쇼", reset);
            ShowPlanningPage(showId);
            BindAdvanceButton(boundSave);
        }
        [SerializeField] private LLMAgent promoNarrativeAgent;

        private void OnEnable()
        {
            instance = this;
            document = GetComponent<UIDocument>();
            foreach (var view in GetComponentsInChildren<DashboardViewHost>())
                view.Mount(document.rootVisualElement);
            DashboardViewHost.MountMissingViews(document.rootVisualElement);
            showPlanningController = new ShowPlanningController(document.rootVisualElement);
            scheduleController = new DashboardScheduleController(document.rootVisualElement, showPlanningController.VenueName, id => ShowPlanningPage(id));
            inboxController = new DashboardInboxController(document.rootVisualElement, () => ShowSchedulePage(), id => ShowPlanningPage(id));
            ApplyIcons();
            document.rootVisualElement.RegisterCallback<ClickEvent>(OnClick);
            rosterController = new DashboardRosterController(document.rootVisualElement);
            BindClick("advance-time", AdvanceDay);
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
            foreach (var view in GetComponentsInChildren<DashboardViewHost>())
                view.Unmount();
            simulationPlayer?.Dispose();
            simulationPlayer = null;
            DashboardSession.SaveChanged -= Bind;
            if (document != null && document.rootVisualElement != null)
                document.rootVisualElement.UnregisterCallback<ClickEvent>(OnClick);
            if (instance == this) instance = null;
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
                if (DashboardSession.HasBackup(slot))
                {
                    var backup = new Button(() => LoadSlot(captured, true)) { text = $"{slot} · 이전 백업 불러오기" };
                    backup.AddToClassList("save-load-row");
                    backup.AddToClassList("save-load-row-name");
                    saveLoadRows.Add(backup);
                }
            }
            if (slots.Count == 0) { var empty = new Label("저장된 게임이 없습니다"); empty.AddToClassList("save-load-empty"); saveLoadRows.Add(empty); }
        }

        private void LoadSlot(string slotName, bool backup = false)
        {
            try
            {
                if (backup) DashboardSession.LoadBackup(slotName);
                else DashboardSession.Load(slotName);
                CloseSaveLoadMenu();
            }
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
        public static void ShowRosterShell() { if (instance == null) return; if (instance.navigationIndex > 0) instance.NavigateBack(); else instance.ShowPage(true); }
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
            if (boundSave != save)
            {
                simulationPlayer?.Dispose();
                simulationPlayer = null;
            }
            boundSave = save;
            if (save?.Promotion == null) { ShowEmpty(); return; }
            new InboxService().EnsureForCurrentDate(save);
            var wrestlers = save.Wrestlers?.Where(x => x != null).ToList() ?? new List<WrestlerState>();
            var active = wrestlers.Where(x => x.Roster?.ActivityState == RosterActivityState.Active).ToList();
            var transactions = save.Transactions?.Where(x => x != null && !x.IsVoided).ToList() ?? new List<TransactionRecord>();
            var cash = save.Promotion.CalculateCurrentCash(transactions);
            var seasonStart = save.SeasonPolicy?.StartDate ?? save.CurrentDate;

            Set("current-date", DashboardText.Date(save.CurrentDate)); Set("top-cash", DashboardText.Money(cash)); Set("cash-value", Compact(cash));
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
            BindTasks(tasks); inboxController.Bind(save); BindFanReaction(); BindStories(save); BindResult(save); BindAdvanceButton(save);
            Badge("home-badge", tasks.Count); Badge("message-badge", inboxController.UnreadCount); Badge("show-badge", save.Shows?.Count(x => x != null && x.Status != ShowStatus.Completed) ?? 0); Badge("report-badge", save.ShowResults?.Count ?? 0);
            var notifications = document.rootVisualElement.Q<Button>("notifications"); if (notifications != null) notifications.text = $"알림  {tasks.Count}";
            rosterController.Render(boundSave);
            scheduleController.Render(boundSave);
            showPlanningController.Bind(boundSave, showTestMode);
        }



        private void ShowPage(bool roster, bool recordNavigation = true)
        {
            var overview = document.rootVisualElement.Q<VisualElement>("overview-content");
            var messageContent = document.rootVisualElement.Q<VisualElement>("message-content");
            var rosterContent = document.rootVisualElement.Q<VisualElement>("roster-content");
            var profileContent = document.rootVisualElement.Q<VisualElement>("wrestler-profile-content");
            var scheduleContent = document.rootVisualElement.Q<VisualElement>("show-schedule-content");
            var showContent = document.rootVisualElement.Q<VisualElement>("show-planning-content");
            if (overview != null) overview.style.display = roster ? DisplayStyle.None : DisplayStyle.Flex;
            if (messageContent != null) messageContent.style.display = DisplayStyle.None;
            if (rosterContent != null) rosterContent.style.display = roster ? DisplayStyle.Flex : DisplayStyle.None;
            if (profileContent != null) profileContent.style.display = DisplayStyle.None;
            if (scheduleContent != null) scheduleContent.style.display = DisplayStyle.None;
            if (showContent != null) showContent.style.display = DisplayStyle.None;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", !roster);
            document.rootVisualElement.Q<Button>("nav-message")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", roster);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", false);
            Set("dashboard-page-title", roster ? "로스터 / 전체 선수" : showTestMode ? "쇼 결과" : "홈 / 개요");
            if (roster) rosterController.Render(boundSave);
            if (recordNavigation) RecordNavigation(new NavigationEntry(roster ? DashboardPage.Roster : DashboardPage.Home));
        }

        private void ShowMessagesPage(bool recordNavigation = true)
        {
            document.rootVisualElement.Q<VisualElement>("overview-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("message-content").style.display = DisplayStyle.Flex;
            document.rootVisualElement.Q<VisualElement>("roster-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("wrestler-profile-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-schedule-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-planning-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-message")?.EnableInClassList("active", true);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", false);
            Set("dashboard-page-title", "메시지 / 수신함");
            inboxController.Render();
            if (recordNavigation) RecordNavigation(new NavigationEntry(DashboardPage.Messages));
        }

        private void ShowSchedulePage(bool recordNavigation = true)
        {
            document.rootVisualElement.Q<VisualElement>("overview-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("message-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("roster-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("wrestler-profile-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-planning-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-schedule-content").style.display = DisplayStyle.Flex;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-message")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", true);
            Set("dashboard-page-title", "쇼 / 전체 일정");
            scheduleController.Render(boundSave);
            if (recordNavigation) RecordNavigation(new NavigationEntry(DashboardPage.ShowSchedule));
        }

        private void ShowPlanningPage(string showId = null, bool recordNavigation = true)
        {
            document.rootVisualElement.Q<VisualElement>("overview-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("message-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("roster-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("wrestler-profile-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-schedule-content").style.display = DisplayStyle.None;
            document.rootVisualElement.Q<VisualElement>("show-planning-content").style.display = DisplayStyle.Flex;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-message")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-roster")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-show")?.EnableInClassList("active", true);
            var show = showPlanningController.Show(showId, showTestMode);
            Set("dashboard-page-title", show == null ? "쇼 / 쇼 기획" : $"쇼 / {(string.IsNullOrWhiteSpace(show.Name) ? DashboardText.ShowName(show.ShowType) : show.Name)}");
            if (recordNavigation) RecordNavigation(new NavigationEntry(DashboardPage.ShowPlanning, showPlanningController.SelectedShowId));
        }

        private void ShowProfilePage()
        {
            var editor = document.rootVisualElement.Q("show-editor-overlay");
            if (editor != null && !editor.ClassListContains("hidden"))
            {
                profileReturnOverlay = editor;
                editor.AddToClassList("hidden");
            }
            simulationPlayer?.ViewProfile(true);
            var overview = document.rootVisualElement.Q<VisualElement>("overview-content");
            var messageContent = document.rootVisualElement.Q<VisualElement>("message-content");
            var rosterContent = document.rootVisualElement.Q<VisualElement>("roster-content");
            var profileContent = document.rootVisualElement.Q<VisualElement>("wrestler-profile-content");
            var scheduleContent = document.rootVisualElement.Q<VisualElement>("show-schedule-content");
            var showContent = document.rootVisualElement.Q<VisualElement>("show-planning-content");
            if (overview != null) overview.style.display = DisplayStyle.None;
            if (messageContent != null) messageContent.style.display = DisplayStyle.None;
            if (rosterContent != null) rosterContent.style.display = DisplayStyle.None;
            if (profileContent != null) profileContent.style.display = DisplayStyle.Flex;
            if (scheduleContent != null) scheduleContent.style.display = DisplayStyle.None;
            if (showContent != null) showContent.style.display = DisplayStyle.None;
            document.rootVisualElement.Q<Button>("nav-home")?.EnableInClassList("active", false);
            document.rootVisualElement.Q<Button>("nav-message")?.EnableInClassList("active", false);
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
            if (navigationHistory[navigationIndex].Page != DashboardPage.WrestlerProfile)
            {
                profileReturnOverlay?.RemoveFromClassList("hidden");
                profileReturnOverlay = null;
                simulationPlayer?.ViewProfile(false);
            }
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
                    case DashboardPage.Messages: ShowMessagesPage(false); break;
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





        private void BindNextShow(GameSave save, ScheduleState schedule, ShowState show)
        {
            if (schedule == null)
            {
                Set("next-show-caption", "다음 쇼 일정"); Set("top-next-show", "예정된 쇼 없음"); Set("next-show-date", "—");
                Set("next-show-name", "예정된 쇼가 없습니다"); Set("next-show-detail", "시즌 일정을 확인하세요"); Set("next-show-type", "—"); Set("next-show-status", "일정 없음");
                SetProgress(0); Set("card-count", "카드 0개"); Set("participant-count", "출연 0명"); Set("estimated-cost", "예상 비용 —"); return;
            }
            Set("next-show-caption", $"다음 쇼 일정 · {save.CurrentDate.DaysUntil(schedule.Date)}일 남음");
            Set("top-next-show", show?.Name ?? DashboardText.ShowName(schedule.ShowType)); Set("next-show-date", $"{schedule.Date.Month:D2}\n{schedule.Date.Day:D2}");
            Set("next-show-name", show?.Name ?? DashboardText.ShowName(schedule.ShowType)); Set("next-show-detail", $"{DashboardText.Date(schedule.Date)} · 부킹 마감 {DashboardText.Date(schedule.BookingDeadline)}");
            Set("next-show-type", DashboardText.ShowType(schedule.ShowType)); Set("next-show-status", show == null ? "카드 미생성" : DashboardText.ShowStatus(show.Status));
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


        private void BindAdvanceButton(GameSave save)
        {
            var button = document.rootVisualElement.Q<Button>("advance-time");
            if (button == null) return;
            var show = TodaysShow(save);
            if (showTestMode && show == null) show = save?.Shows?.FirstOrDefault();
            button.text = show?.Status switch
            {
                ShowStatus.Confirmed => "일정 시작  ›",
                ShowStatus.InProgress => "쇼 진행  ›",
                ShowStatus.ResultReview => "결과 확인  ›",
                ShowStatus.ResultsReviewed => "일정 끝  ›",
                _ => "진행  ›"
            };
            if (showTestMode) button.text = show?.Status switch
            {
                ShowStatus.Confirmed or ShowStatus.InProgress => "쇼 보기  ›",
                ShowStatus.ResultReview => "쇼 이어 보기  ›",
                ShowStatus.ResultsReviewed or ShowStatus.Completed => "쇼 다시 보기  ›",
                _ => "편성을 확정하세요"
            };
            button.SetEnabled(save != null);
            if (showTestMode) button.SetEnabled(show?.Status is ShowStatus.Confirmed or ShowStatus.InProgress or ShowStatus.ResultReview or ShowStatus.ResultsReviewed or ShowStatus.Completed);
        }

        private async void AdvanceDay()
        {
            if (boundSave == null || advancingShow || simulationPlayer != null) return;
            advancingShow = true;
            var executionSave = boundSave;
            try
            {
                var show = TodaysShow(boundSave);
                if (showTestMode && show == null) show = boundSave.Shows.FirstOrDefault();
                if (show != null)
                {
                    switch (show.Status)
                    {
                        case ShowStatus.Confirmed:
                            show.Status = ShowStatus.InProgress;
                            if (showTestMode) goto case ShowStatus.InProgress;
                            break;
                        case ShowStatus.InProgress:
                            if (showPlanningController.PlanningService == null || showPlanningController.Content == null)
                                throw new InvalidOperationException("쇼 실행 데이터를 불러오지 못했습니다.");
                            var venueContract = boundSave.VenueContracts.Single(x => x.Id == show.VenueContractId);
                            var venue = showPlanningController.Content.Venues[venueContract.VenueId];
                            var showResult = new ShowExecutionService(
                                showPlanningController.PlanningService,
                                new MatchEvaluator(showPlanningController.Content, showPlanningController.Content, findMove: showPlanningController.Content.GetMoveRules),
                                new PromoEvaluator())
                                .Execute(boundSave, show.Id, ShowResultSeed(boundSave, show), venue.Capacity, venue.BaseTicketPrice);
                            DashboardSession.PersistActive();
                            await GeneratePromoNarratives(executionSave, showResult);
                            if (this == null || !isActiveAndEnabled || boundSave != executionSave) return;
                            DashboardSession.PersistActive();
                            OpenShowSimulation(showResult);
                            return;
                        case ShowStatus.ResultReview:
                            OpenShowSimulation(boundSave.ShowResults.Single(x => x.ShowId == show.Id && x.ShowVersion == show.ShowVersion));
                            return;
                        case ShowStatus.ResultsReviewed:
                        case ShowStatus.Completed when showTestMode:
                            var result = boundSave.ShowResults.Single(x => x.ShowId == show.Id && x.ShowVersion == show.ShowVersion);
                            if (showTestMode) { OpenShowSimulation(result); return; }
                            new ShowResultApplicationService().Apply(boundSave, result.Id);
                            AdvanceOneDay();
                            break;
                        default:
                            ShowPlanningPage(show.Id);
                            throw new InvalidOperationException("오늘 쇼의 편성을 먼저 확정해야 합니다.");
                    }
                }
                else
                {
                    AdvanceOneDay();
                }
                DashboardSession.PersistActive();
            }
            catch (Exception exception)
            {
                Set("next-show-detail", exception.Message);
                if (showTestMode) Set("show-planning-notice", exception.Message);
            }
            finally { advancingShow = false; }
        }

        private async Task GeneratePromoNarratives(GameSave save, ShowResultState showResult)
        {
            promoNarrativeAgent ??= FindAnyObjectByType<LLMAgent>();
            var service = new PromoNarrativeGenerationService(promoNarrativeAgent);
            foreach (var id in showResult.PromoResultIds)
            {
                var result = save.PromoResults.Single(x => x.Id == id);
                if (result.Narrative != null) continue;
                var context = PromoContextBuilder.Build(save, result);
                result.Narrative = await service.Generate(context);
            }
        }

        private void AdvanceOneDay()
        {
            var result = new TimeFlowService().AdvanceTo(boundSave, boundSave.CurrentDate.AddDays(1));
            if (result.State == TimeFlowState.Blocked) throw new InvalidOperationException(result.BlockingReason);
        }

        private static ShowState TodaysShow(GameSave save)
        {
            var schedule = save?.Schedules?.FirstOrDefault(x => x != null && x.Status == ScheduleStatus.Confirmed && x.Date.Equals(save.CurrentDate));
            return schedule == null ? null : save.Shows?.SingleOrDefault(x => x?.ScheduleId == schedule.Id);
        }

        private static int ShowResultSeed(GameSave save, ShowState show)
        {
            unchecked
            {
                var seed = save.WorldSeed;
                seed = seed * 397 ^ show.Date.Year;
                seed = seed * 397 ^ show.Date.Month;
                seed = seed * 397 ^ show.Date.Day;
                return seed * 397 ^ show.ShowVersion;
            }
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

        private void BindFanReaction()
        {
            var audience = boundSave?.Promotion?.Audience;
            if (audience?.IsInitialized != true)
            { Set("fan-caption", "—"); Set("mania-value", "—"); Set("light-value", "—"); Set("family-value", "—"); return; }
            Set("fan-caption", $"{audience.TotalFollowers:N0}명 · 주간 {Signed(audience.LastWeeklyChange)}");
            Set("mania-value", $"{audience.Hardcore.Followers:N0}명 · 만족 {audience.Hardcore.Satisfaction:0}");
            Set("light-value", $"{audience.Casual.Followers:N0}명 · 만족 {audience.Casual.Satisfaction:0}");
            Set("family-value", $"{audience.Mark.Followers:N0}명 · 만족 {audience.Mark.Satisfaction:0}");
        }
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
            Set("result-name", show?.Name ?? "완료된 쇼"); Set("result-finance", $"수익 {Compact(result.FinancialSettlement.Revenue)} · 비용 {Compact(result.FinancialSettlement.Cost)} · 순손익 {SignedMoney(result.FinancialSettlement.NetIncome)}");
            Set("show-score", Stars(result.ShowEvaluation.CriticReview)); Set("match-score", matches.Count == 0 ? "—" : Stars(matches.OrderByDescending(x => x.CriticReview?.FinalScore ?? 0f).First().CriticReview));
            Set("result-mania", result.FanSatisfaction?.IsCalculated == true ? $"{result.FanSatisfaction.Mania:0}" : "—");
            Set("result-light", result.FanSatisfaction?.IsCalculated == true ? $"{result.FanSatisfaction.Light:0}" : "—");
            Set("result-family", result.FanSatisfaction?.IsCalculated == true ? $"{result.FanSatisfaction.Family:0}" : "—");
            var change = result.WrestlerChanges?.Concat(result.StoryChanges ?? new List<ResultChangeState>()).FirstOrDefault(); Set("result-change", change == null ? "기록된 변화 없음" : $"{change.ValueKey} {Signed(change.Amount)} · {change.Reason}");
            BindShowSimulation(save, result);
        }

        private void BindShowSimulation(GameSave save, ShowResultState result)
        {
            var card = document.rootVisualElement.Q<VisualElement>(className: "result-card");
            if (card == null) return;
            var button = card.Q<Button>("show-simulation");
            if (button == null)
            {
                button = new Button(() =>
                {
                    var latest = boundSave?.ShowResults?.LastOrDefault(x => x != null);
                    if (latest != null && !advancingShow) OpenShowSimulation(latest);
                }) { name = "show-simulation", text = "쇼 다시 보기" };
                button.AddToClassList("show-action-button");
                card.Add(button);
            }
            button.RemoveFromClassList("hidden");
            button.SetEnabled(result.TimelineResultIds?.Count > 0);
        }

        private void OpenShowSimulation(ShowResultState result)
        {
            if (simulationPlayer != null) return;
            try
            {
                simulationPlayer = new ShowSimulationPlayer(document.rootVisualElement, boundSave, result, () =>
                {
                    var show = boundSave.Shows.Single(x => x.Id == result.ShowId);
                    if (show.Status == ShowStatus.ResultReview)
                    {
                        show.Status = ShowStatus.ResultsReviewed;
                        try
                        {
                            if (showTestMode) new ShowResultApplicationService().Apply(boundSave, result.Id);
                            DashboardSession.PersistActive();
                        }
                        catch (Exception exception)
                        {
                            show.Status = ShowStatus.ResultReview;
                            simulationPlayer.ShowSaveError(exception.Message);
                            return;
                        }
                    }
                    simulationPlayer.Dispose();
                    simulationPlayer = null;
                    ShowPage(false);
                });
            }
            catch (Exception exception) { Set("next-show-detail", exception.Message); }
        }

        private void ShowEmpty()
        {
            foreach (var id in new[] { "current-date", "top-cash", "top-next-show", "cash-value", "prestige-value", "roster-value", "risk-value", "staff-value" }) Set(id, "—");
            Set("next-show-caption", "다음 쇼 일정"); Set("next-show-date", "—"); Set("next-show-name", "활성 저장 데이터가 없습니다"); Set("next-show-detail", "새 게임 또는 저장 데이터를 연결하세요"); Set("next-show-type", "—"); Set("next-show-status", "저장 없음");
            Set("cash-detail", "활성 저장 없음"); Set("promotion-name", "—"); Set("promotion-abbreviation", "—"); Set("roster-detail", "전체 —"); Set("risk-detail", "부상 — · 컨디션 —"); Set("staff-detail", "부서 데이터 없음");
            SetProgress(0); Set("card-count", "카드 —"); Set("participant-count", "출연 —"); Set("estimated-cost", "예상 비용 —");
            BindTasks(new List<TaskItem> { new("—", "활성 저장 데이터 없음", "") }); BindFanReaction(); BindStories(new GameSave());
            Set("result-name", "결과 데이터 없음"); Set("result-finance", "완료된 쇼 결과가 생성되면 표시됩니다"); SetResultEmpty();
            inboxController.Bind(boundSave);
            Badge("home-badge", 0); Badge("message-badge", 0); Badge("show-badge", 0); Badge("report-badge", 0); var button = document.rootVisualElement.Q<Button>("notifications"); if (button != null) button.text = "알림  0";
            rosterController.Render(boundSave);
            scheduleController.Render(boundSave);
        }

        private void SetResultEmpty() { foreach (var id in new[] { "show-score", "match-score", "result-mania", "result-light", "result-family" }) Set(id, "—"); Set("result-change", "데이터 없음"); document?.rootVisualElement?.Q("show-simulation")?.AddToClassList("hidden"); }
        private void SetProgress(int value) { Set("preparation-value", $"준비도 {value}%"); var bar = document.rootVisualElement.Q<VisualElement>(className: "progress-fill"); if (bar != null) bar.style.width = Length.Percent(value); }
        private void Badge(string id, int count) { var e = document.rootVisualElement.Q<Label>(id); if (e == null) return; e.text = count.ToString(); e.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None; }
        private void Set(string id, string value) { var label = document.rootVisualElement.Q<Label>(id); WrestlerNameText.Set(label, value, boundSave?.Wrestlers); }
        private void ApplyIcons()
        {
            foreach (var name in new[] { "home", "message", "roster", "teams", "locker", "scout", "show", "story", "title", "tournament", "schedule", "staff", "company", "finance", "report", "world", "history", "settings" })
            {
                var icon = document.rootVisualElement.Q<VisualElement>($"icon-{name}");
                if (icon == null) continue;
                icon.Clear();
                icon.AddToClassList($"nav-icon-{name}");
            }
        }
        private static int ParticipantCount(GameSave save, List<ShowEventState> events) { var ids = new HashSet<string>(StringComparer.Ordinal); foreach (var e in events) if (e.EventType == ShowEventType.Match) { var m = save.MatchPlans?.FirstOrDefault(x => x?.Id == e.DetailId); if (m != null) { foreach (var s in m.Sides ?? new List<MatchSideState>()) foreach (var id in s.MemberIds ?? new List<string>()) if (!string.IsNullOrWhiteSpace(id)) ids.Add(id); foreach (var id in m.ParticipantIds ?? new List<string>()) if (!string.IsNullOrWhiteSpace(id)) ids.Add(id); } } else { var p = save.PromoPlans?.FirstOrDefault(x => x?.Id == e.DetailId); if (p != null) foreach (var id in p.ParticipantIds ?? new List<string>()) if (!string.IsNullOrWhiteSpace(id)) ids.Add(id); } return ids.Count; }
        private static string Compact(long v) => Math.Abs(v) >= 1_000_000 ? $"${v / 1_000_000f:0.0}M" : $"${v:N0}";
        private static string SignedMoney(long v) => $"{(v >= 0 ? "+" : "−")}{Compact(Math.Abs(v))}";
        private static string Signed(float v) => $"{(v >= 0 ? "+" : "")}{v:0.0}";
        private static string Stars(CriticReviewState review) => review == null ? "—" : $"★ {review.DisplayedStars:0.##}";
        private void OnClick(ClickEvent evt)
        {
            var target = evt.target as VisualElement;
            var button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
            if (button == null) return;
            if (button.name == "dashboard-back") { NavigateBack(); evt.StopPropagation(); return; }
            if (button.name == "dashboard-forward") { NavigateForward(); evt.StopPropagation(); return; }
            if (button.name == "nav-home") { WrestlerProfileController.CloseOpenProfile(false); ShowPage(false); evt.StopPropagation(); return; }
            if (button.name == "nav-message") { WrestlerProfileController.CloseOpenProfile(false); ShowMessagesPage(); evt.StopPropagation(); return; }
            if (button.name == "nav-roster") { WrestlerProfileController.CloseOpenProfile(false); ShowPage(true); evt.StopPropagation(); return; }
            if (button.name == "nav-show") { WrestlerProfileController.CloseOpenProfile(false); ShowSchedulePage(); evt.StopPropagation(); return; }
            Debug.Log($"Dashboard action selected: {button.name}");
        }
        private enum DashboardPage { Home, Messages, Roster, WrestlerProfile, ShowSchedule, ShowPlanning }
        private sealed class NavigationEntry
        {
            public readonly DashboardPage Page;
            public readonly string ContextId;
            public NavigationEntry(DashboardPage page, string contextId = null) { Page = page; ContextId = contextId; }
            public bool Matches(NavigationEntry other) => other != null && Page == other.Page && string.Equals(ContextId, other.ContextId, StringComparison.Ordinal);
        }
        private readonly struct TaskItem { public readonly string Severity, Text, Context; public readonly WrestlerState Wrestler; public TaskItem(string severity, string text, string context, WrestlerState wrestler = null) { Severity = severity; Text = text; Context = context; Wrestler = wrestler; } }
    }

}
