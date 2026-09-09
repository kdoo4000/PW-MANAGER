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

namespace PWManager.Tests
{
    public sealed class ShowProducingEditorTests
    {
        [Test]
        public void Playback_FastForwardsClockToHighlightsWithoutShorteningThem()
        {
            var highlight = typeof(ShowSimulationPlayer).GetMethod("IsHighlight", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (MatchBeatType type in Enum.GetValues(typeof(MatchBeatType)))
            {
                var beat = new MatchSimulationBeatState { BeatType = type, DurationSeconds = 12f };
                var importantAction = type is MatchBeatType.PlannedSpot or MatchBeatType.NearFall or MatchBeatType.Finish;
                Assert.That(highlight.Invoke(null, new object[] { beat }), Is.EqualTo(importantAction));
                Assert.That(beat.DurationSeconds, Is.EqualTo(12f));
            }
            var advance = typeof(ShowSimulationPlayer).GetMethod("AdvanceMatchClock", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(advance.Invoke(null, new object[] { 0f, 600f, 1f, true }), Is.EqualTo(120f));
            Assert.That(advance.Invoke(null, new object[] { 550f, 600f, 1f, true }), Is.EqualTo(600f));
            Assert.That(advance.Invoke(null, new object[] { 600f, 900f, 1f, false }), Is.EqualTo(601f));
            Assert.That(advance.Invoke(null, new object[] { 600f, 550f, 1f, true }), Is.EqualTo(600f));
            Assert.That(advance.Invoke(null, new object[] { 600f, 900f, 0f, true }), Is.EqualTo(600f));
        }

        [Test]
        public void Booking_DisablesDuplicatesSortsCardsAndPersistsTeamProduction()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Producing Test UI");
            var host = new GameObject("Producing Test Controller");
            host.SetActive(false);
            try
            {
                var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
                var save = ShowTestSaveFactory.Create(catalog, 20260903);
                DashboardSession.SetActiveSave(save, transient: true);
                var content = new StaticContentRegistry(catalog);
                var planning = new ShowPlanningService(content);
                var show = save.Shows[0];
                var ids = save.Wrestlers.Take(5).Select(x => x.Id).ToArray();
                var match = new MatchPlanState
                {
                    MatchTypeId = "matchtype_002", MatchGimmickId = "gimmick_000", FinishType = MatchFinishType.Draw,
                    Sides = { new MatchSideState { Id = "a", MemberIds = { ids[0], ids[1] } }, new MatchSideState { Id = "b", MemberIds = { ids[2], ids[3] } } }
                };
                var showEvent = planning.AddMatch(save, show.Id, match, 20);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var root = document.rootVisualElement;
                DashboardViewHost.MountMissingViews(root);
                var controllerType = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.ShowPlanningController");
                var controller = Activator.CreateInstance(controllerType, root);
                void Set(string name, object value) => controllerType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value);
                object Call(string name, params object[] args) => controllerType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, args);
                void Open(string section) => Call("OpenSelectedEventEditor", Enum.Parse(controllerType.GetNestedType("ShowEditSection", BindingFlags.NonPublic), section));
                DropdownField Field(string name) => root.Q<DropdownField>(name);
                Button Card(string id) => root.Q("show-editor-roster").Children().OfType<Button>().Single(x => (string)x.userData == id);
                Set("save", save); Set("selectedShowId", show.Id); Set("selectedShowEventId", showEvent.Id);
                Call("RenderShowPlanning");
                Assert.That(root.Q<Label>("show-detail-position").text, Is.EqualTo("메인"));
                var timelineSubtitle = root.Q($"show-event-{showEvent.Id}").Q<Label>(className: "show-event-participants").text;
                Assert.That(timelineSubtitle, Does.Contain("기본 경기"));
                Assert.That(ids.Any(id => timelineSubtitle.Contains(WrestlerNameText.DisplayName(save.Wrestlers.First(x => x.Id == id)))), Is.False);
                Assert.That(root.Q<Label>("show-detail-position").ClassListContains("show-panel-title"), Is.True);
                Assert.That(root.Q<Label>("show-detail-duration").text, Is.EqualTo("20분"));
                for (var i = 0; i < 3; i++) Call("ChangeSelectedEventDuration", 5);
                Assert.That(showEvent.PlannedDuration, Is.EqualTo(35));
                Assert.That(root.Q<Label>("show-duration-used").text, Is.EqualTo("35분 사용"));
                for (var i = 0; i < 6; i++) Call("ChangeSelectedEventDuration", -5);
                Assert.That(showEvent.PlannedDuration, Is.EqualTo(5));
                Assert.That(root.Q<Button>("show-duration-decrease").enabledSelf, Is.False);
                Call("ChangeSelectedEventDuration", -5);
                Assert.That(showEvent.PlannedDuration, Is.EqualTo(5));
                for (var i = 0; i < 3; i++) Call("ChangeSelectedEventDuration", 5);
                Open("Participants");
                var overlay = root.Q<VisualElement>("show-editor-overlay");
                Assert.That(overlay.parent.ElementAt(overlay.parent.childCount - 1), Is.SameAs(overlay));
                Assert.That(ids.Take(4).All(id => !Card(id).enabledSelf), Is.True);
                Assert.That(Card(ids[4]).enabledSelf, Is.True);
                Call("AssignWrestlerToSelectedSlot", ids[1], show);
                Assert.That(match.Sides[0].MemberIds, Is.EqualTo(ids.Take(2)));
                Set("pendingParticipantId", ids[4]);
                Call("SubmitShowEditor");
                Open("Participants");
                Assert.That(Card(ids[0]).enabledSelf, Is.True);
                Assert.That(Card(ids[4]).enabledSelf, Is.False);
                Field("show-editor-roster-sort").index = 1;
                var order = root.Q("show-editor-roster").Children().OfType<Button>().Select(x => save.Wrestlers.Single(w => w.Id == (string)x.userData)).ToList();
                Assert.That(order.Select(x => WrestlerOverallCalculator.Match(x)), Is.Ordered.Descending);
                Assert.That(root.Q<Button>("show-editor-roster-sort-direction").text, Is.EqualTo("내림차순"));
                Set("editorRosterAscending", true); Call("RenderShowEditorRoster", show);
                order = root.Q("show-editor-roster").Children().OfType<Button>().Select(x => save.Wrestlers.Single(w => w.Id == (string)x.userData)).ToList();
                Assert.That(order.Select(x => WrestlerOverallCalculator.Match(x)), Is.Ordered);

                Open("Producing");
                Assert.That(Field("show-editor-finish-type").choices, Does.Not.Contain("탈출"));
                Field("show-editor-finish-type").value = "핀폴";
                Field("show-editor-winning-side").index = 2;
                Field("show-editor-finish-performer").index = 1;
                Field("show-editor-loser").index = 1;
                root.Q<TextField>("show-editor-opening-spot").value = "경기 전 악수";
                root.Q<TextField>("show-editor-middle-spot").value = "경기 중 도발";
                root.Q<TextField>("show-editor-closing-spot").value = "경기 후 난투";
                Call("SubmitShowEditor");
                Assert.That(match.WinningSideId, Is.EqualTo("b"));
                Assert.That(match.FinishPerformerId, Is.EqualTo(ids[3]));
                Assert.That(match.LoserTargetId, Is.EqualTo(ids[1]));
                Assert.That(Call("ShowSpotSummary", showEvent), Is.EqualTo(
                    string.Join(" & ", save.Wrestlers.Skip(2).Take(2).Select(x => x.Identity.RingName)) + " 승리"));
                Open("Producing");
                Assert.That(Field("show-editor-winning-side").index, Is.EqualTo(2));
                Assert.That(Field("show-editor-finish-performer").index, Is.EqualTo(1));
                Field("show-editor-finish-type").value = "무승부";
                Assert.That(Field("show-editor-winning-side").enabledSelf, Is.False);
                Call("SubmitShowEditor");
                Assert.That(new[] { match.WinnerId, match.WinningSideId, match.FinishPerformerId, match.LoserTargetId }, Is.All.Null);
                Assert.That(Call("ShowSpotSummary", showEvent), Is.EqualTo("무승부"));
                Open("Producing");
                Field("show-editor-finish-type").value = "서브미션";
                Field("show-editor-winning-side").index = 2;
                Field("show-editor-finish-performer").index = 1;
                Call("SubmitShowEditor");
                Assert.That(showEvent.PlannedDuration, Is.EqualTo(20));
                show.DurationLimit = 20;
                planning.Confirm(save, show.Id);
                Call("RenderShowPlanning");
                Assert.That(root.Q<Button>("show-duration-increase").enabledSelf, Is.False);
                Assert.That(root.Q<Button>("show-duration-decrease").enabledSelf, Is.False);
                Call("ChangeSelectedEventDuration", 5);
                Assert.That(showEvent.PlannedDuration, Is.EqualTo(20));
                Assert.That(show.Status, Is.EqualTo(ShowStatus.Confirmed));
                show.Status = ShowStatus.InProgress;
                var venue = content.Venues[save.VenueContracts[0].VenueId];
                new ShowExecutionService(planning, new MatchEvaluator(content, content, findMove: content.GetMoveRules), new PromoEvaluator()).Execute(save, show.Id, 73, venue.Capacity, venue.BaseTicketPrice);
                Assert.That(save.MatchResults.Single().FinishPerformerId, Is.EqualTo(ids[3]));
                Assert.That(save.MatchResults.Single().FinishType, Is.EqualTo(MatchFinishType.Submission));
                Assert.That(save.MatchResults.Single().SimulationBeats.First().Detail, Is.EqualTo("경기 전 악수"));
                Assert.That(save.MatchResults.Single().SimulationBeats.Last().Detail, Is.EqualTo("경기 후 난투"));
                using (var playback = new ShowSimulationPlayer(root, save, save.ShowResults.Single(), () => { }))
                {
                    var sheet = root.Q<Image>("simulation-sheet").image;
                    Assert.That(root.Q("simulation-cast").childCount, Is.EqualTo(4));
                    if (sheet == null)
                    {
                        Assert.That(root.Q("simulation-cast").Query<Button>().ToList(), Has.Count.EqualTo(4));
                        return;
                    }
                    Assert.That(sheet.width / sheet.height, Is.EqualTo(4));
                    var images = root.Q("simulation-cast").Query<Image>().ToList();
                    Assert.That(images, Has.Count.EqualTo(4));
                    foreach (var image in images) Assert.That(image.uv, Is.EqualTo(new Rect(0, 0, .25f, 1)));
                    typeof(ShowSimulationPlayer).GetField("animationElapsed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(playback, .75f);
                    typeof(ShowSimulationPlayer).GetMethod("Animate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(playback, null);
                    foreach (var image in images) Assert.That(image.uv, Is.EqualTo(new Rect(.75f, 0, .25f, 1)));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(ui);
                DashboardSession.SetActiveSave(previous, transient);
            }
        }
    }
}
