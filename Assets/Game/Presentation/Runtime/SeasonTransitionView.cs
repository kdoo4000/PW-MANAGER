using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class SeasonTransitionView
    {
        public static VisualElement Open(VisualElement host)
        {
            var save = DashboardSession.ActiveSave;
            if (!SeasonTransitionService.CanConfigure(save)) return null;
            var service = new SeasonTransitionService();
            var report = service.GetOrCreateResult(save);
            var settings = SeasonTransitionService.DefaultSettings(save);
            var closingPrestige = SeasonTransitionService.ProjectedClosingPrestige(save);
            var nextTier = Math.Max(PromotionProgressionRules.ActiveTier(save), PromotionProgressionRules.Tier(closingPrestige));
            var allowedPpvTypes = PromotionProgressionRules.PpvTypes(nextTier);
            host.Q("season-transition-overlay")?.RemoveFromHierarchy();
            var previousFocus = host.focusController?.focusedElement;
            var overlay = new VisualElement { name = "season-transition-overlay" }; overlay.AddToClassList("contract-overlay");
            overlay.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ContractNegotiation")); host.Add(overlay);
            var dialog = new VisualElement(); dialog.AddToClassList("contract-dialog"); dialog.style.maxWidth = 860; dialog.style.width = Length.Percent(90); overlay.Add(dialog);
            var title = new Label($"{report.Policy.StartDate.Year}/{report.Policy.EndDate.Year} 시즌 결산"); title.AddToClassList("contract-heading"); dialog.Add(title);
            var body = new ScrollView(); body.AddToClassList("contract-body"); dialog.Add(body);
            var summary = new Label(Summary(report)) { name = "season-summary" }; summary.AddToClassList("contract-status"); body.Add(summary);
            var unlockTitle = new Label("다음 시즌 해금"); unlockTitle.AddToClassList("contract-section-title"); body.Add(unlockTitle);
            var unlocks = new VisualElement { name = "season-new-unlocks" }; body.Add(unlocks);
            var newOptions = PromotionProgressionRules.NewOptions(save, closingPrestige).ToList();
            unlocks.Add(new Label($"결산 후 예상 명성 {closingPrestige:N0}"));
            foreach (var option in newOptions) unlocks.Add(new Label(option));
            if (newOptions.Count == 0) unlocks.Add(new Label("새로 해금되는 항목 없음"));
            var form = new VisualElement(); body.Add(form);
            var missing = SeasonTransitionService.IncompleteShows(save);
            Toggle cancelMissed = null;
            if (missing.Count > 0)
            {
                cancelMissed = new Toggle($"날짜가 지난 미완료 쇼 {missing.Count}건을 취소") { name = "season-cancel-missed" };
                cancelMissed.tooltip = "취소 기록과 기존 쇼 편성을 보관합니다."; form.Add(cancelMissed);
            }
            var contracts = new VisualElement(); form.Add(contracts);
            void RefreshContracts()
            {
                contracts.Clear();
                var ending = save.OwnContracts.Where(x => x.IsInForce && x.EndDate.CompareTo(report.Policy.EndDate) <= 0 &&
                    !save.OwnContracts.Any(c => c.PersonId == x.PersonId && c.Type == x.Type && c.Status == ContractStatus.Scheduled)).ToList();
                if (ending.Count == 0) return;
                var foldout = new Foldout { text = $"미갱신 계약 {ending.Count}건", value = true }; contracts.Add(foldout);
                foreach (var contract in ending)
                {
                    var row = new VisualElement(); row.AddToClassList("contract-actions"); foldout.Add(row);
                    if (contract.Type == ContractType.Wrestler)
                    {
                        var person = save.Wrestlers.Single(x => x.Id == contract.PersonId);
                        var link = WrestlerProfileController.CreateLink(person); row.Add(link);
                        link.clicked += () => overlay.RemoveFromHierarchy();
                    }
                    else row.Add(new Label(save.Managers.Single(x => x.Id == contract.PersonId).Name ?? "전문 매니저"));
                    row.Add(new Button(() => ContractNegotiationView.Open(host, contract.PersonId, contract.Type, () =>
                    {
                        if (DashboardSession.ActiveSave != save || !SeasonTransitionService.RequiresSetup(save)) return;
                        report = service.GetOrCreateResult(save); summary.text = Summary(report); RefreshContracts();
                    })) { text = "재계약" });
                }
            }
            RefreshContracts();
            var settingsTitle = new Label("다음 시즌 설정"); settingsTitle.AddToClassList("contract-section-title"); form.Add(settingsTitle);
            var name = new TextField("정기 쇼 제목") { name = "season-regular-name", value = settings.RegularShowName }; form.Add(name);
            var duration = new IntegerField("정기 쇼 시간 (분)") { name = "season-regular-duration", value = settings.RegularDurationMinutes, tooltip = "60분 단위" }; form.Add(duration);
            var regular = new DropdownField("정기 쇼 빈도", new[] { "월 1회", "격주", "주 1회", "주 2회", "주 3회" }.Take(PromotionProgressionRules.MaxRegular(nextTier) + 1).ToList(), (int)settings.RegularFrequency)
                { name = "season-regular-frequency" }; form.Add(regular);
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var venues = (catalog?.Venues ?? new List<VenueDefinition>()).Where(x => x != null &&
                (x.UnlockCost == 0 || x.RequiredPrestige <= closingPrestige || save.Promotion.UnlockedIds.Contains(x.Id) || save.VenueContracts.Any(v => v.VenueId == x.Id)))
                .OrderBy(x => x.Capacity).Select(x => (Id: x.Id, Name: x.DisplayName, Cost: x.ProductionCost, UnlockCost: x.UnlockCost, Required: x.RequiredPrestige)).ToList();
            if (!venues.Any(x => x.Id == settings.VenueId)) venues.Add((settings.VenueId, "기존 경기장", settings.ProductionCost, 0, 0));
            var venue = new DropdownField("정기 쇼 경기장", venues.Select(x => x.Name).ToList(), venues.FindIndex(x => x.Id == settings.VenueId))
                { name = "season-venue" }; form.Add(venue);
            var cost = new Label(); form.Add(cost);
            void SelectVenue()
            {
                var selected = venues[venue.index]; settings.VenueId = selected.Id; settings.ProductionCost = selected.Cost;
                settings.VenueRequiredPrestige = selected.Required;
                settings.VenueUnlockCost = save.Promotion.UnlockedIds.Contains(selected.Id) || save.VenueContracts.Any(v => v.VenueId == selected.Id) ? 0 : selected.UnlockCost;
                cost.text = $"기본 제작비  시간당 ${selected.Cost:N0}";
                if (settings.VenueUnlockCost > 0) cost.text += $" · 경기장 등급 해금 비용 ${settings.VenueUnlockCost:N0}";
            }
            venue.RegisterValueChangedCallback(_ => SelectVenue()); SelectVenue();
            var frequency = new DropdownField("PPV 빈도", new[] { "4개월마다", "분기별", "2개월마다", "매월" }.Take(PromotionProgressionRules.MaxPpvFrequency(nextTier) + 1).ToList(), (int)settings.PpvFrequency)
                { name = "season-ppv-frequency" }; form.Add(frequency);
            var ppvs = new VisualElement(); form.Add(ppvs);
            var ppvInputs = new Dictionary<int, (TextField Name, IntegerField Duration, DropdownField Tier)>();
            void CapturePpvs()
            {
                foreach (var entry in ppvInputs)
                {
                    var setup = settings.Ppvs.Single(x => x.Month == entry.Key);
                    setup.Title = entry.Value.Name.value; setup.DurationMinutes = entry.Value.Duration.value;
                    setup.Type = allowedPpvTypes[entry.Value.Tier.index];
                }
            }
            void RenderPpvs()
            {
                ppvs.Clear(); ppvInputs.Clear();
                foreach (var month in SeasonTransitionService.PpvMonths(settings.PpvFrequency))
                {
                    var setup = settings.Ppvs.Single(x => x.Month == month);
                    var heading = new Label($"{month}월 PPV"); heading.AddToClassList("contract-section-title"); ppvs.Add(heading);
                    var ppvName = new TextField("제목") { value = setup.Title, name = $"season-ppv-name-{month}" }; ppvs.Add(ppvName);
                    var ppvDuration = new IntegerField("시간 (분)") { value = setup.DurationMinutes, tooltip = "60분 단위", name = $"season-ppv-duration-{month}" }; ppvs.Add(ppvDuration);
                    var tier = new DropdownField("등급", allowedPpvTypes.Select(x => x == ScheduledShowType.PpvRegular ? "일반 PPV" : x == ScheduledShowType.PpvMajor ? "대형 PPV" : "대표 PPV").ToList(), allowedPpvTypes.IndexOf(setup.Type))
                        { name = $"season-ppv-tier-{month}" }; ppvs.Add(tier);
                    ppvInputs[month] = (ppvName, ppvDuration, tier);
                }
            }
            frequency.RegisterValueChangedCallback(_ => { CapturePpvs(); settings.PpvFrequency = (PpvFrequency)frequency.index; RenderPpvs(); }); RenderPpvs();
            var result = new Label { name = "season-result" }; result.AddToClassList("contract-status"); body.Add(result);
            var actions = new VisualElement(); actions.AddToClassList("contract-actions"); dialog.Add(actions);
            void Close() { overlay.RemoveFromHierarchy(); previousFocus?.Focus(); }
            var close = new Button(Close) { text = "닫기", name = "season-close" }; actions.Add(close);
            var confirm = new Button { text = "스폰서 선택", name = "season-confirm" }; actions.Add(confirm);
            if (cancelMissed != null)
            {
                confirm.SetEnabled(false);
                cancelMissed.RegisterValueChangedCallback(evt => confirm.SetEnabled(evt.newValue));
            }
            confirm.clicked += () =>
            {
                if (!confirm.enabledSelf || DashboardSession.ActiveSave != save) return;
                var before = JsonUtility.ToJson(save);
                try
                {
                    settings.RegularShowName = name.value; settings.RegularDurationMinutes = duration.value;
                    settings.RegularFrequency = (RegularShowFrequency)regular.index; settings.PpvFrequency = (PpvFrequency)frequency.index;
                    settings.CancelMissedShows = cancelMissed?.value ?? false; SelectVenue(); CapturePpvs();
                    SponsorSelectionView.OpenNextSeason(host, save, settings, () =>
                    {
                        form.SetEnabled(false); confirm.SetEnabled(false); result.text = "다음 시즌 준비가 완료되었습니다."; close.Focus();
                    });
                }
                catch (Exception exception) { JsonUtility.FromJsonOverwrite(before, save); result.text = exception.Message; }
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
            overlay.schedule.Execute(() => name.Focus());
            return overlay;
        }

        private static bool IsDisplayed(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.style.display == DisplayStyle.None || current.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }
        private static string Summary(SeasonResultState result) =>
            $"완료 쇼 {result.CompletedShows}회 · PPV {result.CompletedPpvs}회 · 취소 {result.CancelledShows}회\n" +
            $"수입 ${result.Income:N0} · 지출 ${result.Expenses:N0} · 기말 자금 ${result.ClosingCash:N0}\n" +
            $"기말 미지급 급여 ${result.OutstandingSalary:N0}\n" +
            $"{result.ObservedDate.Year}.{result.ObservedDate.Month:D2}.{result.ObservedDate.Day:D2}  팬 {result.Followers:N0}명 · 명성 {result.Prestige:N0}";
    }
}
