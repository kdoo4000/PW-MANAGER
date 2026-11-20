using System;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class PromotionQuarterView
    {
        public static void Bind(VisualElement root, GameSave save)
        {
            var button = root.Q<Button>("prestige-open-quarter"); if (button == null) return;
            if (button.userData is Action previous) button.clicked -= previous;
            Action open = () => Open(root, save); button.clicked += open; button.userData = open;
            var state = save.Promotion.QuarterlyGrowth; button.SetEnabled(state?.IsInitialized == true);
            var preview = PromotionQuarterService.Preview(save); var last = state?.History.LastOrDefault();
            var estimate = root.Q<Label>("prestige-quarter-estimate");
            if (estimate != null) estimate.text = preview != null ? $"이번 분기 예상 +{preview.TotalGain:N0}" : last != null ? $"최근 분기 +{last.TotalGain:N0}" : "분기 결산 기록 없음";
            var next = root.Q<Label>("prestige-next-unlock"); var threshold = PromotionProgressionRules.NextThreshold(save);
            if (next != null) next.text = threshold == 0 ? "모든 단계 해금" : save.Promotion.PromotionPrestige >= threshold ? "다음 시즌 해금 예정" : $"다음 해금까지 {threshold - save.Promotion.PromotionPrestige:N0}";
        }

        public static VisualElement Open(VisualElement host, GameSave save)
        {
            host.Q("quarter-prestige-overlay")?.RemoveFromHierarchy(); var previousFocus = host.focusController?.focusedElement;
            var overlay = new VisualElement { name = "quarter-prestige-overlay" }; overlay.AddToClassList("contract-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ContractNegotiation")); host.Add(overlay);
            var dialog = new VisualElement(); dialog.AddToClassList("contract-dialog"); overlay.Add(dialog);
            var heading = new Label("분기 결산"); heading.AddToClassList("contract-heading"); dialog.Add(heading);
            var body = new ScrollView(); body.AddToClassList("contract-body"); dialog.Add(body);
            var preview = PromotionQuarterService.Preview(save);
            if (preview != null)
            {
                var row = new Foldout { text = $"진행 중 · 예상 명성 +{preview.TotalGain:N0}", value = true }; body.Add(row); Describe(row, preview);
            }
            var history = save.Promotion.QuarterlyGrowth?.History;
            foreach (var record in (history ?? new()).AsEnumerable().Reverse())
            {
                var row = new Foldout { text = $"{Date(record.StartDate)} ~ {Date(record.EndDate)} · 명성 +{record.TotalGain:N0}", value = preview == null && record == history.Last() };
                body.Add(row); Describe(row, record); row.Add(new Label($"단체 명성 {record.PrestigeBefore:N0} → {record.PrestigeAfter:N0}"));
            }
            if (history?.Count is not > 0)
            {
                var state = save.Promotion.QuarterlyGrowth;
                body.Add(new Label(state?.IsInitialized == true ? $"첫 분기 결산 {Date(PromotionQuarterService.EndOfQuarter(state.OpeningDate))}" : "분기 결산 기록 없음"));
            }
            void Close() { overlay.RemoveFromHierarchy(); previousFocus?.Focus(); }
            var actions = new VisualElement(); actions.AddToClassList("contract-actions"); dialog.Add(actions);
            var close = new Button(Close) { text = "닫기", name = "quarter-prestige-close" }; actions.Add(close);
            overlay.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            overlay.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Tab)
                {
                    var targets = dialog.Query<VisualElement>().ToList().Where(x => x.focusable && x.tabIndex >= 0 && x.enabledInHierarchy && x.resolvedStyle.display != DisplayStyle.None).ToList();
                    if (targets.Count == 0) return;
                    var index = targets.FindIndex(x => x == dialog.focusController?.focusedElement);
                    targets[(index + (e.shiftKey ? targets.Count - 1 : 1)) % targets.Count].Focus(); e.StopPropagation(); e.PreventDefault();
                }
            });
            overlay.schedule.Execute(() => close.Focus()); return overlay;
        }
        private static void Describe(VisualElement row, PromotionQuarterRecord r)
        {
            row.Add(new Label($"집계 기간 {Date(r.StartDate)} ~ {Date(r.EndDate)}"));
            row.Add(new Label($"팬 {r.OpeningFollowers:N0} → {r.ClosingFollowers:N0} · 완료 쇼 {r.CompletedShows}회 · 유료 관객 {r.TotalAttendance:N0}명"));
            row.Add(new Label($"기본 +{r.BaseScore:N0} · 팬 +{r.FanScore:N0} · 현장 흥행 +{r.AttendanceScore:N0}"));
            row.Add(new Label($"선수 반응 +{r.RosterScore:N0} · 선수 성과 +{r.MilestoneBonus:N0} · PPV +{r.PpvBonus:N0}"));
        }
        private static string Date(GameDate date) => $"{date.Year}.{date.Month:D2}.{date.Day:D2}";
    }
}
