using System;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class DashboardTrainingController
    {
        private static readonly string[] Tabs = { "plans", "history", "resources" };
        private readonly VisualElement root, panel;
        private GameSave save;
        private string selectedId, selectedTab = "plans";

        public DashboardTrainingController(VisualElement root)
        {
            this.root = root; panel = root.Q("training-panel");
            foreach (var tab in Tabs)
            {
                var key = tab;
                var button = root.Q<Button>("training-tab-" + key);
                if (button != null) button.clicked += () => { selectedTab = key; ShowTab(); };
            }
            root.Q("training-content")?.RegisterCallback<GeometryChangedEvent>(evt =>
                root.Q("training-content").EnableInClassList("ops-narrow", evt.newRect.width < 900));
        }

        public void Render(GameSave value)
        {
            save = value; if (panel == null) return;
            SetStatus("");
            var initialized = save?.Training?.IsInitialized == true;
            var state = save?.Training;
            var roster = initialized ? save.Wrestlers.Where(w => TrainingService.Eligible(save, w)).ToList() : new();
            if (!roster.Any(w => w.Id == selectedId)) selectedId = roster.FirstOrDefault()?.Id;
            var facility = TrainingService.Facility(initialized ? state.FacilityLevel : 1);
            var assigned = initialized ? state.Plans.Count(p => p.Mode != TrainingMode.Rest && roster.Any(w => w.Id == p.WrestlerId && w.Condition.InjuryStatus != InjuryStatus.Severe)) : 0;
            Set("training-assignment-count", initialized ? $"훈련 배정 {assigned}/{facility.Capacity}명" : "훈련 데이터 없음");
            Set("training-efficiency", initialized ? $"훈련 효율 {TrainingService.Trainer(state.TrainerLevel).Efficiency * Math.Min(1, (double)facility.Capacity / Math.Max(1, assigned)):P0}" : "—");
            Set("training-next-date", initialized ? $"다음 정산 {DashboardText.Date(Earlier(state.WeekStartDate.AddDays(7), PromotionQuarterService.EndOfQuarter(state.LastProcessedDate.AddDays(1))))}" : "—");
            var list = root.Q<ScrollView>("training-roster"); list.Clear();
            foreach (var w in roster)
            {
                var row = new VisualElement { name = "training-select-" + w.Id }; row.AddToClassList("ops-table-row");
                row.EnableInClassList("ops-alt", list.childCount % 2 != 0); row.EnableInClassList("ops-selected", selectedId == w.Id);
                OperationsView.NameCell(row, w);
                var gaugeCell = new VisualElement(); gaugeCell.AddToClassList("ops-cell"); gaugeCell.AddToClassList("ops-gauge-cell"); gaugeCell.Add(OperationsView.Condition(w.Condition.Condition)); row.Add(gaugeCell);
                var plan = state.Plans.FirstOrDefault(p => p.WrestlerId == w.Id);
                OperationsView.Cell(row, plan == null ? "미설정" : Mode(plan.Mode));
                OperationsView.Selectable(row, () => { selectedId = w.Id; Render(save); }); list.Add(row);
            }
            if (roster.Count == 0) Empty(list, initialized ? "훈련 가능한 계약 선수가 없습니다." : "훈련 데이터를 불러올 수 없습니다.");
            RenderDetail(roster.FirstOrDefault(w => w.Id == selectedId));
            RenderHistory();
            var cards = root.Q("training-upgrade-grid"); cards.Clear();
            if (initialized) { AddUpgradeCard(cards, true); AddUpgradeCard(cards, false); }
            ShowTab();
        }

        private void RenderDetail(WrestlerState w)
        {
            var host = root.Q<ScrollView>("training-detail"); host.Clear();
            if (w == null) { Empty(host, "계약 선수를 영입하면 훈련 계획을 설정할 수 있습니다."); return; }
            var identity = OperationsView.Identity(host, w, Injury(w.Condition.InjuryStatus));
            var gauge = OperationsView.Condition(w.Condition.Condition); gauge.name = "training-condition"; identity.Add(gauge);
            var columns = new VisualElement(); columns.AddToClassList("ops-detail-columns"); host.Add(columns);
            var form = new VisualElement(); form.AddToClassList("ops-detail-section"); form.AddToClassList("ops-form"); columns.Add(form);
            OperationsView.Heading(form, "훈련 설정");
            var current = save.Training.Plans.FirstOrDefault(x => x.WrestlerId == w.Id);
            var modes = new DropdownField("훈련 방식", new() { "미설정", "일반", "집중", "휴식" }, current == null ? 0 : (int)current.Mode + 1) { name = "training-mode-" + w.Id };
            var focus = new DropdownField("집중 능력", OperationsView.AbilityNames.ToList(), current == null ? 0 : (int)current.Focus) { name = "training-focus-" + w.Id };
            form.Add(modes); form.Add(focus);
            var apply = new Button(() => Mutate(() =>
            {
                if (modes.index == 0) save.Training.Plans.RemoveAll(x => x.WrestlerId == w.Id);
                else new TrainingService().SetPlan(save, w.Id, (TrainingMode)(modes.index - 1), (WrestlerAttribute)focus.index);
            })) { name = "training-apply-" + w.Id, text = "계획 적용" };
            apply.AddToClassList("ops-action"); apply.AddToClassList("ops-primary");
            var actions = new VisualElement(); actions.AddToClassList("ops-actions"); actions.Add(apply); form.Add(actions);
            void Refresh()
            {
                modes.SetEnabled(!DashboardSession.IsShowRunning);
                focus.SetEnabled(modes.index == 2 && !DashboardSession.IsShowRunning && w.Condition.InjuryStatus != InjuryStatus.Severe);
                apply.SetEnabled(!DashboardSession.IsShowRunning && (w.Condition.InjuryStatus != InjuryStatus.Severe || modes.index is 0 or 3));
            }
            modes.RegisterValueChangedCallback(_ => Refresh()); Refresh();
            if (w.Condition.InjuryStatus == InjuryStatus.Severe) Notice(form, "중상 기간에는 휴식만 가능합니다.");
            else if (w.Condition.Condition < 40) Notice(form, "체력이 낮아 집중 훈련 시 부상 위험이 높습니다.");
            var report = new VisualElement(); report.AddToClassList("ops-detail-section"); columns.Add(report);
            OperationsView.Heading(report, "최근 훈련 결과");
            var latest = save.Training.History.LastOrDefault(week => week.Results.Any(r => r.WrestlerId == w.Id));
            var result = latest?.Results.First(r => r.WrestlerId == w.Id);
            if (result == null) { var empty = new Label("아직 정산된 훈련이 없습니다."); empty.AddToClassList("ops-meta"); report.Add(empty); }
            else
            {
                var period = new Label($"{DashboardText.Date(latest.StartDate)} ~ {DashboardText.Date(latest.EndDate)}"); period.AddToClassList("ops-period"); report.Add(period);
                OperationsView.AddValue(report, "훈련 / 휴식", $"{result.TrainingDays}일 / {result.RestDays}일");
                OperationsView.AddValue(report, "체력 변화", $"{result.ConditionChange:+0.0;-0.0;0}");
                OperationsView.AddValue(report, "경험", $"+{result.Experience:0.0}");
                if (result.Injured) Notice(report, "훈련 중 부상이 발생했습니다.");
            }
            var a = w.Attributes;
            var values = new[] { a.RingPsychology, a.RingImprovisation, a.Technical, a.Brawling, a.Power, a.HighFlying, a.SpotWork, a.SpecialtyMatches, a.Selling, a.Stamina,
                a.Charisma, a.MicWork, a.Improvisation, a.Acting, a.FaceWork, a.HeelWork, a.Comedy };
            OperationsView.AddAbilities(host, type => WrestlerOverallCalculator.Grade(values[(int)type]));
            OperationsView.AddValue(host, "경기 잠재력", WrestlerOverallCalculator.Grade(w.Growth.MatchPotentialCap), ability: true);
            OperationsView.AddValue(host, "프로모 잠재력", WrestlerOverallCalculator.Grade(w.Growth.PromoPotentialCap), ability: true);
        }

        private void RenderHistory()
        {
            var host = root.Q<ScrollView>("training-history-panel"); host.Clear();
            if (save?.Training?.IsInitialized != true || save.Training.History.Count == 0) { Empty(host, "아직 정산된 훈련이 없습니다."); return; }
            foreach (var week in save.Training.History.TakeLast(4).Reverse())
            {
                var block = new Foldout { text = $"{DashboardText.Date(week.StartDate)} ~ {DashboardText.Date(week.EndDate)}", value = week == save.Training.History.Last() }; block.AddToClassList("ops-record"); host.Add(block);
                foreach (var result in week.Results)
                {
                    var row = new VisualElement(); row.AddToClassList("ops-table-row"); block.Add(row);
                    var w = save.Wrestlers.FirstOrDefault(x => x.Id == result.WrestlerId);
                    if (w != null) OperationsView.NameCell(row, w);
                    OperationsView.Cell(row, $"훈련 {result.TrainingDays}일 / 휴식 {result.RestDays}일");
                    OperationsView.Cell(row, $"체력 {result.ConditionChange:+0.0;-0.0;0}" + (result.Injured ? " · 부상" : ""));
                }
            }
        }

        private void AddUpgradeCard(VisualElement cards, bool trainer)
        {
            var level = trainer ? save.Training.TrainerLevel : save.Training.FacilityLevel;
            var facility = TrainingService.Facility(save.Training.FacilityLevel);
            var effect = trainer ? $"훈련 경험 효율 {TrainingService.Trainer(level).Efficiency:P0}" : $"수용 {facility.Capacity}명 · 일일 추가 회복 +{facility.Recovery / 25} · 안전 {facility.Safety}";
            var cost = level >= 5 ? 0 : trainer ? TrainingService.Trainer(level + 1).Cost : TrainingService.Facility(level + 1).Cost;
            var required = level >= 5 ? 0 : trainer ? TrainingService.Trainer(level + 1).Required : TrainingService.Facility(level + 1).Required;
            var eligible = PromotionProgressionRules.ActivePrestige(save) >= required;
            var status = level >= 5 ? "최대 레벨" : !eligible ? $"시즌 명성 자격 {required:N0} 필요" : trainer ?
                $"다음 훈련 경험 효율 {TrainingService.Trainer(level + 1).Efficiency:P0}" : $"다음 수용 {TrainingService.Facility(level + 1).Capacity}명 · 추가 회복 +{TrainingService.Facility(level + 1).Recovery / 25}";
            var card = DashboardFacilitiesController.AddCard(cards, trainer ? "트레이너" : "훈련 시설", $"Lv.{level}", effect, status,
                level >= 5 ? "—" : $"업그레이드 비용 ${cost:N0}", level >= 5 ? "완료" : "업그레이드",
                level < 5 && eligible && !DashboardSession.IsShowRunning && save.Promotion.CalculateCurrentCash(save.Transactions) >= cost,
                () => Mutate(() =>
                {
                    if ((trainer ? save.Training.TrainerLevel : save.Training.FacilityLevel) != level) return;
                    if (trainer) new TrainingService().UpgradeTrainer(save); else new TrainingService().Upgrade(save);
                }), !eligible);
            card.name = trainer ? "training-trainer-card" : "training-facility-card";
            card.Q<Button>().name = trainer ? "training-trainer-upgrade" : "training-upgrade";
        }

        private void Mutate(Action action)
        {
            if (save == null || DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning) return;
            var before = JsonUtility.ToJson(save);
            try { action(); DashboardSession.PersistActive(); Render(save); }
            catch (Exception e) { JsonUtility.FromJsonOverwrite(before, save); Render(save); SetStatus(e.Message); }
        }
        private void ShowTab() => OperationsView.ShowTab(root, "training", selectedTab, Tabs);
        private void Set(string key, string text) { var label = root.Q<Label>(key); if (label != null) label.text = text; }
        private void SetStatus(string text) { var label = root.Q<Label>("training-status"); if (label == null) return; label.text = text; label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex; }
        private static void Empty(VisualElement host, string text) { var label = new Label(text); label.AddToClassList("ops-empty"); host.Add(label); }
        private static void Notice(VisualElement host, string text) { var label = new Label(text); label.AddToClassList("ops-notice"); host.Add(label); }
        private static GameDate Earlier(GameDate a, GameDate b) => a.CompareTo(b) < 0 ? a : b;
        private static string Injury(InjuryStatus injury) => injury switch { InjuryStatus.None => "부상 없음", InjuryStatus.Minor => "경상", InjuryStatus.Moderate => "부상", _ => "중상" };
        private static string Mode(TrainingMode mode) => mode switch { TrainingMode.Normal => "일반", TrainingMode.Focused => "집중", _ => "휴식" };
    }
}
