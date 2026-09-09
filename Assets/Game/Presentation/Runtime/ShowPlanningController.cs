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
    internal sealed class ShowPlanningController
    {
        private readonly VisualElement root;
        private GameSave save;
        private StaticContentRegistry staticContent;
        private ShowPlanningService showPlanningService;
        private string selectedShowEventId;
        private string selectedShowId;
        private readonly HashSet<string> editorParticipantIds = new(StringComparer.Ordinal);
        private readonly List<string> editorParticipantSlots = new();
        private int selectedParticipantSlotIndex = -1;
        private string pendingParticipantId;
        private bool editorRosterAscending;
        private readonly List<MatchFinishType> editorFinishTypes = new();
        private readonly List<MatchSideState> editorFinishSides = new();
        private readonly List<string> editorFinishPerformers = new();
        private readonly List<string> editorFinishLosers = new();
        private MatchSpotEditor matchSpotEditor;
        private ShowEditorMode showEditorMode;
        private ShowEditSection showEditSection;
        private string editingShowEventId;
        private MatchRuleOption selectedMatchRule;
        private string selectedMatchGimmickId;
        private bool showTestMode;

        public string SelectedShowId => selectedShowId;
        public StaticContentRegistry Content => staticContent;
        public ShowPlanningService PlanningService => showPlanningService;

        public ShowPlanningController(VisualElement root) { this.root = root; SetupControls(); }
        public void Bind(GameSave value, bool testMode) { save = value; showTestMode = testMode; RenderShowPlanning(); }
        public ShowState Show(string showId, bool testMode)
        {
            if (!string.IsNullOrEmpty(showId)) { selectedShowId = showId; selectedShowEventId = null; }
            showTestMode = testMode;
            RenderShowPlanning();
            return NextPlanningShow();
        }

        private void SetupControls()
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
            BindClick("show-duration-increase", () => ChangeSelectedEventDuration(5));
            BindClick("show-duration-decrease", () => ChangeSelectedEventDuration(-5));
            BindClick("show-edit-rules", () => OpenSelectedEventEditor(ShowEditSection.Rules));
            BindClick("show-edit-spots", () => OpenSelectedEventEditor(ShowEditSection.Producing));
            BindClick("show-move-up", () => MoveSelectedEvent(-1));
            BindClick("show-move-down", () => MoveSelectedEvent(1));
            BindClick("show-remove-event", RemoveSelectedEvent);
            BindClick("show-editor-close", CloseShowEditor);
            BindClick("show-editor-cancel", CloseShowEditor);
            BindClick("show-editor-submit", SubmitShowEditor);
            var sort = root.Q<DropdownField>("show-editor-roster-sort");
            sort.choices = new List<string> { "이름", "경기력", "프로모", "모멘텀", "컨디션", "만족도" };
            sort.SetValueWithoutNotify(sort.choices[0]);
            sort.RegisterValueChangedCallback(_ => RenderShowEditorRoster(NextPlanningShow()));
            BindClick("show-editor-roster-sort-direction", () =>
            {
                editorRosterAscending = !editorRosterAscending;
                root.Q<Button>("show-editor-roster-sort-direction").text = editorRosterAscending ? "오름차순" : "내림차순";
                RenderShowEditorRoster(NextPlanningShow());
            });
            root.Q<DropdownField>("show-editor-finish-type").RegisterValueChangedCallback(_ => RefreshFinishParticipants());
            root.Q<DropdownField>("show-editor-winning-side").RegisterValueChangedCallback(_ => RefreshFinishParticipants());
        }

        private void RenderShowPlanning()
        {
            var rows = root?.Q<VisualElement>("show-timeline-rows");
            var timelineScroll = root?.Q<ScrollView>("show-timeline-scroll");
            var participants = root?.Q<VisualElement>("show-detail-participants");
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

            var eventsById = (save.ShowEvents ?? new List<ShowEventState>()).Where(x => x != null && x.ShowId == show.Id)
                .ToDictionary(x => x.Id, StringComparer.Ordinal);
            var ordered = (show.TimelineEventIds ?? new List<string>()).Where(eventsById.ContainsKey).Select(id => eventsById[id]).ToList();
            if (selectedShowEventId == null || ordered.All(x => x.Id != selectedShowEventId)) selectedShowEventId = ordered.FirstOrDefault()?.Id;

            Set("show-plan-name", string.IsNullOrWhiteSpace(show.Name) ? DashboardText.ShowName(show.ShowType) : show.Name);
            Set("show-plan-meta", $"{DashboardText.Date(show.Date)} · {DashboardText.ShowType(show.ShowType)} · {VenueName(show)}" + (showTestMode ? string.Empty : $" · 제한 시간 {show.DurationLimit}분"));
            Set("show-plan-status", DashboardText.ShowStatus(show.Status)); Set("show-plan-cost", DashboardText.Money(show.EstimatedCost));
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
                var title = WrestlerNameText.Create(ShowEventTitle(showEvent), save?.Wrestlers); title.AddToClassList("show-event-title"); copy.Add(title);
                var subtitle = showEvent.EventType == ShowEventType.Match
                    ? ShowEventDescription(showEvent)
                    : string.Join(" · ", ShowEventParticipantIds(showEvent).Select(WrestlerName));
                var cast = WrestlerNameText.Create(subtitle, save?.Wrestlers); cast.AddToClassList("show-event-participants"); copy.Add(cast); row.Add(copy);
                var duration = new Label($"{showEvent.PlannedDuration}분"); duration.AddToClassList("show-event-duration"); row.Add(duration);
                var positionText = ordered.Count == 1 ? "오프닝 · 메인" : index == 0 ? "오프닝" : index == ordered.Count - 1 ? "메인" : string.Empty;
                var position = new Label(positionText); position.AddToClassList("show-event-position-label"); row.Add(position);
                var captured = showEvent.Id; row.AddManipulator(new Clickable(() => { selectedShowEventId = captured; RenderShowPlanning(); }));
                if (IsShowEditable(show))
                {
                    handle.AddManipulator(new ShowTimelineDragManipulator(
                        row,
                        () => show.TimelineEventIds.IndexOf(captured),
                        TimelineDropIndex,
                        (from, to) => MoveEventByIndex(show, from, to)));
                }
                rows.Add(row);
            }
            if (ordered.Count == 0) { var empty = new Label("아직 편성된 경기나 프로모가 없습니다"); empty.AddToClassList("show-empty-state"); rows.Add(empty); }

            var used = show.CalculatePlannedDuration(ordered);
            var matchCount = ordered.Count(x => x.EventType == ShowEventType.Match);
            var promoCount = ordered.Count - matchCount;
            var castCount = ordered.SelectMany(ShowEventParticipantIds).Distinct(StringComparer.Ordinal).Count();
            Set("show-duration-used", $"{used}분 사용"); Set("show-duration-limit", $"총 {(showTestMode ? used : show.DurationLimit)}분");
            Set("show-match-count", matchCount.ToString()); Set("show-promo-count", promoCount.ToString()); Set("show-participant-count", castCount.ToString());
            var percent = showTestMode ? (used > 0 ? 100f : 0f) : show.DurationLimit <= 0 ? 0 : Math.Min(100f, used * 100f / show.DurationLimit); Width("show-duration-fill", percent);
            var overDuration = !showTestMode && used > show.DurationLimit;
            root.Q<VisualElement>("show-duration-fill")?.EnableInClassList("over", overDuration);
            root.Q<Label>("show-duration-used")?.EnableInClassList("over", overDuration);
            Set("show-planning-notice", showTestMode ? string.Empty : used == show.DurationLimit ? "제한 시간에 맞게 편성되었습니다" : used < show.DurationLimit ? $"남은 편성 시간 {show.DurationLimit - used}분" : $"제한 시간을 {used - show.DurationLimit}분 초과했습니다");
            RenderShowDetail(show, ordered);
        }

        private ShowState NextPlanningShow()
        {
            if (save?.Shows == null) return null;
            var explicitlySelected = save.Shows.FirstOrDefault(x => x != null && x.Id == selectedShowId);
            if (explicitlySelected != null) return explicitlySelected;
            var available = save.Shows.Where(x => x != null && x.Status != ShowStatus.Completed)
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day).ToList();
            var selected = available.FirstOrDefault(x => x.Id == selectedShowId);
            if (selected != null) return selected;
            selected = available.FirstOrDefault(x => x.Date.CompareTo(save.CurrentDate) >= 0) ?? available.FirstOrDefault();
            selectedShowId = selected?.Id;
            return selected;
        }

        private void RefreshShowActions(ShowState selected)
        {
            foreach (var id in new[] { "show-add-match", "show-add-promo", "show-save-draft", "show-confirm" })
                root.Q<Button>(id)?.SetEnabled(IsShowEditable(selected));
        }

        private static bool IsShowEditable(ShowState show) =>
            show != null && show.Status is ShowStatus.Draft or ShowStatus.Preparing or ShowStatus.Review;

        private void AddEmptyMatch()
        {
            try
            {
                var show = NextPlanningShow() ?? throw new InvalidOperationException("편성할 쇼를 선택하세요.");
                var type = MatchTypes().FirstOrDefault(x => x.Id == "matchtype_001") ?? MatchTypes().FirstOrDefault()
                    ?? throw new InvalidOperationException("경기 유형 데이터를 불러오지 못했습니다.");
                var added = showPlanningService.AddMatch(save, show.Id, new MatchPlanState
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
                var added = showPlanningService.AddPromo(save, show.Id, new PromoPlanState
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
            root.Q<Button>("show-editor-submit").text = "추가";
            var primary = root.Q<DropdownField>("show-editor-primary");
            var secondary = root.Q<DropdownField>("show-editor-secondary");
            var duration = root.Q<DropdownField>("show-editor-duration");
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
            var overlay = root.Q<VisualElement>("show-editor-overlay");
            overlay?.BringToFront();
            overlay?.RemoveFromClassList("hidden");
        }

        private void OpenSelectedEventEditor(ShowEditSection section)
        {
            var show = NextPlanningShow();
            var showEvent = save?.ShowEvents?.FirstOrDefault(x => x?.Id == selectedShowEventId && x.ShowId == show?.Id);
            if (!IsShowEditable(show) || showEvent == null) return;
            OpenShowEditor(showEvent.EventType == ShowEventType.Match ? ShowEditorMode.Match : ShowEditorMode.Promo);
            showEditSection = section;
            editingShowEventId = showEvent.Id;
            editorParticipantIds.Clear();
            editorParticipantSlots.Clear();
            selectedParticipantSlotIndex = -1;
            var duration = root.Q<DropdownField>("show-editor-duration");
            duration.index = Math.Max(0, showEvent.PlannedDuration / 5 - 1);
            var primary = root.Q<DropdownField>("show-editor-primary");
            var secondary = root.Q<DropdownField>("show-editor-secondary");
            if (showEvent.EventType == ShowEventType.Match)
            {
                var match = save.MatchPlans.First(x => x.Id == showEvent.DetailId);
                primary.index = Math.Max(0, MatchTypes().FindIndex(x => x.Id == match.MatchTypeId));
                secondary.index = (int)match.FinishType;
                InitializeMatchRuleSelection(match, showEvent);
                InitializeMatchParticipantSlots(match);
                SetSpotFields(match.OpeningSpot, match.MiddleSpot, match.ClosingSpot);
                if (section == ShowEditSection.Producing)
                {
                    InitializeProduction(match);
                    matchSpotEditor = new MatchSpotEditor(root.Q("show-editor-match-spots"), save, match, show.Date);
                }
            }
            else
            {
                var promo = save.PromoPlans.First(x => x.Id == showEvent.DetailId);
                foreach (var id in promo.ParticipantIds ?? new List<string>()) editorParticipantIds.Add(id);
                primary.index = (int)promo.Purpose;
                secondary.index = (int)promo.Presentation;
                SetSpotFields(promo.OpeningSpot, promo.MiddleSpot, promo.ClosingSpot);
            }
            Set("show-editor-title", section switch { ShowEditSection.Rules when showEvent.EventType == ShowEventType.Match => "경기 규칙 및 기믹", ShowEditSection.Rules => "규칙 편집", ShowEditSection.Participants => "참가자 편집", _ => "프로듀싱" });
            root.Q<Button>("show-editor-submit").text = "변경 저장";
            RenderShowEditorRoster(show);
            ConfigureShowEditorSection();
        }

        private void ConfigureShowEditorSection()
        {
            var fields = root.Q<VisualElement>(className: "show-editor-fields");
            var rosterHeading = root.Q<VisualElement>(className: "show-editor-roster-heading");
            var rosterScroll = root.Q<ScrollView>(className: "show-editor-roster-scroll");
            var spots = root.Q<VisualElement>("show-editor-spots");
            root.Q(className: "show-editor-dialog").EnableInClassList("spot-production", showEditSection == ShowEditSection.Producing);
            var matchRules = root.Q<VisualElement>("show-editor-match-rules");
            var showMatchRulePicker = showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Rules;
            var assignsSingleMatchSlot = showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Participants;
            var showRules = (showEditSection is ShowEditSection.Full or ShowEditSection.Rules) && !showMatchRulePicker;
            var showParticipants = showEditSection is ShowEditSection.Full or ShowEditSection.Participants;
            if (fields != null) fields.style.display = showRules ? DisplayStyle.Flex : DisplayStyle.None;
            if (matchRules != null) matchRules.style.display = showMatchRulePicker ? DisplayStyle.Flex : DisplayStyle.None;
            if (rosterHeading != null) rosterHeading.style.display = showParticipants ? DisplayStyle.Flex : DisplayStyle.None;
            if (rosterScroll != null) rosterScroll.style.display = showParticipants ? DisplayStyle.Flex : DisplayStyle.None;
            if (spots != null) spots.style.display = showEditSection == ShowEditSection.Producing ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("show-editor-finish").EnableInClassList("hidden", showEditorMode != ShowEditorMode.Match);
            root.Q("show-editor-match-spots").EnableInClassList("hidden", showEditorMode != ShowEditorMode.Match);
            root.Q<TextField>("show-editor-opening-spot").label = showEditorMode == ShowEditorMode.Match ? "경기 전 스팟" : "오프닝 스팟";
            root.Q<TextField>("show-editor-middle-spot").label = showEditorMode == ShowEditorMode.Match ? "경기 중 스팟" : "중반 스팟";
            root.Q<TextField>("show-editor-closing-spot").label = showEditorMode == ShowEditorMode.Match ? "경기 후 스팟" : "마무리 스팟";
            var submit = root.Q<Button>("show-editor-submit");
            if (submit != null)
            {
                submit.style.display = DisplayStyle.Flex;
                submit.SetEnabled(!assignsSingleMatchSlot || (!string.IsNullOrEmpty(pendingParticipantId) && !editorParticipantSlots.Contains(pendingParticipantId)));
                if (assignsSingleMatchSlot) submit.text = "결정";
            }
            var cancel = root.Q<Button>("show-editor-cancel");
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
            var ruleHost = root.Q<VisualElement>("show-editor-rule-options");
            var gimmickHost = root.Q<VisualElement>("show-editor-gimmick-options");
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
            root.Q<TextField>("show-editor-opening-spot").value = opening ?? string.Empty;
            root.Q<TextField>("show-editor-middle-spot").value = middle ?? string.Empty;
            root.Q<TextField>("show-editor-closing-spot").value = closing ?? string.Empty;
        }

        private void InitializeProduction(MatchPlanState match)
        {
            editorFinishSides.Clear();
            editorFinishSides.AddRange((match.Sides ?? new List<MatchSideState>()).Where(x => x != null));
            editorFinishTypes.Clear();
            editorFinishTypes.AddRange(AllowedFinishTypes(match));
            SetEditorChoices("show-editor-finish-type", editorFinishTypes.Select(MatchFinishText).ToList(), editorFinishTypes.IndexOf(match.FinishType));
            var sides = new List<string> { "미정" };
            sides.AddRange(editorFinishSides.Select((side, index) => $"{index + 1}. {string.Join(" & ", (side.MemberIds ?? new List<string>()).Select(WrestlerName))}"));
            var winningSide = match.WinningSideId ?? editorFinishSides.FirstOrDefault(x => x.MemberIds?.Contains(match.WinnerId) == true)?.Id;
            SetEditorChoices("show-editor-winning-side", sides, editorFinishSides.FindIndex(x => x.Id == winningSide) + 1);
            editorFinishPerformers.Clear();
            editorFinishLosers.Clear();
            RefreshFinishParticipants();
            SetEditorChoices("show-editor-finish-performer", editorFinishPerformers.Select(WrestlerName).ToList(), editorFinishPerformers.IndexOf(match.FinishPerformerId ?? match.WinnerId));
            SetEditorChoices("show-editor-loser", editorFinishLosers.Select(WrestlerName).ToList(), editorFinishLosers.IndexOf(match.LoserTargetId));
        }

        private IEnumerable<MatchFinishType> AllowedFinishTypes(MatchPlanState match)
        {
            staticContent.TryGetGimmickRules(match.MatchGimmickId, out var gimmick);
            var isTeam = staticContent.MatchTypes.TryGetValue(match.MatchTypeId, out var type) && type.MinimumTeamCount > 0;
            var count = match.TeamCount * match.MembersPerTeam;
            return Enum.GetValues(typeof(MatchFinishType)).Cast<MatchFinishType>()
                .Where(x => MatchFinishRules.IsAllowed(x, isTeam, count, match.TeamCount, gimmick));
        }

        private void SetEditorChoices(string name, List<string> choices, int index)
        {
            var field = root.Q<DropdownField>(name);
            field.choices = choices;
            field.SetValueWithoutNotify(choices.Count == 0 ? string.Empty : choices[Math.Max(0, Math.Min(index, choices.Count - 1))]);
        }

        private void RefreshFinishParticipants()
        {
            var finishIndex = root.Q<DropdownField>("show-editor-finish-type").index;
            var draw = finishIndex < 0 || finishIndex >= editorFinishTypes.Count || editorFinishTypes[finishIndex] == MatchFinishType.Draw;
            var winner = root.Q<DropdownField>("show-editor-winning-side");
            var sideIndex = winner.index - 1;
            var side = sideIndex >= 0 && sideIndex < editorFinishSides.Count ? editorFinishSides[sideIndex] : null;
            var performerIndex = root.Q<DropdownField>("show-editor-finish-performer").index;
            var loserIndex = root.Q<DropdownField>("show-editor-loser").index;
            var performer = performerIndex >= 0 && performerIndex < editorFinishPerformers.Count ? editorFinishPerformers[performerIndex] : null;
            var loser = loserIndex >= 0 && loserIndex < editorFinishLosers.Count ? editorFinishLosers[loserIndex] : null;
            editorFinishPerformers.Clear();
            editorFinishLosers.Clear();
            if (side != null)
            {
                editorFinishPerformers.AddRange((side.MemberIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));
                editorFinishLosers.AddRange(editorFinishSides.Where(x => x != side).SelectMany(x => x.MemberIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)));
            }
            SetEditorChoices("show-editor-finish-performer", editorFinishPerformers.Select(WrestlerName).ToList(), editorFinishPerformers.IndexOf(performer));
            SetEditorChoices("show-editor-loser", editorFinishLosers.Select(WrestlerName).ToList(), editorFinishLosers.IndexOf(loser));
            winner.SetEnabled(!draw && editorFinishSides.Count > 1);
            root.Q("show-editor-finish-performer").SetEnabled(!draw && editorFinishPerformers.Count > 0);
            root.Q("show-editor-loser").SetEnabled(!draw && editorFinishLosers.Count > 0);
        }

        private void SaveProduction(MatchPlanState match)
        {
            var finishIndex = root.Q<DropdownField>("show-editor-finish-type").index;
            if (finishIndex < 0 || finishIndex >= editorFinishTypes.Count) throw new InvalidOperationException("결말 방식을 선택하세요.");
            var finish = editorFinishTypes[finishIndex];
            if (!AllowedFinishTypes(match).Contains(finish)) throw new InvalidOperationException("이 경기 규칙에서 사용할 수 없는 결말입니다.");
            string sideId = null, performer = null, loser = null;
            if (finish != MatchFinishType.Draw)
            {
                if (match.Sides.Count < 2 || match.Sides.Any(x => x?.MemberIds == null || x.MemberIds.Count != match.MembersPerTeam || x.MemberIds.Any(string.IsNullOrWhiteSpace)))
                    throw new InvalidOperationException("먼저 경기의 모든 선수를 배정하세요.");
                var sideIndex = root.Q<DropdownField>("show-editor-winning-side").index - 1;
                var performerIndex = root.Q<DropdownField>("show-editor-finish-performer").index;
                var loserIndex = root.Q<DropdownField>("show-editor-loser").index;
                if (sideIndex < 0 || sideIndex >= editorFinishSides.Count) throw new InvalidOperationException("승리 선수 또는 팀을 선택하세요.");
                if (performerIndex < 0 || performerIndex >= editorFinishPerformers.Count || loserIndex < 0 || loserIndex >= editorFinishLosers.Count)
                    throw new InvalidOperationException("승부를 결정할 선수와 패배 대상을 선택하세요.");
                sideId = editorFinishSides[sideIndex].Id;
                performer = editorFinishPerformers[performerIndex];
                loser = editorFinishLosers[loserIndex];
                if (!match.Sides.Any(x => x.Id == sideId && x.MemberIds.Contains(performer)) || !match.Sides.Any(x => x.Id != sideId && x.MemberIds.Contains(loser)))
                    throw new InvalidOperationException("승리 선수와 패배 대상은 서로 다른 팀이어야 합니다.");
            }
            match.FinishType = finish;
            match.WinningSideId = sideId;
            match.FinishPerformerId = performer;
            match.WinnerId = performer;
            match.LoserTargetId = loser;
        }

        private void RenderShowEditorRoster(ShowState show)
        {
            var host = root.Q<VisualElement>("show-editor-roster");
            if (host == null || show == null) return;
            host.Clear();
            var usesMatchSlots = showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Participants;
            var sortIndex = root.Q<DropdownField>("show-editor-roster-sort").index;
            var sortKey = new[] { "name", "match", "promo", "momentum", "condition", "satisfaction" }[Math.Max(0, sortIndex)];
            var wrestlers = (save.Wrestlers ?? new List<WrestlerState>()).Where(x => x != null &&
                x.Roster?.ActivityState == RosterActivityState.Active && save.Contracts.Any(c => c != null && c.PersonId == x.Id &&
                (c.Status == ContractStatus.Active || c.Status == ContractStatus.Expiring) && c.StartDate.CompareTo(show.Date) <= 0 && c.EndDate.CompareTo(show.Date) >= 0));
            var ordered = (editorRosterAscending ? wrestlers.OrderBy(SortSelector(sortKey)) : wrestlers.OrderByDescending(SortSelector(sortKey))).ThenBy(WrestlerNameText.DisplayName);
            foreach (var wrestler in ordered)
            {
                var button = CreateWrestlerSelectionCard(wrestler);
                button.userData = wrestler.Id;
                var assigned = usesMatchSlots && editorParticipantSlots.Contains(wrestler.Id);
                button.SetEnabled(!assigned);
                if (assigned) button.tooltip = "이미 이 경기에 배정된 선수입니다";
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
            root.Q<Button>("show-editor-submit").SetEnabled(!usesMatchSlots ||
                (!string.IsNullOrEmpty(pendingParticipantId) && !editorParticipantSlots.Contains(pendingParticipantId)));
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
                var parts = WrestlerNameText.DisplayName(wrestler).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
            pendingParticipantId = null;
        }

        private void AssignWrestlerToSelectedSlot(string wrestlerId, ShowState show)
        {
            try
            {
                if (selectedParticipantSlotIndex < 0 || selectedParticipantSlotIndex >= editorParticipantSlots.Count)
                    throw new InvalidOperationException("배치할 참가자 슬롯을 먼저 선택하세요.");
                if (!IsShowEditable(show)) throw new InvalidOperationException("편집 가능한 쇼가 아닙니다.");
                if (string.IsNullOrEmpty(wrestlerId) || editorParticipantSlots.Contains(wrestlerId))
                    throw new InvalidOperationException("이미 이 경기에 배정된 선수는 선택할 수 없습니다.");
                var showEvent = save.ShowEvents.FirstOrDefault(x => x?.Id == editingShowEventId && x.ShowId == show?.Id)
                    ?? throw new InvalidOperationException("편집할 경기를 찾을 수 없습니다.");
                var match = save.MatchPlans.FirstOrDefault(x => x?.Id == showEvent.DetailId)
                    ?? throw new InvalidOperationException("경기 세부 정보를 찾을 수 없습니다.");
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
                match.WinnerId = null;
                DashboardSession.PersistActive();
                CloseShowEditor();
                RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-editor-message", exception.Message); }
        }

        private void CloseShowEditor() => root.Q<VisualElement>("show-editor-overlay")?.AddToClassList("hidden");

        private void SubmitShowEditor()
        {
            try
            {
                var show = NextPlanningShow() ?? throw new InvalidOperationException("편성할 쇼를 선택하세요.");
                if (!IsShowEditable(show)) throw new InvalidOperationException("편집 가능한 쇼가 아닙니다.");
                if (!string.IsNullOrEmpty(editingShowEventId) && showEditorMode == ShowEditorMode.Match && showEditSection == ShowEditSection.Participants)
                {
                    if (string.IsNullOrEmpty(pendingParticipantId)) throw new InvalidOperationException("배정할 선수를 선택하세요.");
                    AssignWrestlerToSelectedSlot(pendingParticipantId, show);
                    return;
                }
                var primary = root.Q<DropdownField>("show-editor-primary");
                var secondary = root.Q<DropdownField>("show-editor-secondary");
                var durationField = root.Q<DropdownField>("show-editor-duration");
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
                        var added = showPlanningService.AddMatch(save, show.Id, plan, duration);
                        selectedShowEventId = added.Id;
                    }
                    else showPlanningService.UpdateMatch(save, show.Id, editingShowEventId, plan, duration);
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
                        var added = showPlanningService.AddPromo(save, show.Id, plan, duration);
                        selectedShowEventId = added.Id;
                    }
                    else
                    {
                        var showEvent = save.ShowEvents.First(x => x.Id == editingShowEventId && x.ShowId == show.Id);
                        plan.Id = showEvent.DetailId;
                        var index = save.PromoPlans.FindIndex(x => x?.Id == plan.Id);
                        if (index < 0) throw new InvalidOperationException("프로모 세부 정보를 찾을 수 없습니다.");
                        save.PromoPlans[index] = plan;
                        showPlanningService.SetEventDuration(save, show.Id, showEvent.Id, duration);
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
            var showEvent = save.ShowEvents.FirstOrDefault(x => x?.Id == editingShowEventId && x.ShowId == show.Id)
                ?? throw new InvalidOperationException("편집할 항목을 찾을 수 없습니다.");
            if (showEvent.EventType == ShowEventType.Match)
            {
                var match = save.MatchPlans.FirstOrDefault(x => x?.Id == showEvent.DetailId)
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
                        match.WinnerId = null;
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
                    match.WinnerId = null;
                }
                else
                {
                    var plannedSpots = matchSpotEditor.Read();
                    SaveProduction(match); SaveSpots(match);
                    match.Spots = plannedSpots;
                }
            }
            else
            {
                var promo = save.PromoPlans.FirstOrDefault(x => x?.Id == showEvent.DetailId)
                    ?? throw new InvalidOperationException("프로모 세부 정보를 찾을 수 없습니다.");
                if (showEditSection == ShowEditSection.Rules)
                {
                    promo.Purpose = (PromoPurpose)Math.Max(0, primary.index);
                    promo.Presentation = (PromoPresentation)Math.Max(0, secondary.index);
                    promo.SponsorRequirementId = promo.Purpose == PromoPurpose.SponsorAdvertisement ? "sponsor_requirement_runtime" : null;
                    showPlanningService.SetEventDuration(save, show.Id, showEvent.Id, duration);
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
            plan.OpeningSpot = root.Q<TextField>("show-editor-opening-spot").value?.Trim();
            plan.MiddleSpot = root.Q<TextField>("show-editor-middle-spot").value?.Trim();
            plan.ClosingSpot = root.Q<TextField>("show-editor-closing-spot").value?.Trim();
        }

        private void SaveSpots(PromoPlanState plan)
        {
            plan.OpeningSpot = root.Q<TextField>("show-editor-opening-spot").value?.Trim();
            plan.MiddleSpot = root.Q<TextField>("show-editor-middle-spot").value?.Trim();
            plan.ClosingSpot = root.Q<TextField>("show-editor-closing-spot").value?.Trim();
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
            var rows = root.Q<VisualElement>("show-timeline-rows");
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
                showPlanningService.MoveEvent(save, show.Id, from, to);
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
                showPlanningService.MoveEvent(save, show.Id, oldIndex, newIndex); DashboardSession.PersistActive(); RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void RemoveSelectedEvent()
        {
            try
            {
                var show = NextPlanningShow(); if (show == null || string.IsNullOrEmpty(selectedShowEventId)) return;
                showPlanningService.RemoveEvent(save, show.Id, selectedShowEventId); selectedShowEventId = null; DashboardSession.PersistActive(); RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void ConfirmSelectedShow()
        {
            try
            {
                var show = NextPlanningShow() ?? throw new InvalidOperationException("편성할 쇼를 선택하세요.");
                if (showTestMode)
                {
                    var duration = show.CalculatePlannedDuration(save.ShowEvents);
                    if (duration <= 0) throw new InvalidOperationException("경기 또는 프로모를 먼저 편성하세요.");
                    show.DurationLimit = duration;
                }
                showPlanningService.Confirm(save, show.Id); DashboardSession.PersistActive(); RenderShowPlanning();
            }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        private void SaveShowDraft()
        {
            try { DashboardSession.PersistActive(); Set("show-planning-notice", "현재 편성 내용을 저장했습니다"); }
            catch (Exception exception) { Set("show-planning-notice", exception.Message); }
        }

        public string VenueName(ShowState show)
        {
            var contract = save?.VenueContracts?.FirstOrDefault(x => x?.Id == show?.VenueContractId);
            if (contract == null) return "경기장 미정";
            return staticContent?.Venues.TryGetValue(contract.VenueId, out var venue) == true ? venue.DisplayName : contract.VenueId;
        }

        private void RenderShowDetail(ShowState show, List<ShowEventState> ordered)
        {
            var host = root.Q<VisualElement>("show-detail-participants");
            host.Clear();
            host.RemoveFromClassList("matchup");
            host.RemoveFromClassList("multi-side");
            host.RemoveFromClassList("duel");
            host.RemoveFromClassList("team-match");
            host.RemoveFromClassList("promo-cast");
            var showEvent = ordered.FirstOrDefault(x => x.Id == selectedShowEventId);
            if (showEvent == null) { ClearShowDetail(); return; }
            var editable = IsShowEditable(show);
            SetShowDetailActionsEnabled(editable);
            var isPromo = showEvent.EventType == ShowEventType.Promo;
            var kind = root.Q<Label>("show-detail-kind"); kind.text = isPromo ? "프로모" : "경기"; kind.EnableInClassList("promo", isPromo);
            Set("show-detail-title", ShowEventTitle(showEvent));
            Set("show-detail-subtitle", ShowEventDescription(showEvent));
            Set("show-detail-rules-summary", ShowEventDescription(showEvent));
            Set("show-detail-spots-summary", ShowSpotSummary(showEvent));
            if (isPromo) RenderPromoCast(host, showEvent, editable);
            else RenderMatchup(host, showEvent, editable);
            if (host.childCount == 0) { var empty = new Label("배정된 선수가 없습니다"); empty.AddToClassList("show-panel-caption"); host.Add(empty); }
            var index = ordered.IndexOf(showEvent);
            var position = show.MainEventId == showEvent.Id ? "메인" : show.OpeningEventId == showEvent.Id ? "오프닝" : $"{index + 1}번째 세그먼트";
            Set("show-detail-duration", $"{showEvent.PlannedDuration}분");
            root.Q<Button>("show-duration-increase").SetEnabled(editable);
            root.Q<Button>("show-duration-decrease").SetEnabled(editable && showEvent.PlannedDuration > 5);
            Set("show-detail-position", position);
        }

        private void ChangeSelectedEventDuration(int change)
        {
            var show = NextPlanningShow();
            var showEvent = save?.ShowEvents?.FirstOrDefault(x => x?.Id == selectedShowEventId && x.ShowId == show?.Id);
            if (showEvent == null) return;
            var previousDuration = showEvent.PlannedDuration;
            var previousStatus = show.Status;
            try
            {
                if (!IsShowEditable(show)) throw new InvalidOperationException("편집 가능한 쇼가 아닙니다.");
                var duration = checked(previousDuration + change);
                if (duration < 5) return;
                showPlanningService.SetEventDuration(save, show.Id, showEvent.Id, duration);
                DashboardSession.PersistActive();
                RenderShowPlanning();
            }
            catch (Exception exception)
            {
                showEvent.PlannedDuration = previousDuration;
                show.Status = previousStatus;
                RenderShowPlanning();
                Set("show-planning-notice", exception.Message);
            }
        }

        private void RenderMatchup(VisualElement host, ShowEventState showEvent, bool editable)
        {
            var match = save.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
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
                    var portraitCard = AddParticipantPortrait(host, sides[sideIndex][memberIndex], slotIndex, editable);
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

        private void RenderPromoCast(VisualElement host, ShowEventState showEvent, bool editable)
        {
            host.AddToClassList("promo-cast");
            TrapezoidPortraitCard firstCard = null;
            TrapezoidPortraitCard lastCard = null;
            foreach (var wrestlerId in ShowEventParticipantIds(showEvent).Distinct(StringComparer.Ordinal))
            {
                var card = AddParticipantPortrait(host, wrestlerId, -1, editable);
                firstCard ??= card;
                lastCard = card ?? lastCard;
            }
            if (firstCard != null) firstCard.IsFirst = true;
            if (lastCard != null) lastCard.IsLast = true;
        }

        private TrapezoidPortraitCard AddParticipantPortrait(VisualElement host, string wrestlerId, int slotIndex = -1, bool editParticipantsOnCardClick = false)
        {
            var wrestler = save.Wrestlers?.FirstOrDefault(x => x?.Id == wrestlerId);
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
                        pendingParticipantId = null;
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
            var kind = root.Q<Label>("show-detail-kind"); if (kind != null) { kind.text = "—"; kind.EnableInClassList("promo", false); }
            Set("show-detail-title", "타임라인에서 항목을 선택하세요"); Set("show-detail-subtitle", "경기 또는 프로모의 구성 정보를 확인할 수 있습니다");
            Set("show-detail-rules-summary", "항목을 선택하세요");
            Set("show-detail-spots-summary", "설정된 스팟 없음");
            Set("show-detail-duration", "—");
            root.Q<Button>("show-duration-increase").SetEnabled(false);
            root.Q<Button>("show-duration-decrease").SetEnabled(false);
            Set("show-detail-position", "선택한 세그먼트");
            root.Q<VisualElement>("show-detail-participants")?.Clear();
            SetShowDetailActionsEnabled(false);
        }

        private void SetShowDetailActionsEnabled(bool enabled)
        {
            foreach (var id in new[] { "show-edit-rules", "show-edit-participants", "show-edit-spots", "show-move-up", "show-move-down", "show-remove-event" })
                root.Q<VisualElement>(id)?.SetEnabled(enabled);
        }

        private string ShowSpotSummary(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Match)
            {
                var plan = save.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
                var side = plan?.Sides?.FirstOrDefault(x => x?.Id == plan.WinningSideId);
                return plan?.FinishType == MatchFinishType.Draw ? "무승부" : side == null ? "승패 미정" :
                    $"{string.Join(" & ", side.MemberIds.Select(WrestlerName))} 승리";
            }
            var promo = save.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            var entries = new List<string>();
            if (!string.IsNullOrWhiteSpace(promo?.OpeningSpot)) entries.Add("오프닝");
            if (!string.IsNullOrWhiteSpace(promo?.MiddleSpot)) entries.Add("중반");
            if (!string.IsNullOrWhiteSpace(promo?.ClosingSpot)) entries.Add("마무리");
            return entries.Count == 0 ? "설정된 스팟 없음" : $"{string.Join(" · ", entries)} 스팟 설정됨";
        }

        private string ShowEventTitle(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Promo)
            {
                var promo = save.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
                return promo == null ? "프로모 정보 없음" : promo.ParticipantIds == null || promo.ParticipantIds.Count == 0 ? "내용 미정 프로모" : PromoPurposeText(promo.Purpose);
            }
            var match = save.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            if (match == null) return "경기 정보 없음";
            var sides = (match.Sides ?? new List<MatchSideState>()).Where(x => x != null && x.MemberIds != null && x.MemberIds.Any(id => !string.IsNullOrWhiteSpace(id)))
                .Select(x => string.Join(" & ", x.MemberIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(WrestlerName))).ToList();
            if (sides.Count > 1) return string.Join("  vs  ", sides);
            var names = (match.ParticipantIds ?? new List<string>()).Select(WrestlerName).ToList();
            return names.Count > 0 ? string.Join("  vs  ", names) : "미정";
        }

        private IEnumerable<string> ShowEventParticipantIds(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Promo)
                return save.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId)?.ParticipantIds ?? new List<string>();
            var match = save.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            if (match == null) return Enumerable.Empty<string>();
            var sideIds = (match.Sides ?? new List<MatchSideState>()).Where(x => x?.MemberIds != null).SelectMany(x => x.MemberIds).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return sideIds.Count > 0 ? sideIds : match.ParticipantIds ?? new List<string>();
        }

        private string ShowEventDescription(ShowEventState showEvent)
        {
            if (showEvent.EventType == ShowEventType.Match)
            {
                var match = save.MatchPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
                if (match == null) return "경기 세부 데이터가 없습니다";
                var rule = MatchRuleOptions().FirstOrDefault(x => x.MatchTypeId == match.MatchTypeId && x.TeamCount == match.TeamCount && x.MembersPerTeam == match.MembersPerTeam);
                var ruleLabel = rule?.Label ?? MatchTypeText(match.MatchTypeId);
                var gimmick = staticContent?.MatchGimmicks.Values.FirstOrDefault(x => x.Id == match.MatchGimmickId);
                return $"{ruleLabel} · {MatchGimmickText(gimmick)}";
            }
            var promo = save.PromoPlans?.FirstOrDefault(x => x?.Id == showEvent.DetailId);
            return promo == null ? "프로모 세부 데이터가 없습니다" : $"{PromoPresentationText(promo.Presentation)} · {PromoPurposeText(promo.Purpose)}";
        }

        private string WrestlerName(string id) => WrestlerNameText.DisplayName(save?.Wrestlers?.FirstOrDefault(x => x?.Id == id));
        private static string MatchTypeText(string id) => id switch { "matchtype_001" => "개인전", "matchtype_002" => "태그팀 경기", _ => "경기" };
        private static string MatchGimmickText(MatchGimmickDefinition gimmick) => gimmick?.Id switch { "gimmick_000" => "기본 경기", "gimmick_001" => "스틸 케이지", "gimmick_002" => "래더 매치", "gimmick_003" => "테이블 매치", "gimmick_004" => "하드코어", _ => gimmick?.DisplayName ?? "기믹 미정" };
        private static string MatchFinishText(MatchFinishType value) => value switch { MatchFinishType.Submission => "서브미션", MatchFinishType.RollUp => "롤업", MatchFinishType.Disqualification => "반칙", MatchFinishType.CountOut => "카운트아웃", MatchFinishType.Draw => "무승부", MatchFinishType.Escape => "탈출", MatchFinishType.ObjectRetrieval => "목표물 획득", MatchFinishType.TableBreak => "테이블 파괴", _ => "핀폴" };
        private static string PromoPurposeText(PromoPurpose value) => value switch { PromoPurpose.CharacterIntroduction => "캐릭터 소개", PromoPurpose.ChampionStatement => "챔피언 선언", PromoPurpose.Rivalry => "라이벌리 전개", PromoPurpose.AlignmentChange => "성향 전환", PromoPurpose.TeamFormation => "팀 결성", PromoPurpose.TeamBreakup => "팀 해체", PromoPurpose.Challenge => "도전 선언", PromoPurpose.MatchBuild => "경기 빌드업", PromoPurpose.SponsorAdvertisement => "스폰서 광고", _ => "프로모" };
        private static string PromoPresentationText(PromoPresentation value) => value switch { PromoPresentation.InRingMic => "링 위 마이크", PromoPresentation.Interview => "인터뷰", PromoPresentation.BackstageConversation => "백스테이지 대화", PromoPresentation.InterruptionAttack => "난입 공격", PromoPresentation.RescueBetrayal => "구출·배신", PromoPresentation.VideoPackage => "비디오 패키지", _ => "프로모" };


        private void BindClick(string name, Action action)
        {
            var button = root.Q<Button>(name);
            if (button != null) button.clicked += action;
        }
        private void Set(string id, string value) => WrestlerNameText.Set(root.Q<Label>(id), value, save?.Wrestlers);
        private void Width(string id, float value) { var element = root.Q<VisualElement>(id); if (element != null) element.style.width = Length.Percent(value); }
        private Func<WrestlerState, IComparable> SortSelector(string key) => key switch
        {
            "gender" => x => x.Identity.Gender, "age" => x => Age(x), "height" => x => x.Identity.HeightCm, "weight" => x => x.Identity.WeightKg,
            "style" => x => StyleText(x.Presentation?.WrestlingStyleId), "match" => x => WrestlerOverallCalculator.Match(x),
            "promo" => x => WrestlerOverallCalculator.Promo(x), "status" => x => x.Status?.StatusValue ?? 0, "momentum" => x => x.Momentum?.Momentum ?? 0,
            "condition" => x => x.Condition?.Condition ?? 100f, "satisfaction" => x => x.Condition?.Satisfaction ?? 0,
            "salary" => x => Salary(x), _ => x => WrestlerNameText.DisplayName(x)
        };
        private long Salary(WrestlerState wrestler) => save?.Contracts?.FirstOrDefault(x => x != null && x.PersonId == wrestler.Id && (x.Status == ContractStatus.Active || x.Status == ContractStatus.Expiring))?.MonthlySalary ?? 0;
        private int Age(WrestlerState wrestler) => wrestler.Identity.BirthDate.AgeOn(save?.CurrentDate ?? default);
        private static string StyleText(string id) => id switch { "style_001" => "브롤러", "style_002" => "파워하우스", "style_003" => "테크니션", "style_004" => "하이플라이어", "style_005" => "루차 리브레", "style_006" => "자이언트", _ => "올라운더" };
        private static string GradeClass(string grade) => grade switch { "SS" => "grade-ss", "S+" => "grade-sp", "S" => "grade-s", "A+" => "grade-ap", "A" => "grade-a", "B+" => "grade-bp", "B" => "grade-b", "C+" => "grade-cp", "C" => "grade-c", "D+" => "grade-dp", "D" => "grade-d", "E+" => "grade-ep", "E" => "grade-e", "F+" => "grade-fp", "F" => "grade-f", "G+" => "grade-gp", _ => "grade-g" };
        private static string GenderText(WrestlerGender value) => value == WrestlerGender.Female ? "여성" : "남성";
        private static string MeterClass(float value, MeterKind kind) => kind switch { MeterKind.Momentum => value < 20 ? "meter-low" : value < 40 ? "meter-blue" : value < 50 ? "meter-cyan" : value < 80 ? "meter-orange" : "meter-gold", _ => value < 40 ? "meter-red" : value < 60 ? "meter-orange" : value < 80 ? "meter-yellow" : "meter-green" };
        private enum MeterKind { Momentum, Condition, Satisfaction }
        private enum ShowEditorMode { Match, Promo }
        private enum ShowEditSection { Full, Rules, Participants, Producing }
        private sealed class MatchRuleOption
        {
            public readonly string MatchTypeId;
            public readonly int TeamCount;
            public readonly int MembersPerTeam;
            public readonly string Label;
            public int ParticipantCount => TeamCount * MembersPerTeam;
            public string Key => $"{MatchTypeId}:{TeamCount}:{MembersPerTeam}";
            public MatchRuleOption(string matchTypeId, int teamCount, int membersPerTeam, string label)
            { MatchTypeId = matchTypeId; TeamCount = teamCount; MembersPerTeam = membersPerTeam; Label = label; }
        }
    }
}
