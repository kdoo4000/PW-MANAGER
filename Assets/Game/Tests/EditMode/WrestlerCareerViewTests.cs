using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class WrestlerCareerViewTests
    {
        [Test]
        public void ProfileButton_OpensLinkedReadOnlyHistoryWithoutFailureNumbers()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = WrestlerCareerServiceTests.AddMatch(save); var w = save.Wrestlers[0];
            w.Presentation.WrestlingStyleId = "style_001"; w.Momentum.Momentum = 60;
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Career profile test"); go.SetActive(false); var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { root });
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { w });
                var button = root.Q<Button>("wp-open-career"); Assert.That(button, Is.Not.Null);
                var before = JsonUtility.ToJson(save); Click(button); var overlay = root.Q("career-history-overlay"); Assert.That(overlay, Is.Not.Null);
                Assert.That(overlay.Query<Button>(className: "wrestler-link").ToList(), Is.Not.Empty);
                var text = string.Join(" ", overlay.Query<Label>().ToList().Select(x => x.text));
                Assert.That(text, Does.Contain("완전 해소")); Assert.That(text, Does.Not.Contain("실패 누적")); Assert.That(text, Does.Not.Contain("3.0"));
                Click(overlay.Q<Button>("career-history-close")); Assert.That(root.Q("career-history-overlay"), Is.Null);
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void PlanningAndProduction_ShowLinkedRiskWithoutExposingFailure()
        {
            var save = WrestlerCareerServiceTests.Create(); WrestlerCareerServiceTests.AddMatch(save); var w = save.Wrestlers[0];
            w.Momentum.Momentum = 60; w.Momentum.MomentumFailure = 2; w.Identity.RingName = "Risk wrestler";
            var show = save.Shows[0]; show.Status = ShowStatus.Draft;
            var match = save.MatchPlans[0]; match.MatchTypeId = "matchtype_001"; match.MatchGimmickId = "gimmick_000";
            match.MembersPerTeam = 1; match.TeamCount = 2; match.WinningSideId = "right"; match.FinishPerformerId = save.Wrestlers[1].Id; match.LoserTargetId = w.Id;
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(root);
            var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.ShowPlanningController");
            var controller = Activator.CreateInstance(type, root);
            void Set(string key, object value) => type.GetField(key, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value);
            Set("save", save); Set("selectedShowId", show.Id); Set("selectedShowEventId", "event");
            type.GetMethod("RenderShowPlanning", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            var warning = root.Q("show-momentum-risks"); Assert.That(warning, Is.Not.Null);
            Assert.That(warning.Query<Button>(className: "wrestler-link").ToList(), Is.Not.Empty);
            Assert.That(string.Join(" ", warning.Query<Label>().ToList().Select(x => x.text)), Does.Not.Contain("2"));
            var section = Enum.Parse(type.GetNestedType("ShowEditSection", BindingFlags.NonPublic), "Producing");
            type.GetMethod("OpenSelectedEventEditor", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new[] { section });
            Assert.That(root.Q("show-editor-momentum-risks").Query<Button>(className: "wrestler-link").ToList(), Is.Not.Empty);
        }

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
    }
}
