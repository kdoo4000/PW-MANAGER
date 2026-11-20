using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class WrestlerGrowthView
    {
        private static readonly string[] AttributeNames = { "경기 운영", "경기 즉흥성", "테크니컬", "브롤링", "파워", "하이플라잉",
            "스팟워크", "특수 경기", "셀링", "스태미나", "카리스마", "마이크", "즉흥성", "연기", "페이스 연기", "힐 연기", "코미디" };

        public static VisualElement Open(VisualElement host, string wrestlerId)
        {
            var save = DashboardSession.ActiveSave;
            var w = save?.Wrestlers.FirstOrDefault(x => x.Id == wrestlerId);
            if (w == null || host == null) return null;
            host.Q("growth-history-overlay")?.RemoveFromHierarchy();
            var previousFocus = host.focusController?.focusedElement;
            var overlay = new VisualElement { name = "growth-history-overlay" }; overlay.AddToClassList("contract-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ContractNegotiation")); host.Add(overlay);
            var dialog = new VisualElement(); dialog.AddToClassList("contract-dialog"); overlay.Add(dialog);
            var heading = new Label("성장 기록"); heading.AddToClassList("contract-heading"); dialog.Add(heading);
            dialog.Add(WrestlerProfileController.CreateLink(w));
            var body = new ScrollView(); body.AddToClassList("contract-body"); dialog.Add(body);
            var stage = WrestlerGrowthService.Stage(w.Identity.BirthDate.AgeOn(save.CurrentDate));
            Line(body, "성장 단계 · " + (stage switch { AgingState.Young => "성장기", AgingState.Prime => "전성기", AgingState.Veteran => "베테랑", _ => "노화기" }));
            var pending = save.ExperienceRecords.Where(x => x.WrestlerId == w.Id && !x.SettledQuarter.HasValue)
                .OrderByDescending(x => x.Date.Year).ThenByDescending(x => x.Date.Month).ThenByDescending(x => x.Date.Day).ToList();
            var activities = new Foldout { text = $"미정산 활동 · {pending.Count}건", value = true }; body.Add(activities);
            if (pending.Count == 0) Line(activities, "정산을 기다리는 활동이 없습니다.");
            foreach (var record in pending)
            {
                var showId = record.Activity == ExperienceActivity.Match ? save.MatchResults.FirstOrDefault(x => x.Id == record.SourceResultId)?.ShowId :
                    save.PromoResults.FirstOrDefault(x => x.Id == record.SourceResultId)?.ShowId;
                var show = save.Shows.FirstOrDefault(x => x.Id == showId);
                Line(activities, $"{Date(record.Date)} · {(record.Activity switch { ExperienceActivity.Match => "경기", ExperienceActivity.Training => "훈련", _ => "프로모" })} · {show?.Name ?? "활동 기록"}");
            }
            var history = save.GrowthQuarterResults.Where(x => x.WrestlerId == w.Id)
                .OrderByDescending(x => x.EndDate.Year).ThenByDescending(x => x.EndDate.Month).ToList();
            if (history.Count == 0) Line(body, "아직 완료된 분기 성장 기록이 없습니다.");
            foreach (var record in history)
            {
                var quarter = new Foldout { text = $"{Date(record.StartDate)} ~ {Date(record.EndDate)}", value = record == history[0] };
                quarter.style.marginTop = 12; body.Add(quarter);
                if (record.Changes.Count == 0) Line(quarter, "기록된 능력 변화가 없습니다.");
                foreach (var change in record.Changes)
                    Line(quarter, $"{AttributeNames[(int)change.Attribute]} {(change.After > change.Before ? "상승" : "하락")} · {(change.Cause == AttributeChangeCause.Growth ? "분기 성장" : "연말 노화")}");
            }
            void Close() { overlay.RemoveFromHierarchy(); previousFocus?.Focus(); }
            var actions = new VisualElement(); actions.AddToClassList("contract-actions"); dialog.Add(actions);
            var close = new Button(Close) { name = "growth-history-close", text = "닫기" }; actions.Add(close);
            overlay.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Tab)
                {
                    var targets = dialog.Query<VisualElement>().ToList().Where(x => x.focusable && x.tabIndex >= 0 && x.enabledInHierarchy && IsDisplayed(x)).ToList();
                    if (targets.Count == 0) return;
                    var index = targets.FindIndex(x => x == dialog.focusController?.focusedElement);
                    targets[(index + (evt.shiftKey ? targets.Count - 1 : 1)) % targets.Count].Focus();
                    evt.StopPropagation(); evt.PreventDefault();
                }
            });
            overlay.schedule.Execute(close.Focus);
            return overlay;
        }
        private static bool IsDisplayed(VisualElement element)
        {
            for (var p = element; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }
        private static void Line(VisualElement parent, string text)
        {
            var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 6; parent.Add(label);
        }
        private static string Date(GameDate date) => $"{date.Year}.{date.Month:D2}.{date.Day:D2}";
    }
}
