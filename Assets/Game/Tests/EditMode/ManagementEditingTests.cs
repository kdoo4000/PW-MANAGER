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

namespace PWManager.Tests
{
    public sealed class ManagementEditingTests
    {
        private GameSave previous, save;
        private bool transient;
        private VisualElement host;
        private GameObject owner;

        [SetUp]
        public void SetUp()
        {
            previous = DashboardSession.ActiveSave; transient = DashboardSession.IsTransient;
            save = new GameSave { Promotion = new PromotionState { Id = "promotion", Name = "PW" }, CurrentDate = new GameDate(2026, 6, 1) };
            foreach (var id in new[] { "a", "b", "c", "d" })
            {
                var w = new WrestlerState { Id = id, PromotionId = "promotion" }; w.Identity.RingName = id; save.Wrestlers.Add(w);
            }
            DashboardSession.SetActiveSave(save, true);
            owner = new GameObject("Management UI Tests");
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
            host = document.rootVisualElement;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(owner); DashboardSession.SetActiveSave(previous, transient);
        }

        [Test]
        public void EmptyGroupMenus_ShowList_AndOnlyOpenCreationAfterNewIsClicked()
        {
            StoryManagementView.OpenGroups(host);
            Assert.That(host.Q<ScrollView>("group-list"), Is.Not.Null);
            Assert.That(host.Q<TextField>("group-name"), Is.Null);
            foreach (var tab in new[] { "groups-tab-teams", "groups-tab-stables" })
            {
                Click(tab);
                Assert.That(host.Q<TextField>("group-name"), Is.Null);
                Click("group-new");
                Assert.That(host.Q<TextField>("group-name"), Is.Not.Null);
                Click("group-cancel");
                Assert.That(host.Q<TextField>("group-name"), Is.Null);
                Assert.That(host.Q<ScrollView>("group-list"), Is.Not.Null);
            }
            Assert.That(save.TagTeams, Is.Empty);
            Assert.That(save.Stables, Is.Empty);
        }

        [Test]
        public void ReopeningGroups_FromCreation_ReturnsToList()
        {
            StoryManagementView.OpenGroups(host); Click("group-new");
            host.Q<TextField>("group-name").value = "Draft";
            StoryManagementView.OpenGroups(host);
            Assert.That(host.Q<TextField>("group-name"), Is.Null);
            Assert.That(host.Q<ScrollView>("group-list"), Is.Not.Null);
        }

        [Test]
        public void StoryBrowser_KeepsListOnLeftWhileSelectionUpdatesRightDetail()
        {
            var first = new StorylineState { Id = "first", PromotionId = save.Promotion.Id, Name = "First story",
                FirstWrestlerId = "a", SecondWrestlerId = "b", Context = "First context" };
            var second = new StorylineState { Id = "second", PromotionId = save.Promotion.Id, Name = "Second story",
                FirstWrestlerId = "c", SecondWrestlerId = "d", Context = "Second context" };
            save.Storylines.Add(first); save.Storylines.Add(second);
            StoryManagementView.Open(host);
            var list = host.Q<ScrollView>("story-list");
            var detail = host.Q<ScrollView>("story-detail");
            Assert.That(list.Q("story-select-first"), Is.Not.Null);
            Assert.That(list.Q("story-select-second"), Is.Not.Null);
            Assert.That(list.Query<VisualElement>(className: "story-poster").ToList(), Is.Empty);
            Assert.That(detail.Query<VisualElement>(className: "story-poster").ToList(), Has.Count.EqualTo(1));
            Assert.That(list.Q("story-select-first").Q<Label>(className: "story-list-name").text, Is.EqualTo(WrestlerNameText.Format("a vs b", save.Wrestlers)));
            Assert.That(detail.Q<Label>(className: "story-poster-title").text, Is.EqualTo(WrestlerNameText.Format("a vs b", save.Wrestlers)));
            Assert.That(detail.Query<Label>().ToList().Any(x => x.text == "대립의 시작"), Is.True);
            Assert.That(detail.Query<Label>().ToList().Any(x => x.text == "촉발 사건 기록이 없습니다."), Is.True);
            Assert.That(detail.Query<Label>().ToList().Any(x => x.text.Contains("First context")), Is.False);
            Click("story-select-second");
            Assert.That(host.Q<ScrollView>("story-list"), Is.SameAs(list));
            Assert.That(detail.Q<Label>(className: "story-poster-title").text, Is.EqualTo(WrestlerNameText.Format("c vs d", save.Wrestlers)));
            Assert.That(detail.Query<Label>().ToList().Any(x => x.text.Contains("Second context")), Is.False);
            Assert.That(detail.Query<Label>().ToList().Any(x => x.text.Contains("First context")), Is.False);
            Assert.That(list.Q("story-select-second").ClassListContains("story-selected"), Is.True);
        }

