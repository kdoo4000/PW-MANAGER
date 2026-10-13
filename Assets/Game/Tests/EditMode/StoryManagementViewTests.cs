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
        public void GroupCreationAndCandidateAdoption_WorkThroughVisibleButtons()
        {
            var previous = DashboardSession.ActiveSave;
            var wasTransient = DashboardSession.IsTransient;
            try
            {
                var save = ShowExecutionServiceTests.CreateSave();
                foreach (var wrestler in save.Wrestlers) wrestler.PromotionId = save.Promotion.Id;
                save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -35 });
                save.StoryCandidates.Add(new StoryCandidateState
                {
                    Id = "candidate", Quality = 70,
                    Event = new StoryEventState { SourceResultId = "promo-result", EventType = StoryEventType.Promo,
                        Incident = StoryIncident.Betrayal, ActorId = "a", TargetId = "b", Date = save.CurrentDate }
                });
                DashboardSession.SetActiveSave(save, true);
                var host = new VisualElement();
                StoryManagementView.OpenGroups(host);
                Assert.That(host.Query<Label>(className: "story-heading").ToList().Any(x => x.text.StartsWith("스토리 후보")), Is.False);
                var group = host.Query<VisualElement>(className: "story-card").ToList()
                    .Single(x => x.Q<Label>(className: "story-heading")?.text == "새 태그팀");
                group.Q<TextField>().value = "새 태그팀";
                var selection = group.Q<Foldout>();
                selection.value = true;
                foreach (var row in selection.Query<VisualElement>(className: "story-member").ToList())
                    row.Q<Toggle>().value = true;
                Click(group.Query<Button>().ToList().Single(x => x.text == "태그팀 결성"));
                Assert.That(host.Q<Label>("story-error").text, Is.Empty);
                Assert.That(save.TagTeams.Single().Name, Is.EqualTo("새 태그팀"));
                Assert.That(save.Wrestlers.All(x => x.Roster.ActiveTagTeamId == save.TagTeams[0].Id), Is.True);

                StoryManagementView.Open(host);
                Assert.That(host.Query<Label>(className: "story-heading").ToList().Any(x => x.text == "새 태그팀"), Is.False);
                var candidate = host.Query<VisualElement>(className: "story-card").ToList()
                    .Single(x => x.Q<Label>(className: "story-heading")?.text == "스토리 후보 · 배신");
                candidate.Q<TextField>().value = "배신의 시작";
                Click(candidate.Query<Button>().ToList().Single(x => x.text == "새 스토리로 채택"));
                Assert.That(save.Storylines.Single().History, Has.Count.EqualTo(1));
                Assert.That(save.StoryCandidates.Single().Status, Is.EqualTo(StoryCandidateStatus.Adopted));
                Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-35));
            }
            finally { DashboardSession.SetActiveSave(previous, wasTransient); }
        }

        private static void Click(Button button) => typeof(Clickable)
            .GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button.clickable, new object[] { null });

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
                    ActorId = "a", TargetId = "b", Date = save.CurrentDate, InterestChange = 6 });
                save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -35 });
                DashboardSession.SetActiveSave(save, true);
                Assert.That(Resources.Load<StyleSheet>("PWManagerUI/StoryManagement"), Is.Not.Null);
                var host = new VisualElement();
                StoryManagementView.Open(host);
                Assert.That(host.Q<ScrollView>(className: "story-main"), Is.Not.Null);
                Assert.That(host.Q<ScrollView>(className: "story-side"), Is.Not.Null);
                Assert.That(host.Query<Label>(className: "story-section-heading").ToList().Select(x => x.text),
                    Does.Contain("진행 중인 스토리"));
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "스토리 생성"), Is.True);
                Assert.That(host.Query<VisualElement>(className: "story-poster").ToList(), Has.Count.EqualTo(1));
                Assert.That(host.Query<VisualElement>(className: "story-portrait").ToList(), Has.Count.EqualTo(2));
                Assert.That(host.Query<Label>().ToList().Any(x => x.text.Contains("관중 반응 64") && x.text.Contains("+6")), Is.True);
                Click(host.Query<Button>().ToList().Single(x => x.text == "전개 보기"));
                Assert.That(host.Query<Label>().ToList().Any(x => x.text.Contains("도전")), Is.True);
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "대립 목록"), Is.True);
                Click(host.Query<Button>().ToList().Single(x => x.text == "대립 목록"));
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "관계 -35"), Is.False);
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "태그팀 결성"), Is.False);
                StoryManagementView.Open(host, "a");
                Assert.That(host.Children().Count(), Is.EqualTo(1));
                var labels = host.Query<Label>().ToList().Select(x => x.text).ToList();
                Assert.That(labels, Does.Contain("관계 -35"));
                Assert.That(labels, Does.Not.Contain("개인 대립 · 진행 중"));
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "스토리 생성"), Is.False);
                StoryManagementView.OpenGroups(host);
                Assert.That(host.Query<Button>().ToList().Any(x => x.text == "태그팀 결성"), Is.True);
                Assert.That(host.Query<Label>().ToList().Any(x => x.text == "관계 -35"), Is.False);
            }
            finally { DashboardSession.SetActiveSave(previous, wasTransient); }
        }
    }
}
