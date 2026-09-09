using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace PWManager.Tests
{
    public sealed class WrestlerNameTextTests
    {
        [Test]
        public void Names_LinkFullNamesAndKoreanParticlesWithoutMatchingOtherWords()
        {
            var wrestlers = new[]
            {
                new WrestlerState { Identity = new WrestlerIdentityState { Id = "one", RingName = "Ace", LegalName = "김철수" } },
                new WrestlerState { Identity = new WrestlerIdentityState { Id = "two", RingName = "Ace King" } },
                new WrestlerState { Identity = new WrestlerIdentityState { Id = "three", LegalName = "이영희" } }
            };
            Assert.That(WrestlerNameText.Format("Ace King / Ace · 김철수는 이영희를 상대한다. Aces SpaceAce", wrestlers), Is.EqualTo(
                "<link=\"wrestler:two\"><u>Ace King</u></link> / <link=\"wrestler:one\"><u>Ace</u></link> · <link=\"wrestler:one\"><u>김철수</u></link>는 <link=\"wrestler:three\"><u>이영희</u></link>를 상대한다. Aces SpaceAce"));
            Assert.That(WrestlerNameText.DisplayName(wrestlers[2]), Is.EqualTo("이영희"));
            Assert.That(WrestlerNameText.Format("결과 없음", wrestlers), Is.EqualTo("결과 없음"));
            Assert.That(WrestlerNameText.Format("Ace", new[] { wrestlers[0], new WrestlerState { Identity = new WrestlerIdentityState { Id = "other", RingName = "Ace" } } }), Is.EqualTo("Ace"));
        }

        [Test]
        public void InlineLink_OpensCorrectProfileAndReturnsToUnsavedEditor()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Name Link Test UI");
            try
            {
                var save = ShowTestSaveFactory.Create(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"), 20260903);
                DashboardSession.SetActiveSave(save, transient: true);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var dashboard = ui.AddComponent<DashboardController>();
                var profile = ui.AddComponent<WrestlerProfileController>();
                typeof(DashboardController).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dashboard, null);
                typeof(WrestlerProfileController).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(profile, null);
                var root = document.rootVisualElement;
                var editor = root.Q("show-editor-overlay");
                editor.RemoveFromClassList("hidden");
                var target = save.Wrestlers[1];
                var label = WrestlerNameText.Create(WrestlerNameText.DisplayName(target), save.Wrestlers);
                root.Add(label);
                // Rebinding must not retain an earlier name or duplicate click handlers.
                WrestlerNameText.Set(label, WrestlerNameText.DisplayName(target), save.Wrestlers);
                using (var pointer = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 }))
                using (var click = PointerUpLinkTagEvent.GetPooled(pointer, "wrestler:" + Uri.EscapeDataString(target.Id), WrestlerNameText.DisplayName(target)))
                {
                    click.target = label;
                    label.SendEvent(click);
                }
                Assert.That(root.Q<Label>("wp-name").text, Is.EqualTo(target.Identity.RingName));
                Assert.That(root.Q<Label>("dashboard-page-title").text, Is.EqualTo($"{target.Identity.RingName} / 개요"));
                Assert.That(editor.ClassListContains("hidden"), Is.True);
                Assert.That(root.Q("wrestler-profile-content").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                typeof(DashboardController).GetMethod("NavigateBack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dashboard, null);
                Assert.That(editor.ClassListContains("hidden"), Is.False);

                editor.AddToClassList("hidden");
                var content = new StaticContentRegistry(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"));
                var planning = new ShowPlanningService(content);
                var show = save.Shows[0];
                planning.AddMatch(save, show.Id, new MatchPlanState
                {
                    MatchTypeId = "matchtype_001", MatchGimmickId = "gimmick_000",
                    ParticipantIds = { save.Wrestlers[0].Id, target.Id }, WinnerId = target.Id,
                    LoserTargetId = save.Wrestlers[0].Id, FinishType = MatchFinishType.Pinfall
                }, 15);
                show.DurationLimit = 15;
                planning.Confirm(save, show.Id);
                show.Status = ShowStatus.InProgress;
                var venue = content.Venues[save.VenueContracts[0].VenueId];
                var result = new ShowExecutionService(planning, new MatchEvaluator(content, content, findMove: content.GetMoveRules), new PromoEvaluator())
                    .Execute(save, show.Id, 73, venue.Capacity, venue.BaseTicketPrice);
                using var player = new ShowSimulationPlayer(root, save, result, () => { });
                typeof(DashboardController).GetField("simulationPlayer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dashboard, player);
                WrestlerProfileController.Open(target);
                Assert.That(player.ViewingProfile, Is.True);
                Assert.That(root.Q("simulation-screen").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q("dashboard").ClassListContains("hidden"), Is.False);
                var clock = root.Q<Label>("simulation-clock").text;
                typeof(ShowSimulationPlayer).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
                Assert.That(root.Q<Label>("simulation-clock").text, Is.EqualTo(clock));
                typeof(DashboardController).GetMethod("NavigateBack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dashboard, null);
                Assert.That(player.ViewingProfile, Is.False);
                Assert.That(root.Q("simulation-screen").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                foreach (var behaviour in ui.GetComponents<MonoBehaviour>())
                    behaviour.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(behaviour, null);
                UnityEngine.Object.DestroyImmediate(ui);
                DashboardSession.SetActiveSave(previous, transient);
            }
        }
    }
}
