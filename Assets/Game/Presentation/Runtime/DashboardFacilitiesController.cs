using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class DashboardFacilitiesController
    {
        private readonly VisualElement root;
        private readonly VisualElement staffPanel;
        private readonly VisualElement venuePanel;
        private readonly VisualElement staffGrid;
        private readonly VisualElement venueGrid;
        private readonly Button staffTab;
        private readonly Button venueTab;
        private readonly StaticContentCatalog catalog;
        private GameSave save;

        public DashboardFacilitiesController(VisualElement root)
        {
            this.root = root;
            staffPanel = root.Q("facilities-staff-panel");
            venuePanel = root.Q("facilities-venues-panel");
            staffGrid = root.Q("facilities-staff-grid");
            venueGrid = root.Q("facilities-venue-grid");
            staffTab = root.Q<Button>("facilities-tab-staff");
            venueTab = root.Q<Button>("facilities-tab-venues");
            catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            if (staffTab != null) staffTab.clicked += () => ShowTab(true);
            if (venueTab != null) venueTab.clicked += () => ShowTab(false);
        }

        public void Render(GameSave value)
        {
            save = value;
            Set("facilities-cash", value?.Promotion == null ? "—" : DashboardText.Money(value.Promotion.CalculateCurrentCash(value.Transactions)));
            Set("facilities-prestige", value?.Promotion == null ? "—" : value.Promotion.PromotionPrestige.ToString("N0"));
            RenderStaff();
            RenderVenues();
            ShowTab(staffPanel == null || !staffPanel.ClassListContains("hidden"));
        }

        private void ShowTab(bool staff)
        {
            staffPanel?.EnableInClassList("hidden", !staff);
            venuePanel?.EnableInClassList("hidden", staff);
            staffTab?.EnableInClassList("active-tab", staff);
            venueTab?.EnableInClassList("active-tab", !staff);
            Set("facilities-summary-label", staff ? "운영 부서" : "시설");
            var count = staff ? save?.StaffDepartments?.Count(x => x != null && x.CurrentLevel > 0) ?? 0
                : catalog?.Venues?.Count(x => x != null && (x.UnlockCost == 0 || save?.Promotion?.UnlockedIds?.Contains(x.Id) == true ||
                    save?.VenueContracts?.Any(c => c != null && c.VenueId == x.Id && IsCurrent(c.Status)) == true)) ?? 0;
            Set("facilities-summary-count", count.ToString());
        }

        private void RenderStaff()
        {
            if (staffGrid == null) return;
            staffGrid.Clear();
            var definitions = catalog?.StaffDepartments ?? new List<StaffDepartmentDefinition>();
            foreach (var state in save?.StaffDepartments?.Where(x => x != null) ?? Enumerable.Empty<StaffDepartmentState>())
            {
                var definition = definitions.FirstOrDefault(x => x != null && x.DepartmentType == state.DepartmentType);
                var levels = Levels(state.DepartmentType);
                var current = levels.FirstOrDefault(x => x != null && x.Level == state.CurrentLevel);
                var next = levels.FirstOrDefault(x => x != null && x.Level == state.CurrentLevel + 1);
                var nextLevel = next?.Level ?? state.CurrentLevel;
                var requiredPrestige = next?.RequiredPrestige ?? 0;
                var unlocked = state.CurrentLevel > 0;
                var cost = next?.UpgradeCost ?? 0;
                var cash = save?.Promotion?.CalculateCurrentCash(save.Transactions) ?? 0;
                var canPurchase = next != null && (save?.Promotion?.PromotionPrestige ?? 0) >= requiredPrestige && cash >= cost;
                var status = next == null ? "최대 레벨"
                    : (save?.Promotion?.PromotionPrestige ?? 0) >= requiredPrestige ? $"Lv.{nextLevel} 구매 가능"
                    : $"Lv.{nextLevel} 해금 위상 {requiredPrestige:N0}";
                AddCard(staffGrid, definition?.DisplayName ?? DepartmentName(state.DepartmentType),
                    unlocked ? $"Lv.{state.CurrentLevel}" : "미해금", current?.EffectDescription ?? "부서를 해금하면 효과가 적용됩니다.",
                    status, next == null ? "—" : $"{(unlocked ? "업그레이드" : "해금")} 비용 {DashboardText.Money(cost)}",
                    next == null ? "완료" : unlocked ? "업그레이드" : "해금", canPurchase,
                    () => PurchaseStaff(state, nextLevel, requiredPrestige, cost), !unlocked);
            }
            if (staffGrid.childCount == 0) AddEmpty(staffGrid, "스태프 부서 데이터가 없습니다.");
        }

        private void RenderVenues()
        {
            if (venueGrid == null) return;
            venueGrid.Clear();
            var definitions = catalog?.Venues ?? new List<VenueDefinition>();
            var contracts = save?.VenueContracts?.Where(x => x != null).ToList() ?? new List<VenueContractState>();
            foreach (var venue in definitions.Where(x => x != null).OrderBy(x => x.RequiredPrestige))
            {
                var contract = contracts.FirstOrDefault(x => x.VenueId == venue.Id && IsCurrent(x.Status));
                var unlocked = venue.UnlockCost == 0 || contract != null || save?.Promotion?.UnlockedIds?.Contains(venue.Id) == true;
                var available = unlocked || (save?.Promotion?.PromotionPrestige ?? 0) >= venue.RequiredPrestige;
                var cash = save?.Promotion?.CalculateCurrentCash(save.Transactions) ?? 0;
                var status = contract != null ? "사용 가능"
                    : unlocked ? "해금됨" : available ? "해금 가능" : $"위상 {venue.RequiredPrestige:N0} 필요";
                AddCard(venueGrid, venue.DisplayName, $"수용 {venue.Capacity:N0}명",
                    $"시간당 제작비 {DashboardText.Money(venue.ProductionCost)} · 기본 티켓 {DashboardText.Money(venue.BaseTicketPrice)}",
                    status, venue.UnlockCost == 0 ? "해금 비용 없음" : $"해금 비용 {DashboardText.Money(venue.UnlockCost)}",
                    unlocked ? "해금됨" : "해금", !unlocked && available && cash >= venue.UnlockCost,
                    () => UnlockVenue(venue), !available);
            }
            if (venueGrid.childCount == 0) AddEmpty(venueGrid, "등록된 시설이 없습니다.");
        }

        private static void AddCard(VisualElement parent, string title, string level, string effect, string status,
            string cost, string actionText, bool actionEnabled, Action action, bool locked)
        {
            var card = new VisualElement(); card.AddToClassList("facility-card"); card.EnableInClassList("locked", locked);
            var heading = new VisualElement(); heading.AddToClassList("facility-card-heading");
            var name = new Label(title); name.AddToClassList("facility-card-title"); heading.Add(name);
            var badge = new Label(level); badge.AddToClassList("facility-card-level"); badge.EnableInClassList("locked", locked); heading.Add(badge); card.Add(heading);
            var effectLabel = new Label(effect); effectLabel.AddToClassList("facility-card-effect"); card.Add(effectLabel);
            var statusLabel = new Label(status); statusLabel.AddToClassList("facility-card-status"); statusLabel.EnableInClassList("locked", locked); card.Add(statusLabel);
            var actions = new VisualElement(); actions.AddToClassList("facility-card-actions");
            var costLabel = new Label(cost); costLabel.AddToClassList("facility-card-cost"); actions.Add(costLabel);
            var button = new Button(action) { text = actionText }; button.AddToClassList("facility-card-action"); button.SetEnabled(actionEnabled); actions.Add(button); card.Add(actions);
            parent.Add(card);
        }

        private void PurchaseStaff(StaffDepartmentState state, int nextLevel, long requiredPrestige, long cost)
        {
            if (save?.Promotion == null || state == null || state.CurrentLevel >= 5 || save.Promotion.PromotionPrestige < requiredPrestige) return;
            if (save.Promotion.CalculateCurrentCash(save.Transactions) < cost) return;
            state.CurrentLevel = nextLevel;
            state.UnlockedLevel = Math.Max(state.UnlockedLevel, nextLevel);
            AddTransaction(TransactionType.StaffDepartmentUpgrade, cost, $"{state.Id}:level_{nextLevel}");
            DashboardSession.PersistActive();
        }

        private void UnlockVenue(VenueDefinition venue)
        {
            if (save?.Promotion == null || venue == null || venue.UnlockCost <= 0 || save.Promotion.PromotionPrestige < venue.RequiredPrestige) return;
            if (save.Promotion.CalculateCurrentCash(save.Transactions) < venue.UnlockCost) return;
            save.Promotion.UnlockedIds ??= new List<string>();
            if (save.Promotion.UnlockedIds.Contains(venue.Id)) return;
            save.Promotion.UnlockedIds.Add(venue.Id);
            AddTransaction(TransactionType.VenueUnlock, venue.UnlockCost, venue.Id);
            DashboardSession.PersistActive();
        }

        private void AddTransaction(TransactionType type, long cost, string reasonId)
        {
            save.Transactions ??= new List<TransactionRecord>();
            save.Transactions.Add(new TransactionRecord
            {
                Id = PWManager.Domain.Identifiers.EntityId.CreateRuntimeId(), PromotionId = save.Promotion.Id, Date = save.CurrentDate,
                Type = type, Amount = -cost, ReasonId = reasonId
            });
        }

        private static void AddEmpty(VisualElement parent, string text) { var label = new Label(text); label.AddToClassList("facilities-empty"); parent.Add(label); }
        private void Set(string name, string text) { var label = root.Q<Label>(name); if (label != null) label.text = text; }
        private static string DepartmentName(StaffDepartmentType type) => type switch { StaffDepartmentType.Medical => "의료팀", StaffDepartmentType.Scout => "스카우트팀", StaffDepartmentType.Promotion => "홍보팀", StaffDepartmentType.Commentary => "해설진", _ => type.ToString() };
        private IEnumerable<StaffTeamLevelDefinition> Levels(StaffDepartmentType type) => type switch
        {
            StaffDepartmentType.Medical => catalog?.MedicalTeamLevels ?? Enumerable.Empty<MedicalTeamLevelDefinition>(),
            StaffDepartmentType.Scout => catalog?.ScoutTeamLevels ?? Enumerable.Empty<ScoutTeamLevelDefinition>(),
            StaffDepartmentType.Promotion => catalog?.PromotionTeamLevels ?? Enumerable.Empty<PromotionTeamLevelDefinition>(),
            StaffDepartmentType.Commentary => catalog?.CommentaryTeamLevels ?? Enumerable.Empty<CommentaryTeamLevelDefinition>(),
            _ => Enumerable.Empty<StaffTeamLevelDefinition>()
        };
        private static bool IsCurrent(VenueContractStatus status) => status == VenueContractStatus.Reserved || status == VenueContractStatus.Paid;
    }
}
