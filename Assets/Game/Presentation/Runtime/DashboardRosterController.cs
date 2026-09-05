using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class DashboardRosterController
    {
        private readonly VisualElement root;
        private readonly List<SortCriterion> rosterSorts = new();
        private string rosterSearch = string.Empty;
        private RosterMode rosterMode = RosterMode.All;
        private GameSave save;

        public DashboardRosterController(VisualElement root) { this.root = root; SetupControls(); }
        public void Render(GameSave value) { save = value; RenderRoster(); }

        private void SetupControls()
        {
            var search = root.Q<TextField>("roster-search");
            if (search != null)
            {
                search.value = string.Empty;
                search.RegisterValueChangedCallback(evt => { rosterSearch = evt.newValue ?? string.Empty; RenderRoster(); });
            }
            foreach (var key in new[] { "name", "gender", "age", "height", "weight", "role", "style", "match", "promo", "status", "momentum", "condition", "satisfaction", "team", "salary" })
            {
                var captured = key;
                var button = root.Q<Button>($"sort-{key}");
                if (button != null) button.clicked += () => ChangeRosterSort(captured);
            }
            BindRosterTab("roster-tab-all", RosterMode.All);
            BindRosterTab("roster-tab-wrestlers", RosterMode.Wrestlers);
            BindRosterTab("roster-tab-managers", RosterMode.Managers);
        }

        private void BindRosterTab(string name, RosterMode mode) { var button = root.Q<Button>(name); if (button != null) button.clicked += () => { rosterMode = mode; RefreshRosterTabs(); RenderRoster(); }; }
        private void RefreshRosterTabs() { root.Q<Button>("roster-tab-all")?.EnableInClassList("roster-tab-active", rosterMode == RosterMode.All); root.Q<Button>("roster-tab-wrestlers")?.EnableInClassList("roster-tab-active", rosterMode == RosterMode.Wrestlers); root.Q<Button>("roster-tab-managers")?.EnableInClassList("roster-tab-active", rosterMode == RosterMode.Managers); }

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
                var button = root.Q<Button>($"sort-{key}");
                if (button == null) continue;
                var criterion = rosterSorts.FirstOrDefault(x => x.Key == key);
                button.EnableInClassList("sorted", criterion.Key != null);
                button.text = HeaderText(key) + (criterion.Key == null ? string.Empty : criterion.Ascending ? "  ↑" : "  ↓");
            }
        }

        private void RenderRoster()
        {
            if (root == null) return;
            var rows = root.Q<VisualElement>("roster-rows");
            if (rows == null) return;
            rows.Clear();
            var all = save?.Wrestlers?.Where(x => x?.Identity != null).ToList() ?? new List<WrestlerState>();
            var managers = save?.Managers?.Count(x => x != null && x.ActivityState != RosterActivityState.Released) ?? 0;
            Set("roster-summary", $"총 {all.Count + managers}명 · 선수 {all.Count} · 전문 매니저 {managers}");
            if (rosterMode == RosterMode.Managers)
            {
                var empty = new Label(managers == 0 ? "등록된 전문 매니저가 없습니다" : $"전문 매니저 {managers}명 · 상세 명단 데이터 준비 중"); empty.AddToClassList("roster-empty"); rows.Add(empty); return;
            }
            IEnumerable<WrestlerState> filtered = all;
            if (!string.IsNullOrWhiteSpace(rosterSearch))
            {
                var query = rosterSearch.Trim();
                filtered = filtered.Where(x => WrestlerNameText.DisplayName(x).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (x.Identity.LegalName ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var result = SortRoster(filtered).ToList();
            if (result.Count == 0) { var empty = new Label("표시할 선수가 없습니다"); empty.AddToClassList("roster-empty"); rows.Add(empty); return; }
            var contracts = save?.Contracts ?? new List<ContractState>();
            var teams = save?.TagTeams ?? new List<TagTeamState>();
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
                AddCell(row, contract == null ? "—" : DashboardText.Money(contract.MonthlySalary), "roster-col-salary");
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
            return (ordered ?? source.OrderBy(x => WrestlerNameText.DisplayName(x))).ThenBy(x => WrestlerNameText.DisplayName(x));
        }

        private Func<WrestlerState, IComparable> SortSelector(string key) => key switch
        {
            "gender" => x => x.Identity.Gender, "age" => x => Age(x), "height" => x => x.Identity.HeightCm, "weight" => x => x.Identity.WeightKg,
            "role" => x => AlignmentText(x.Roster.Alignment), "style" => x => StyleText(x.Presentation?.WrestlingStyleId), "match" => x => WrestlerOverallCalculator.Match(x),
            "promo" => x => WrestlerOverallCalculator.Promo(x), "status" => x => x.Status?.StatusValue ?? 0, "momentum" => x => x.Momentum?.Momentum ?? 0,
            "condition" => x => x.Condition?.Condition ?? 100f, "satisfaction" => x => x.Condition?.Satisfaction ?? 0,
            "team" => x => x.Roster?.ActiveTagTeamId ?? x.Roster?.ActiveStableId ?? string.Empty, "salary" => x => Salary(x), _ => x => WrestlerNameText.DisplayName(x)
        };

        private long Salary(WrestlerState wrestler) => save?.Contracts?.FirstOrDefault(x => x != null && x.PersonId == wrestler.Id && (x.Status == ContractStatus.Active || x.Status == ContractStatus.Expiring))?.MonthlySalary ?? 0;
        private int Age(WrestlerState wrestler) { var now = save?.CurrentDate ?? default; var birth = wrestler.Identity.BirthDate; return Math.Max(0, now.Year - birth.Year - ((now.Month < birth.Month || now.Month == birth.Month && now.Day < birth.Day) ? 1 : 0)); }
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


        private void Set(string name, string value) { var label = root.Q<Label>(name); if (label != null) label.text = value; }
        private enum RosterMode { All, Wrestlers, Managers }
        private enum MeterKind { Momentum, Condition, Satisfaction }
        private readonly struct SortCriterion { public readonly string Key; public readonly bool Ascending; public SortCriterion(string key, bool ascending) { Key = key; Ascending = ascending; } }
    }
}
