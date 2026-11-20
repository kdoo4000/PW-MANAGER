using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class HistoryDrivenStorylineTests
    {
        [Test]
        public void UnprogressedStory_ReviewPreviewsLossAndConfirmationAppliesItOnce()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Wrestlers.Add(new WrestlerState { Id = "c" });
            var story = new StorylineService().Create(save, "기다리는 이야기", "a", "c");
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            Assert.That(story.Interest, Is.EqualTo(25));
            Assert.That(result.StoryChanges.Where(x => x.TargetId == story.Id).Single().Amount, Is.EqualTo(-5));
            save.Shows[0].Status = ShowStatus.ResultsReviewed;
            new ShowResultApplicationService().Apply(save, result.Id);
            Assert.That(story.Interest, Is.EqualTo(20));
            Assert.That(result.StoryChanges.Count(x => x.TargetId == story.Id), Is.EqualTo(1));
            Assert.That(StorylineHistory.ForStory(save, story), Is.Empty);
        }

        [Test]
        public void UnlinkedBetrayal_CreatesStoryWithoutCandidateAdoption()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var plan = save.PromoPlans[0];
            plan.ParticipantIds = new() { "a", "b" };
            plan.Incident = StoryIncident.Betrayal;
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, Array.Empty<MatchResultState>(),
                new[] { new PromoResultState { Id = "betrayal", PromoPlanId = plan.Id, PromoScore = 70 } });

            Assert.That(save.Storylines, Is.Empty, "Evaluation must not change the source save.");
            Assert.That(output.StorylineChanges, Has.Count.EqualTo(1));
            Assert.That(output.StoryCandidates, Is.Empty);
            StorylineService.Apply(save, output, save.CurrentDate);
            Assert.That(save.Storylines, Has.Count.EqualTo(1));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-35));
        }

        [Test]
        public void OrdinaryMatch_DoesNotCreateStory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, new[] { new MatchResultState {
                Id = "match", MatchPlanId = save.MatchPlans[0].Id,
                WinnerId = "a", FinishPerformerId = "a", LoserTargetId = "b", FinalMatchQuality = 12
            } }, Array.Empty<PromoResultState>());
            StorylineService.Apply(save, output, save.CurrentDate);
            Assert.That(save.Storylines, Is.Empty);
            Assert.That(save.StoryRelationships, Is.Empty);
        }

        [Test]
        public void DefeatThenChallenge_UsesMatchHistoryWithoutReapplyingAffinity()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var first = new ShowResultState();
            StorylineService.Evaluate(save, first, new[] { Match(save, "defeat") }, Array.Empty<PromoResultState>());
            StorylineService.Apply(save, first, save.CurrentDate);
            Assert.That(StorylineHistory.ForPair(save, "b", "a"), Has.Count.EqualTo(1));
            Assert.That(save.Storylines, Is.Empty);

            var promo = save.PromoPlans[0];
            promo.ParticipantIds = new() { "b" }; promo.TargetIds = new() { "a" };
            promo.Purpose = PromoPurpose.Challenge;
            var next = new ShowResultState();
            StorylineService.Evaluate(save, next, Array.Empty<MatchResultState>(),
                new[] { new PromoResultState { Id = "challenge", PromoPlanId = promo.Id, PromoScore = 70 } });
            StorylineService.Apply(save, next, save.CurrentDate);

            var story = save.Storylines.Single();
            Assert.That(StorylineHistory.ForStory(save, story).Select(x => x.SourceResultId), Is.EqualTo(new[] { "defeat", "challenge" }));
            Assert.That(save.StoryRelationships, Is.Empty);
            Assert.That(story.Context, Is.Null);
            Assert.That(StorylineHistory.Origin(save, story), Does.Contain("도전").And.Not.Contain("함께 출전한 경기"));
        }

        [TestCase(StoryEventType.Match)]
        [TestCase(StoryEventType.Promo)]
        public void Origin_UsesTriggerShowInsteadOfBackgroundMatchOrRecentEvent(StoryEventType type)
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var show = save.Shows[0]; show.Name = "Trigger Show";
            var story = new StorylineState { Id = "origin-story", IsAutomatic = true, StartedDate = save.CurrentDate,
                FirstWrestlerId = "a", SecondWrestlerId = "b", Context = "Old context" };
            story.History.Add(new StoryEventState { Id = "background", SourceResultId = "background-result",
                Incident = StoryIncident.Competition, Date = save.CurrentDate });
            story.History.Add(new StoryEventState { Id = "trigger", SourceResultId = "trigger-result", EventType = type,
                ActorId = "b", TargetId = "a", Incident = StoryIncident.Challenge, Date = save.CurrentDate });
            story.History.Add(new StoryEventState { Id = "recent", SourceResultId = "recent-result",
                ActorId = "a", TargetId = "b", Incident = StoryIncident.Betrayal, Date = save.CurrentDate });
            save.Storylines.Add(story);
            save.ShowResults.Add(new ShowResultState { Id = "trigger-show-result", ShowId = show.Id,
                StoryHistoryApplied = true, TimelineResultIds = { "trigger-result" } });
            var origin = StorylineHistory.Origin(save, story);
            Assert.That(origin, Does.Contain("Trigger Show").And.Contain("도전").And.Not.Contain("배신").And.Not.Contain("Old context"));

            save.ShowResults.Clear();
            if (type == StoryEventType.Match)
                save.MatchResults.Add(new MatchResultState { Id = "trigger-result", ShowId = show.Id });
            else save.PromoResults.Add(new PromoResultState { Id = "trigger-result", ShowId = show.Id });
            Assert.That(StorylineHistory.Origin(save, story), Is.EqualTo(origin));
        }

        [Test]
        public void Titles_UseCurrentOpponentsAndOriginsDoNotReuseLegacyContext()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Wrestlers[0].Identity.RingName = "Alpha";
            save.Wrestlers[1].Identity.RingName = "Bravo";
            var story = new StorylineState { FirstWrestlerId = "a", SecondWrestlerId = "b", Name = "Old title", Context = "Old context" };
            Assert.That(StorylineHistory.Title(save, story), Is.EqualTo(save.Wrestlers.First(x => x.Id == "a").Identity.RingName +
                " vs " + save.Wrestlers.First(x => x.Id == "b").Identity.RingName));
            Assert.That(StorylineHistory.Origin(save, story), Is.EqualTo("촉발 사건 기록이 없습니다."));
            save.TagTeams.Add(new TagTeamState { Id = "first-team", Name = "First team" });
            save.TagTeams.Add(new TagTeamState { Id = "second-team", Name = "Second team" });
            story.PartyType = StoryPartyType.TagTeam; story.FirstGroupId = "first-team"; story.SecondGroupId = "second-team";
            Assert.That(StorylineHistory.Title(save, story), Is.EqualTo("First team vs Second team"));
            save.Stables.Add(new StableState { Id = "first-stable", Name = "First stable" });
            save.Stables.Add(new StableState { Id = "second-stable", Name = "Second stable" });
            story.PartyType = StoryPartyType.Stable; story.FirstGroupId = "first-stable"; story.SecondGroupId = "second-stable";
            Assert.That(StorylineHistory.Title(save, story), Is.EqualTo("First stable vs Second stable"));
        }

        [Test]
        public void SeveralSpotsInOneMatch_KeepDistinctHistoryAndOneStory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var match = Match(save, "several-spots");
            match.SpotResults.Add(new SpotResultState { SpotId = "spot_009", ActorId = "a", TargetId = "b", Phase = MatchSpotPhase.Early });
            match.SpotResults.Add(new SpotResultState { SpotId = "spot_016", ActorId = "b", TargetId = "a", Phase = MatchSpotPhase.PostMatch });
            match.SpotResults.Add(new SpotResultState { SpotId = "spot_001", ActorId = "a", TargetId = "b", WasSkipped = true });
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, new[] { match }, Array.Empty<PromoResultState>());
            StorylineService.Apply(save, output, save.CurrentDate);
            Assert.That(save.Storylines, Has.Count.EqualTo(1));
            var history = StorylineHistory.ForPair(save, "a", "b");
            Assert.That(history, Has.Count.EqualTo(3));
            Assert.That(history.Select(x => x.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-70));
        }

        [Test]
        public void LongPause_PreservesActiveStoryHistoryAndAffinity_NewIncidentContinuesSameFlow()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var promo = save.PromoPlans[0];
            promo.ParticipantIds = new() { "a", "b" }; promo.Incident = StoryIncident.Attack;
            ApplyPromo(save, "attack");
            var old = save.Storylines.Single();
            save.CurrentDate = save.CurrentDate.AddDays(60);
            Assert.That(StorylineHistory.FindActive(save, new[] { "a", "b" }), Is.SameAs(old));
            Assert.That(old.Status, Is.EqualTo(StorylineStatus.Active));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-15));
            Assert.That(StorylineHistory.ForPair(save, "a", "b"), Has.Count.EqualTo(1));
            ApplyPromo(save, "new-attack");
            Assert.That(save.Storylines, Has.Count.EqualTo(1));
            Assert.That(save.Storylines.Count(x => x.Status == StorylineStatus.Active), Is.EqualTo(1));
            Assert.That(StorylineHistory.ForPair(save, "a", "b"), Has.Count.EqualTo(2));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-30));
        }

        [Test]
        public void CancellationAndOrdinaryCoParticipation_DoNotCreateStory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var cancelled = Match(save, "cancelled"); cancelled.CancellationReason = "unavailable";
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, new[] { cancelled }, Array.Empty<PromoResultState>());
            Assert.That(output.StoryEvents, Is.Empty);
            var promo = save.PromoPlans[0]; promo.ParticipantIds = new() { "a", "b" };
            promo.Purpose = PromoPurpose.CharacterIntroduction;
            ApplyPromo(save, "together");
            Assert.That(save.Storylines, Is.Empty);
            Assert.That(StorylineHistory.ForPair(save, "a", "b"), Has.Count.EqualTo(1));
        }

        [Test]
        public void RespectWithoutStory_IsPermanentAndCannotBeAppliedTwice()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var promo = save.PromoPlans[0]; promo.ParticipantIds = new() { "a", "b" }; promo.Incident = StoryIncident.Respect;
            var output = ApplyPromo(save, "respect");
            Assert.That(save.Storylines, Is.Empty);
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(8));
            Assert.That(StorylineHistory.ForPair(save, "a", "b").Single().Incident, Is.EqualTo(StoryIncident.Respect));
            Assert.Throws<InvalidOperationException>(() => StorylineService.Apply(save, output, save.CurrentDate));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(8));
        }

        [Test]
        public void SameShow_UsesEarlierEvents_OnlyConfirmationCommitsAndRoundTrips()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var promo = save.PromoPlans[0]; promo.ParticipantIds = new() { "a", "b" }; promo.Incident = StoryIncident.Betrayal;
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            Assert.That(save.Storylines, Is.Empty);
            Assert.That(save.StoryRelationships, Is.Empty);
            Assert.That(StorylineHistory.ForPair(save, "a", "b"), Is.Empty);
            Assert.That(result.CreatedStorylines, Has.Count.EqualTo(1));
            Assert.That(result.StorylineChanges.Count(x => x.Incident == StoryIncident.Competition), Is.EqualTo(1));
            var restored = UnityEngine.JsonUtility.FromJson<GameSave>(UnityEngine.JsonUtility.ToJson(save));
            restored.Shows[0].Status = ShowStatus.ResultsReviewed;
            new ShowResultApplicationService().Apply(restored, result.Id);
            Assert.That(restored.Storylines, Has.Count.EqualTo(1));
            Assert.That(StorylineHistory.ForPair(restored, "a", "b").Count, Is.GreaterThanOrEqualTo(2));
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(restored, result.Id));
            var again = UnityEngine.JsonUtility.FromJson<GameSave>(UnityEngine.JsonUtility.ToJson(restored));
            Assert.That(StorylineHistory.ForStory(again, again.Storylines.Single()).Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(again.StoryRelationships.Single().Value, Is.EqualTo(-35));
        }

        [Test]
        public void OrdinaryMatch_ChoosesOneExistingPairStory_AndIgnoresDisbandedGroupStory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var service = new StorylineService();
            service.Create(save, "one", "a", "b"); service.Create(save, "two", "a", "b");
            save.TagTeams.Add(new TagTeamState { Id = "team-a", MemberIds = new() { "a" }, Status = TagTeamStatus.Active });
            save.TagTeams.Add(new TagTeamState { Id = "team-b", MemberIds = new() { "b" }, Status = TagTeamStatus.Active });
            var group = service.CreateGroupStory(save, "old group", StoryPartyType.TagTeam, "team-a", "team-b");
            save.TagTeams[0].Status = TagTeamStatus.Disbanded;
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, new[] { Match(save, "ordinary") }, Array.Empty<PromoResultState>());
            Assert.That(output.StorylineChanges, Has.Count.EqualTo(1));
            Assert.That(output.StorylineChanges[0].StorylineId, Is.Not.EqualTo(group.Id));
            Assert.DoesNotThrow(() => StorylineService.Apply(save, output, save.CurrentDate));
        }

        [Test]
        public void LegacyMatchAndPromoHistory_AreNotDuplicated()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var match = Match(save, "old-match"); save.MatchResults.Add(match);
            var story = new StorylineService().Create(save, "legacy", "a", "b");
            story.History.Add(new StoryEventState { SourceResultId = match.Id, Date = save.CurrentDate,
                EventType = StoryEventType.Match, Incident = StoryIncident.Competition, ActorId = "a", TargetId = "b" });
            var promo = new PromoResultState { Id = "old-promo", PromoPlanId = save.PromoPlans[0].Id };
            save.PromoResults.Add(promo);
            story.History.Add(new StoryEventState { SourceResultId = promo.Id, Date = save.CurrentDate,
                EventType = StoryEventType.Promo, Incident = StoryIncident.Challenge, ActorId = "b", TargetId = "a" });
            save.ShowResults.Add(new ShowResultState { ShowId = save.Shows[0].Id, ShowVersion = save.Shows[0].ShowVersion,
                TimelineResultIds = { match.Id, promo.Id }, StorylineChanges = {
                    new StorylineResultChangeState { StorylineId = story.Id, SourceResultId = promo.Id,
                        EventType = StoryEventType.Promo, Incident = StoryIncident.Challenge, ActorId = "b", TargetId = "a" } } });
            save.Shows[0].Status = ShowStatus.Completed;
            Assert.That(StorylineHistory.ForPair(save, "a", "b"), Has.Count.EqualTo(2));
        }

        [Test]
        public void LegacyStoppedMatch_RetainsExecutedBetrayalButNotACompletedMatch()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var match = Match(save, "stopped"); match.WasStoppedByExecution = true;
            match.SpotResults.Add(new SpotResultState { SpotId = "spot_016", ActorId = "a", TargetId = "b" });
            match.SpotResults.Add(new SpotResultState { SpotId = "spot_001", ActorId = "a", TargetId = "b", WasSkipped = true });
            save.MatchResults.Add(match);
            save.ShowResults.Add(new ShowResultState { ShowId = save.Shows[0].Id, ShowVersion = save.Shows[0].ShowVersion,
                TimelineResultIds = { match.Id } });
            save.Shows[0].Status = ShowStatus.Completed;
            var history = StorylineHistory.ForPair(save, "a", "b");
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].Incident, Is.EqualTo(StoryIncident.Betrayal));
        }

        [Test]
        public void Version21Migration_PreservesRelationshipsAndManualStories()
        {
            var save = ShowExecutionServiceTests.CreateSave(); save.SaveVersion = 21;
            var story = new StorylineService().Create(save, "keep", "a", "b"); story.Context = "manual";
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = 42 });
            new PWManager.Infrastructure.Save.SaveVersion21To22Migration().Migrate(save);
            Assert.That(save.SaveVersion, Is.EqualTo(22));
            Assert.That(story.Name, Is.EqualTo("keep")); Assert.That(story.Context, Is.EqualTo("manual"));
            Assert.That(story.IsAutomatic, Is.False); Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(42));
        }

        [Test]
        public void MatchThenChallengeInSameShow_IncludesDefeatInStoryAndPromoContext()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.Shows[0].TimelineEventIds.Reverse();
            var promo = save.PromoPlans[0]; promo.ParticipantIds = new() { "b" }; promo.TargetIds = new() { "a" };
            promo.Purpose = PromoPurpose.Challenge;
            var output = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            Assert.That(output.CreatedStorylines, Has.Count.EqualTo(1));
            Assert.That(output.CreatedStorylines[0].EventIds, Does.Contain(save.MatchResults.Single().Id + ":match"));
            var context = PromoContextBuilder.Build(save, save.PromoResults.Single());
            Assert.That(context.RecentEvents.Any(x => x.ValueKey == "관계 이력" && x.Reason.Contains("승리")), Is.True);
            Assert.That(save.Storylines, Is.Empty);
        }

        [Test]
        public void PromoContext_DoesNotRevealLaterMatchEvenAfterConfirmation()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var promo = save.PromoPlans[0]; promo.ParticipantIds = new() { "a", "b" }; promo.Incident = StoryIncident.Betrayal;
            var output = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            save.Shows[0].Status = ShowStatus.ResultsReviewed;
            new ShowResultApplicationService().Apply(save, output.Id);
            Assert.That(StorylineHistory.ForPairBefore(save, "a", "b", save.PromoResults.Single().Id), Is.Empty);
            Assert.That(PromoContextBuilder.Build(save, save.PromoResults.Single()).RecentEvents
                .Any(x => x.ValueKey == "관계 이력"), Is.False);
        }

        private static MatchResultState Match(GameSave save, string id) => new() {
            Id = id, MatchPlanId = save.MatchPlans[0].Id, WinnerId = "a", FinishPerformerId = "a",
            LoserTargetId = "b", FinalMatchQuality = 12,
            ParticipantProfiles = { new MatchParticipantProfileState { SideId = "left", MemberIds = { "a" } },
                new MatchParticipantProfileState { SideId = "right", MemberIds = { "b" } } }
        };

        private static ShowResultState ApplyPromo(GameSave save, string id)
        {
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, Array.Empty<MatchResultState>(),
                new[] { new PromoResultState { Id = id, PromoPlanId = save.PromoPlans[0].Id, PromoScore = 70 } });
            StorylineService.Apply(save, output, save.CurrentDate);
            return output;
        }
    }
}
