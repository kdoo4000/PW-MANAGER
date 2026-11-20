using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class DashboardScoutingController
    {
        private static readonly string[] Tabs = { "reports", "assignment" };
        private readonly VisualElement root;
        private readonly DropdownField gender, background;
        private readonly Button start;
        private GameSave save;
        private string selectedId, selectedTab = "reports";
        private int previousCount = -1;

        public DashboardScoutingController(VisualElement root)
        {
            this.root = root;
            gender = root.Q<DropdownField>("scouting-gender"); background = root.Q<DropdownField>("scouting-background"); start = root.Q<Button>("scouting-start");
            if (gender == null || background == null || start == null) return;
            gender.choices = new() { "전체", "남성", "여성" }; gender.index = 0;
            background.choices = new() { "전체", "신인", "운동선수 출신", "엔터테이너 출신", "타 단체 출신" }; background.index = 0;
            start.clicked += Start;
            foreach (var tab in Tabs)
            {
                var key = tab; root.Q<Button>("scouting-tab-" + key).clicked += () => { selectedTab = key; ShowTab(); };
            }
            root.Q("scouting-content").RegisterCallback<GeometryChangedEvent>(evt =>
                root.Q("scouting-content").EnableInClassList("ops-narrow", evt.newRect.width < 900));
        }

        public void Render(GameSave value)
        {
            save = value; if (start == null) return;
            var level = save?.StaffDepartments.FirstOrDefault(x => x.DepartmentType == StaffDepartmentType.Scout)?.CurrentLevel ?? 0;
            Set("scouting-team", $"스카우트팀 Lv.{level}");
            var assignment = save?.ScoutAssignments.LastOrDefault(x => x.Status == ScoutAssignmentStatus.InProgress);
            root.Q("scouting-filters").SetEnabled(save?.Promotion != null && level > 0 && assignment == null && !DashboardSession.IsShowRunning);
            Set("scouting-status", assignment != null ? $"탐색 중 · {DashboardText.Date(assignment.CompleteDate)} 보고 예정 · {Math.Max(0, save.CurrentDate.DaysUntil(assignment.CompleteDate))}일 남음" :
                level == 0 ? "스카우트팀을 해금하면 탐색을 배정할 수 있습니다." : $"탐색 기간 {ScoutAssignmentState.DurationDays}일 · 한 번에 한 건을 배정할 수 있습니다.");
            Set("scouting-progress", assignment != null ? $"탐색 중 · {Math.Max(0, save.CurrentDate.DaysUntil(assignment.CompleteDate))}일 남음" : "진행 중인 탐색 없음");
            var results = root.Q<ScrollView>("scouting-results"); results.Clear();
            var candidates = (save?.ScoutCandidates ?? new()).Where(c => save.Wrestlers.Any(w => w.Id == c.WrestlerId &&
                string.IsNullOrEmpty(w.PromotionId) && w.Roster.ActivityState == RosterActivityState.Inactive && !c.IsExpired(save.CurrentDate))).Reverse().ToList();
            if (previousCount < 0 && candidates.Count == 0) selectedTab = "assignment";
            else if (previousCount == 0 && candidates.Count > 0) selectedTab = "reports";
            previousCount = candidates.Count;
            if (!candidates.Any(c => c.WrestlerId == selectedId)) selectedId = candidates.FirstOrDefault()?.WrestlerId;
            Set("scouting-candidate-count", $"후보 {candidates.Count}명");
            foreach (var candidate in candidates)
            {
                var w = save.Wrestlers.Single(w => w.Id == candidate.WrestlerId);
                var row = new VisualElement { name = "scout-candidate-" + w.Id }; row.AddToClassList("ops-table-row");
                row.EnableInClassList("ops-alt", results.childCount % 2 != 0); row.EnableInClassList("ops-selected", selectedId == w.Id);
                OperationsView.NameCell(row, w); OperationsView.Cell(row, Age(w).ToString(), "ops-number-cell");
                var matchGrade = Estimate(candidate, ScoutValueType.MatchOverall); var promoGrade = Estimate(candidate, ScoutValueType.PromoOverall);
                WrestlerProfileController.SetScoutGrade(OperationsView.Cell(row, matchGrade), matchGrade); WrestlerProfileController.SetScoutGrade(OperationsView.Cell(row, promoGrade), promoGrade);
                OperationsView.Selectable(row, () => { selectedId = w.Id; Render(save); }); results.Add(row);
            }
            if (candidates.Count == 0) Empty(results, assignment == null ? "탐색을 배정해 영입 후보를 찾아보세요." : "탐색이 끝나면 후보 보고서가 도착합니다.");
            RenderDetail(candidates.FirstOrDefault(c => c.WrestlerId == selectedId)); ShowTab();
        }

        private void Start()
        {
            if (selectedTab != "assignment" || save?.Promotion == null || DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning || !start.enabledInHierarchy) return;
            var before = JsonUtility.ToJson(save);
            try
            {
                CreateService(save).Start(save, new ScoutSearchConditions { HasGender = gender.index > 0, Gender = gender.index == 2 ? WrestlerGender.Female : WrestlerGender.Male,
                    HasBackground = background.index > 0, Background = (WrestlerBackground)Math.Max(0, background.index - 1) });
                DashboardSession.PersistActive(); Render(save);
            }
            catch (Exception exception)
            {
                JsonUtility.FromJsonOverwrite(before, save); Render(save); Set("scouting-status", $"탐색 배정 실패: {exception.Message}");
            }
        }

        private void RenderDetail(ScoutCandidateState report)
        {
            var host = root.Q<ScrollView>("scouting-detail"); host.Clear();
            if (report == null)
            {
                Empty(host, "후보 보고서가 도착하면 능력과 계약 조건을 확인할 수 있습니다.");
                var assign = new Button(() => { selectedTab = "assignment"; ShowTab(); }) { text = "탐색 배정" }; assign.AddToClassList("ops-action"); host.Add(assign); return;
            }
            var w = save.Wrestlers.Single(w => w.Id == report.WrestlerId);
            OperationsView.Identity(host, w, $"{(w.Identity.Gender == WrestlerGender.Male ? "남성" : "여성")} · {Age(w)}세 · {w.Identity.HeightCm}cm / {w.Identity.WeightKg}kg");
            var columns = new VisualElement(); columns.AddToClassList("ops-detail-columns"); host.Add(columns);
            var overall = new VisualElement(); overall.AddToClassList("ops-detail-section"); columns.Add(overall); OperationsView.Heading(overall, "종합 평가");
            OperationsView.AddValue(overall, "경기", Estimate(report, ScoutValueType.MatchOverall), ability: true); OperationsView.AddValue(overall, "프로모", Estimate(report, ScoutValueType.PromoOverall), ability: true);
            var known = report.ValueEstimates.Count(x => (int)x.Type < 17 && x.HasEvaluation && x.IsFullyRevealed);
            OperationsView.AddValue(overall, "세부 등급 공개", $"{known} / 17개");
            var growth = new VisualElement(); growth.AddToClassList("ops-detail-section"); columns.Add(growth); OperationsView.Heading(growth, "성장 평가");
            OperationsView.AddValue(growth, "경기 잠재력", Estimate(report, ScoutValueType.MatchPotential), ability: true); OperationsView.AddValue(growth, "프로모 잠재력", Estimate(report, ScoutValueType.PromoPotential), ability: true);
            OperationsView.Heading(host, "영입 조건");
            try
            {
                var demand = ContractNegotiationService.Demand(save, w.Id, ContractType.Wrestler);
                OperationsView.AddValue(host, "요구 계약금", DashboardText.Money(demand.SigningBonus)); OperationsView.AddValue(host, "요구 월 기본급", DashboardText.Money(demand.MonthlySalary));
                OperationsView.AddValue(host, "경기 수당", DashboardText.Money(demand.MatchAppearanceFee)); OperationsView.AddValue(host, "프로모 수당", DashboardText.Money(demand.PromoAppearanceFee));
            }
            catch (InvalidOperationException e) { var note = new Label(e.Message); note.AddToClassList("ops-notice"); host.Add(note); }
            if (report.ExpiryDate.HasValue) OperationsView.AddValue(host, "보고서 유효 기한", DashboardText.Date(report.ExpiryDate.Value));
            var actions = new VisualElement(); actions.AddToClassList("ops-actions"); host.Add(actions);
            var negotiate = new Button(() => ContractNegotiationView.Open(root, w.Id, ContractType.Wrestler, () => Render(DashboardSession.ActiveSave))) { text = "계약 협상", name = "scout-negotiate-" + w.Id };
            negotiate.AddToClassList("ops-action"); negotiate.AddToClassList("ops-primary"); negotiate.SetEnabled(!DashboardSession.IsShowRunning); actions.Add(negotiate);
            var abilities = new VisualElement(); abilities.style.marginTop = 18; host.Add(abilities);
            OperationsView.AddAbilities(abilities, type => Estimate(report, type));
        }
        private int Age(WrestlerState w)
        {
            var age = save.CurrentDate.Year - w.Identity.BirthDate.Year;
            if (new GameDate(save.CurrentDate.Year, w.Identity.BirthDate.Month, Math.Min(w.Identity.BirthDate.Day, DateTime.DaysInMonth(save.CurrentDate.Year, w.Identity.BirthDate.Month))).CompareTo(save.CurrentDate) > 0) age--;
            return age;
        }
        private void ShowTab() => OperationsView.ShowTab(root, "scouting", selectedTab, Tabs);
        private static void Empty(VisualElement parent, string text) { var label = new Label(text); label.AddToClassList("ops-empty"); parent.Add(label); }

        internal static string Estimate(ScoutCandidateState report, ScoutValueType type)
        {
            var estimate = report?.ValueEstimates?.FirstOrDefault(x => x.Type == type);
            return ScoutAssessmentRules.Display(estimate);
        }


        internal static ScoutingService CreateService(GameSave save)
        {
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var seed = unchecked(save.WorldSeed * 397 ^ save.CurrentDate.Year * 10000 ^ save.CurrentDate.Month * 100 ^ save.CurrentDate.Day ^ save.ScoutAssignments.Count);
            return new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), seed), catalog.ScoutTeamLevels, random: new System.Random(seed));
        }

        internal static void ProcessDay(GameSave save)
        {
            if (!save.ScoutAssignments.Any(x => x.Status == ScoutAssignmentStatus.InProgress && x.CompleteDate.CompareTo(save.CurrentDate) <= 0)) return;
            CreateService(save).ProcessDay(save);
        }

        private void Set(string name, string text) { var label = root.Q<Label>(name); if (label != null) label.text = text; }
    }
}
