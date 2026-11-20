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
    public sealed class ManagementVisualLayoutTests
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private GameSave save, previous;
        private bool transient;
        private EditorWindow window;
        private WrestlerState[] members;

        [SetUp] public void SetUp()
        {
            previous = DashboardSession.ActiveSave; transient = DashboardSession.IsTransient;
            save = (GameSave)typeof(OperationsScreenLayoutTests).GetMethod("Sample", Flags).Invoke(null, null);
            save.Shows.Clear(); members = save.Wrestlers.Where(w => w.PromotionId == save.Promotion.Id).ToArray();
            DashboardSession.SetActiveSave(save, true);
            window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility(); window.position = new Rect(24, 60, 1440, 900);
        }
        [TearDown] public void TearDown() { window.Close(); DashboardSession.SetActiveSave(previous, transient); }
        private IEnumerator Layout() { for (var i = 0; i < 8; i++) { window.Repaint(); yield return null; } }
        private static void Inside(Rect inner, Rect outer)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 1)); Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 1));
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 1)); Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 1));
        }
        private VisualElement Dashboard()
        {
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); root.style.flexGrow = 1;
            window.rootVisualElement.Add(root); DashboardViewHost.MountMissingViews(root);
            foreach (var child in root.Q("dashboard-workspace").Children().Where(c => !c.ClassListContains("topbar"))) child.style.display = DisplayStyle.None;
            return root;
        }

        [UnityTest] public IEnumerator CardsKeepTheirSizeAndReignDaysWithoutOverlappingNames()
        {
            var world = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, members[0].Id);
            world.Reigns.Last().WonDate = save.CurrentDate.AddDays(-18);
            ChampionshipService.Create(save, "Intercontinental", ChampionshipType.Individual, holderId: members[2].Id);
            var team = WrestlerGroupService.CreateTagTeam(save, "Black Impact", members.Take(2).Select(w => w.Id));
            members[0].Identity.RingName = "Alexandria Montgomery-Whitmore"; members[1].Identity.RingName = "Christopher Alexander Kensington";
            var tag = ChampionshipService.Create(save, "Tag", ChampionshipType.TagTeam, holderId: team.Id);
            foreach (var width in new[] { 1180, 760 })
            {
                window.position = new Rect(24, 60, width, 780); ChampionshipManagementView.Open(window.rootVisualElement); yield return Layout();
                Assert.That(window.rootVisualElement.Q<Label>("championship-reign-days-" + world.Id).text, Is.EqualTo("재위 18일"));
                foreach (var category in new[] { 0, 1, 2 }) Assert.That(window.rootVisualElement.Q("championship-tier-" + category), Is.Not.Null);
                foreach (var card in window.rootVisualElement.Query<VisualElement>(className: "cp-card").ToList())
                {
                    Assert.That(card.worldBound.width, Is.EqualTo(320).Within(1)); Assert.That(card.worldBound.height, Is.EqualTo(460).Within(1));
                    var names = card.Q(className: "cp-card-champion"); var date = card.Q<Label>(className: "cp-card-date");
                    foreach (var name in names.Query<TextElement>().ToList()) Assert.That(name.worldBound.yMax, Is.LessThanOrEqualTo(date.worldBound.yMin + 1));
                }
                var photo = window.rootVisualElement.Q("championship-select-" + tag.Id).Q<GroupPortraitView>();
                foreach (var child in photo.Children()) Inside(child.worldBound, photo.worldBound);
            }
            window.position = new Rect(24, 60, 1440, 900); ChampionshipManagementView.OpenProfile(window.rootVisualElement, world.Id); yield return Layout();
            var overview = window.rootVisualElement.Q("championship-profile"); var belt = overview.Q(className: "cp-profile-belt"); var portrait = overview.Q(className: "cp-profile-champion").Q<GroupPortraitView>();
            Assert.That(belt.worldBound.size, Is.EqualTo(portrait.worldBound.size));
            Assert.That(overview.Q<Foldout>(), Is.Null); Assert.That(overview.Q("championship-records").worldBound.yMax, Is.LessThanOrEqualTo(overview.worldBound.yMax + 1));
        }

        [UnityTest] public IEnumerator GroupPortraitsFitAndTheStableLeaderIsCenteredInFront()
        {
            var tag = new GroupPortraitView(members.Take(2)); tag.style.width = 320; tag.style.height = 250; window.rootVisualElement.Add(tag);
            var stable = new GroupPortraitView(members, leaderId: members[0].Id); stable.style.width = 700; stable.style.height = 280; window.rootVisualElement.Add(stable);
            yield return Layout();
            Assert.That(tag[0].worldBound.Overlaps(tag[1].worldBound), Is.True);
            foreach (var child in tag.Children()) { Inside(child.worldBound, tag.worldBound); Assert.That(child.worldBound.yMax, Is.EqualTo(tag.worldBound.yMax).Within(1)); }
            foreach (var child in stable.Children()) { Inside(child.worldBound, stable.worldBound); Assert.That(child.worldBound.yMax, Is.EqualTo(stable.worldBound.yMax).Within(1)); }
            var leader = stable.Q<Button>("group-portrait-" + members[0].Id);
            Assert.That(stable[stable.childCount - 1], Is.SameAs(leader));
            Assert.That(leader.worldBound.center.x, Is.EqualTo(stable.worldBound.center.x).Within(1));
            Assert.That(stable.Children().Where(c => c != leader).All(c => c.worldBound.width < leader.worldBound.width), Is.True);
            var near = stable.Q<Button>("group-portrait-" + members[1].Id); var far = stable.Q<Button>("group-portrait-" + members[3].Id);
            Assert.That(far.worldBound.width, Is.LessThan(near.worldBound.width));
            Assert.That(stable.IndexOf(far), Is.LessThan(stable.IndexOf(near)));
        }

        [UnityTest] public IEnumerator ProfileSeparatesStatusFameAndAudienceInsideTheExistingCard()
        {
            var root = Dashboard(); var profileHost = root.Q("wrestler-profile-content"); profileHost.style.display = DisplayStyle.Flex;
            var profile = profileHost.Q("wrestler-profile"); profile.style.display = DisplayStyle.Flex;
            var owner = new GameObject("Profile layout verification"); owner.AddComponent<UIDocument>();
            var instance = typeof(WrestlerProfileController).GetField("instance", Flags); var oldInstance = instance.GetValue(null);
            try
            {
                var controller = owner.AddComponent<WrestlerProfileController>();
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", Flags).Invoke(controller, new object[] { profile });
                members[0].Fame.Value = 123456; typeof(WrestlerProfileController).GetMethod("Bind", Flags).Invoke(controller, new object[] { members[0] });
                foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720) })
                {
                    window.position = new Rect(24, 60, size.x, size.y); yield return Layout();
                    Assert.That(profile.Q<ScrollView>(className: "wp-scroll"), Is.Null);
                    Assert.That(profile.Q(className: "wp-player-panel").Q<GroupPortraitView>(), Is.Null);
                    var card = profile.Q("wp-standing-card"); var summary = card.Q(className: "wp-standing-summary");
                    Assert.That(card.Q(className: "wp-card-title").worldBound.yMax, Is.LessThanOrEqualTo(summary.worldBound.yMin + 1));
                    Assert.That(summary.worldBound.yMax, Is.LessThanOrEqualTo(card.Q(className: "wp-audience-heading").worldBound.yMin + 1));
                    Assert.That(card.Q("wp-fame-tier").worldBound.xMax, Is.LessThanOrEqualTo(card.Q("wp-fame-value").worldBound.xMin + 1));
                    foreach (var row in card.Query<VisualElement>(className: "wp-audience-row").ToList()) Inside(row.worldBound, card.worldBound);
                }
            }
            finally { Object.DestroyImmediate(owner); instance.SetValue(null, oldInstance); }
        }

        [UnityTest] public IEnumerator TrainingUsesExistingGradeColorsInsteadOfOneAccentColor()
        {
            var root = Dashboard(); root.Q("training-content").style.display = DisplayStyle.Flex;
            members[0].Attributes.Power = 20; members[0].Attributes.Charisma = 3;
            var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardTrainingController");
            var controller = System.Activator.CreateInstance(type, new object[] { root }); type.GetMethod("Render").Invoke(controller, new object[] { save });
            yield return Layout();
            var power = root.Q<Label>("ops-ability-Power"); var charisma = root.Q<Label>("ops-ability-Charisma");
            Assert.That(power.ClassListContains("grade-ss"), Is.True); Assert.That(charisma.ClassListContains("grade-g"), Is.True);
            Assert.That(power.resolvedStyle.color, Is.Not.EqualTo(charisma.resolvedStyle.color));
            Assert.That(root.Q<GroupPortraitView>(className: "ops-identity-portrait").worldBound.height, Is.EqualTo(100).Within(1));
        }
    }
}
