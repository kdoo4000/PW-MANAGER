using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using System.Collections;

namespace PWManager.Tests
{
    public sealed class LivingGamePresentationTests
    {
        private GameSave previous;
        private bool transient;
        private UnityEditor.EditorWindow window;
        [SetUp] public void Before() { previous = DashboardSession.ActiveSave; transient = DashboardSession.IsTransient; }
        [TearDown] public void After() { if (window != null) window.Close(); DashboardSession.SetActiveSave(previous, transient); }

        [Test]
        public void Inbox_SeparatesLiveRequestsPromisesAndGroupedBriefsWithoutDeletingHistory()
        {
            var save = CardPlacementBehaviorTests.Create(); var w = save.Wrestlers[0];
            var state = new WrestlerBehaviorState { PromotionId = save.Promotion.Id, WrestlerId = w.Id };
            var concern = new WrestlerConcernState { Id = "concern", Status = WrestlerConcernStatus.AwaitingResponse, RequestId = "request", ResponseDueDate = new(save.CurrentDate.AddDays(7)) };
            state.Concerns.Add(concern); save.WrestlerBehaviors.Add(state);
            new InboxService().Publish(save, "wrestler-request:request", InboxMessageType.Decision, InboxPriority.Important, "선수", "면담", "요청", InboxTargetType.WrestlerReport, w.Id);
            for (var i = 0; i < 30; i++) new InboxService().Publish(save, "daily-brief:" + i, InboxMessageType.Information, InboxPriority.Normal, "운영실", "브리핑", "운영 기록");
            DashboardSession.SetActiveSave(save, true);
            var dashboard = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(dashboard);
            window = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>(); window.ShowUtility(); window.rootVisualElement.Add(dashboard);
            var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardInboxController");
            var inbox = Activator.CreateInstance(type, new object[] { dashboard, (Action)(() => { }), (Action<string>)(_ => { }) });
            void Bind() => type.GetMethod("Bind").Invoke(inbox, new object[] { save });
            Bind(); Assert.That(type.GetProperty("UnreadCount").GetValue(inbox), Is.EqualTo(1));
            Assert.That(dashboard.Query<Label>(className: "message-deadline").ToList().Single().text, Does.Contain(concern.ResponseDueDate.Value.Year.ToString()));
            Assert.That(dashboard.Query<Button>(className: "message-row").ToList(), Has.Count.EqualTo(1));
            concern.Status = WrestlerConcernStatus.PromiseActive; save.InboxMessages[0].Status = InboxMessageStatus.Resolved;
            Bind(); Assert.That(type.GetProperty("UnreadCount").GetValue(inbox), Is.EqualTo(0));
            dashboard.Q<DropdownField>("message-filter").index = 1;
            Assert.That(dashboard.Query<Button>(className: "message-row").ToList(), Has.Count.EqualTo(1));
            dashboard.Q<DropdownField>("message-filter").index = 2;
            Assert.That(dashboard.Q("message-list").Query<Foldout>().ToList(), Has.Count.EqualTo(1));
            Assert.That(save.InboxMessages, Has.Count.EqualTo(31));
        }

        [Test]
        public void PersonalReport_PutsCurrentProblemBeforeHistoryAndKeepsObservationsCollapsed()
        {
            var save = CardPlacementBehaviorTests.Create(); var w = save.Wrestlers[0];
            var state = new WrestlerBehaviorState { PromotionId = save.Promotion.Id, WrestlerId = w.Id };
            state.Concerns.Add(new() { Id = "old", Status = WrestlerConcernStatus.Resolved, StartedDate = save.CurrentDate.AddDays(-30) });
            state.Concerns.Add(new() { Id = "current", Kind = WrestlerConcernKind.CardPlacement, Status = WrestlerConcernStatus.Watching, StartedDate = save.CurrentDate });
            state.Events.Add(new() { Id = "event", ConcernId = "old", Kind = WrestlerBehaviorEventKind.PromiseBroken, Date = save.CurrentDate.AddDays(-10), Description = "출연 약속이 지켜지지 않았습니다." });
            save.WrestlerBehaviors.Add(state); DashboardSession.SetActiveSave(save, true);
            var host = new VisualElement(); WrestlerBehaviorView.Open(host, w.Id);
            var labels = host.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(labels.IndexOf("현재 상황"), Is.LessThan(labels.IndexOf("문제별 경과")));
            Assert.That(host.Q<Foldout>("wrestler-report-observations").value, Is.False);
            Assert.That(host.Q<Foldout>("wrestler-concern-old").value, Is.False);
            Assert.That(host.Q<Foldout>("wrestler-concern-current").value, Is.True);
            Assert.That(labels.Any(l => l.Contains("Professionalism")), Is.False);
        }

