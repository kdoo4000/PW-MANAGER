using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class WrestlerCareerView
    {
        public static VisualElement Open(VisualElement host, string wrestlerId)
        {
            var save = DashboardSession.ActiveSave; var w = save?.Wrestlers.FirstOrDefault(x => x.Id == wrestlerId);
            if (w == null || host == null) return null;
            host.Q("career-history-overlay")?.RemoveFromHierarchy(); var previousFocus = host.focusController?.focusedElement;
            var overlay = new VisualElement { name = "career-history-overlay" }; overlay.AddToClassList("contract-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ContractNegotiation")); host.Add(overlay);
            var dialog = new VisualElement(); dialog.AddToClassList("contract-dialog"); overlay.Add(dialog);
            var heading = new Label("선수 변화 기록"); heading.AddToClassList("contract-heading"); dialog.Add(heading);
            dialog.Add(WrestlerProfileController.CreateLink(w)); var body = new ScrollView(); body.AddToClassList("contract-body"); dialog.Add(body);
            var history = save.CareerEvents.Where(x => x.WrestlerId == w.Id).Reverse()
                .OrderByDescending(x => x.Date.Year).ThenByDescending(x => x.Date.Month).ThenByDescending(x => x.Date.Day).ToList();
            if (history.Count == 0) Line(body, "아직 기록된 변화가 없습니다.");
            foreach (var e in history)
            {
                var source = e.Kind switch { CareerEventKind.Match => "경기", CareerEventKind.Promo => "프로모", CareerEventKind.Inactivity => "분기 미활동", CareerEventKind.Fame => "명성", _ => "신인 해제" };
                var row = new Foldout { text = $"{e.Date.Year}.{e.Date.Month:D2}.{e.Date.Day:D2} · {source}", value = e == history[0] };
                body.Add(row);
                if (e.Kind == CareerEventKind.Fame)
                {
                    Line(row, $"명성 {e.FameBefore:N0} → {e.FameAfter:N0} · +{e.FameGain:0.##}");
                    if (e.FameActivity > 0) Line(row, "쇼 노출과 활약 반영");
                    if (e.FameTitle > 0) Line(row, "타이틀 성취 반영");
                    if (e.FameMilestoneBefore < 1 && e.FameMilestoneAfter >= 1) Line(row, "유명 선수 최초 도달");
                    if (e.FameMilestoneBefore < 2 && e.FameMilestoneAfter >= 2) Line(row, "세계적인 선수 최초 도달");
                    if (e.FamePromotionBonus > 0)
                    {
                        var paid = save.Promotion.AppliedPrestigeReasonIds.Contains(e.Id);
                        Line(row, $"단체 명성 +{e.FamePromotionBonus} · {(paid ? "분기 정산 완료" : "분기 정산 예정")}");
                        if (e.FameRookieDevelopment) Line(row, "신인 육성 보너스 +10 포함");
                    }
                }
                foreach (var reason in e.StatusReasons) Line(row, $"위상 {(reason.Amount > 0 ? "상승" : "하락")} · {Reason(reason.Reason)}");
                if (e.MomentumGain > 0) Line(row, "모멘텀 상승");
                if (e.ReleaseType != MomentumReleaseType.None) Line(row, e.ReleaseType == MomentumReleaseType.Full ? "모멘텀 완전 해소" : "인상적인 패배 · 모멘텀 50% 부분 해소");
                if (e.Collapsed) Line(row, "팬들의 기대 붕괴 · 모멘텀 초기화");
                if (e.Kind == CareerEventKind.Inactivity) Line(row, "한 분기 미활동 · 추가 페널티 없이 모멘텀 초기화");
                if (e.WasRookie && !e.IsRookie) Line(row, "신인 상태 해제");
                DescribeFans(row, e.InterestChange, "선수 개인 관심도"); DescribeFans(row, e.PromotionSatisfactionChange, "단체 팬 만족도");
                if (row.contentContainer.childCount == 0) Line(row, "추가 변화 없음");
            }
            void Close() { overlay.RemoveFromHierarchy(); previousFocus?.Focus(); }
            var actions = new VisualElement(); actions.AddToClassList("contract-actions"); dialog.Add(actions);
            var close = new Button(Close) { name = "career-history-close", text = "닫기" }; actions.Add(close);
            overlay.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            overlay.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Tab)
                {
                    var targets = dialog.Query<VisualElement>().ToList().Where(x => x.focusable && x.tabIndex >= 0 && x.enabledInHierarchy && IsDisplayed(x)).ToList();
                    if (targets.Count == 0) return;
                    var index = targets.FindIndex(x => x == dialog.focusController?.focusedElement);
                    targets[(index + (e.shiftKey ? targets.Count - 1 : 1)) % targets.Count].Focus(); e.StopPropagation(); e.PreventDefault();
                }
            });
            overlay.schedule.Execute(close.Focus); return overlay;
        }

        public static void RenderRisks(VisualElement host, GameSave save, ShowState show, MatchPlanState plan)
        {
            if (host == null) return; host.Clear();
            if (save != null && show != null && plan != null)
            foreach (var w in save.Wrestlers.Where(x => WrestlerCareerService.HasMomentumRisk(save, show, plan, x.Id)))
            {
                var row = new VisualElement(); row.AddToClassList("show-panel-caption");
                row.Add(WrestlerProfileController.CreateLink(w));
                Line(row, "이번 기회에서 성과를 내지 못하면 팬들의 기대가 무너질 수 있습니다."); host.Add(row);
            }
            host.style.display = host.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private static string Reason(StatusChangeReason reason) => reason switch
        {
            StatusChangeReason.Card => "카드 배치", StatusChangeReason.Victory => "승리", StatusChangeReason.Defeat => "패배",
            StatusChangeReason.TitleChallenge => "타이틀 도전", StatusChangeReason.Championship => "챔피언 등극",
            StatusChangeReason.MomentumRelease => "모멘텀 성취", _ => "기대 붕괴"
        };
        private static void DescribeFans(VisualElement host, FanReactionResultState delta, string name)
        {
            if (new[] { delta.Family, delta.Light, delta.Mania }.Any(x => x > 0)) Line(host, name + " 상승");
            if (new[] { delta.Family, delta.Light, delta.Mania }.Any(x => x < 0)) Line(host, name + " 하락");
        }
        private static bool IsDisplayed(VisualElement v)
        {
            for (var p = v; p != null; p = p.parent) if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }
        private static void Line(VisualElement host, string text)
        { var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 6; host.Add(label); }
    }
}
