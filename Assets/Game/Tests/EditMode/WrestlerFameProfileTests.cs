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
    public sealed class WrestlerFameProfileTests
    {
        [Test]
        public void Profile_DisplaysFameTierWithoutNumericValueAndReadOnlyHistory()
        {
            var save = WrestlerCareerServiceTests.Create(); var ctx = WrestlerCareerServiceTests.AddMatch(save); var w = save.Wrestlers[0];
            ctx.ShowResult.HasFameSnapshot = true; ctx.ShowResult.FameReach = 10000; save.MatchResults[0].ActualMatchDuration = 10;
            w.Fame = WrestlerFameRules.Create(1000); w.Presentation.WrestlingStyleId = "style_001";
            new WrestlerCareerService().PrepareShow(ctx).Apply();
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Fame profile test"); go.SetActive(false); var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            try
            {
                DashboardSession.SetActiveSave(save, transient: true);
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { root });
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { w });
                Assert.That(root.Q<Label>("wp-fame-tier"), Is.Not.Null);
                Assert.That(root.Q<Label>("wp-fame-tier").text, Is.EqualTo("유명 선수"));
                Assert.That(root.Q<Label>("wp-fame-value"), Is.Null);
                Assert.That(root.Q<Label>("wp-card-status").text, Is.Not.EqualTo("유명 선수"));
                var before = JsonUtility.ToJson(save); var overlay = WrestlerCareerView.Open(root, w.Id);
                var text = string.Join(" ", overlay.Query<Label>().ToList().Select(x => x.text));
                Assert.That(text, Does.Contain("명성")); Assert.That(text, Does.Contain("1,010")); Assert.That(text, Does.Contain("활약"));
                Assert.That(overlay.Query<Button>(className: "wrestler-link").ToList(), Is.Not.Empty);
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }
    }
}
