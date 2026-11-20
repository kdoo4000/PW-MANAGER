using System;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class WrestlerBehaviorView
    {
        public static VisualElement Open(VisualElement host, string wrestlerId, Action changed = null)
        {
            var save = DashboardSession.ActiveSave;
            var person = save?.Wrestlers.FirstOrDefault(w => w.Id == wrestlerId);
            if (host == null || person == null || !save.IsWrestlerDiscovered(wrestlerId)) return null;
            if (person.PromotionId != save.Promotion.Id && !save.WrestlerBehaviors.Any(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == wrestlerId)) return null;
            host.Q("wrestler-report-overlay")?.RemoveFromHierarchy();
            var previousFocus = host.focusController?.focusedElement;
            var overlay = new VisualElement { name = "wrestler-report-overlay" }; overlay.AddToClassList("contract-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ContractNegotiation")); host.Add(overlay);
            var dialog = new VisualElement(); dialog.AddToClassList("contract-dialog"); dialog.AddToClassList("wrestler-report-dialog"); overlay.Add(dialog);
            var heading = new Label("개인 보고서"); heading.AddToClassList("contract-heading"); dialog.Add(heading);
            dialog.Add(WrestlerProfileController.CreateLink(person));
            var body = new ScrollView { name = "wrestler-report-body" }; body.AddToClassList("contract-body"); dialog.Add(body);
            var notice = new Label { name = "wrestler-report-result" }; notice.AddToClassList("contract-result"); dialog.Add(notice);

            void Render()
            {
                body.Clear(); notice.text = "";
                var state = save.WrestlerBehaviors.SingleOrDefault(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == wrestlerId);
                var hasOther = save.LockerRoomEvents.Any(e => e.PromotionId == save.Promotion.Id && (e.ActorId == wrestlerId || e.PartnerId == wrestlerId || e.MediatorId == wrestlerId)) ||
                    HasAdaptationObservation(save, person);
                if (state == null || state.Observations.Count == 0 && state.CardObservations.Count == 0 && state.Events.Count == 0)
                {
                    if (hasOther) { OtherObservations(body, save, person); return; }
                    Line(body, "아직 확인한 출연 관찰이나 면담 기록이 없습니다."); return;
                }
                Heading(body, "현재 상황");
                var current = state.Concerns.Where(c => c.Status != WrestlerConcernStatus.Resolved)
                    .OrderBy(c => c.Status == WrestlerConcernStatus.AwaitingResponse ? 0 : c.Status == WrestlerConcernStatus.PromiseActive ? 1 : 2).ThenBy(c => c.Kind).ToList();
                if (current.Count == 0) Line(body, "현재 확인된 미해결 요구나 진행 중인 약속이 없습니다.");
                foreach (var concern in current)
                {
                    var card = concern.Kind == WrestlerConcernKind.CardPlacement;
                    if (concern.Status == WrestlerConcernStatus.AwaitingResponse)
                    {
                        Line(body, card ? "카드 대우에 대한 면담 요청" : "출연 기회에 대한 면담 요청");
                        Line(body, $"응답 기한: {Date(concern.ResponseDueDate.Value)}");
                        Line(body, card ? $"카드 배치 약속 제안: 앞으로 28일 안에 해당 카드 경기를 제공할 수 있는 정상 출전 가능 최대 2회 쇼 중 최소 1회 {WrestlerBehaviorService.CardRoleName(concern.RequestedCardRole)} 출전" :
                            "출연 약속 제안: 앞으로 28일 안에 정상 출전 가능한 최대 2회 쇼 중 최소 1회 출연 기회 제공");
                        var actions = new VisualElement(); actions.AddToClassList("contract-actions"); body.Add(actions);
                        var promise = new Button(() => Respond(concern.Id, WrestlerConcernResponse.OfferAppearancePromise))
                            { name = card ? "wrestler-offer-card-promise" : "wrestler-offer-promise", text = card ? "카드 배치 약속 제안" : "출연 약속 제안" };
                        var decline = new Button(() => Respond(concern.Id, WrestlerConcernResponse.Decline))
                            { name = card ? "wrestler-decline-card-request" : "wrestler-decline-request", text = "요구 거절" };
                        actions.Add(promise); actions.Add(decline);
                        var enabled = !DashboardSession.IsShowRunning && save.CurrentDate.CompareTo(concern.ResponseDueDate.Value) <= 0 &&
                            save.Wrestlers.Any(w => w.Id == wrestlerId && w.PromotionId == save.Promotion.Id) &&
                            save.OwnContracts.Any(c => c.PersonId == wrestlerId && c.Type == ContractType.Wrestler && c.Covers(save.CurrentDate));
                        actions.SetEnabled(enabled);
                    }
                    else if (concern.Status == WrestlerConcernStatus.PromiseActive)
                    {
                        var promise = concern.Promises.Single(p => p.Status == WrestlerPromiseStatus.Active);
                        Line(body, $"{(card ? WrestlerBehaviorService.CardRoleName(promise.RequiredCardRole) + " 배치" : "출연")} 약속 관찰 중 · 기한 {Date(promise.DueDate)}");
                        Line(body, card ? $"해당 카드 경기를 제공할 수 있는 정상 출전 가능 쇼 {promise.EligibleShows}회 확인 · 실제 카드 배치로 이행 여부를 판단합니다." :
                            $"정상 출전 가능한 쇼 {promise.EligibleShows}회 확인 · 다음 출연 결과로 이행 여부를 판단합니다.");
                    }
                    else
                    {
                        Line(body, card ? "카드 대우 변화 관찰 중" : "출연 기회 변화 관찰 중");
                        var observation = state.Events.LastOrDefault(e => e.ConcernId == concern.Id && e.Kind == WrestlerBehaviorEventKind.Observation);
                        if (observation != null) Line(body, observation.Description);
                    }
                }
                var recent = state.Events.LastOrDefault(e => e.Kind != WrestlerBehaviorEventKind.Observation);
                if (recent != null) { Heading(body, "최근 반응"); Line(body, $"{Date(recent.Date)} · {recent.Description}"); }
                Heading(body, "최근 출연");
                if (state.Observations.Count > 0)
                    Line(body, $"정상 출전 가능했던 {state.Observations.Count}회 쇼 중 {state.Observations.Count(x => x.Appeared)}회 출연");
                if (state.CardObservations.Count > 0)
                    Line(body, $"실제 경기 출연 {state.CardObservations.Count}회 쇼 중 상위 카드 {state.CardObservations.Count(o => o.UpperCard)}회, 메인이벤트 {state.CardObservations.Count(o => o.MainEvent)}회");
                Heading(body, "문제별 경과");
                foreach (var concern in state.Concerns.OrderByDescending(c => c.StartedDate.Year).ThenByDescending(c => c.StartedDate.Month).ThenByDescending(c => c.StartedDate.Day))
                {
                    var history = new Foldout { name = "wrestler-concern-" + concern.Id, value = concern.Status != WrestlerConcernStatus.Resolved,
                        text = $"{(concern.Kind == WrestlerConcernKind.CardPlacement ? "카드 대우" : "출연 기회")} · {Date(concern.StartedDate)} · {(concern.Status == WrestlerConcernStatus.Resolved ? "관찰 종료" : "진행 중")}" };
                    body.Add(history);
                    var latestObservation = state.Events.LastOrDefault(e => e.ConcernId == concern.Id && e.Kind == WrestlerBehaviorEventKind.Observation);
                    if (latestObservation != null) Line(history, $"{Date(latestObservation.Date)} · {latestObservation.Description}");
                    foreach (var promise in concern.Promises)
                    {
                        var result = promise.Status switch { WrestlerPromiseStatus.Active => "진행 중", WrestlerPromiseStatus.Kept => "이행", WrestlerPromiseStatus.Broken => "위반", _ => "판단 기회 없음" };
                        Line(history, $"약속 {Date(promise.StartDate)} ~ {Date(promise.DueDate)} · 정상 출전 가능 쇼 {promise.EligibleShows}회 · {result}");
                    }
                    foreach (var entry in state.Events.Where(e => e.ConcernId == concern.Id && e.Kind != WrestlerBehaviorEventKind.Observation))
                    {
                        Line(history, $"{Date(entry.Date)} · {entry.Description}");
                        var show = save.Shows.FirstOrDefault(s => s.Id == entry.ShowId);
                        if (show != null) Line(history, $"관련 쇼 · {show.Name}");
                    }
                }
                var observations = new Foldout { name = "wrestler-report-observations", text = "전체 관찰 원문", value = false }; body.Add(observations);
                foreach (var entry in state.Events.AsEnumerable().Reverse()) Line(observations, $"{Date(entry.Date)} · {entry.Description}");
                OtherObservations(body, save, person);
            }
            void Respond(string concernId, WrestlerConcernResponse response)
            {
                if (DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning) return;
                var before = JsonUtility.ToJson(save);
                try
                {
                    new WrestlerBehaviorService().Respond(save, wrestlerId, concernId, response);
                    DashboardSession.PersistActive(); changed?.Invoke(); Render();
                }
                catch (Exception exception)
                {
                    JsonUtility.FromJsonOverwrite(before, save); Render(); notice.text = exception.Message;
                }
            }
            Render();
            var footer = new VisualElement(); footer.AddToClassList("contract-actions"); dialog.Add(footer);
            var close = new Button(() => { overlay.RemoveFromHierarchy(); previousFocus?.Focus(); }) { name = "wrestler-report-close", text = "닫기" };
            footer.Add(close);
            overlay.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            overlay.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { overlay.RemoveFromHierarchy(); previousFocus?.Focus(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Tab)
                {
                    var targets = dialog.Query<VisualElement>().ToList().Where(x => x.focusable && x.tabIndex >= 0 && x.enabledInHierarchy && Displayed(x)).ToList();
                    if (targets.Count == 0) return;
                    var index = targets.FindIndex(x => x == dialog.focusController?.focusedElement);
                    targets[(index + (e.shiftKey ? targets.Count - 1 : 1)) % targets.Count].Focus(); e.StopPropagation(); e.PreventDefault();
                }
            });
            overlay.schedule.Execute(close.Focus); return overlay;
        }
        private static void Line(VisualElement host, string text)
        {
            var label = WrestlerNameText.Create(text, DashboardSession.ActiveSave?.Wrestlers);
            label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 6; host.Add(label);
        }
        private static void Heading(VisualElement host, string text)
        {
            var label = new Label(text); label.AddToClassList("contract-section-title"); host.Add(label);
        }
        private static void OtherObservations(VisualElement host, GameSave save, WrestlerState person)
        {
            if (HasAdaptationObservation(save, person))
                Line(host, "변경된 스타일·역할·소속 환경에 적응하고 있습니다.");
            var events = save.LockerRoomEvents.Where(e => e.PromotionId == save.Promotion.Id &&
                (e.ActorId == person.Id || e.PartnerId == person.Id || e.MediatorId == person.Id)).TakeLast(20).Reverse().ToList();
            if (events.Count == 0) return;
            var records = new Foldout { text = $"최근 라커룸 교류 {events.Count}건", value = false }; host.Add(records);
            foreach (var entry in events)
            {
                var row = new VisualElement(); records.Add(row); Line(row, Date(entry.Date));
                foreach (var id in new[] { entry.ActorId, entry.PartnerId, entry.MediatorId }.Where(id => !string.IsNullOrEmpty(id)).Distinct())
                {
                    var member = save.Wrestlers.FirstOrDefault(w => w.Id == id);
                    if (member != null && save.IsWrestlerDiscovered(id)) row.Add(WrestlerProfileController.CreateLink(member));
                }
                Line(row, entry.Description);
            }
        }
        private static bool HasAdaptationObservation(GameSave save, WrestlerState person) => person.PromotionId == save.Promotion.Id &&
            (person.Id == save.Player?.WrestlerId || save.OwnContracts.Any(c => c.PersonId == person.Id && c.Type == ContractType.Wrestler && c.Covers(save.CurrentDate))) &&
            WrestlerAdaptationService.Penalty(save, person, save.CurrentDate) > 0;
        private static bool Displayed(VisualElement item)
        {
            for (var node = item; node != null; node = node.parent)
                if (node.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }
        private static string Date(GameDate date) => $"{date.Year}.{date.Month:D2}.{date.Day:D2}";
    }
}
