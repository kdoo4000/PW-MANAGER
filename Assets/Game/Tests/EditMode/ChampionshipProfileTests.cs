using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PWManager.Tests
{
    public sealed class ChampionshipProfileTests
    {
        private GameSave previous, save;
        private bool transient;
        private GameObject owner;
        private VisualElement root;

        [SetUp]
        public void SetUp()
        {
            previous = DashboardSession.ActiveSave; transient = DashboardSession.IsTransient;
            save = new GameSave { Promotion = new PromotionState { Id = "promotion", Name = "PW" }, CurrentDate = new GameDate(2026, 9, 1) };
            foreach (var (id, year) in new[] { ("a", 1990), ("b", 1980), ("c", 2000) })
            {
                var wrestler = new WrestlerState { Id = id, PromotionId = save.Promotion.Id }; wrestler.Identity.RingName = id; wrestler.Identity.BirthDate = new GameDate(year, 1, 1); save.Wrestlers.Add(wrestler);
            }
            DashboardSession.SetActiveSave(save, true);
            owner = new GameObject("Championship profile tests"); var document = owner.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings"); document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard"); root = document.rootVisualElement;
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(owner); DashboardSession.SetActiveSave(previous, transient); }

        private ChampionshipState Title()
        {
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true);
            title.Reigns.Add(new ChampionshipReignState { HolderId = "a", MemberIds = new() { "a" }, WonDate = new GameDate(2026, 6, 1), LostDate = new(new GameDate(2026, 7, 1)) });
            title.Reigns.Add(new ChampionshipReignState { HolderId = "b", MemberIds = new() { "b" }, WonDate = new GameDate(2026, 7, 1), LostDate = new(new GameDate(2026, 7, 11)) });
            title.Reigns.Add(new ChampionshipReignState { HolderId = "a", MemberIds = new() { "a" }, WonDate = new GameDate(2026, 7, 11), LostDate = new(new GameDate(2026, 8, 10)) });
            title.Reigns.Add(new ChampionshipReignState { HolderId = "c", MemberIds = new() { "c" }, WonDate = new GameDate(2026, 8, 10), Defenses = 2 }); title.HolderId = "c";
            return title;
        }

        [Test]
        public void RecordsUseActualReignsAndKeepTies()
        {
            var title = Title(); var before = JsonUtility.ToJson(title); var records = ChampionshipRecords.Calculate(save, title);
            Assert.That(records.First.HolderId, Is.EqualTo("a")); Assert.That(records.MostWins.Single().Count, Is.EqualTo(2));
            Assert.That(records.Longest, Has.Count.EqualTo(2)); Assert.That(records.Longest.All(r => r.Days == 30), Is.True);
            Assert.That(records.Shortest.Single().Days, Is.EqualTo(10)); Assert.That(records.Oldest.Single().WrestlerId, Is.EqualTo("b")); Assert.That(records.Youngest.Single().WrestlerId, Is.EqualTo("c"));
            Assert.That(JsonUtility.ToJson(title), Is.EqualTo(before));
        }

        [Test]
        public void AgeRecordsUseAgeAtVictoryAndShortestExcludesCurrentReign()
        {
            var title = ChampionshipService.Create(save, "Test", ChampionshipType.Individual);
            title.Reigns.Add(new ChampionshipReignState { HolderId = "a", MemberIds = new() { "a" }, WonDate = new GameDate(2010, 1, 1), LostDate = new(new GameDate(2010, 2, 1)) });
            title.Reigns.Add(new ChampionshipReignState { HolderId = "c", MemberIds = new() { "c" }, WonDate = save.CurrentDate });
            title.HolderId = "c";
            var records = ChampionshipRecords.Calculate(save, title);
            Assert.That(records.Oldest.Single().WrestlerId, Is.EqualTo("c")); Assert.That(records.Youngest.Single().WrestlerId, Is.EqualTo("a"));
            Assert.That(records.Shortest.Single().Days, Is.EqualTo(31));
        }

        [Test]
        public void GalleryOpensSeparateProfileAndReignList()
        {
            var title = Title(); ChampionshipManagementView.Open(root);
            Assert.That(root.Q("championship-gallery"), Is.Not.Null); Assert.That(root.Q("championship-profile"), Is.Null);
            var card = root.Q("championship-select-" + title.Id);
            Assert.That(card.Q<GroupPortraitView>().Query<Button>(className: "group-portrait-member").ToList(), Has.Count.EqualTo(1));
            Click(card.Q<Button>(className: "championship-link"));
            Assert.That(root.Q("championship-gallery"), Is.Null); Assert.That(root.Q("championship-profile"), Is.Not.Null);
            Assert.That(root.Q("championship-records"), Is.Not.Null);
            Click(root.Q<Button>("championship-tab-reigns"));
            Assert.That(root.Query<VisualElement>(className: "cp-reign-row").ToList(), Has.Count.EqualTo(5));
            Assert.That(root.Q("championship-reign-0").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(1));
            Click(root.Q<Button>("championship-back-to-list")); Assert.That(root.Q("championship-gallery"), Is.Not.Null);
        }

        [Test]
        public void EmptyAndLegacyRecordsDoNotInventCreationDatesOrAges()
        {
            var title = ChampionshipService.Create(save, "Empty", ChampionshipType.Individual); title.CreatedDate = default;
            ChampionshipManagementView.OpenProfile(root, title.Id);
            Assert.That(root.Query<Label>().ToList().Count(l => l.text == "기록 없음"), Is.GreaterThanOrEqualTo(7));
            Assert.That(ChampionshipRecords.Calculate(save, title).Youngest, Is.Empty);
        }

        [UnityTest]
        public IEnumerator GalleryAndProfileStayInsideWideAndCompactWindows()
        {
            var title = Title(); ChampionshipService.Create(save, "National", ChampionshipType.Individual);
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                window.ShowUtility();
                foreach (var width in new[] { 1180, 650 })
                {
                    window.position = new Rect(24, 60, width, 760); var host = window.rootVisualElement;
                    ChampionshipManagementView.Open(host);
                    for (var frame = 0; frame < 5; frame++) { window.Repaint(); yield return null; }
                    foreach (var card in host.Query<VisualElement>(className: "cp-card").ToList()) Assert.That(card.worldBound.xMax, Is.LessThanOrEqualTo(host.worldBound.xMax + 1));
                    ChampionshipManagementView.OpenProfile(host, title.Id); Click(host.Q<Button>("championship-tab-reigns"));
                    for (var frame = 0; frame < 5; frame++) { window.Repaint(); yield return null; }
                    foreach (var row in host.Query<VisualElement>(className: "cp-reign-row").ToList()) Assert.That(row.worldBound.xMax, Is.LessThanOrEqualTo(host.worldBound.xMax + 1));
                }
            }
            finally { window.Close(); }
        }

        private static void Click(Button button)
        {
            Assert.That(button, Is.Not.Null); typeof(Clickable).GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button.clickable, new object[] { null });
        }
    }
}