        [Test]
        public void ChampionshipRename_RetainsDraftAfterDuplicateAndCanRetryWithoutLosingHistory()
        {
            var title = ChampionshipService.Create(save, "One", ChampionshipType.Individual, false, "a");
            var other = ChampionshipService.Create(save, "Two", ChampionshipType.Individual, false);
            var id = title.Id; var prestige = title.Prestige;
            ChampionshipManagementView.OpenProfile(host, title.Id);
            Click("championship-edit");
            host.Q<TextField>("championship-name").value = other.Name;
            Click("championship-save");
            Assert.That(host.Q<Label>("championship-error").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(host.Q<TextField>("championship-name").value, Is.EqualTo(other.Name));
            host.Q<TextField>("championship-name").value = "Revised Championship";
            host.Q<TextField>("championship-holder-search").value = "b";
            host.Q<Toggle>("championship-holder-b").value = true;
            host.Q<TextField>("championship-holder-search").value = "";
            Assert.That(host.Q<Toggle>("championship-holder-b").value, Is.True);
            Click("championship-save");
            var current = save.Championships.Single(t => t.Id == id);
            Assert.That(current.Name, Is.EqualTo("Revised Championship"));
            Assert.That(current.HolderId, Is.EqualTo("b"));
            Assert.That(current.Reigns, Has.Count.EqualTo(2));
            Assert.That(current.Reigns[0].LostDate.HasValue, Is.True);
            Assert.That(current.Prestige, Is.EqualTo(prestige));
            Assert.That(host.Q<Button>("championship-new"), Is.Not.Null);
        }

        [Test]
        public void TagTeamEdit_KeepsSearchSelectionAndRequiresExplicitDisbandConfirmation()
        {
            var team = WrestlerGroupService.CreateTagTeam(save, "Team", new[] { "a", "b" });
            WrestlerGroupsView.Open(host); Click("group-edit");
            host.Q<TextField>("group-name").value = "New Team";
            Click("group-remove-b");
            host.Q<TextField>("group-member-search").value = "c";
            host.Q<Toggle>("group-member-c").value = true;
            host.Q<TextField>("group-member-search").value = "";
            Assert.That(host.Q("group-selected-members").Q("management-wrestler-a"), Is.Not.Null);
            Assert.That(host.Q("group-selected-members").Q("management-wrestler-c"), Is.Not.Null);
            Assert.That(host.Q<Toggle>("group-member-a"), Is.Null);
            Click("group-save");
            Assert.That(team.Name, Is.EqualTo("New Team"));
            Assert.That(team.MemberIds, Is.EquivalentTo(new[] { "a", "c" }));
            Assert.That(save.Wrestlers.Single(w => w.Id == "b").Roster.ActiveTagTeamId, Is.Null);
            Click("group-disband");
            Assert.That(team.Status, Is.EqualTo(TagTeamStatus.Active));
            Click("group-disband-cancel");
            Assert.That(team.Status, Is.EqualTo(TagTeamStatus.Active));
            Click("group-disband"); Click("group-disband-confirm");
            Assert.That(team.Status, Is.EqualTo(TagTeamStatus.Disbanded));
            Assert.That(save.Wrestlers.All(w => w.Roster.ActiveTagTeamId == null), Is.True);
        }

        [Test]
        public void StableCreation_OnlyAllowsSelectedLeaderAndCanEditMembership()
        {
            WrestlerGroupsView.Open(host); Click("groups-tab-stables"); Click("group-new");
            host.Q<TextField>("group-name").value = "Stable";
            foreach (var id in new[] { "a", "b", "c" }) host.Q<Toggle>("group-member-" + id).value = true;
            Assert.That(host.Q<Button>("group-leader-d"), Is.Null);
            Click("group-leader-b");
            Click("group-save");
            var stable = save.Stables.Single();
            Assert.That(stable.LeaderId, Is.EqualTo("b"));
            Click("group-edit"); Click("group-remove-b");
            host.Q<Toggle>("group-member-d").value = true;
            host.Q<TextField>("group-name").value = "Edited Stable";
            Click("group-save");
            Assert.That(stable.Name, Is.EqualTo("Edited Stable"));
            Assert.That(stable.MemberIds, Is.EquivalentTo(new[] { "a", "c", "d" }));
            Assert.That(stable.MemberIds, Does.Contain(stable.LeaderId));
            Assert.That(host.Q<ScrollView>("group-detail").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(3));
        }

        [Test]
        public void StableValidationFailure_RetainsDraftAndCanRetry()
        {
            WrestlerGroupService.CreateTagTeam(save, "Team", new[] { "a", "b" });
            WrestlerGroupsView.Open(host); Click("groups-tab-stables"); Click("group-new");
            host.Q<TextField>("group-name").value = "Draft";
            foreach (var id in new[] { "a", "c", "d" }) host.Q<Toggle>("group-member-" + id).value = true;
            Click("group-save");
            Assert.That(save.Stables, Is.Empty);
            Assert.That(host.Q<TextField>("group-name").value, Is.EqualTo("Draft"));
            Assert.That(host.Q<Label>("story-error").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            host.Q<Toggle>("group-member-b").value = true;
            Click("group-save");
            Assert.That(save.Stables.Single().Name, Is.EqualTo("Draft"));
            Assert.That(save.Stables[0].MemberIds, Has.Count.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator CreationForms_KeepSaveVisibleAtWideAndCompactSizes()
        {
            foreach (var size in new[] { new Vector2(1180, 760), new Vector2(760, 640) })
            foreach (var championship in new[] { true, false })
            {
                var window = ScriptableObject.CreateInstance<EditorWindow>();
                try
                {
                    window.ShowUtility(); window.position = new Rect(24, 60, size.x, size.y);
                    host = window.rootVisualElement;
                    if (championship) ChampionshipManagementView.Open(host);
                    else { WrestlerGroupsView.Open(host); Click("group-new"); }
                    for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                    Assert.That(window.position.width, Is.EqualTo(size.x).Within(1));
                    Assert.That(window.position.height, Is.EqualTo(size.y).Within(1));
                    var root = host.Q(championship ? "championship-management" : "story-management");
                    var detail = host.Q<ScrollView>(championship ? "championship-detail" : "group-detail");
                    var apply = host.Q<Button>(championship ? "championship-create" : "group-save");
                    Assert.That(root.ClassListContains("ops-narrow"), Is.EqualTo(size.x < 900));
                    Assert.That(detail.worldBound.height, Is.GreaterThan(80));
                    Assert.That(apply.worldBound.height, Is.GreaterThanOrEqualTo(30));
                    Assert.That(apply.worldBound.yMin, Is.GreaterThanOrEqualTo(detail.worldBound.yMax - 1));
                    Assert.That(apply.worldBound.yMax, Is.LessThanOrEqualTo(root.worldBound.yMax + 1));
                    Assert.That(apply.worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax + 1));
                }
                finally { window.Close(); }
            }
        }

        private void Click(string id)
        {
            var button = host.Q<Button>(id); Assert.That(button, Is.Not.Null, id);
            Assert.That(button.enabledInHierarchy, Is.True, id);
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
        }
    }
}
