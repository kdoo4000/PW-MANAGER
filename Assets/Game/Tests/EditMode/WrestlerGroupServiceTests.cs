using System;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class WrestlerGroupServiceTests
    {
        [Test]
        public void FormationPromo_ReplaysSameGroupId_AndRejectsDuplicateFormation()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "promotion" } };
            foreach (var id in new[] { "a", "b" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
            var plan = new PromoPlanState { Purpose = PromoPurpose.TeamFormation, GroupType = StoryPartyType.TagTeam, GroupName = "새 팀", ParticipantIds = new() { "a", "b" } };
            var change = WrestlerGroupService.FromPromo(save, plan, "promo-result");
            var invalid = new PromoPlanState { Purpose = PromoPurpose.TeamFormation, GroupType = StoryPartyType.Stable, GroupName = "인원 부족", ParticipantIds = new() { "a", "b" } };
            Assert.Throws<InvalidOperationException>(() => WrestlerGroupService.ValidatePromo(save, invalid));
            var simulation = save.CopyForStorySimulation();
            WrestlerGroupService.ApplyPromoChange(simulation, change);
            Assert.That(save.TagTeams, Is.Empty);
            WrestlerGroupService.ApplyPromoChange(save, change);
            Assert.That(save.TagTeams[0].Id, Is.EqualTo(simulation.TagTeams[0].Id));
            Assert.Throws<InvalidOperationException>(() => WrestlerGroupService.ApplyPromoChange(save, change));
            Assert.That(save.TagTeams, Has.Count.EqualTo(1));
        }

        [Test]
        public void GroupSimulation_DoesNotMutateSavedMembership()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "promotion" } };
            foreach (var id in new[] { "a", "b" }) save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
            var team = WrestlerGroupService.CreateTagTeam(save, "팀", new[] { "a", "b" });
            var simulation = save.CopyForStorySimulation();
            WrestlerGroupService.Disband(simulation, team.Id, false);
            Assert.That(team.Status, Is.EqualTo(TagTeamStatus.Active));
            Assert.That(save.Wrestlers[0].Roster.ActiveTagTeamId, Is.EqualTo(team.Id));
            Assert.That(simulation.Wrestlers[0].Roster.ActiveTagTeamId, Is.Null);
        }

        [Test]
        public void BreakupPromo_RequiresAnActiveMemberOnStage()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "promotion" } };
            foreach (var id in new[] { "a", "b", "c" })
                save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
            var team = WrestlerGroupService.CreateTagTeam(save, "팀", new[] { "a", "b" });
            var plan = new PromoPlanState { Purpose = PromoPurpose.TeamBreakup, GroupType = StoryPartyType.TagTeam,
                GroupId = team.Id, ParticipantIds = new() { "c" } };
            Assert.Throws<InvalidOperationException>(() => WrestlerGroupService.ValidatePromo(save, plan));
            plan.ParticipantIds.Add("a");
            Assert.DoesNotThrow(() => WrestlerGroupService.ValidatePromo(save, plan));
        }

        [Test]
        public void GroupStory_AllowsRepresentatives_WithoutChangingPersonalRelations()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "promotion" } };
            foreach (var id in new[] { "a", "b", "c", "d" })
                save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
            var first = WrestlerGroupService.CreateTagTeam(save, "첫 팀", new[] { "a", "b" });
            var second = WrestlerGroupService.CreateTagTeam(save, "둘째 팀", new[] { "c", "d" });
            var story = new StorylineService().CreateGroupStory(save, "팀 대립", StoryPartyType.TagTeam, first.Id, second.Id);
            Assert.That(StorylineService.IncludesBothParties(save, story, new[] { "b", "d" }), Is.True);
            Assert.That(StorylineService.IncludesBothParties(save, story, new[] { "a", "b" }), Is.False);
            Assert.That(save.StoryRelationships, Is.Empty);
            Assert.DoesNotThrow(() => StorylineService.ValidateState(save));
            WrestlerGroupService.Disband(save, first.Id, false);
            Assert.That(StorylineService.CanProgress(save, story), Is.False);
            Assert.DoesNotThrow(() => StorylineService.ValidateState(save));
            var result = new ShowResultState();
            result.StorylineChanges.Add(new StorylineResultChangeState
            {
                StorylineId = story.Id, SourceResultId = "last-match", Action = StoryAction.Progress,
                EventType = StoryEventType.Match, Incident = StoryIncident.Competition, InterestChange = 2
            });
            Assert.Throws<InvalidOperationException>(() => StorylineService.Apply(save, result, save.CurrentDate));
            Assert.That(story.History, Is.Empty);
            Assert.That(story.Interest, Is.EqualTo(25));
            result.StorylineChanges[0].Action = StoryAction.End;
            Assert.DoesNotThrow(() => StorylineService.Apply(save, result, save.CurrentDate));
            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Completed));
        }

        [Test]
        public void StableMembership_KeepsOfficialTeamTogether_AndDisbandPreservesRelationships()
        {
            var save = new GameSave { Promotion = new PromotionState { Id = "promotion" } };
            foreach (var id in new[] { "a", "b", "c", "d" })
                save.Wrestlers.Add(new WrestlerState { Id = id, PromotionId = "promotion" });
            var team = WrestlerGroupService.CreateTagTeam(save, "팀", new[] { "a", "b" });
            Assert.Throws<InvalidOperationException>(() => WrestlerGroupService.CreateStable(save, "그룹", new[] { "a", "c", "d" }, "a"));
            Assert.That(save.Stables, Is.Empty);
            var stable = WrestlerGroupService.CreateStable(save, "그룹", new[] { "a", "b", "c" }, "c");
            Assert.Throws<InvalidOperationException>(() => WrestlerGroupService.SetTagTeamMembers(save, team.Id, new[] { "a", "d" }));
            Assert.That(team.MemberIds, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.Throws<InvalidOperationException>(() => WrestlerGroupService.SetStableMembers(save, stable.Id, new[] { "a", "c", "d" }, "a"));
            Assert.That(stable.MemberIds, Is.EquivalentTo(new[] { "a", "b", "c" }));
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = -80 });
            WrestlerGroupService.Disband(save, stable.Id, true);
            Assert.That(team.Status, Is.EqualTo(TagTeamStatus.Active));
            Assert.That(save.Wrestlers[0].Roster.ActiveStableId, Is.Null);
            Assert.That(save.Wrestlers[0].Roster.ActiveTagTeamId, Is.EqualTo(team.Id));
            Assert.That(save.StoryRelationships[0].Value, Is.EqualTo(-80));
        }
    }
}
