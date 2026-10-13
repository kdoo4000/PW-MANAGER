using System;
using System.Collections.Generic;
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
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void TeamSlots_SelectExactTeamSizeAndRenderTeammatesTogether(int teamSize)
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Team Slots Test UI");
            try
            {
                var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
                var save = ShowTestSaveFactory.Create(catalog, 20260903);
                DashboardSession.SetActiveSave(save, transient: true);
                var ids = save.Wrestlers.Take(teamSize * 2 + 1).Select(x => x.Id).ToArray();
                var match = new MatchPlanState
                {
                    MatchTypeId = teamSize == 1 ? "matchtype_001" : "matchtype_002",
                    MatchGimmickId = "gimmick_000", FinishType = MatchFinishType.Draw,
                    TeamCount = 2, MembersPerTeam = teamSize,
                    Sides =
                    {
                        new MatchSideState { Id = "a", MemberIds = ids.Take(teamSize).ToList() },
                        new MatchSideState { Id = "b", MemberIds = ids.Skip(teamSize).Take(teamSize).ToList(), RepresentedTagTeamId = "opponents" }
                    }
                };
                var show = save.Shows[0];
                var showEvent = new ShowPlanningService(new StaticContentRegistry(catalog)).AddMatch(save, show.Id, match, 20);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var root = document.rootVisualElement;
                DashboardViewHost.MountMissingViews(root);
                var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.ShowPlanningController");
                var controller = Activator.CreateInstance(type, root);
                void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value);
                object Call(string name, params object[] args) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, args);
                void Open() => Call("OpenSelectedEventEditor", Enum.Parse(type.GetNestedType("ShowEditSection", BindingFlags.NonPublic), "Participants"));
                Button Card(string id) => root.Q("show-editor-roster").Children().OfType<Button>().Single(x => (string)x.userData == id);
                void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(button.clickable, new object[] { null });
                Set("save", save); Set("selectedShowId", show.Id); Set("selectedShowEventId", showEvent.Id);
                Open();
                Assert.That(root.Q("show-editor-team-slots").childCount, Is.EqualTo(2));
                Assert.That(root.Q<Button>("show-editor-submit").enabledSelf, Is.True);
                Assert.That(Card(ids[teamSize]).enabledSelf, Is.False);
                Assert.That(Card(ids.Last()).enabledSelf, Is.False);
                Click(Card(ids[0]));
                Assert.That(root.Q<Button>("show-editor-submit").enabledSelf, Is.False);
                Assert.That(Card(ids.Last()).enabledSelf, Is.True);
                Call("SubmitShowEditor");
                Assert.That(match.Sides[0].MemberIds, Is.EqualTo(ids.Take(teamSize)));
                Click(Card(ids.Last()));
                Assert.That(root.Q<Button>("show-editor-submit").enabledSelf, Is.True);
                Assert.That(Card(ids[0]).enabledSelf, Is.False);
                Click(root.Q<Button>("show-editor-team-1"));
                Assert.That(Card(ids[0]).enabledSelf, Is.False);
                Assert.That(Card(ids[teamSize]).ClassListContains("selected"), Is.True);
                Call("CloseShowEditor");
                Assert.That(match.Sides[0].MemberIds, Is.EqualTo(ids.Take(teamSize)), "Cancellation must keep committed teams intact.");
                Open();
                Click(Card(ids[0])); Click(Card(ids.Last()));
                var opponents = match.Sides[1];
                match.WinningSideId = "b"; match.FinishPerformerId = ids[teamSize]; match.LoserTargetId = ids[0];
                Call("SubmitShowEditor");
                Assert.That(match.Sides[0].MemberIds, Is.EquivalentTo(ids.Skip(1).Take(teamSize - 1).Append(ids.Last())));
                Assert.That(match.Sides[0].Id, Is.EqualTo("a"));
                Assert.That(match.Sides[1], Is.SameAs(opponents));
                Assert.That(match.Sides[1].RepresentedTagTeamId, Is.EqualTo("opponents"));
                Assert.That(new[] { match.WinningSideId, match.FinishPerformerId, match.LoserTargetId }, Is.All.Null);
                var matchup = new VisualElement();
                Call("RenderMatchup", matchup, showEvent, true);
                var cards = matchup.Children().OfType<TrapezoidPortraitCard>().ToArray();
                Assert.That(cards, Has.Length.EqualTo(2));
                Assert.That(cards.All(card => card.Portraits.Count == teamSize), Is.True);
                Assert.That(cards.Select(card => card.Query<Button>().ToList().Count), Is.All.EqualTo(teamSize));
                Assert.That(cards[0].IsFirst && cards[1].IsLast, Is.True);
                Assert.That(matchup.Query<Label>(className: "show-match-versus").ToList(), Has.Count.EqualTo(1));
                Open();
                match.WinningSideId = "b"; match.FinishPerformerId = ids[teamSize];
                Call("SubmitShowEditor");
                Assert.That(match.WinningSideId, Is.EqualTo("b"), "Saving unchanged members must keep the finish.");
                match.Sides[0].MemberIds.Clear();
                Open();
                Assert.That(root.Q<Button>("show-editor-submit").enabledSelf, Is.False);
                for (var member = 0; member < teamSize; member++) Click(Card(ids[member]));
                Assert.That(root.Q<Button>("show-editor-submit").enabledSelf, Is.True);
                Call("SubmitShowEditor");
                Assert.That(match.Sides[0].MemberIds, Is.EqualTo(ids.Take(teamSize)), "An empty team must be assigned in one confirmation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ui);
                DashboardSession.SetActiveSave(previous, transient);
            }
        }

        [Test]
        public void ScheduleSettings_OnlyUpdateUnconfirmedShowsAndKeepEpisodeNumbers()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Schedule Settings Test UI");
            try
            {
                var save = ShowTestSaveFactory.Create(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"), 20260903);
                var template = JsonUtility.ToJson(save.Shows[0]);
                var statuses = new[] { ShowStatus.Confirmed, ShowStatus.Draft, ShowStatus.Preparing, ShowStatus.Review,
                    ShowStatus.InProgress, ShowStatus.ResultReview, ShowStatus.ResultsReviewed, ShowStatus.Completed };
                save.Shows.Clear();
                for (var i = 0; i < statuses.Length; i++)
                {
                    var show = JsonUtility.FromJson<ShowState>(template);
                    show.Id = Guid.NewGuid().ToString("D");
                    show.Date = save.CurrentDate.AddDays(i);
                    show.ShowType = ScheduledShowType.Regular;
                    show.Status = statuses[i];
                    show.Name = $"Original {i}";
                    show.DurationLimit = 120;
                    show.EstimatedCost = 123;
                    save.Shows.Add(show);
                }
                DashboardSession.SetActiveSave(save, transient: true);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var root = document.rootVisualElement;
                DashboardViewHost.MountMissingViews(root);
                var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardScheduleController");
                var controller = Activator.CreateInstance(type, root, new Func<ShowState, string>(_ => "Venue"), new Action<string>(_ => { }));
                type.GetMethod("Render").Invoke(controller, new object[] { save });
                type.GetMethod("OpenTitleEditor", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { true });
                root.Q<TextField>("schedule-regular-title-field").value = "New Show";
                ((IntegerField)type.GetField("regularDurationField", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).value = 180;
                type.GetMethod("SaveTitles", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

                for (var i = 0; i < statuses.Length; i++)
                {
                    var show = save.Shows[i];
                    var editable = statuses[i] is ShowStatus.Draft or ShowStatus.Preparing or ShowStatus.Review;
                    Assert.That(show.Status, Is.EqualTo(statuses[i]));
                    Assert.That(show.DurationLimit, Is.EqualTo(editable ? 180 : 120));
                    Assert.That(show.Name, Is.EqualTo(editable
                        ? ShowNamingRules.Compose(save.Promotion.Abbreviation, "New Show", show.ShowType, i + 1, show.Date.Year)
                        : $"Original {i}"));
                    Assert.That(show.EstimatedCost, Is.EqualTo(editable
                        ? save.VenueContracts.Single(x => x.Id == show.VenueContractId).CalculateProductionCost(180) : 123));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ui);
                DashboardSession.SetActiveSave(previous, transient);
            }
        }

        [Test]
        public void ShowInProgress_BlocksOtherActionsUntilSettlementAndKeepsProfileReadOnly()
        {
            var previous = DashboardSession.ActiveSave;
            var transient = DashboardSession.IsTransient;
            var ui = new GameObject("Show Lock Test UI");
            var host = new GameObject("Show Lock Test Controller");
            host.SetActive(false);
            try
            {
                var save = ShowTestSaveFactory.Create(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"), 20260903);
                DashboardSession.SetActiveSave(save, transient: true);
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                var root = document.rootVisualElement;
                var controller = host.AddComponent<DashboardController>();
                var type = typeof(DashboardController);
                host.GetComponent<UIDocument>().panelSettings = document.panelSettings;
                host.GetComponent<UIDocument>().visualTreeAsset = document.visualTreeAsset;
                type.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                root = host.GetComponent<UIDocument>().rootVisualElement;
                var block = type.GetMethod("BlockShowInteraction", BindingFlags.Instance | BindingFlags.NonPublic);
                var edit = new Button { name = "edit-group" };
                root.Add(edit);
                var link = WrestlerProfileController.CreateLink(save.Wrestlers[0]);
                root.Add(link);
                var show = save.Shows[0];
                foreach (var status in new[] { ShowStatus.InProgress, ShowStatus.ResultReview, ShowStatus.ResultsReviewed })
                {
                    show.Status = status;
                    controller.Bind(save);
                    Assert.That(DashboardSession.IsShowRunning, Is.True);
                    Assert.That(root.Q<Button>("nav-teams").enabledSelf, Is.False);
                    Assert.That(root.Q<Button>("nav-settings").enabledSelf, Is.False);
                    Assert.That(root.Q("show-schedule-content").enabledSelf, Is.False);
                    Assert.That(root.Q<Button>("advance-time").enabledSelf, Is.True);
                    using (var evt = ClickEvent.GetPooled())
                    {
                        evt.target = edit;
                        block.Invoke(controller, new object[] { evt });
                        Assert.That(evt.isPropagationStopped, Is.True);
                    }
                    using (var evt = ClickEvent.GetPooled())
                    {
                        evt.target = link;
                        block.Invoke(controller, new object[] { evt });
                        Assert.That(evt.isPropagationStopped, Is.False);
                    }
                }
                var profile = host.AddComponent<WrestlerProfileController>();
                var profileType = typeof(WrestlerProfileController);
                profileType.GetField("canSaveStyles", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(profile, true);
                profileType.GetField("boundWrestler", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(profile, save.Wrestlers[0]);
                profileType.GetField("previewStyleId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(profile, "style_002");
                var before = save.Wrestlers[0].Presentation.WrestlingStyleId;
                profileType.GetMethod("SaveMatchStyle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(profile, null);
                Assert.That(save.Wrestlers[0].Presentation.WrestlingStyleId, Is.EqualTo(before));
                DashboardController.ShowWrestlerShell(save.Wrestlers[0].Identity.RingName, save.Wrestlers[0].Id);
                Assert.That(root.Q<Button>("dashboard-back").enabledSelf, Is.True);
                type.GetMethod("NavigateBack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Assert.That(root.Q<Button>("dashboard-back").enabledSelf, Is.False);
                show.Status = ShowStatus.Completed;
                controller.Bind(save);
                Assert.That(DashboardSession.IsShowRunning, Is.False);
                Assert.That(root.Q<Button>("nav-teams").enabledSelf, Is.True);
                Assert.That(root.Q<Button>("nav-settings").enabledSelf, Is.True);
                Assert.That(root.Q("show-schedule-content").enabledSelf, Is.True);
                type.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(ui);
                DashboardSession.SetActiveSave(previous, transient);
            }
        }

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
                Assert.That(ids.Take(2).All(id => Card(id).enabledSelf), Is.True);
                Assert.That(ids.Skip(2).Take(2).All(id => !Card(id).enabledSelf), Is.True);
                Assert.That(Card(ids[4]).enabledSelf, Is.False);
                var pending = (List<string>)controllerType.GetField("pendingParticipantIds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                pending.Clear(); pending.AddRange(new[] { ids[1], ids[2] });
                Call("SubmitShowEditor");
                Assert.That(match.Sides[0].MemberIds, Is.EqualTo(ids.Take(2)));
                pending.Clear(); pending.AddRange(new[] { ids[4], ids[1] });
                Call("SubmitShowEditor");
                Open("Participants");
                Assert.That(Card(ids[0]).enabledSelf, Is.False);
                Assert.That(Card(ids[4]).enabledSelf, Is.True);
                Assert.That(Card(ids[4]).ClassListContains("selected"), Is.True);
                Field("show-editor-roster-sort").index = 1;
                var order = root.Q("show-editor-roster").Children().OfType<Button>().Select(x => save.Wrestlers.Single(w => w.Id == (string)x.userData)).ToList();
                Assert.That(order.Select(x => WrestlerOverallCalculator.Match(x)), Is.Ordered.Descending);
                Assert.That(root.Q<Button>("show-editor-roster-sort-direction").text, Is.EqualTo("내림차순"));
                Set("editorRosterAscending", true); Call("RenderShowEditorRoster", show);
                order = root.Q("show-editor-roster").Children().OfType<Button>().Select(x => save.Wrestlers.Single(w => w.Id == (string)x.userData)).ToList();
                Assert.That(order.Select(x => WrestlerOverallCalculator.Match(x)), Is.Ordered);

                Open("Producing");
                Assert.That(Field("show-editor-finish-type").choices, Does.Not.Contain("탈출"));
                var championship = ChampionshipService.Create(save, "태그 타이틀", ChampionshipType.TagTeam);
                Open("Producing");
                Field("match-championship").index = 1;
                Field("show-editor-finish-type").value = "핀폴";
                Field("show-editor-winning-side").index = 2;
                Field("show-editor-finish-performer").index = 1;
                Field("show-editor-loser").index = 1;
                root.Q<TextField>("show-editor-opening-spot").value = "경기 전 악수";
                root.Q<TextField>("show-editor-middle-spot").value = "경기 중 도발";
                root.Q<TextField>("show-editor-closing-spot").value = "경기 후 난투";
                var injuryRow = root.Q<Foldout>("show-editor-story-injuries").contentContainer.Children().Single(x => (string)x.userData == ids[1]);
                injuryRow.Q<DropdownField>("story-injury-severity").index = 2;
                injuryRow.Q<DropdownField>("story-injury-region").index = (int)BodyRegion.Legs;
                injuryRow.Q<IntegerField>("story-injury-days").value = 21;
                injuryRow.Q<Toggle>("story-injury-absent").value = true;
                Call("SubmitShowEditor");
                Assert.That(match.StoryInjuries.Single().WrestlerId, Is.EqualTo(ids[1]));
                Assert.That(match.ChampionshipId, Is.EqualTo(championship.Id));
                Assert.That(match.StoryInjuries.Single().Days, Is.EqualTo(21));
                Assert.That(match.StoryInjuries.Single().Absent, Is.True);
                Assert.That(match.WinningSideId, Is.EqualTo("b"));
                Assert.That(match.FinishPerformerId, Is.EqualTo(ids[3]));
                Assert.That(match.LoserTargetId, Is.EqualTo(ids[1]));
                Assert.That(Call("ShowSpotSummary", showEvent), Is.EqualTo(
                    string.Join(" & ", save.Wrestlers.Skip(2).Take(2).Select(x => x.Identity.RingName)) + " 승리"));
                Open("Producing");
                Assert.That(Field("show-editor-winning-side").index, Is.EqualTo(2));
                Assert.That(Field("match-championship").index, Is.EqualTo(1));
                Field("match-championship").index = 0;
                Assert.That(Field("show-editor-finish-performer").index, Is.EqualTo(1));
                Assert.That(root.Q<Foldout>("show-editor-story-injuries").contentContainer.Children()
                    .Single(x => (string)x.userData == ids[1]).Q<DropdownField>("story-injury-region").index, Is.EqualTo((int)BodyRegion.Legs));
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
