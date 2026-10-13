using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class UiAccessibilityTests
    {
        [UnityTest]
        public IEnumerator InlineLinks_TabSelectsEachNameAndRebindingRemovesOldLinks()
        {
            var owner = new GameObject("Inline Keyboard Test");
            var previousWindow = UnityEditor.EditorWindow.focusedWindow;
            try
            {
                UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                var root = document.rootVisualElement;
                var wrestlers = new[]
                {
                    new WrestlerState { Identity = new WrestlerIdentityState { Id = "one", RingName = "Ace" } },
                    new WrestlerState { Identity = new WrestlerIdentityState { Id = "two", RingName = "King" } }
                };
                var label = WrestlerNameText.Create("Ace vs King", wrestlers);
                root.Add(label);
                var next = new Button { text = "Next" };
                root.Add(next);
                for (int frame = 0; frame < 10 && float.IsNaN(next.layout.width); frame++)
                {
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditor.EditorWindow.focusedWindow?.Repaint();
                    yield return null;
                }
                Assert.That(float.IsNaN(next.layout.width), Is.False, "Keyboard navigation requires a completed panel layout.");
                label.Focus();
                Assert.That(label.text, Does.Contain("<color=#090a0c><u>Ace</u>"));
                Press(label, KeyCode.Tab);
                Assert.That(label.text, Does.Contain("<color=#090a0c><u>King</u>"));
                Assert.That(label.focusController.focusedElement, Is.SameAs(label));
                Press(label, KeyCode.Tab, EventModifiers.Shift);
                Assert.That(label.text, Does.Contain("<color=#090a0c><u>Ace</u>"));
                Press(label, KeyCode.Tab);
                Press(label, KeyCode.Tab);
                Assert.That(label.focusController.focusedElement, Is.SameAs(next));
                Assert.That(label.text, Does.Not.Contain("<mark="));
                Press(next, KeyCode.Tab, EventModifiers.Shift);
                Assert.That(label.focusController.focusedElement, Is.SameAs(label));
                Assert.That(label.text, Does.Contain("<color=#090a0c><u>King</u>"));
                WrestlerNameText.Set(label, "Ace", wrestlers);
                Assert.That(label.text, Does.Not.Contain("King"));
                WrestlerNameText.Set(label, "결과 없음", wrestlers);
                Assert.That(label.focusable, Is.False);
                Assert.That(label.tabIndex, Is.EqualTo(-1));
                Assert.That(label.text, Is.EqualTo("결과 없음"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                previousWindow?.Focus();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SaveDialog_TabStaysInsideAndEscapeRestoresOpener(bool dashboard)
        {
            var owner = new GameObject("Save Keyboard Test");
            var controllerOwner = new GameObject("Save Controller Test");
            controllerOwner.SetActive(false);
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>(dashboard ? "PWManagerUI/Dashboard" : "PWManagerUI/TitleScreen");
                var root = document.rootVisualElement;
                var overlay = root.Q<VisualElement>(dashboard ? "dashboard-save-load-overlay" : "title-load-overlay");
                var rows = root.Q<VisualElement>(dashboard ? "dashboard-save-load-rows" : "title-load-rows");
                var opener = root.Q<Button>(dashboard ? "nav-settings" : "load-game");
                var close = root.Q<Button>(dashboard ? "dashboard-save-load-close" : "title-load-close");
                var row = new Button { text = "test_save" };
                rows.Add(row);
                var type = dashboard ? typeof(DashboardController) : typeof(TitleScreenController);
                var controller = controllerOwner.AddComponent(type);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetField(dashboard ? "saveLoadOverlay" : "loadOverlay", flags).SetValue(controller, overlay);
                opener.Focus();
                type.GetField(dashboard ? "saveLoadReturnFocus" : "loadReturnFocus", flags).SetValue(controller, opener);
                var callback = (EventCallback<KeyDownEvent>)Delegate.CreateDelegate(typeof(EventCallback<KeyDownEvent>), controller,
                    type.GetMethod(dashboard ? "OnSaveLoadKeyDown" : "OnLoadMenuKeyDown", flags));
                overlay.RegisterCallback(callback, TrickleDown.TrickleDown);
                var navigation = (EventCallback<NavigationMoveEvent>)Delegate.CreateDelegate(typeof(EventCallback<NavigationMoveEvent>), controller,
                    type.GetMethod(dashboard ? "OnSaveLoadNavigation" : "OnLoadMenuNavigation", flags));
                overlay.RegisterCallback(navigation, TrickleDown.TrickleDown);
                overlay.RemoveFromClassList("hidden");
                row.Focus();
                Press(row, KeyCode.Tab);
                Assert.That(root.focusController.focusedElement, Is.SameAs(close));
                Press(close, KeyCode.Tab, EventModifiers.Shift);
                Assert.That(root.focusController.focusedElement, Is.SameAs(row));
                Press(row, KeyCode.Escape);
                Assert.That(overlay.ClassListContains("hidden"), Is.True);
                Assert.That(root.focusController.focusedElement, Is.SameAs(opener));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerOwner);
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static void Press(VisualElement element, KeyCode key, EventModifiers modifiers = EventModifiers.None)
        {
            using var evt = KeyDownEvent.GetPooled(key == KeyCode.Tab ? '\t' : '\0', key, modifiers);
            evt.target = element;
            element.SendEvent(evt);
            if (key != KeyCode.Tab) return;
            using var navigation = NavigationMoveEvent.GetPooled((modifiers & EventModifiers.Shift) != 0
                ? NavigationMoveEvent.Direction.Previous : NavigationMoveEvent.Direction.Next, modifiers);
            navigation.target = element;
            element.SendEvent(navigation);
        }
    }
}
