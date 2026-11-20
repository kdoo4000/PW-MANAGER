using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

namespace PWManager.Tests
{
    public sealed class ContractNegotiationViewTests
    {
        public sealed class ContractFocusWindow : UnityEditor.EditorWindow { }

        [UnityTest]
        public IEnumerator TabNavigation_SkipsHiddenConfirmationAndReachesClose()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var window = ScriptableObject.CreateInstance<ContractFocusWindow>();
            try
            {
                DashboardSession.SetActiveSave(CreateReleaseSave(), transient: true);
                window.Show();
                var overlay = ContractNegotiationView.Open(window.rootVisualElement, DashboardSession.ActiveSave.Wrestlers[0].Id, ContractType.Wrestler);
                yield return null;
                var mutual = overlay.Q<Button>("contract-mutual-release"); mutual.Focus();
                Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(mutual));
                var hidden = overlay.Q("contract-termination-confirmation-area");
                for (var i = 0; i < 30 && window.rootVisualElement.focusController.focusedElement != overlay.Q<Button>("contract-close"); i++)
                {
                    var focused = window.rootVisualElement.focusController.focusedElement as VisualElement;
                    Assert.That(focused, Is.Not.Null);
                    using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab })) focused.SendEvent(key);
                    var next = window.rootVisualElement.focusController.focusedElement as VisualElement;
                    Assert.That(next, Is.Not.SameAs(focused));
                    Assert.That(hidden.Contains(next), Is.False);
                }
                Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(overlay.Q<Button>("contract-close")));
            }
            finally { window.Close(); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void Release_ShowsFixedAmountsAndRequiresConfirmationBeforePaying()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var save = CreateReleaseSave();
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var host = new VisualElement(); var overlay = ContractNegotiationView.Open(host, save.Wrestlers[0].Id, ContractType.Wrestler);
                var amounts = overlay.Q<Label>("contract-termination-amounts");
                Assert.That(amounts, Is.Not.Null);
                Assert.That(amounts.text, Does.Contain(32600L.ToString("N0")).And.Contain(22820L.ToString("N0")));
                Click(overlay.Q<Button>("contract-release"));
                Assert.That(save.Transactions, Is.Empty);
                Assert.That(overlay.Q<Label>("contract-termination-confirmation").text, Does.Contain(32600L.ToString("N0")));
                Click(overlay.Q<Button>("contract-termination-confirm"));
                Assert.That(save.Transactions, Has.Count.EqualTo(1));
                Assert.That(save.Wrestlers[0].Roster.ActivityState, Is.EqualTo(RosterActivityState.Released));
                Assert.That(overlay.Q<Button>("contract-submit").enabledSelf, Is.False);
                Click(overlay.Q<Button>("contract-termination-confirm"));
                Assert.That(save.Transactions, Has.Count.EqualTo(1));
                var reopened = ContractNegotiationView.Open(host, save.Wrestlers[0].Id, ContractType.Wrestler);
                Assert.That(reopened.Q<Label>("contract-result").text, Does.Contain("해지"));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void Release_InsufficientCashDisablesBothActionsWithReason()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var save = CreateReleaseSave(); save.Promotion.InitialCash = 1;
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var overlay = ContractNegotiationView.Open(new VisualElement(), save.Wrestlers[0].Id, ContractType.Wrestler);
                Assert.That(overlay.Q<Button>("contract-release"), Is.Not.Null);
                Assert.That(overlay.Q<Button>("contract-release").enabledSelf, Is.False);
                Assert.That(overlay.Q<Button>("contract-mutual-release").enabledSelf, Is.False);
                Assert.That(overlay.Q<Label>("contract-termination-status").text, Does.Contain("자금"));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button.clickable, new object[] { null });

        [Test]
        public void OldTermination_DoesNotPreventOfferingANewContract()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var save = CreateReleaseSave(); new ContractTerminationService().Submit(save, save.Wrestlers[0].Id, ContractType.Wrestler, false);
            save.CurrentDate = save.CurrentDate.AddDays(1);
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var overlay = ContractNegotiationView.Open(new VisualElement(), save.Wrestlers[0].Id, ContractType.Wrestler);
                Assert.That(overlay.Q<Button>("contract-submit").enabledSelf, Is.True);
                Click(overlay.Q<Button>("contract-submit"));
                Assert.That(save.ContractNegotiations, Has.Count.EqualTo(1));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void MutualRejection_StillAllowsUnilateralConfirmationInSameDialog()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var save = CreateReleaseSave();
            for (var seed = 0; seed < 1000; seed++)
            {
                save.WorldSeed = seed;
                var copy = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
                if (!new ContractTerminationService().Submit(copy, copy.Wrestlers[0].Id, ContractType.Wrestler, true).Accepted) break;
            }
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var overlay = ContractNegotiationView.Open(new VisualElement(), save.Wrestlers[0].Id, ContractType.Wrestler);
                Click(overlay.Q<Button>("contract-mutual-release")); Click(overlay.Q<Button>("contract-termination-confirm"));
                Assert.That(save.ContractTerminations[0].Accepted, Is.False);
                Click(overlay.Q<Button>("contract-release"));
                Assert.That(overlay.Q<Button>("contract-termination-confirm").enabledSelf, Is.True);
                Click(overlay.Q<Button>("contract-termination-confirm"));
                Assert.That(save.Transactions, Has.Count.EqualTo(1));
                Assert.That(save.Wrestlers[0].Roster.ActivityState, Is.EqualTo(RosterActivityState.Released));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }
        private static GameSave CreateReleaseSave()
        {
            var save = GameSave.CreateNew(12, DateTime.UtcNow); save.CurrentDate = new GameDate(2026, 7, 15);
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString("D"), Name = "Test", InitialCash = 1000000 };
            var wrestler = new WrestlerState { Identity = new WrestlerIdentityState { Id = Guid.NewGuid().ToString("D"), RingName = "해지 선수" },
                PromotionId = save.Promotion.Id }; save.Wrestlers.Add(wrestler);
            save.Contracts.Add(new ContractState { Id = Guid.NewGuid().ToString("D"), PersonId = wrestler.Id,
                StartDate = new GameDate(2026, 6, 1), EndDate = new GameDate(2027, 5, 31), MonthlySalary = 3100, Status = ContractStatus.Active });
            return save;
        }

        [Test]
        public void SubmitAndReopen_UsesRealResultAndDisablesRepeatedNegotiation()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var save = GameSave.CreateNew(12, DateTime.UtcNow); save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString("D"), Name = "Test", InitialCash = 100000 };
            var wrestler = new WrestlerState { Identity = new WrestlerIdentityState { Id = Guid.NewGuid().ToString("D"), RingName = "협상 선수" },
                Condition = new WrestlerConditionState { Satisfaction = 50 } }; save.Wrestlers.Add(wrestler);
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                var host = new VisualElement(); var overlay = ContractNegotiationView.Open(host, wrestler.Id, ContractType.Wrestler);
                Assert.That(overlay.Q<Label>("contract-chance").text, Does.Contain("70%"));
                var button = overlay.Q<Button>("contract-submit");
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
                Assert.That(save.ContractNegotiations, Has.Count.EqualTo(1));
                Assert.That(button.enabledSelf, Is.False);
                var reopened = ContractNegotiationView.Open(host, wrestler.Id, ContractType.Wrestler);
                Assert.That(reopened.Q<Button>("contract-submit").enabledSelf, Is.False);
                Assert.That(reopened.Q<Label>("contract-result").text, Is.Not.Empty);
                Assert.That(host.Query<VisualElement>("contract-negotiation-overlay").ToList(), Has.Count.EqualTo(1));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void ProfileContractTab_IsAnActionAndRunningShowCannotOpenNegotiation()
        {
            var tree = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            Assert.That(tree.Q<Button>("wp-open-contract"), Is.Not.Null);
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var save = new GameSave { Promotion = new PromotionState() }; save.Shows.Add(new ShowState { Status = ShowStatus.InProgress });
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                Assert.That(ContractNegotiationView.Open(new VisualElement(), "not-found", ContractType.Wrestler), Is.Null);
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }
    }
}
