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

namespace PWManager.Tests
{
    public sealed class WrestlerRelationshipsViewTests
    {
        [UnityTest]
        public IEnumerator Gradient_FollowsWrestlerDuringPanAndZoom_WhileNeutralShelfStaysInPlace()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = 20 });
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.ShowUtility(); window.position = new Rect(24, 60, 1200, 800);
            window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PWManagerTheme"));
            var root = WrestlerRelationshipsView.Open(window.rootVisualElement, save, "a");
            try
            {
                for (var frame = 0; frame < 8; frame++) yield return null;
                var map = root.Q("wr-map");
                var background = root.Q<Image>("wr-map-gradient");
                var center = root.Q("wr-center-a");
                var shelf = root.Q("wr-neutral-shelf");
                Assert.That(background.image, Is.Not.Null);
                var shelfBounds = shelf.worldBound;
                float CenterColorCoordinate()
                {
                    var y = background.WorldToLocal(center.worldBound.center).y;
                    return background.uv.yMax - y / background.contentRect.height * background.uv.height;
                }
                Assert.That(CenterColorCoordinate(), Is.EqualTo(.5f).Within(.02f));
                var before = background.uv;
                map.GetType().GetMethod("ZoomAt").Invoke(map, new object[] { new Vector2(30, 30), 1.4f });
                yield return null;
                Assert.That(background.uv.height, Is.LessThan(before.height));
                Assert.That(CenterColorCoordinate(), Is.EqualTo(.5f).Within(.02f));

                before = background.uv;
                var pan = map.GetType().GetField("pan", BindingFlags.Instance | BindingFlags.NonPublic);
                pan.SetValue(map, (Vector2)pan.GetValue(map) + new Vector2(60, 90));
                map.GetType().GetMethod("ApplyTransform", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(map, null);
                yield return null;
                Assert.That(background.uv.y, Is.Not.EqualTo(before.y));
                Assert.That(CenterColorCoordinate(), Is.EqualTo(.5f).Within(.02f));
                Assert.That(shelf.worldBound, Is.EqualTo(shelfBounds));
            }
            finally { window.Close(); }
        }

        [Test]
        public void PortraitSelection_UpdatesPairDetailWithoutRebuildingPersonalMap()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "c", RingName = "Charlie" } });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -20 });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "c", Value = 10 });
            save.Storylines.Add(new StorylineState { Id = "past", Status = StorylineStatus.Completed,
                FirstWrestlerId = "a", SecondWrestlerId = "c", History = {
                    new StoryEventState { Id = "rescue", ActorId = "a", TargetId = "c", Incident = StoryIncident.Rescue,
                        Date = save.CurrentDate } } });
            var before = UnityEngine.JsonUtility.ToJson(save);
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            var row = root.Q("wr-node-b");

            Click(root.Q<Button>("wr-select-c"));

            Assert.That(root.Q("wr-node-b"), Is.SameAs(row));
            Assert.That(root.Q("wr-node-c").ClassListContains("wr-selected"), Is.True);
            Assert.That(row.ClassListContains("wr-selected"), Is.False);
            Assert.That(root.Q("wr-detail").Query<Label>().ToList().Any(x => x.text.EndsWith("+10")), Is.True);
            Assert.That(root.Q<Label>("wr-pair-status").text, Is.EqualTo("우호"));
            Assert.That(root.Q("wr-pair-history").Query<VisualElement>(className: "story-event").ToList(), Has.Count.EqualTo(1));
            Assert.That(root.Q("wr-pair-history").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(2));
            Assert.That(UnityEngine.JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void PersonalMap_ShowsCurrentTeammateOnCenterRowWithoutInventingStoryEdges()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -35 });
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "c", RingName = "Unrelated" } });
            save.TagTeams.Add(new TagTeamState { Id = "team", Name = "Partners", Status = TagTeamStatus.Active,
                MemberIds = { "a", "b" } });
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "d", RingName = "Stable member" } });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "d", Value = 30 });
            save.Stables.Add(new StableState { Id = "stable", Name = "Stable", Status = TagTeamStatus.Active, MemberIds = { "a", "b", "d" } });
            foreach (var wrestler in save.Wrestlers) PortraitAppearanceRules.Ensure(wrestler);
            var before = UnityEngine.JsonUtility.ToJson(save);
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            Assert.That(root.Q("wr-center-a"), Is.Not.Null);
            Assert.That(root.Q("wr-node-b"), Is.Not.Null);
            Assert.That(root.Q("wr-neutral-players").Q("wr-node-c"), Is.Null);
            Assert.That(root.Q("wr-edge-b"), Is.Null);
            Assert.That(root.Q("wr-node-b").style.top.value.value, Is.EqualTo(root.Q("wr-center-a").style.top.value.value));
            Assert.That(root.Q("wr-node-d").style.top.value.value, Is.EqualTo(root.Q("wr-center-a").style.top.value.value));
            Assert.That(root.Q("wr-membership-band"), Is.Not.Null);
            Click(root.Q<Button>("wr-select-b"));
            Assert.That(root.Q("wr-node-b").ClassListContains("wr-selected"), Is.True);
            Assert.That(root.Q("wr-tag-frame"), Is.Not.Null);
            Assert.That(root.Q("wr-detail").Query<Label>().ToList().Any(x => x.text.Contains("Partners")), Is.True);
            Assert.That(root.Q("wr-pair-history").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(2));
            Assert.That(UnityEngine.JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void PersonalMap_ManyRelationshipsKeepEveryPortraitSeparate()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            for (var i = 0; i < 25; i++)
            {
                var id = "related-" + i;
                save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = id, RingName = id } });
                save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = id, Value = i - 12 });
            }
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            var nodes = root.Q("wr-map").Query<VisualElement>(className: "sm-wrestler").ToList();
            Assert.That(nodes, Has.Count.EqualTo(25));
            Assert.That(root.Query<Button>(className: "wr-edge").ToList(), Is.Empty);
            for (var i = 0; i < nodes.Count; i++)
            for (var j = i + 1; j < nodes.Count; j++)
            {
                UnityEngine.Rect Bounds(VisualElement node) => new(node.style.left.value.value, node.style.top.value.value,
                    node.style.width.value.value, node.style.height.value.value);
                Assert.That(Bounds(nodes[i]).Overlaps(Bounds(nodes[j])), Is.False);
            }
        }

        [Test]
        public void AffinityLayout_PlacesFriendsAboveEnemiesBelowWithoutUsingMagnitude()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            foreach (var (id, value) in new[] { ("near-friend", 10f), ("far-friend", 80f), ("near-enemy", -10f), ("far-enemy", -80f) })
            {
                save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = id, RingName = id } });
                save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = id, Value = value });
            }
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            float Y(string id) => root.Q(id).style.top.value.value;
            var center = Y("wr-center-a");
            Assert.That(Y("wr-node-near-friend"), Is.LessThan(center));
            Assert.That(Y("wr-node-far-friend"), Is.EqualTo(Y("wr-node-near-friend")));
            Assert.That(Y("wr-node-near-enemy"), Is.GreaterThan(center));
            Assert.That(Y("wr-node-far-enemy"), Is.EqualTo(Y("wr-node-near-enemy")));
            Assert.That(center - Y("wr-node-far-friend"), Is.EqualTo(Y("wr-node-far-enemy") - center).Within(.01f));
        }

        [Test]
        public void ZeroAffinity_GroupMemberRemainsInCentralStableBox()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = 0 });
            save.Storylines.Add(new StorylineState { Id = "active", FirstWrestlerId = "a", SecondWrestlerId = "b", Status = StorylineStatus.Active });
            save.Stables.Add(new StableState { Id = "stable", Name = "Stable", Status = TagTeamStatus.Active, MemberIds = { "a", "b" } });
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            Assert.That(root.Q("wr-center-a"), Is.Not.Null);
            Assert.That(root.Q("wr-node-b"), Is.Not.Null);
            Assert.That(root.Q("wr-edge-b"), Is.Null);
            Assert.That(root.Q("wr-membership-band"), Is.Not.Null);
            Assert.That(root.Q("wr-node-b").style.top.value.value, Is.EqualTo(root.Q("wr-center-a").style.top.value.value));
            Assert.That(root.Q("wr-neutral-players").Q("wr-node-b"), Is.Null);
        }

        [Test]
        public void Connections_OnlyLinkActiveStoriesWithoutButtons_AndPortraitShowsFullPairHistory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "c", RingName = "Past opponent" } });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = 25 });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "c", Value = -25 });
            save.Storylines.Add(new StorylineState { Id = "active", FirstWrestlerId = "a", SecondWrestlerId = "b", Status = StorylineStatus.Active });
            save.Storylines.Add(new StorylineState { Id = "old", FirstWrestlerId = "a", SecondWrestlerId = "c", Status = StorylineStatus.Completed });
            save.Storylines.Add(new StorylineState { Id = "past-pair", FirstWrestlerId = "a", SecondWrestlerId = "b", Status = StorylineStatus.Completed,
                History = { new StoryEventState { Id = "prior-attack", ActorId = "a", TargetId = "b", Incident = StoryIncident.Attack, Date = save.CurrentDate } } });
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            Assert.That(root.Q("wr-node-c"), Is.Not.Null);
            Assert.That(root.Q("wr-edge-c"), Is.Null);
            Assert.That(ConnectedIds(root), Is.EqualTo(new[] { "b" }));
            Assert.That(root.Q<Button>("wr-edge-b"), Is.Null);
            Click(root.Q<Button>("wr-select-c"));
            Click(root.Q<Button>("wr-select-b"));
            Assert.That(root.Q("wr-node-b").ClassListContains("wr-selected"), Is.True);
            Assert.That(root.Q("wr-map").Query<Button>(className: "wr-edge").ToList(), Is.Empty);
            Assert.That(root.Q("wr-pair-history").Query<VisualElement>(className: "story-event").ToList(), Has.Count.EqualTo(1));
            Assert.That(root.Q("wr-pair-history").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(2));
        }

        [Test]
        public void NeutralRoster_UsesBottomShelfAndOpensPairDetailsWithoutAConnection()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            Assert.That(root.Q("wr-map").Q("wr-node-b"), Is.Null);
            Assert.That(root.Q("wr-neutral-players").Q("wr-node-b"), Is.Not.Null);
            Assert.That(root.Q<Image>("wr-map-gradient"), Is.Not.Null);
            Assert.That(ConnectedIds(root), Is.Empty);
            root.Q<DropdownField>("wr-filter").index = 3;
            Assert.That(root.Q("wr-neutral-players").Q("wr-node-b"), Is.Not.Null);
            Click(root.Q<Button>("wr-select-b"));
            Assert.That(root.Q("wr-neutral-players").Q("wr-node-b").ClassListContains("wr-selected"), Is.True);
            Assert.That(root.Q("wr-pair-history").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(2));
        }

        [Test]
        public void ProfileRelations_ShowStoredAffinityWithoutAnActiveStory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Wrestlers[0].Identity.RingName = "Alpha";
            save.Wrestlers[1].Identity.RingName = "Bravo";
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "b", SecondWrestlerId = "a", Value = -35 });
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            Assert.That(root.Q("wr-node-b"), Is.Not.Null);
            Assert.That(root.Q<Label>("wr-affinity-b").text, Is.EqualTo("우호도 -35"));
            Assert.That(root.Q<VisualElement>("wr-detail").Query<Label>().ToList().Any(x => x.text == "우호도 -35"), Is.True);
            Assert.That(save.Storylines, Is.Empty);
        }

        [Test]
        public void SharedMatch_NonzeroAffinityOpponentShowsHistory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var match = new MatchResultState { Id = "shared-match", MatchPlanId = save.MatchPlans[0].Id,
                WinnerId = "a", FinishPerformerId = "a", LoserTargetId = "b", FinalMatchQuality = 12 };
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, new[] { match }, Array.Empty<PromoResultState>());
            save.MatchResults.Add(match);
            StorylineService.Apply(save, output, save.CurrentDate);
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = 1 });
            save.Wrestlers.Add(new WrestlerState { Identity = new WrestlerIdentityState { Id = "c", RingName = "Charlie" } });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "c", Value = 20 });
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            Assert.That(root.Q("wr-node-b"), Is.Not.Null);
            Assert.That(root.Q<Label>("wr-pair-matches").text, Is.EqualTo("공동 경기 1회"));
            Assert.That(root.Q("wr-detail").Query<VisualElement>(className: "story-event").ToList(), Has.Count.EqualTo(1));
            Click(root.Q<Button>("wr-select-c"));
            Assert.That(root.Q("wr-detail").Query<Label>().ToList().Any(x => x.text == "우호도 +20"), Is.True);
            Assert.That(root.Q("wr-detail").Query<VisualElement>(className: "story-event").ToList(), Is.Empty);
            Click(root.Q<Button>("wr-select-b"));
            Assert.That(root.Q("wr-detail").Query<VisualElement>(className: "story-event").ToList(), Has.Count.EqualTo(1));
            Assert.That(save.Storylines, Is.Empty);
        }

        [Test]
        public void SearchAndAffinityFilter_ClearHiddenSelection()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Wrestlers[1].Identity.RingName = "Bravo";
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -20 });
            var root = WrestlerRelationshipsView.Open(new VisualElement(), save, "a");
            root.Q<DropdownField>("wr-filter").index = 1;
            Assert.That(root.Q("wr-node-b"), Is.Null);
            Assert.That(root.Q("wr-detail").Query<VisualElement>(className: "story-event").ToList(), Is.Empty);
            root.Q<DropdownField>("wr-filter").index = 0;
            root.Q<TextField>("wr-search").value = "brav";
            Assert.That(root.Q("wr-node-b"), Is.Not.Null);
            root.Q<TextField>("wr-search").value = "nobody";
            Assert.That(root.Q("wr-node-b"), Is.Null);
        }

        [Test]
        public void ReopeningRelationships_ReplacesTheExistingPanelAndShowsNeutralRoster()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var host = new VisualElement();
            WrestlerRelationshipsView.Open(host, save, "a");
            var root = WrestlerRelationshipsView.Open(host, save, "b");
            Assert.That(host.childCount, Is.EqualTo(1));
            Assert.That(root.Q("wr-neutral-players").Q("wr-node-a"), Is.Not.Null);
            Assert.That(root.Query<Label>().ToList().Any(x => x.text.Contains("아직")), Is.True);
        }

        private static string[] ConnectedIds(VisualElement root)
        {
            var canvas = root.Q("wr-map");
            var edges = (System.Collections.IEnumerable)canvas.GetType().GetField("edges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(canvas);
            return edges.Cast<object>().Select(edge => (string)edge.GetType().GetField("Item1").GetValue(edge)).ToArray();
        }
        private static void Click(Button button) => typeof(Clickable)
            .GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button.clickable, new object[] { null });
    }
}
