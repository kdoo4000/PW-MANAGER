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
    public sealed class StoryManagementViewTests
    {
        [Test]
        public void StoryReactionTrend_IncludesConfirmedNeglectAndIgnoresPreview()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                var save = ShowExecutionServiceTests.CreateSave();
                var story = new StorylineService().Create(save, "이야기", "a", "b");
                story.History.Add(new StoryEventState { SourceResultId = "old", Incident = StoryIncident.Challenge,
                    EventType = StoryEventType.Promo, ActorId = "a", TargetId = "b", Date = save.CurrentDate, InterestChange = 6 });
                var result = new ShowResultState { Id = "preview", ShowId = "show", ShowVersion = 1 };
                result.StoryChanges.Add(new ResultChangeState { TargetId = story.Id, ValueKey = "관심도", Amount = -5,
                    Reason = "쇼에서 스토리 전개 없음" });
                save.ShowResults.Add(result); DashboardSession.SetActiveSave(save, true);
                var host = new VisualElement(); StoryManagementView.Open(host);
                Assert.That(host.Query<Label>(className: "story-reaction-label").ToList().Single().text, Does.Contain("+6"));
                story.Interest = 20; result.StoryHistoryApplied = true;
                StoryManagementView.Open(host);
                Assert.That(host.Query<Label>(className: "story-reaction-label").ToList().Single().text,
                    Does.Contain("관중 반응 20").And.Contain("-5"));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        [Test]
        public void GroupCreationAndHistoryBrowser_WorkWithoutStoryEntry()
        {
            var previous = DashboardSession.ActiveSave;
            var wasTransient = DashboardSession.IsTransient;
            var owner = new GameObject("Group UI Test");
            try
            {
                var save = ShowExecutionServiceTests.CreateSave();
                save.Shows.Clear();
                foreach (var wrestler in save.Wrestlers) wrestler.PromotionId = save.Promotion.Id;
                save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -35 });
                save.StoryCandidates.Add(new StoryCandidateState
                {
                    Id = "candidate", Quality = 70,
                    Event = new StoryEventState { SourceResultId = "promo-result", EventType = StoryEventType.Promo,
                        Incident = StoryIncident.Betrayal, ActorId = "a", TargetId = "b", Date = save.CurrentDate }
                });
                DashboardSession.SetActiveSave(save, true);
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var host = document.rootVisualElement;
                StoryManagementView.OpenGroups(host);
                Click(host.Q<Button>("group-new"));
                Assert.That(host.Query<Label>(className: "story-heading").ToList().Any(x => x.text.StartsWith("스토리 후보")), Is.False);
                host.Q<TextField>("group-name").value = "새 태그팀";
                host.Q<Toggle>("group-member-a").value = true;
                host.Q<Toggle>("group-member-b").value = true;
                Click(host.Q<Button>("group-save"));
                Assert.That(host.Q<Label>("story-error").text, Is.Empty);
                Assert.That(save.TagTeams.Single().Name, Is.EqualTo("새 태그팀"));
                Assert.That(save.Wrestlers.All(x => x.Roster.ActiveTagTeamId == save.TagTeams[0].Id), Is.True);

                StoryManagementView.Open(host);
                Assert.That(host.Query<Label>(className: "story-heading").ToList().Any(x => x.text == "새 태그팀"), Is.False);
                Assert.That(host.Q("story-management").Query<TextField>().ToList(), Is.Empty);
                Assert.That(host.Q<ScrollView>("story-list"), Is.Not.Null);
                Assert.That(host.Q<ScrollView>("story-detail"), Is.Not.Null);
                Assert.That(save.StoryCandidates.Single().Status, Is.EqualTo(StoryCandidateStatus.Pending));
                Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-35));

            }
            finally { Object.DestroyImmediate(owner); DashboardSession.SetActiveSave(previous, wasTransient); }
        }

        private static void Click(Button button) => typeof(Clickable)
            .GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button.clickable, new object[] { null });

        [TestCase("match-event")]
        [TestCase("promo-event")]
        public void EditingLegacySegment_PreservesStoryLinkAndEnding(string eventId)
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.MatchPlans[0].StorylineId = "legacy"; save.MatchPlans[0].StoryAction = StoryAction.End;
            save.PromoPlans[0].StorylineId = "legacy"; save.PromoPlans[0].StoryAction = StoryAction.End;
            var view = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree();
            DashboardViewHost.MountMissingViews(view);
            var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.ShowPlanningController");
            var controller = System.Activator.CreateInstance(type, view);
            type.GetField("save", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, save);
            type.GetField("editingShowEventId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, eventId);
            var link = ((string, StoryAction))type.GetMethod("ReadStoryControls", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            Assert.That(link.Item1, Is.EqualTo("legacy"));
            Assert.That(link.Item2, Is.EqualTo(StoryAction.End));
        }

        [Test]
        public void StoryGroupAndProfileRelation_StayInTheirOwnMenus()
        {
            var previous = DashboardSession.ActiveSave;
            var wasTransient = DashboardSession.IsTransient;
            try
            {
                var save = ShowExecutionServiceTests.CreateSave();
                foreach (var wrestler in save.Wrestlers) wrestler.PromotionId = save.Promotion.Id;
                var story = new StorylineService().Create(save, "개인 대립", "a", "b");
                story.Interest = 64;
                story.History.Add(new StoryEventState { Incident = StoryIncident.Challenge, EventType = StoryEventType.Promo,
                    ActorId = "a", TargetId = "b", Date = save.CurrentDate, InterestChange = 6, SourceResultId = "origin-promo" });
                save.Shows[0].Name = "Origin Show";
                save.PromoResults.Add(new PromoResultState { Id = "origin-promo", ShowId = save.Shows[0].Id });
                save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -35 });
                DashboardSession.SetActiveSave(save, true);
                Assert.That(Resources.Load<StyleSheet>("PWManagerUI/StoryManagement"), Is.Not.Null);
                var host = new VisualElement();
                StoryManagementView.Open(host);
                Assert.That(host.Q<ScrollView>(className: "story-main"), Is.Not.Null);
                Assert.That(host.Q<ScrollView>(className: "story-side"), Is.Not.Null);
                Assert.That(host.Query<Label>(className: "story-section-heading").ToList().Select(x => x.text),
                    Does.Contain("스토리라인 목록"));
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "스토리 생성"), Is.False);
                Assert.That(host.Query<VisualElement>(className: "story-poster").ToList(), Has.Count.EqualTo(1));
                Assert.That(host.Query<VisualElement>(className: "story-portrait").ToList(), Has.Count.EqualTo(2));
                Assert.That(host.Query<Label>().ToList().Any(x => x.text.Contains("관중 반응 64") && x.text.Contains("+6")), Is.True);
                Click(host.Q<Button>("story-select-" + story.Id));
                Assert.That(host.Query<Label>().ToList().Any(x => x.text.Contains("도전")), Is.True);
                Assert.That(host.Query<Label>().ToList().Any(x => x.text.Contains("Origin Show") && x.text.Contains("프로모") && x.text.Contains("도전")), Is.True);
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "대립 맥락"), Is.False);
                Assert.That(host.Q<ScrollView>("story-list"), Is.Not.Null);
                Click(host.Q<Button>("story-select-" + story.Id));
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "관계 -35"), Is.False);
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "태그팀 결성"), Is.False);
                StoryManagementView.Open(host, "a");
                Assert.That(host.Children().Count(), Is.EqualTo(1));
                var labels = host.Query<Label>().ToList().Select(x => x.text).ToList();
                Assert.That(labels, Does.Contain("우호도 -35"));
                Assert.That(labels, Does.Not.Contain("개인 대립 · 진행 중"));
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "스토리 생성"), Is.False);
                StoryManagementView.OpenGroups(host);
                Click(host.Q<Button>("group-new"));
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "태그팀 결성"), Is.True);
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "관계 -35"), Is.False);
            }
            finally { DashboardSession.SetActiveSave(previous, wasTransient); }
        }
    }
}