        [Test]
        public void ContractNotice_ShowsCurrentTimeRemainingAndPreservesOriginalArchive()
        {
            var save = CardPlacementBehaviorTests.Create(); var w = save.Wrestlers[0]; var contract = save.Contracts[0];
            contract.EndDate = save.CurrentDate;
            var message = new InboxService().Publish(save, "contract-expiry-warning:" + contract.Id + ":28", InboxMessageType.Navigation,
                InboxPriority.Important, "계약 담당", "원래 알림: 28일 후 만료", "계약 확인", InboxTargetType.Wrestler, w.Id);
            DashboardSession.SetActiveSave(save, true);
            var dashboard = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(dashboard);
            var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardInboxController");
            var inbox = Activator.CreateInstance(type, new object[] { dashboard, (Action)(() => { }), (Action<string>)(_ => { }) });
            type.GetMethod("Bind").Invoke(inbox, new object[] { save });
            Assert.That(dashboard.Q<Label>("message-detail-subject").text, Does.Contain("오늘"));
            Assert.That(message.Subject, Is.EqualTo("원래 알림: 28일 후 만료"));
            dashboard.Q<DropdownField>("message-filter").index = 2; type.GetMethod("Bind").Invoke(inbox, new object[] { save });
            Assert.That(dashboard.Q<Label>("message-detail-subject").text, Is.EqualTo(message.Subject));
        }

        [Test]
        public void StoryHiatus_PreservesHistoryAndStopsNeglectUntilResumed()
        {
            var save = ShowExecutionServiceTests.CreateSave(); save.Shows.Clear();
            var story = new StorylineService().Create(save, "대립", "a", "b"); story.Interest = 30;
            save.CurrentDate = save.CurrentDate.AddDays(28);
            Assert.That(StorylineService.DaysWithoutProgress(save, story), Is.EqualTo(28));
            var result = new ShowResultState();
            Assert.That(StorylineService.UnprogressedInterestChanges(save, result), Has.Count.EqualTo(1));
            StorylineService.ChangeStatus(save, story.Id, StorylineStatus.Inactive);
            Assert.That(StorylineService.UnprogressedInterestChanges(save, result), Is.Empty);
            Assert.That(story.Interest, Is.EqualTo(30));
            StorylineService.ChangeStatus(save, story.Id, StorylineStatus.Active);
            Assert.That(StorylineService.UnprogressedInterestChanges(save, result), Has.Count.EqualTo(1));
            StorylineService.ChangeStatus(save, story.Id, StorylineStatus.Completed);
            Assert.That(story.CompletedDate, Is.EqualTo(save.CurrentDate));
            Assert.Throws<InvalidOperationException>(() => StorylineService.ChangeStatus(save, story.Id, StorylineStatus.Active));
        }

