using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Data.Generation;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class StorylineMapTests
    {
        private GameSave previous;
        private bool previousTransient;

        [SetUp]
        public void RememberSession() { previous = DashboardSession.ActiveSave; previousTransient = DashboardSession.IsTransient; }

        [TearDown]
        public void RestoreSession() => DashboardSession.SetActiveSave(previous, previousTransient);

        private static GameSave Sample()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "p", Name = "PW" }, CurrentDate = new GameDate(2026, 11, 2) };
            foreach (var id in new[] { "a", "b", "c", "d" })
            {
                var wrestler = new WrestlerState { Id = id, PromotionId = "p" };
                wrestler.Identity.RingName = "선수 " + id.ToUpperInvariant();
                wrestler.Roster.Alignment = id == "b" ? KayfabeAlignment.Heel : id == "c" ? KayfabeAlignment.Tweener : KayfabeAlignment.Face;
                save.Wrestlers.Add(wrestler);
            }
            save.Storylines.Add(new StorylineState { Id = "s1", PromotionId = "p", Name = "왕좌를 향한 도전", FirstWrestlerId = "a", SecondWrestlerId = "b", StartedDate = save.CurrentDate,
                Context = "", History = { new StoryEventState { ActorId = "b", TargetId = "a", Incident = StoryIncident.Challenge, Date = save.CurrentDate } } });
            save.Storylines.Add(new StorylineState { Id = "s2", PromotionId = "p", Name = "흔들리는 동맹", FirstWrestlerId = "a", SecondWrestlerId = "c", StartedDate = save.CurrentDate });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = 25 });
            save.TagTeams.Add(new TagTeamState { Id = "team", PromotionId = "p", Name = "파트너", MemberIds = { "a", "b" } });
            save.Championships.Add(new ChampionshipState { Id = "title", PromotionId = "p", Name = "월드 챔피언십", HolderId = "a" });
            save.MatchPlans.Add(new MatchPlanState { Id = "title-plan", StorylineId = "s1", ChampionshipId = "title" });
            return save;
        }

        [Test]
        public void InitialView_KeepsOriginalOverviewWithoutAutomaticallySelectingAStory()
        {
            var save = Sample(); save.Storylines[1].History.Add(new StoryEventState { ActorId = "a", TargetId = "c", Incident = StoryIncident.Respect, Date = save.CurrentDate.AddDays(1) });
            save.CurrentDate = save.CurrentDate.AddDays(1); DashboardSession.SetActiveSave(save, true);
            var host = new VisualElement(); StorylineMapView.Open(host, () => { });
            Assert.That(host.Q<DropdownField>("story-map-stories").index, Is.EqualTo(0));
            Assert.That(host.Q("story-map-detail").Query<Label>().ToList().Any(l => l.text == "관계 살펴보기"), Is.True);
            Assert.That(host.Q("story-map-card-a").style.width.value.value, Is.EqualTo(120));
            Assert.That(host.Q("story-map-card-a").style.height.value.value, Is.EqualTo(166));
        }

        [Test]
        public void Projection_SeparatesOppositionMembershipAndEventsWithoutChangingSave()
        {
            var save = Sample();
            save.Storylines.Add(new StorylineState { Id = "ended", PromotionId = "p", Status = StorylineStatus.Completed });
            save.Storylines.Add(new StorylineState { Id = "other", PromotionId = "other" });
            var before = JsonUtility.ToJson(save);
            var data = StorylineMapData.Build(save); var first = data.Clusters[0];
            Assert.That(data.Clusters.Select(x => x.Story.Id), Is.EqualTo(new[] { "s1", "s2" }));
            Assert.That(first.Relations.Count(x => x.Kind == StorylineMapRelationKind.Rivalry), Is.EqualTo(1));
            Assert.That(first.Relations.Count(x => x.Kind == StorylineMapRelationKind.Alliance), Is.EqualTo(2));
            var incident = first.Relations.Single(x => x.Kind == StorylineMapRelationKind.Incident);
            Assert.That(incident.FirstId, Is.EqualTo("b")); Assert.That(incident.SecondId, Is.EqualTo("a")); Assert.That(incident.Directed, Is.True);
            Assert.That(data.Clusters[0].First[0], Is.SameAs(data.Clusters[1].First[0]));
            Assert.That(first.Championships.Single().Id, Is.EqualTo("title"));
            Assert.That(data.ChampionTitles["a"].Single().Name, Is.EqualTo("월드 챔피언십"));
            Assert.That(data.ChampionTitles.ContainsKey("b"), Is.False);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void Groups_UseOneOppositionAndMembershipChainInsteadOfHostileClique()
        {
            var save = Sample(); save.Storylines.Clear();
            save.Stables.Add(new StableState { Id = "first", PromotionId = "p", Name = "첫 그룹", MemberIds = { "c", "a", "b" } });
            save.Stables.Add(new StableState { Id = "second", PromotionId = "p", Name = "둘째 그룹", MemberIds = { "d" } });
            save.Storylines.Add(new StorylineState { Id = "group", PromotionId = "p", PartyType = StoryPartyType.Stable, FirstGroupId = "first", SecondGroupId = "second" });
            var first = StorylineMapData.Build(save).Clusters.Single();
            Assert.That(first.Wrestlers.Count(), Is.EqualTo(4));
            Assert.That(first.Relations.Count(x => x.Kind == StorylineMapRelationKind.Rivalry), Is.EqualTo(1));
            Assert.That(first.Relations.Count(x => x.Label.StartsWith("스테이블")), Is.EqualTo(2));
            save.Stables[0].MemberIds.Reverse(); save.Wrestlers.Reverse();
            var second = StorylineMapData.Build(save).Clusters.Single();
            Assert.That(second.Wrestlers.Select(x => x.Id), Is.EqualTo(first.Wrestlers.Select(x => x.Id)));
            Assert.That(second.Relations.Select(x => (x.FirstId, x.SecondId, x.Label)), Is.EqualTo(first.Relations.Select(x => (x.FirstId, x.SecondId, x.Label))));
        }

        [Test]
        public void MissingGroupsAndParticipants_ShowAvailableHistoryWithoutInventingOpposition()
        {
            var save = Sample(); var story = save.Storylines[0];
            story.PartyType = StoryPartyType.Stable; story.FirstGroupId = "missing"; story.SecondGroupId = "missing-too";
            var cluster = StorylineMapData.Build(save).Clusters[0];
            Assert.That(cluster.MissingParticipants, Is.True);
            Assert.That(cluster.Wrestlers.Count(), Is.EqualTo(2));
            Assert.That(cluster.Relations.Any(x => x.Kind == StorylineMapRelationKind.Rivalry), Is.False);
            Assert.That(StorylineMapData.Build(null).Clusters, Is.Empty);
        }

        [Test]
        public void TagChampions_UseCurrentReignMembersAndNeverPastHolders()
        {
            var save = Sample(); save.Championships.Clear();
            var title = new ChampionshipState { Id = "tag", PromotionId = "p", Type = ChampionshipType.TagTeam, HolderId = "team" };
            title.Reigns.Add(new ChampionshipReignState { HolderId = "old", MemberIds = { "c", "d" }, LostDate = new OptionalGameDate { HasValue = true, Value = save.CurrentDate } });
            title.Reigns.Add(new ChampionshipReignState { HolderId = "team", MemberIds = { "a", "b" } });
            save.Championships.Add(title);
            var data = StorylineMapData.Build(save);
            Assert.That(data.ChampionTitles.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        }

        [Test]
        public void RosterWithoutStories_ShowsPortraitsAndManagementTabWithoutCreatingStories()
        {
            var save = Sample(); save.Storylines.Clear(); DashboardSession.SetActiveSave(save, true);
            var managed = false; var host = new VisualElement();
            StorylineMapView.Open(host, () => managed = true);
            Assert.That(host.Q<StorylineMapCanvas>(), Is.Not.Null);
            Assert.That(host.Query<VisualElement>(className: "sm-wrestler").ToList(), Has.Count.EqualTo(4));
            Assert.That(host.Query<Button>(className: "sm-line-target").ToList(), Is.Empty);
            Assert.That(host.Query<Label>().ToList().Any(x => x.text == "진행 중인 스토리라인이 없습니다."), Is.True);
            Click(host.Q<Button>("story-tab-management"));
            Assert.That(managed, Is.True); Assert.That(save.Storylines, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ContactPoints_IdentifyActualPartyTypesOpenStoriesAndFollowFilters()
        {
            var save = Sample();
            foreach (var id in new[] { "e", "f" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "p" });
            save.Stables.Add(new StableState { Id = "first", PromotionId = "p", Name = "첫 스테이블", MemberIds = { "a", "b", "c" } });
            save.Stables.Add(new StableState { Id = "second", PromotionId = "p", Name = "둘째 스테이블", MemberIds = { "d", "e", "f" } });
            save.TagTeams.Add(new TagTeamState { Id = "opponents", PromotionId = "p", Name = "상대 팀", MemberIds = { "d", "e" } });
            save.Storylines.Add(new StorylineState { Id = "stable-story", PromotionId = "p", Name = "스테이블 대립", PartyType = StoryPartyType.Stable,
                FirstGroupId = "first", SecondGroupId = "second", StartedDate = save.CurrentDate });
            save.Storylines.Add(new StorylineState { Id = "team-story", PromotionId = "p", Name = "태그팀 대립", PartyType = StoryPartyType.TagTeam,
                FirstGroupId = "team", SecondGroupId = "opponents", StartedDate = save.CurrentDate });
            DashboardSession.SetActiveSave(save, true);
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility(); window.position = new Rect(24, 60, 1200, 800);
            window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
            var root = StorylineMapView.Open(window.rootVisualElement, () => { });
            try
            {
                for (var i = 0; i < 8; i++) yield return null;
                Assert.That(root.Query<Button>(className: "sm-contact-target").ToList(), Has.Count.EqualTo(8));
                Assert.That(root.Q<Button>("story-map-contact-s1-first").tooltip, Is.EqualTo("개인 · 선수 A"));
                Assert.That(root.Q<Button>("story-map-contact-stable-story-first").tooltip, Is.EqualTo("스테이블 · 첫 스테이블"));
                Assert.That(root.Q<Button>("story-map-contact-team-story-first").tooltip, Is.EqualTo("태그팀 · 파트너"));
                Click(root.Q<Button>("story-map-contact-team-story-first"));
                Assert.That(root.Q("story-map-detail").Query<Label>().ToList().Any(l => l.text.Contains("파트너 vs 상대 팀")), Is.True);
                root.Q<DropdownField>("story-map-relations").index = 2;
                Assert.That(root.Q<Button>("story-map-contact-s1-first").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q<Button>("story-map-contact-s1-second").style.display.value, Is.EqualTo(DisplayStyle.None));
                root.Q<DropdownField>("story-map-relations").index = 0;
                Assert.That(root.Q<Button>("story-map-contact-s1-first").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            }
            finally { window.Close(); }
        }

        [Test]
        public void RosterProjection_ExcludesForeignAndReleasedWrestlersWithoutInventingStories()
        {
            var save = Sample(); save.Storylines.Clear();
            save.Wrestlers[2].Roster.ActivityState = RosterActivityState.Inactive;
            save.Wrestlers[3].Roster.ActivityState = RosterActivityState.Released;
            save.Wrestlers.Add(new WrestlerState { Id = "foreign", PromotionId = "other" });
            var before = JsonUtility.ToJson(save);
            var data = StorylineMapData.Build(save);
            Assert.That(data.Wrestlers.Select(x => x.Id), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(data.Groups.Single().MemberIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(data.Clusters, Is.Empty);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator StorylessRoster_FitsAndSelectsAndStaysVisibleAsStoriesChange()
        {
            var save = Sample(); var story = save.Storylines[0]; save.Storylines.Clear();
            DashboardSession.SetActiveSave(save, true);
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility(); window.position = new Rect(24, 60, 1200, 800);
            window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
            var root = StorylineMapView.Open(window.rootVisualElement, () => { });
            try
            {
                for (var i = 0; i < 8; i++) yield return null;
                var canvas = root.Q<StorylineMapCanvas>();
                Assert.That(canvas.Zoom, Is.GreaterThan(0));
                foreach (var card in root.Query<VisualElement>(className: "sm-wrestler").ToList())
                {
                    Assert.That(card.style.opacity.value, Is.EqualTo(1));
                    Assert.That(card.worldBound.xMin, Is.GreaterThanOrEqualTo(canvas.worldBound.xMin));
                    Assert.That(card.worldBound.yMin, Is.GreaterThanOrEqualTo(canvas.worldBound.yMin));
                    Assert.That(card.worldBound.xMax, Is.LessThanOrEqualTo(canvas.worldBound.xMax));
                    Assert.That(card.worldBound.yMax, Is.LessThanOrEqualTo(canvas.worldBound.yMax));
                }
                Click(root.Q<Button>("story-map-select-d"));
                Assert.That(root.Q("story-map-card-d").ClassListContains("sm-selected"), Is.True);
                Assert.That(root.Q("story-map-card-d").style.opacity.value, Is.EqualTo(1));
                Assert.That(root.Q("story-map-detail").Query<Label>().ToList().Any(x => x.text == "참여 중인 스토리라인이 없습니다."), Is.True);
                Click(root.Q<Button>("story-map-select-a"));
                Assert.That(root.Q("story-map-detail").Query<Button>(className: "championship-link").ToList().Any(x => x.text == "월드 챔피언십"), Is.True);
                Assert.That(root.Q("story-map-detail").Query<Label>().ToList().Any(x => x.text == "파트너"), Is.True);
                Click(root.Q<Button>("story-map-fit"));
                save.Storylines.Add(story); DashboardSession.SetActiveSave(save, true);
                Assert.That(root.Q("story-map-card-d"), Is.Not.Null);
                Assert.That(root.Query<Button>(className: "sm-line-target").ToList(), Has.Count.EqualTo(1));
                Click(root.Q<Button>("story-map-select-d"));
                Assert.That(root.Q("story-map-detail").Query<Label>().ToList().Any(x => x.text == "참여 중인 스토리라인이 없습니다."), Is.True);
                story.Status = StorylineStatus.Completed; DashboardSession.SetActiveSave(save, true);
                Assert.That(root.Query<VisualElement>(className: "sm-wrestler").ToList(), Has.Count.EqualTo(4));
                Assert.That(root.Query<Button>(className: "sm-line-target").ToList(), Is.Empty);
                DashboardSession.SetActiveSave(new GameSave { Promotion = new PromotionState { Id = "p" } }, true);
                Assert.That(root.Q<StorylineMapCanvas>(), Is.Null);
                Assert.That(root.Query<Label>().ToList().Any(x => x.text == "표시할 선수가 없습니다."), Is.True);
            }
            finally { window.Close(); }
        }

        [UnityTest]
        public IEnumerator IndependentStoryParticipants_AlignWithoutOverlapAndReopenDeterministically()
        {
            var save = Sample(); save.Storylines.Clear(); save.TagTeams.Clear(); save.Championships.Clear(); save.MatchPlans.Clear(); save.StoryRelationships.Clear();
            foreach (var id in new[] { "e", "f" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "p" });
            save.Stables.Add(new StableState { Id = "stable", PromotionId = "p", Name = "스테이블", MemberIds = { "a", "b", "c" } });
            save.Storylines.Add(new StorylineState { Id = "bridge", PromotionId = "p", FirstWrestlerId = "a", SecondWrestlerId = "d" });
            save.Storylines.Add(new StorylineState { Id = "chain", PromotionId = "p", FirstWrestlerId = "d", SecondWrestlerId = "e" });
            DashboardSession.SetActiveSave(save, true);
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility(); window.position = new Rect(24, 60, 1200, 800);
            window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
            var root = StorylineMapView.Open(window.rootVisualElement, () => { });
            try
            {
                for (var i = 0; i < 8; i++) yield return null;
                var first = root.Q("story-map-card-d").worldBound; var second = root.Q("story-map-card-e").worldBound;
                Assert.That(Mathf.Abs(first.center.x - second.center.x) < .05f || Mathf.Abs(first.center.y - second.center.y) < .05f, Is.True);
                var edges = (System.Collections.IEnumerable)typeof(StorylineMapCanvas).GetField("edges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(root.Q<StorylineMapCanvas>());
                var line = edges.Cast<object>().Single(e => ((Button)e.GetType().GetField("Label").GetValue(e)).name == "story-map-line-chain");
                Assert.That((Vector2[])line.GetType().GetField("Points").GetValue(line), Has.Length.EqualTo(2));
                var cards = root.Query<VisualElement>(className: "sm-wrestler").ToList().OrderBy(c => c.name, StringComparer.Ordinal).ToList();
                for (var i = 0; i < cards.Count; i++)
                for (var j = i + 1; j < cards.Count; j++) Assert.That(cards[i].worldBound.Overlaps(cards[j].worldBound), Is.False);
                var world = root.Q("story-map-world"); var positions = cards.Select(c => world.WorldToLocal(c.worldBound).position).ToArray();
                var before = JsonUtility.ToJson(save);
                DashboardSession.SetActiveSave(save, true);
                for (var i = 0; i < 8; i++) yield return null;
                world = root.Q("story-map-world");
                var reopened = root.Query<VisualElement>(className: "sm-wrestler").ToList().OrderBy(c => c.name, StringComparer.Ordinal).Select(c => world.WorldToLocal(c.worldBound).position).ToArray();
                Assert.That(reopened.Length, Is.EqualTo(positions.Length));
                for (var i = 0; i < positions.Length; i++) Assert.That(Vector2.Distance(positions[i], reopened[i]), Is.LessThan(.01f));
                Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            }
            finally { window.Close(); }
        }

        [UnityTest]
        public IEnumerator ConnectedOpponents_StayAdjacentAcrossRowBreaks()
        {
            var save = Sample(); save.Storylines.Clear(); save.TagTeams.Clear(); save.Championships.Clear(); save.MatchPlans.Clear(); save.StoryRelationships.Clear();
            foreach (var id in new[] { "e", "f", "g", "h" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "p" });
            var ids = save.Wrestlers.Select(w => w.Id).ToArray();
            for (var i = 1; i < ids.Length; i++) save.Storylines.Add(new StorylineState { Id = "chain-" + i, PromotionId = "p", FirstWrestlerId = ids[i - 1], SecondWrestlerId = ids[i] });
            DashboardSession.SetActiveSave(save, true);
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility(); window.position = new Rect(24, 60, 1400, 860);
            var root = StorylineMapView.Open(window.rootVisualElement, () => { });
            try
            {
                for (var i = 0; i < 8; i++) yield return null;
                var world = root.Q("story-map-world");
                Rect Bounds(string id) => world.WorldToLocal(root.Q("story-map-card-" + id).worldBound);
                var distance = 0f;
                foreach (var story in save.Storylines)
                {
                    var a = Bounds(story.FirstWrestlerId); var b = Bounds(story.SecondWrestlerId);
                    var delta = b.center - a.center; distance += Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
                    Assert.That(Mathf.Abs(delta.x) < 1 || Mathf.Abs(delta.y) < 1, Is.True, "Connected opponents should share a clear horizontal or vertical corridor.");
                }
                Assert.That(distance, Is.LessThan(1500), "Avoid the long return from the end of one row to the start of the next.");
                var cards = ids.Select(Bounds).ToList();
                for (var i = 0; i < cards.Count; i++)
                for (var j = i + 1; j < cards.Count; j++) Assert.That(cards[i].Overlaps(cards[j]), Is.False);
            }
            finally { window.Close(); }
        }

        [Test]
        public void MemberSlots_AccountForBlockedOpponentConnections()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "p" }, CurrentDate = new GameDate(2026, 11, 16) };
            string Id(int i) => "w-" + i.ToString("D2");
            for (var i = 0; i < 30; i++) save.Wrestlers.Add(new WrestlerState { Id = Id(i), PromotionId = "p" });
            string Stable(int start, int count)
            {
                var id = "stable-" + start;
                save.Stables.Add(new StableState { Id = id, PromotionId = "p", Name = id, MemberIds = Enumerable.Range(start, count).Select(Id).ToList() });
                return id;
            }
            string Team(int start)
            {
                var id = "team-" + start;
                save.TagTeams.Add(new TagTeamState { Id = id, PromotionId = "p", Name = id, MemberIds = { Id(start), Id(start + 1) } });
                return id;
            }
            void Groups(string first, string second, StoryPartyType type = StoryPartyType.Stable) => save.Storylines.Add(new StorylineState {
                Id = "s-" + save.Storylines.Count.ToString("D2"), PromotionId = "p", PartyType = type, FirstGroupId = first, SecondGroupId = second });
            void Duel(int first, int second) => save.Storylines.Add(new StorylineState {
                Id = "s-" + save.Storylines.Count.ToString("D2"), PromotionId = "p", FirstWrestlerId = Id(first), SecondWrestlerId = Id(second) });
            var a = Stable(0, 4); var b = Stable(4, 7); var c = Stable(11, 5);
            Groups(a, b); Groups(b, c); Groups(c, a);
            var ta = Team(0); var tb = Team(4); var tc = Team(11);
            Groups(ta, tb, StoryPartyType.TagTeam); Groups(tb, tc, StoryPartyType.TagTeam);
            var td = Team(16); var te = Team(18); var tf = Team(20);
            Groups(td, te, StoryPartyType.TagTeam); Groups(te, tf, StoryPartyType.TagTeam);
            foreach (var pair in new[] { (2, 3), (6, 7), (8, 9), (13, 15), (3, 10), (9, 14), (2, 22), (6, 23), (14, 24),
                (16, 25), (18, 26), (20, 27), (24, 28), (22, 29), (22, 23), (25, 26), (27, 28), (28, 29) }) Duel(pair.Item1, pair.Item2);
            foreach (var wrestler in save.Wrestlers) PWManager.Domain.Services.PortraitAppearanceRules.Ensure(wrestler);
            var before = JsonUtility.ToJson(save);
            var canvas = new StorylineMapCanvas(StorylineMapData.Build(save), (_, _, _) => { }, () => { });
            var edges = ((IEnumerable)typeof(StorylineMapCanvas).GetField("edges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(canvas)).Cast<object>();
            var edge = edges.Single(e => ((Button)e.GetType().GetField("Label").GetValue(e)).name == "story-map-line-s-11");
            var path = (Vector2[])edge.GetType().GetField("Points").GetValue(edge);
            Assert.That(path, Has.Length.EqualTo(2), "Face opponents toward one another instead of routing along an unrelated team's border.");
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void StableMemberPlacement_KeepsLinksShortAndPreservesTeamsAndSaveData()
        {
            var save = Sample(); save.Storylines.Clear(); save.Championships.Clear(); save.MatchPlans.Clear(); save.StoryRelationships.Clear();
            foreach (var id in new[] { "e", "f", "g", "h", "i", "j", "k", "l" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "p" });
            save.Stables.Add(new StableState { Id = "first", PromotionId = "p", Name = "첫 스테이블", MemberIds = { "a", "b", "c", "d", "e" } });
            save.Stables.Add(new StableState { Id = "second", PromotionId = "p", Name = "둘째 스테이블", MemberIds = { "f", "g", "h", "i", "j" } });
            save.TagTeams.Add(new TagTeamState { Id = "other-team", PromotionId = "p", Name = "다른 팀", MemberIds = { "f", "g" } });
            save.Storylines.Add(new StorylineState { Id = "00-groups", PromotionId = "p", PartyType = StoryPartyType.Stable, FirstGroupId = "first", SecondGroupId = "second" });
            save.Storylines.Add(new StorylineState { Id = "01-external", PromotionId = "p", FirstWrestlerId = "c", SecondWrestlerId = "j" });
            save.Storylines.Add(new StorylineState { Id = "02-internal", PromotionId = "p", FirstWrestlerId = "d", SecondWrestlerId = "e" });
            save.Storylines.Add(new StorylineState { Id = "03-vertical", PromotionId = "p", FirstWrestlerId = "i", SecondWrestlerId = "k" });
            save.Storylines.Add(new StorylineState { Id = "04-other-vertical", PromotionId = "p", FirstWrestlerId = "e", SecondWrestlerId = "l" });
            foreach (var wrestler in save.Wrestlers) PWManager.Domain.Services.PortraitAppearanceRules.Ensure(wrestler);
            var before = JsonUtility.ToJson(save);
            var canvas = new StorylineMapCanvas(StorylineMapData.Build(save), (_, _, _) => { }, () => { });
            var edges = ((IEnumerable)typeof(StorylineMapCanvas).GetField("edges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(canvas)).Cast<object>().ToList();
            Vector2[] Path(string id)
            {
                var edge = edges.Single(e => ((Button)e.GetType().GetField("Label").GetValue(e)).name == "story-map-line-" + id);
                return (Vector2[])edge.GetType().GetField("Points").GetValue(edge);
            }
            var inside = Path("02-internal"); var outside = Path("01-external"); var vertical = Path("03-vertical");
            Assert.That(inside, Has.Length.EqualTo(2));
            Assert.That(outside, Has.Length.EqualTo(2), "Reorder members and choose a clear port rather than weaving around other relationship lines.");
            Assert.That(Vector2.Distance(outside[0], outside[1]), Is.LessThan(350));
            Assert.That(vertical, Has.Length.EqualTo(2));
            Assert.That(vertical[0].x, Is.EqualTo(vertical[1].x));
            var otherVertical = Path("04-other-vertical");
            Assert.That(otherVertical, Has.Length.EqualTo(2));
            Assert.That(otherVertical[0].x, Is.EqualTo(otherVertical[1].x));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator StableBox_ContainsTagTeamAndUniquePortraitsWithLinesOutsideCards()
        {
            var save = Sample();
            save.Stables.Add(new StableState { Id = "stable", PromotionId = "p", Name = "스테이블", MemberIds = { "a", "b", "c" } });
            DashboardSession.SetActiveSave(save, true);
            var window = ScriptableObject.CreateInstance<EditorWindow>(); window.ShowUtility(); window.position = new Rect(24, 60, 1200, 800);
            window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
            var root = StorylineMapView.Open(window.rootVisualElement, () => { });
            try
            {
                for (var i = 0; i < 8; i++) yield return null;
                Assert.That(root.Query<VisualElement>(className: "sm-wrestler").ToList(), Has.Count.EqualTo(4));
                Assert.That(root.Q("story-map-group-stable").Contains(root.Q("story-map-group-team")), Is.True);
                var team = root.Q("story-map-group-team");
                Assert.That(team.worldBound.Contains(root.Q("story-map-card-a").worldBound.center), Is.True);
                Assert.That(team.worldBound.Contains(root.Q("story-map-card-b").worldBound.center), Is.True);
                Assert.That(team.worldBound.Contains(root.Q("story-map-card-c").worldBound.center), Is.False);
                var canvas = root.Q<StorylineMapCanvas>(); var world = root.Q("story-map-world");
                var edgeList = (System.Collections.IEnumerable)typeof(StorylineMapCanvas).GetField("edges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(canvas);
                var cardBounds = root.Query<VisualElement>(className: "sm-wrestler").ToList().Select(x =>
                    new Rect(x.worldBound.x + 2, x.worldBound.y + 2, x.worldBound.width - 4, x.worldBound.height - 4)).ToList();
                foreach (var edge in edgeList)
                {
                    var points = (Vector2[])edge.GetType().GetField("Points").GetValue(edge);
                    for (var p = 1; p < points.Length; p++)
                    for (var step = 0; step <= 20; step++)
                    {
                        var point = world.LocalToWorld(Vector2.Lerp(points[p - 1], points[p], step / 20f));
                        Assert.That(cardBounds.Any(rect => rect.Contains(point)), Is.False, "A storyline line crosses a portrait or name.");
                    }
                }
            }
            finally { window.Close(); }
        }

        [Test]
        public void LinePicking_HasScreenSpaceToleranceAndClampedSegmentProjection()
        {
            var path = new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100) };
            Assert.That(StorylineMapCanvas.DistanceToEdge(new Vector2(50, 11), path), Is.EqualTo(11).Within(.01));
            Assert.That(StorylineMapCanvas.DistanceToEdge(new Vector2(108, 70), path), Is.EqualTo(8).Within(.01));
            Assert.That(StorylineMapCanvas.DistanceToEdge(new Vector2(-10, 0), path), Is.EqualTo(10).Within(.01));
            var diagonal = new[] { Vector2.zero, new Vector2(100, 100) };
            Assert.That(StorylineMapCanvas.DistanceToEdge(new Vector2(50, 65), diagonal), Is.EqualTo(15 / Mathf.Sqrt(2)).Within(.01));
            Assert.That(StorylineMapCanvas.DistanceToEdge(new Vector2(110, 110), diagonal), Is.EqualTo(Mathf.Sqrt(200)).Within(.01));
        }

        [Test]
        public void DashboardMenu_OpensListFirstAndSupportsMapProfileAndHistory()
        {
            var save = ShowTestSaveFactory.Create(Resources.Load<PWManager.Data.Catalogs.StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"), 20261102);
            var first = save.Wrestlers[0]; var second = save.Wrestlers[1];
            save.Storylines.Add(new StorylineState { Id = Guid.NewGuid().ToString(), PromotionId = save.Promotion.Id,
                Name = "메뉴 검증", FirstWrestlerId = first.Id, SecondWrestlerId = second.Id, StartedDate = save.CurrentDate });
            DashboardSession.SetActiveSave(save, true);
            var owner = new GameObject("Storyline navigation test");
            DashboardController dashboard = null; WrestlerProfileController profile = null;
            try
            {
                var document = owner.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard");
                dashboard = owner.AddComponent<DashboardController>(); profile = owner.AddComponent<WrestlerProfileController>();
                Invoke(dashboard, "OnEnable"); Invoke(profile, "OnEnable");
                var root = document.rootVisualElement;
                void Menu(string name)
                {
                    var button = root.Q<Button>(name);
                    using var click = ClickEvent.GetPooled(); click.target = button; button.SendEvent(click);
                }
                Menu("nav-story"); Assert.That(root.Q("storyline-map"), Is.Not.Null);
                Assert.That(root.Q("story-tabs").Children().First().name, Is.EqualTo("story-tab-map"));
                Assert.That(root.Q<Button>("story-tab-map").ClassListContains("ops-active"), Is.True);
                Click(root.Q<Button>("story-tab-management")); Assert.That(root.Q("story-management"), Is.Not.Null);
                Assert.That(root.Q<ScrollView>("story-list"), Is.Not.Null);
                Assert.That(root.Q<ScrollView>("story-detail"), Is.Not.Null);
                Assert.That(root.Q<Button>("story-tab-management").ClassListContains("ops-active"), Is.True);
                Click(root.Q<Button>("story-tab-map")); Assert.That(root.Q("storyline-map"), Is.Not.Null);
                Click(root.Q("story-map-card-" + first.Id).Q<Button>(className: "sm-name"));
                Assert.That(root.Q<Label>("wp-name").text, Is.EqualTo(first.Identity.RingName));
                Assert.That(root.Q("storyline-map"), Is.Null);
                Invoke(dashboard, "NavigateBack"); Assert.That(root.Q("storyline-map"), Is.Not.Null);
                Invoke(dashboard, "NavigateBack"); Assert.That(root.Q("story-management"), Is.Not.Null);
                Invoke(dashboard, "NavigateForward"); Assert.That(root.Q("storyline-map"), Is.Not.Null);
                Menu("nav-home"); Assert.That(root.Q("storyline-map"), Is.Null);
                Menu("nav-locker"); Assert.That(root.Q("locker-room"), Is.Not.Null);
                Click(root.Q("locker-room-card-" + first.Id).Q<Button>(className: "sm-name"));
                Assert.That(root.Q("locker-room"), Is.Null);
                Invoke(dashboard, "NavigateBack"); Assert.That(root.Q("locker-room"), Is.Not.Null);
                Menu("nav-home"); Assert.That(root.Q("locker-room"), Is.Null);
            }
            finally
            {
                if (profile != null) Invoke(profile, "OnDisable");
                if (dashboard != null) Invoke(dashboard, "OnDisable");
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [UnityTest]
        public IEnumerator View_SelectionFiltersZoomFitAndResponsiveLayoutWorkOnAttachedPanel()
        {
            var save = Sample();
            DashboardSession.SetActiveSave(save, true);
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.ShowUtility(); window.position = new Rect(24, 60, 1400, 860);
            var host = window.rootVisualElement;
            host.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
            var managed = false;
            var root = StorylineMapView.Open(host, () => managed = true);
            try
            {
                foreach (var size in new[] { new Vector2(1400, 860), new Vector2(960, 640), new Vector2(650, 700) })
                {
                    window.position = new Rect(24, 60, size.x, size.y); window.Focus(); window.Repaint();
                    for (var frame = 0; frame < 8; frame++) yield return null;
                    var canvas = root.Q<StorylineMapCanvas>(); var detail = root.Q<ScrollView>("story-map-detail");
                    Assert.That(canvas.worldBound.width, Is.GreaterThan(200)); Assert.That(canvas.worldBound.height, Is.GreaterThan(140));
                    Assert.That(detail.worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax + 1));
                    Assert.That(detail.worldBound.yMax, Is.LessThanOrEqualTo(root.worldBound.yMax + 1));
                    Assert.That(canvas.worldBound.xMax <= detail.worldBound.xMin + 1 || canvas.worldBound.yMax <= detail.worldBound.yMin + 1, Is.True);
                    Click(root.Q<Button>("story-map-select-a"));
                    Assert.That(root.Q("story-map-card-a").ClassListContains("sm-face"), Is.True);
                    Assert.That(root.Q("story-map-card-b").ClassListContains("sm-heel"), Is.True);
                    Assert.That(root.Q("story-map-card-c").ClassListContains("sm-tweener"), Is.True);
                    ColorUtility.TryParseHtmlString("#68b8ff", out var faceColor);
                    Assert.That(root.Q("story-map-card-a").resolvedStyle.borderLeftColor, Is.EqualTo(faceColor));
                    Assert.That(detail.Query<Button>(className: "sm-story-link").ToList(), Has.Count.EqualTo(2));
                    using (var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
                    {
                        escape.target = root.Q<Button>("story-map-select-a");
                        root.Q<Button>("story-map-select-a").SendEvent(escape);
                    }
                    Assert.That(detail.Query<Label>().ToList().Any(x => x.text == "관계 살펴보기"), Is.True);
                    Assert.That(root.Query<VisualElement>(className: "sm-wrestler").ToList(), Has.Count.EqualTo(4), "Every roster wrestler appears once, including those without a story.");
                    Assert.That(root.Query<Button>(className: "sm-line-target").ToList(), Has.Count.EqualTo(2), "One line represents one storyline.");
                    Assert.That(root.Query<Button>(className: "sm-line-target").ToList().All(x => string.IsNullOrEmpty(x.text)), Is.True);
                    Click(root.Q<Button>("story-map-line-s1"));
                    Assert.That(detail.Query<Label>().ToList().Any(x => x.text == WrestlerNameText.Format("선수 A vs 선수 B", save.Wrestlers)), Is.True);
                    Assert.That(detail.Query<Button>(className: "championship-link").ToList().Any(x => x.text.Contains("월드 챔피언십")), Is.True);
                    Assert.That(detail.Query<Label>().ToList().Any(x => x.text.Contains("도전")), Is.True);
                    root.Q<DropdownField>("story-map-relations").index = 2;
                    Assert.That(root.Q<Button>("story-map-line-s1").style.display.value, Is.EqualTo(DisplayStyle.None));
                    root.Q<DropdownField>("story-map-relations").index = 1;
                    Assert.That(root.Q<Button>("story-map-line-s1").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                    Assert.That(root.Q<Button>("story-map-line-s2").style.display.value, Is.EqualTo(DisplayStyle.None));
                    root.Q<DropdownField>("story-map-relations").index = 0;
                    var beforeZoom = canvas.Zoom; Click(root.Q<Button>("story-map-zoom-in"));
                    Assert.That(canvas.Zoom, Is.GreaterThan(beforeZoom)); Click(root.Q<Button>("story-map-zoom-out"));
                    Assert.That(canvas.Zoom, Is.EqualTo(beforeZoom).Within(.001));
                    var world = root.Q("story-map-world"); var beforePan = world.style.translate.value;
                    var blank = canvas.worldBound.position + new Vector2(8, 8);
                    Pointer(canvas, EventType.MouseDown, blank); Pointer(canvas, EventType.MouseMove, blank + new Vector2(60, 35));
                    Pointer(canvas, EventType.MouseUp, blank + new Vector2(60, 35));
                    Assert.That(world.style.translate.value.x.value, Is.EqualTo(beforePan.x.value + 60).Within(.1));
                    Assert.That(world.style.translate.value.y.value, Is.EqualTo(beforePan.y.value + 35).Within(.1));
                    var wheelBefore = canvas.Zoom;
                    using (var wheel = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = blank, delta = new Vector2(0, -1) }))
                    { wheel.target = canvas; canvas.SendEvent(wheel); }
                    Assert.That(canvas.Zoom, Is.GreaterThan(wheelBefore));
                    Click(root.Q<Button>("story-map-select-a"));
                    Pointer(canvas, EventType.MouseDown, blank); Pointer(canvas, EventType.MouseUp, blank);
                    Assert.That(detail.Query<Label>().ToList().Any(x => x.text == "관계 살펴보기"), Is.True);
                    var dropdown = root.Q<DropdownField>("story-map-stories"); dropdown.index = 2;
                    Assert.That(detail.Query<Label>().ToList().Any(x => x.text == WrestlerNameText.Format("선수 A vs 선수 C", save.Wrestlers)), Is.True);
                    dropdown.index = 0; Click(root.Q<Button>("story-map-fit"));
                    Assert.That(canvas.Zoom, Is.GreaterThan(0)); canvas.ClearSelection();
                    Assert.That(detail.Query<Label>().ToList().Any(x => x.text == "관계 살펴보기"), Is.True);
                    dropdown.index = 1;
                }
                Click(root.Q<Button>("story-tab-management")); Assert.That(managed, Is.True);
                DashboardSession.SetActiveSave(new GameSave { Promotion = new PromotionState { Id = "p" } }, true);
                Assert.That(root.Q<StorylineMapCanvas>(), Is.Null);
                Assert.That(root.Q<Button>("story-map-empty-manage"), Is.Not.Null);
            }
            finally { window.Close(); }
        }

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button.clickable, new object[] { null });

        private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static void Pointer(VisualElement target, EventType type, Vector2 position)
        {
            var source = new Event { type = type, button = 0, mousePosition = position };
            using EventBase evt = type == EventType.MouseDown ? PointerDownEvent.GetPooled(source)
                : type == EventType.MouseUp ? PointerUpEvent.GetPooled(source) : PointerMoveEvent.GetPooled(source);
            evt.target = target; target.SendEvent(evt);
        }

    }
}
