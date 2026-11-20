using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class ContractNegotiationView
    {
        public static VisualElement Open(VisualElement host, string personId, ContractType type, Action changed = null)
        {
            var save = DashboardSession.ActiveSave;
            if (save?.Promotion == null || DashboardSession.IsShowRunning) return null;
            host.Q("contract-negotiation-overlay")?.RemoveFromHierarchy();
            var scheduled = save.OwnContracts.FirstOrDefault(x => x.PersonId == personId && x.Type == type &&
                x.Status == ContractStatus.Scheduled && x.EndDate.CompareTo(save.CurrentDate) >= 0);
            var demand = scheduled == null ? ContractNegotiationService.Demand(save, personId, type) : type == ContractType.Wrestler
                ? InitialContractOfferRules.Calculate(save.Wrestlers.Single(x => x.Id == personId))
                : InitialContractOfferRules.Calculate(save.Managers.Single(x => x.Id == personId));
            var period = scheduled == null ? ContractNegotiationService.Period(save, personId, type) : (Start: scheduled.StartDate, End: scheduled.EndDate);
            var overlay = new VisualElement { name = "contract-negotiation-overlay" };
            overlay.AddToClassList("contract-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ContractNegotiation")); host.Add(overlay);
            var dialog = new VisualElement { name = "contract-dialog" }; dialog.AddToClassList("contract-dialog"); overlay.Add(dialog);
            var heading = new Label("계약 협상"); heading.AddToClassList("contract-heading"); dialog.Add(heading);
            if (type == ContractType.Wrestler) dialog.Add(WrestlerProfileController.CreateLink(save.Wrestlers.Single(x => x.Id == personId)));
            else dialog.Add(new Label(save.Managers.Single(x => x.Id == personId).Name ?? "전문 매니저"));
            var body = new ScrollView(); body.AddToClassList("contract-body"); dialog.Add(body);
            body.Add(new Label($"계약 기간  {Date(period.Start)} ~ {Date(period.End)}"));
            var current = save.OwnContracts.FirstOrDefault(x => x.PersonId == personId && x.Type == type && x.Covers(save.CurrentDate));
            if (current != null) body.Add(new Label(type == ContractType.Wrestler
                ? $"현재 계약  월 기본급 ${current.MonthlySalary:N0} · 경기 수당 ${current.MatchAppearanceFee:N0} · 프로모 수당 ${current.PromoAppearanceFee:N0}"
                : $"현재 계약  월 기본급 ${current.MonthlySalary:N0}"));
            var form = new VisualElement(); body.Add(form);
            var bonus = new LongField("계약금 ($)") { name = "contract-signing-bonus", value = demand.SigningBonus }; form.Add(bonus);
            bonus.tooltip = $"요구 계약금 ${demand.SigningBonus:N0} · 체결 즉시 지급";
            var salary = new LongField("월 기본급 ($)") { name = "contract-monthly-salary", value = demand.MonthlySalary }; form.Add(salary);
            salary.tooltip = $"요구 기본급 ${demand.MonthlySalary:N0} · 매월 1일 지급, 계약 첫 달은 남은 일수만큼 지급";
            var matchFee = new LongField("경기 수당 ($)") { name = "contract-match-fee", value = demand.MatchAppearanceFee };
            var promoFee = new LongField("프로모 수당 ($)") { name = "contract-promo-fee", value = demand.PromoAppearanceFee };
            if (type == ContractType.Wrestler) { form.Add(matchFee); form.Add(promoFee); }
            matchFee.tooltip = $"요구 경기 수당 ${demand.MatchAppearanceFee:N0} · 쇼당 한 번, 출연한 항목 중 높은 수당 지급";
            promoFee.tooltip = $"요구 프로모 수당 ${demand.PromoAppearanceFee:N0} · 쇼당 한 번, 출연한 항목 중 높은 수당 지급";
            var role = new DropdownField("예정 역할", new List<string> { "미설정", "자버", "미드카더", "메인이벤터" }, 0) { name = "contract-role" };
            if (type == ContractType.Wrestler) form.Add(role);
            var chance = new Label { name = "contract-chance" }; form.Add(chance);
            var status = new Label { name = "contract-result" }; status.AddToClassList("contract-status"); body.Add(status);
            var previousTermination = save.ContractTerminations.LastOrDefault(x => x.PersonId == personId && x.Type == type && x.Accepted);
            if (previousTermination != null && !save.OwnContracts.Any(x => x.PersonId == personId && x.Type == type && (x.IsInForce || x.Status == ContractStatus.Scheduled)))
                status.text = $"{Date(previousTermination.Date)} 계약 해지 · 보상금 ${previousTermination.Compensation:N0} 지급";
            var actions = new VisualElement(); actions.AddToClassList("contract-actions"); dialog.Add(actions);
            var previousFocus = host.focusController?.focusedElement;
            void Close() { overlay.RemoveFromHierarchy(); previousFocus?.Focus(); }
            var close = new Button(Close) { name = "contract-close", text = "닫기" }; actions.Add(close);
            var submit = new Button { name = "contract-submit", text = "계약 제안" }; actions.Add(submit);
            var termination = new VisualElement(); body.Add(termination);
            void RenderTermination() => AddTermination(termination, save, personId, type, result =>
            {
                status.text = result.Accepted ? $"계약을 해지했습니다. 보상금 ${result.Compensation:N0} 지급" :
                    $"상호 해지 제안이 거절되었습니다. {Date(result.NextAttemptDate)}부터 다시 제안할 수 있습니다.";
                if (result.Accepted) { form.SetEnabled(false); submit.SetEnabled(false); }
                close.Focus();
            }, changed);
            RenderTermination();
            bool hasRole = false; ExpectedRole expected = ExpectedRole.Midcarder;
            void Preview()
            {
                if (scheduled != null) { chance.style.display = DisplayStyle.None; return; }
                hasRole = type == ContractType.Wrestler && role.index > 0;
                expected = hasRole ? (ExpectedRole)(role.index - 1) : ExpectedRole.Midcarder;
                try { chance.text = $"수락 가능성 {ContractNegotiationService.Acceptance(save, personId, type, bonus.value, salary.value, hasRole, expected, matchFee.value, promoFee.value):0.#}%"; }
                catch (Exception exception) { chance.text = exception.Message; }
            }
            bonus.RegisterValueChangedCallback(_ => Preview()); salary.RegisterValueChangedCallback(_ => Preview()); role.RegisterValueChangedCallback(_ => Preview()); matchFee.RegisterValueChangedCallback(_ => Preview()); promoFee.RegisterValueChangedCallback(_ => Preview()); Preview();
            var last = save.ContractNegotiations.LastOrDefault(x => x.PersonId == personId && x.Type == type);
            var booked = scheduled;
            if (booked != null)
            {
                status.text = $"{Date(booked.StartDate)}부터 시작하는 재계약이 예약되어 있습니다.";
                form.SetEnabled(false); submit.SetEnabled(false);
            }
            else if (last != null && (!last.Accepted && save.CurrentDate.CompareTo(last.NextAttemptDate) < 0 || last.Accepted && last.Date.Equals(save.CurrentDate)))
            {
                status.text = last.Accepted ? "오늘 계약을 체결했습니다." : $"제안이 거절되었습니다. {Date(last.NextAttemptDate)}부터 재협상할 수 있습니다.";
                form.SetEnabled(false); submit.SetEnabled(false);
            }
            submit.clicked += () =>
            {
                if (DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning || !submit.enabledSelf) return;
                var before = JsonUtility.ToJson(save);
                try
                {
                    var result = new ContractNegotiationService().Submit(save, personId, type, bonus.value, salary.value, hasRole, expected, matchFee.value, promoFee.value);
                    DashboardSession.PersistActive();
                    status.text = result.Accepted ? $"계약이 체결되었습니다. {Date(period.Start)} ~ {Date(period.End)}" :
                        $"제안이 거절되었습니다. {Date(result.NextAttemptDate)}부터 재협상할 수 있습니다.";
                    form.SetEnabled(false); submit.SetEnabled(false); changed?.Invoke(); close.Focus();
                    RenderTermination();
                }
                catch (Exception exception)
                {
                    JsonUtility.FromJsonOverwrite(before, save); changed?.Invoke(); status.text = exception.Message; RenderTermination();
                }
            };
            overlay.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Tab)
                {
                    var elements = dialog.Query<VisualElement>().ToList().Where(x => x.focusable && x.tabIndex >= 0 && x.enabledInHierarchy && IsDisplayed(x)).ToList();
                    if (elements.Count == 0) return;
                    var index = elements.FindIndex(x => x == dialog.focusController?.focusedElement);
                    elements[(index + (evt.shiftKey ? elements.Count - 1 : 1)) % elements.Count].Focus();
                    evt.StopPropagation(); evt.PreventDefault();
                }
            });
            overlay.schedule.Execute(() => { if (form.enabledSelf) bonus.Focus(); else close.Focus(); });
            return overlay;
        }

        private static void AddTermination(VisualElement host, GameSave save, string personId, ContractType type,
            Action<ContractTerminationState> completed, Action changed)
        {
            host.Clear();
            var contracts = save.OwnContracts.Where(x => x.PersonId == personId && x.Type == type &&
                (x.IsInForce || x.Status == ContractStatus.Scheduled) && x.EndDate.CompareTo(save.CurrentDate) >= 0).ToList();
            if (contracts.Count == 0)
            {
                return;
            }
            ContractTerminationQuote quote;
            try { quote = ContractTerminationService.Quote(save, personId, type); }
            catch (Exception exception) { host.Add(new Label(exception.Message)); return; }
            var title = new Label("계약 해지"); title.AddToClassList("contract-section-title"); host.Add(title);
            var amounts = new Label($"일방 방출  ${quote.RemainingSalary:N0}\n상호 해지 제안  ${quote.MutualCompensation:N0} · 수락 가능성 {quote.AcceptancePercent:0.#}%")
                { name = "contract-termination-amounts" }; amounts.AddToClassList("contract-status"); host.Add(amounts);
            var debt = save.SalaryPayments.Where(x => contracts.Any(c => c.Id == x.ContractId)).Sum(x => x.RemainingAmount);
            if (debt > 0)
            {
                var unpaid = new Label($"기존 미지급 급여 ${debt:N0}는 해지 후에도 남습니다."); unpaid.AddToClassList("contract-status"); host.Add(unpaid);
            }
            var status = new Label { name = "contract-termination-status" }; status.AddToClassList("contract-status"); host.Add(status);
            var actions = new VisualElement(); actions.AddToClassList("contract-actions"); host.Add(actions);
            var release = new Button { name = "contract-release", text = "일방 방출" }; actions.Add(release);
            var mutual = new Button { name = "contract-mutual-release", text = "상호 해지 제안" }; actions.Add(mutual);
            var confirmation = new VisualElement { name = "contract-termination-confirmation-area" }; host.Add(confirmation);
            confirmation.style.display = DisplayStyle.None;
            var prompt = new Label { name = "contract-termination-confirmation" }; prompt.AddToClassList("contract-status"); confirmation.Add(prompt);
            var confirmActions = new VisualElement(); confirmActions.AddToClassList("contract-actions"); confirmation.Add(confirmActions);
            var cancel = new Button(() => { confirmation.style.display = DisplayStyle.None; actions.SetEnabled(true); release.Focus(); }) { text = "취소" }; confirmActions.Add(cancel);
            var confirm = new Button { name = "contract-termination-confirm", text = "확인" }; confirmActions.Add(confirm);
            var cash = save.Promotion.CalculateCurrentCash(save.Transactions);
            var rejected = save.ContractTerminations.LastOrDefault(x => x.PersonId == personId && x.Type == type && x.Mutual && !x.Accepted &&
                x.ContractIds.Intersect(contracts.Select(c => c.Id)).Any());
            var cooldown = rejected != null && save.CurrentDate.CompareTo(rejected.NextAttemptDate) < 0;
            release.SetEnabled(quote.BlockingReason == null && cash >= quote.RemainingSalary);
            mutual.SetEnabled(quote.BlockingReason == null && cash >= quote.MutualCompensation && !cooldown);
            var notes = new List<string>();
            if (quote.BlockingReason != null) notes.Add(quote.BlockingReason);
            if (cash < quote.RemainingSalary) notes.Add(cash < quote.MutualCompensation ? "해지 보상금 전액을 지급할 자금이 부족합니다." : "일방 방출 보상금을 지급할 자금이 부족합니다.");
            if (cooldown) notes.Add($"제안이 거절되었습니다. {Date(rejected.NextAttemptDate)}부터 다시 제안할 수 있습니다.");
            status.text = string.Join("\n", notes);
            bool selectedMutual = false; long selectedAmount = 0;
            void Select(bool isMutual)
            {
                if (DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning || !(isMutual ? mutual : release).enabledInHierarchy) return;
                selectedMutual = isMutual; selectedAmount = isMutual ? quote.MutualCompensation : quote.RemainingSalary;
                prompt.text = isMutual ? $"보상금 ${selectedAmount:N0}로 상호 해지를 제안합니다. 수락하면 계약이 종료됩니다." :
                    $"보상금 ${selectedAmount:N0}를 지급하고 계약을 종료합니다.";
                confirmation.style.display = DisplayStyle.Flex; actions.SetEnabled(false); confirm.SetEnabled(true); confirm.Focus();
            }
            release.clicked += () => Select(false); mutual.clicked += () => Select(true);
            confirm.clicked += () =>
            {
                if (confirmation.style.display == DisplayStyle.None || DashboardSession.ActiveSave != save || DashboardSession.IsShowRunning) return;
                var before = JsonUtility.ToJson(save);
                try
                {
                    var current = ContractTerminationService.Quote(save, personId, type);
                    if ((selectedMutual ? current.MutualCompensation : current.RemainingSalary) != selectedAmount)
                        throw new InvalidOperationException("보상금이 변경되었습니다. 창을 다시 열어 확인하세요.");
                    var result = new ContractTerminationService().Submit(save, personId, type, selectedMutual);
                    DashboardSession.PersistActive();
                    completed(result); changed?.Invoke();
                    confirmation.style.display = DisplayStyle.None; confirm.SetEnabled(false);
                    if (result.Accepted) { release.SetEnabled(false); mutual.SetEnabled(false); }
                    else
                    {
                        actions.SetEnabled(true); mutual.SetEnabled(false);
                        status.text = $"{Date(result.NextAttemptDate)}부터 다시 제안할 수 있습니다.";
                        release.Focus();
                    }
                }
                catch (Exception exception)
                {
                    JsonUtility.FromJsonOverwrite(before, save); changed?.Invoke();
                    confirmation.style.display = DisplayStyle.None; actions.SetEnabled(true); status.text = exception.Message;
                }
            };
        }

        private static bool IsDisplayed(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.style.display == DisplayStyle.None || current.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        private static string Date(GameDate date) => $"{date.Year}.{date.Month:D2}.{date.Day:D2}";
    }
}