        [Test]
        public void StoryStatus_PendingResultBlocksMutation()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var story = new StorylineService().Create(save, "대립", "a", "b"); save.Shows[0].Status = ShowStatus.ResultReview;
            Assert.Throws<InvalidOperationException>(() => StorylineService.ChangeStatus(save, story.Id, StorylineStatus.Inactive));
            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Active));
        }

        [Test]
        public void LockerRoom_ShowsRealRelationsAndMediationExcludesForeignRosterAndKayfabe()
        {
            var save = CardPlacementBehaviorTests.Create(); var a = save.Wrestlers[0]; var b = save.Wrestlers[1]; var mediator = save.Wrestlers[2];
            save.LockerRoomRelationships.Add(new() { FirstWrestlerId = a.Id, SecondWrestlerId = b.Id, Affinity = 16, Interactions = 30 });
            save.LockerRoomEvents.Add(new() { PromotionId = save.Promotion.Id, ActorId = a.Id, PartnerId = b.Id, MediatorId = mediator.Id,
                Kind = LockerRoomEventKind.Mediation, Date = save.CurrentDate, Description = "동료가 갈등을 중재했습니다." });
            var foreign = save.Wrestlers.Last(); foreign.PromotionId = "foreign";
            save.StoryRelationships.Add(new() { FirstWrestlerId = a.Id, SecondWrestlerId = b.Id, Value = -100 });
            DashboardSession.SetActiveSave(save, true); var before = JsonUtility.ToJson(save); var host = new VisualElement();
            LockerRoomView.Open(host);
            var group = host.Q("locker-room-group-1");
            Assert.That(group.Q("locker-room-card-" + a.Id), Is.Not.Null);
            Assert.That(group.Q("locker-room-card-" + b.Id), Is.Not.Null);
            Assert.That(host.Q("locker-room-ungrouped").Q("locker-room-card-" + mediator.Id), Is.Not.Null);
            Assert.That(host.Q("locker-room-card-" + foreign.Id), Is.Null);
            Assert.That(host.Query<Label>().ToList().Any(l => l.text.Contains("중재")), Is.True);
            Assert.That(host.Query<Button>().ToList().Any(l => l.text == mediator.Identity.RingName), Is.True);
            Assert.That(host.Query<Label>().ToList().Any(l => l.text.Contains("16.0")), Is.True);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator LockerRoom_ResizesAndSelectsPeopleWithoutClippingCards()
        {
            var save = CardPlacementBehaviorTests.Create();
            for (var i = 0; i < 4; i += 2)
                save.LockerRoomRelationships.Add(new() { FirstWrestlerId = save.Wrestlers[i].Id, SecondWrestlerId = save.Wrestlers[i + 1].Id, Affinity = 15 });
            save.LockerRoomRelationships.Add(new() { FirstWrestlerId = save.Wrestlers[0].Id, SecondWrestlerId = save.Wrestlers[2].Id, Affinity = -20 });
            DashboardSession.SetActiveSave(save, true);
            window = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>(); window.ShowUtility();
            window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
            var root = LockerRoomView.Open(window.rootVisualElement);
            foreach (var width in new[] { 1400, 960, 650 })
            {
                window.position = new Rect(30, 60, width, 800);
                for (var i = 0; i < 8; i++) yield return null;
                var board = root.Q("locker-room-board"); var cards = board.Query<VisualElement>(className: "sm-wrestler").ToList();
                Assert.That(board.layout.width, Is.EqualTo(root.Q<ScrollView>("locker-room-scroll").contentViewport.layout.width).Within(1));
                Assert.That(cards, Has.Count.EqualTo(6));
                var groups = board.Query<VisualElement>(className: "locker-room-group").ToList();
                Assert.That(groups, Has.Count.EqualTo(3));
                Assert.That(groups.All(group => group.Query<VisualElement>(className: "sm-wrestler").ToList().Count == 2), Is.True);
                foreach (var card in cards)
                {
                    Assert.That(card.worldBound.xMin, Is.GreaterThanOrEqualTo(board.worldBound.xMin));
                    Assert.That(card.worldBound.xMax, Is.LessThanOrEqualTo(board.worldBound.xMax + 1));
                }
                for (var i = 0; i < cards.Count; i++)
                for (var j = i + 1; j < cards.Count; j++) Assert.That(cards[i].worldBound.Overlaps(cards[j].worldBound), Is.False);
                var select = root.Q<Button>("locker-room-select-" + save.Wrestlers[0].Id);
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(select.clickable, new object[] { null });
                Assert.That(root.Q("locker-room-detail").Query<Button>(className: "sm-detail-name").ToList(), Has.Count.EqualTo(1));
                using var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None); escape.target = select; select.SendEvent(escape);
                Assert.That(root.Q("locker-room-detail").Query<Button>(className: "sm-detail-name").ToList(), Is.Empty);
            }
        }
    }
}
