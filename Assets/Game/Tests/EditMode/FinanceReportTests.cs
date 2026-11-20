using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace PWManager.Tests
{
    public sealed class FinanceReportTests
    {
        [Test]
        public void DashboardNavigation_OpensFinanceAndRestoresSponsorTabAfterBack()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var instanceField = typeof(DashboardController).GetField("instance", flags); var previousInstance = instanceField.GetValue(null);
            var owner = new GameObject("Finance navigation test"); owner.SetActive(false);
            try
            {
                DashboardSession.SetActiveSave(Sample(), true);
                var document = owner.AddComponent<UIDocument>(); document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var controller = owner.AddComponent<DashboardController>(); owner.SetActive(true);
                typeof(DashboardController).GetMethod("OnEnable", flags).Invoke(controller, null);
                var root = document.rootVisualElement;
                typeof(DashboardController).GetMethod("ShowFinancePage", flags).Invoke(controller, new object[] { true });
                var view = (DashboardFinanceView)typeof(DashboardController).GetField("financeView", flags).GetValue(controller);
                view.SelectTab("sponsors");
                Assert.That(root.Q<Button>("nav-finance").ClassListContains("active"), Is.True);
                Assert.That(root.Q("nav-sponsors"), Is.Null);
                typeof(DashboardController).GetMethod("ShowTrainingPage", flags).Invoke(controller, new object[] { true });
                Assert.That(view.Root.style.display.value, Is.EqualTo(DisplayStyle.None));
                typeof(DashboardController).GetMethod("NavigateBack", flags).Invoke(controller, null);
                Assert.That(view.Root.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(view.SelectedTab, Is.EqualTo("sponsors"));
                Assert.That(root.Q<Label>("dashboard-page-title").text, Is.EqualTo("재정"));
                typeof(DashboardController).GetMethod("ShowProfilePage", flags).Invoke(controller, null);
                Assert.That(view.Root.style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                var controller = owner.GetComponent<DashboardController>();
                if (controller != null) typeof(DashboardController).GetMethod("OnDisable", flags).Invoke(controller, null);
                UnityEngine.Object.DestroyImmediate(owner); instanceField.SetValue(null, previousInstance);
                DashboardSession.SetActiveSave(previous, transient);
            }
        }

        private static GameSave Sample()
        {
            var save = SponsorMissionTests.CreateSave(); save.CurrentDate = new GameDate(2026, 7, 15);
            save.Transactions.Clear(); save.Promotion.InitialCash = 100000; return save;
        }
        private static TransactionRecord Entry(GameSave save, string id, GameDate date, long amount, TransactionType type = TransactionType.TicketRevenue) =>
            new() { Id = id, PromotionId = save.Promotion.Id, Date = date, Amount = amount, Type = type };

        [Test]
        public void LedgerAndHistory_ExcludeVoidedForeignAndFutureAndRetainOpeningCash()
        {
            var save = Sample();
            save.Transactions.Add(Entry(save, "june", new GameDate(2026, 6, 10), 2000));
            save.Transactions.Add(Entry(save, "july", new GameDate(2026, 7, 2), -500, TransactionType.VenueCost));
            var foreign = Entry(save, "foreign", save.CurrentDate, 9000); foreign.PromotionId = "other"; save.Transactions.Add(foreign);
            var voided = Entry(save, "void", save.CurrentDate, 9000); voided.IsVoided = true; save.Transactions.Add(voided);
            save.Transactions.Add(Entry(save, "future", save.CurrentDate.AddDays(1), 9000));
            Assert.That(FinanceReportService.Transactions(save).Select(x => x.Id), Is.EqualTo(new[] { "june", "july" }));
            var months = FinanceReportService.History(save);
            Assert.That(months.Count, Is.EqualTo(2)); Assert.That(months[0].Balance, Is.EqualTo(102000));
            Assert.That(months[1].Income, Is.Zero); Assert.That(months[1].Expense, Is.EqualTo(500));
            Assert.That(months[1].Balance, Is.EqualTo(101500));
        }

        [Test]
        public void Projection_IncludesScheduledSalaryAndExpiryAndSubtractsArrearsOnlyOnce()
        {
            var save = Sample(); save.Contracts.Clear(); save.SalaryPayments.Clear();
            save.Contracts.Add(new ContractState { Id = "active", Status = ContractStatus.Active, StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2026, 8, 31), MonthlySalary = 1000 });
            save.Contracts.Add(new ContractState { Id = "scheduled", Status = ContractStatus.Scheduled, StartDate = new GameDate(2026, 9, 1), EndDate = new GameDate(2026, 10, 1), MonthlySalary = 2000 });
            save.Contracts.Add(new ContractState { Id = "terminated", Status = ContractStatus.Terminated, StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 8, 31), MonthlySalary = 9000 });
            save.SalaryPayments.Add(new SalaryPaymentState { RemainingAmount = 300 });
            var before = JsonUtility.ToJson(save); var months = FinanceReportService.SalaryProjection(save);
            Assert.That(months.Count, Is.EqualTo(12)); Assert.That(months.Take(4).Select(x => x.Expense), Is.EqualTo(new long[] { 1000, 2000, 2000, 0 }));
            Assert.That(months[0].Balance, Is.EqualTo(98700)); Assert.That(months[3].Balance, Is.EqualTo(94700));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void AllTabsRenderWithoutMutatingSaveAndSponsorsReuseChosenOffers()
        {
            var save = Sample(); var service = new SponsorMissionService(); var season = service.PrepareOffers(save, SponsorSelectionView.Companies());
            service.ConfirmSelection(save, season, season.Offers.Take(3).Select(x => x.Id));
            var before = JsonUtility.ToJson(save); var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility();
            try
            {
                var view = new DashboardFinanceView(window.rootVisualElement); view.Open(save);
                foreach (var tab in new[] { "summary", "income", "expense", "wages", "projection", "sponsors" })
                { view.SelectTab(tab); Assert.That(view.Root.Q("finance-body").childCount, Is.GreaterThan(0)); }
                Assert.That(view.Root.Query<VisualElement>(className: "finance-sponsor-column").ToList().Count, Is.EqualTo(3));
                Assert.That(view.Root.Q("finance-sponsor-" + season.Offers.First(x => !x.Selected).CompanyId), Is.Null);
                Assert.That(view.Root.Query<ProgressBar>().ToList().Count, Is.EqualTo(season.Offers.Where(x => x.Selected).Sum(x => x.Goals.Count)));
                Assert.That(view.Root.Q("sponsor-detail"), Is.Null);
                Assert.That(view.Root.Q("sponsor-close"), Is.Null);
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            }
            finally { window.Close(); }
        }

        [Test]
        public void IncomeCategoryAndPeriodControlsFilterTheRealLedger()
        {
            var save = Sample(); save.Transactions.Add(Entry(save, "a", new GameDate(2026, 6, 8), 210));
            save.Transactions.Add(Entry(save, "b", new GameDate(2026, 7, 8), 340, TransactionType.SponsorRevenue));
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility();
            try
            {
                var view = new DashboardFinanceView(window.rootVisualElement); view.Open(save); view.SelectTab("income");
                var button = view.Root.Q<Button>("finance-category-TicketRevenue");
                typeof(Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
                Assert.That(view.Root.Query<Label>().ToList().Any(x => x.text == "티켓 거래 기록"), Is.True);
                view.Root.Q<DropdownField>("finance-ledger-period").value = "이번 달";
                Assert.That(view.Root.Query<Label>().ToList().Any(x => x.text == "선택한 기간에 거래가 없습니다."), Is.True);
            }
            finally { window.Close(); }
        }
    }
}
