using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using PWManager.Data.Catalogs;

namespace PWManager.Tests
{
    public sealed class PromotionQuarterViewTests
    {
        [TestCase(999, 1000000, false)]
        [TestCase(1000, 299999, false)]
        [TestCase(1000, 1000000, true)]
        public void VenueUnlock_UsesCurrentPrestigeAndCashDuringSeasonAndChargesOnlyOnce(long prestige, long cash, bool expected)
        {
            var save = WrestlerCareerServiceTests.Create(); save.Promotion.InitialCash = cash; PromotionQuarterService.Initialize(save);
            save.Promotion.PromotionPrestige = prestige;
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(root);
            var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardFacilitiesController");
            var controller = Activator.CreateInstance(type, new object[] { root }); type.GetMethod("Render").Invoke(controller, new object[] { save });
            var venue = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog").Venues.Single(x => x.Id == "venue_003");
            var purchase = type.GetMethod("UnlockVenue", BindingFlags.NonPublic | BindingFlags.Instance);
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var card = root.Q("facilities-venue-grid").Children().Single(x => x.Query<Label>().ToList().Any(l => l.text == venue.DisplayName));
                Assert.That(card.Q<Button>().enabledSelf, Is.EqualTo(expected));
                Assert.That(string.Join(" ", card.Query<Label>().ToList().Select(x => x.text)), Does.Not.Contain("시즌"));
                purchase.Invoke(controller, new object[] { venue }); purchase.Invoke(controller, new object[] { venue });
                Assert.That(save.Promotion.UnlockedIds.Contains(venue.Id), Is.EqualTo(expected));
                Assert.That(save.Transactions, Has.Count.EqualTo(expected ? 1 : 0));
                Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.EqualTo(cash - (expected ? venue.UnlockCost : 0)));
                Assert.That(PromotionProgressionRules.ActiveTier(save), Is.Zero);
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void QuarterWidgetAndHistory_UseActualValuesWithoutChangingSave()
        {
            var save = WrestlerCareerServiceTests.Create(); PromotionQuarterService.Initialize(save);
            var ctx = WrestlerCareerServiceTests.AddMatch(save); ctx.Show.Status = ShowStatus.Completed;
            ctx.ShowResult.ShowEvaluation.Score = 80; ctx.ShowResult.FinancialSettlement.Attendance = 500;
            PromotionPrestigeRules.Capture(ctx.ShowResult, ScheduledShowType.Regular, 1000);
            save.CurrentDate = new GameDate(2026, 8, 31); new WrestlerCareerService().PrepareDue(save).Apply();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(root);
            var before = JsonUtility.ToJson(save); PromotionQuarterView.Bind(root, save);
            Assert.That(root.Q<Label>("prestige-quarter-estimate").text, Does.Contain("+34"));
            Assert.That(root.Q<Button>("prestige-open-quarter").enabledSelf, Is.True);
            var overlay = PromotionQuarterView.Open(root, save);
            var text = string.Join(" ", overlay.Query<Label>().ToList().Select(x => x.text));
            Assert.That(text, Does.Contain("기본 +10")); Assert.That(text, Does.Contain("현장 흥행 +15"));
            Assert.That(text, Does.Contain("2,500")); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void SeasonConfiguration_OffersOnlyEarnedOperationsAndShowsNewUnlock()
        {
            var save = WrestlerCareerServiceTests.Create(); save.CurrentDate = new GameDate(2027, 5, 31);
            save.Promotion.PromotionPrestige = 995; save.LastCareerQuarterEnd = new(new GameDate(2027, 2, 28));
            var venue = new VenueContractState { Id = Guid.NewGuid().ToString("D"), VenueId = "venue_001", ProductionCost = 250,
                StartDate = new GameDate(2026, 6, 1), EndDate = save.CurrentDate, Status = VenueContractStatus.Reserved }; save.VenueContracts.Add(venue);
            save.SeasonPolicy = new() { Id = Guid.NewGuid().ToString("D"), StartDate = venue.StartDate, EndDate = venue.EndDate,
                RegularVenueContractId = venue.Id, RegularShowFrequency = RegularShowFrequency.Monthly, PpvFrequency = PpvFrequency.Quarterly,
                RegularShowName = "정규 쇼", RegularShowDurationMinutes = 120 };
            PromotionQuarterService.Initialize(save); save.Promotion.UnlockedIds.RemoveAll(x => x.StartsWith("progression.tier."));
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Season configuration test");
            try
            {
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                DashboardSession.SetActiveSave(save, transient: true); var overlay = SeasonTransitionView.Open(document.rootVisualElement);
                Assert.That(overlay.Q<DropdownField>("season-regular-frequency").choices, Does.Contain("주 1회"));
                Assert.That(overlay.Q<DropdownField>("season-regular-frequency").choices.Contains("주 2회"), Is.False);
                Assert.That(overlay.Q<DropdownField>("season-ppv-frequency").choices, Does.Contain("2개월마다"));
                var tier = overlay.Query<DropdownField>().ToList().First(x => x.name.StartsWith("season-ppv-tier-"));
                Assert.That(tier.choices, Does.Contain("대표 PPV")); Assert.That(tier.choices.Contains("대형 PPV"), Is.False);
                var text = string.Join(" ", overlay.Q("season-new-unlocks").Query<Label>().ToList().Select(x => x.text));
                Assert.That(text, Does.Contain("대표 PPV")); Assert.That(text, Does.Not.Contain("아레나"));
                Assert.That(PromotionProgressionRules.ActiveTier(save), Is.Zero);
                var arena = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog").Venues.Single(x => x.Id == "venue_003");
                var venuePicker = overlay.Q<DropdownField>("season-venue");
                Assert.That(venuePicker.choices, Does.Contain(arena.DisplayName));
                venuePicker.value = arena.DisplayName;
                Assert.That(string.Join(" ", overlay.Query<Label>().ToList().Select(x => x.text)), Does.Contain("경기장 등급 해금 비용"));
                Assert.That(save.Transactions, Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(ui); DashboardSession.SetActiveSave(previous, transient); }
        }
    }
}
