using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class StorylineServiceTests
    {
        [Test]
        public void StartingConfirmedShow_ExpiresOnlyPendingCandidates()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var show = save.Shows[0];
            show.Status = ShowStatus.Confirmed;
            var pending = new StoryCandidateState { Id = "pending", Status = StoryCandidateStatus.Pending,
                Event = new StoryEventState { SourceResultId = "pending-source", ActorId = "a", TargetId = "b" } };
            var expired = new StoryCandidateState { Id = "expired", Status = StoryCandidateStatus.Expired,
                Event = new StoryEventState { SourceResultId = "expired-source", ActorId = "a", TargetId = "b" } };
            save.StoryCandidates.AddRange(new[] { pending, expired });

            StorylineService.StartShow(save, show);

            Assert.That(show.Status, Is.EqualTo(ShowStatus.InProgress));
            Assert.That(pending.Status, Is.EqualTo(StoryCandidateStatus.Expired));
            Assert.That(expired.Status, Is.EqualTo(StoryCandidateStatus.Expired));
            Assert.Throws<InvalidOperationException>(() => StorylineService.StartShow(save, show));
        }

        [Test]
        public void InvalidSavedCandidate_RejectsAdoptionBeforeCreatingStory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.StoryCandidates.Add(new StoryCandidateState
            {
                Id = "broken", Quality = float.NaN,
                Event = new StoryEventState { SourceResultId = "source", ActorId = "a", TargetId = "b" }
            });
            Assert.Throws<InvalidOperationException>(() => new StorylineService().AdoptCandidate(save, "broken", "스토리"));
            Assert.That(save.Storylines, Is.Empty);
            Assert.That(save.StoryCandidates[0].Status, Is.EqualTo(StoryCandidateStatus.Pending));
        }

        [Test]
        public void CandidateAdoption_DoesNotRepeatRelationshipEffect_AndPendingCandidatesExpire()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var promo = save.PromoPlans[0];
            promo.ParticipantIds = new() { "a", "b" };
            promo.Incident = StoryIncident.Betrayal;
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, Array.Empty<MatchResultState>(),
                new[] { new PromoResultState { Id = "candidate-source", PromoPlanId = promo.Id, PromoScore = 70 } });
            StorylineService.Apply(save, output, save.CurrentDate);
            save.StoryCandidates.AddRange(output.StoryCandidates);
            Assert.That(save.StoryCandidates, Has.Count.EqualTo(1));
            var candidate = save.StoryCandidates[0];
            var story = new StorylineService().AdoptCandidate(save, candidate.Id, "배신의 시작");
            Assert.That(story.History, Has.Count.EqualTo(1));
            Assert.That(story.Tension, Is.EqualTo(50));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-35));
            Assert.Throws<InvalidOperationException>(() => new StorylineService().AdoptCandidate(save, candidate.Id, "중복"));
            var reloaded = UnityEngine.JsonUtility.FromJson<GameSave>(UnityEngine.JsonUtility.ToJson(save));
            Assert.DoesNotThrow(() => StorylineService.ValidateState(reloaded));
            Assert.Throws<InvalidOperationException>(() => new StorylineService().AdoptCandidate(reloaded, candidate.Id, "재로드 중복"));
            save.StoryCandidates.Add(new StoryCandidateState
            {
                Id = "pending", Quality = 50,
                Event = new StoryEventState { SourceResultId = "later", ActorId = "a", TargetId = "b", Incident = StoryIncident.Challenge }
            });
            StorylineService.ExpireCandidates(save);
            Assert.That(candidate.Status, Is.EqualTo(StoryCandidateStatus.Adopted));
            Assert.That(save.StoryCandidates[1].Status, Is.EqualTo(StoryCandidateStatus.Expired));
        }

        [Test]
        public void LinkedPromo_ProducesAndAppliesStoryAndRelationshipChanges()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var ids = new Queue<string>(new[] { Guid.NewGuid().ToString("D") });
            var service = new StorylineService(() => ids.Dequeue());
            var story = service.Create(save, "첫 대립", save.Wrestlers[0].Id, save.Wrestlers[1].Id);
            var promo = save.PromoPlans[0];
            promo.ParticipantIds = new List<string> { story.FirstWrestlerId, story.SecondWrestlerId };
            promo.Purpose = PromoPurpose.Rivalry;
            promo.StorylineId = story.Id;
            promo.StoryAction = StoryAction.Progress;
            var promoResult = new PromoResultState { Id = "result", PromoPlanId = promo.Id, PromoScore = 80f };
            var showResult = new ShowResultState();

            StorylineService.Evaluate(save, showResult, Array.Empty<MatchResultState>(), new[] { promoResult });
            StorylineService.Validate(save, showResult);
            StorylineService.Apply(save, showResult, save.CurrentDate);

            Assert.That(story.Tension, Is.EqualTo(33f));
            Assert.That(story.Interest, Is.EqualTo(28f));
            Assert.That(story.Fatigue, Is.Zero);
            Assert.That(story.History, Has.Count.EqualTo(1));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-4f));
        }

        [Test]
        public void EndingStory_ClosesItAndRejectsFurtherEvaluation()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var service = new StorylineService(() => Guid.NewGuid().ToString("D"));
            var story = service.Create(save, "결말", save.Wrestlers[0].Id, save.Wrestlers[1].Id);
            var promo = save.PromoPlans[0];
            promo.ParticipantIds = new List<string> { story.FirstWrestlerId, story.SecondWrestlerId };
            promo.StorylineId = story.Id;
            promo.StoryAction = StoryAction.End;
            var result = new PromoResultState { Id = "ending", PromoPlanId = promo.Id, PromoScore = 60f };
            var showResult = new ShowResultState();

            StorylineService.Evaluate(save, showResult, Array.Empty<MatchResultState>(), new[] { result });
            StorylineService.Validate(save, showResult);
            StorylineService.Apply(save, showResult, save.CurrentDate);

            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Completed));
            Assert.That(save.StoryRelationships, Is.Empty, "Ending a story does not imply reconciliation.");
            Assert.Throws<InvalidOperationException>(() => StorylineService.Evaluate(
                save, new ShowResultState(), Array.Empty<MatchResultState>(), new[] { result }));
        }

        [Test]
        public void Migration_PreservesHostilityAndPendingChanges_OnlyOnce()
        {
            var save = new GameSave();
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Hostility = 70 });
            save.ShowResults.Add(new ShowResultState
            {
                RelationshipChanges = { new StoryRelationshipChangeState { HostilityChange = 15 } }
            });
            StorylineService.UpgradeRelationships(save);
            Assert.That(save.StoryRelationships[0].Value, Is.EqualTo(-70));
            Assert.That(save.ShowResults[0].RelationshipChanges[0].ValueChange, Is.EqualTo(-15));
            save.StoryRelationships[0].Value = 25;
            var restored = UnityEngine.JsonUtility.FromJson<GameSave>(UnityEngine.JsonUtility.ToJson(save));
            StorylineService.UpgradeRelationships(restored);
            Assert.That(restored.StoryRelationships[0].Value, Is.EqualTo(25));
        }

        [Test]
        public void BetrayalBeforeMatch_ChangesOpeningWithoutMutatingSavedRelationship()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.PromoPlans[0].ParticipantIds.Add("b");
            save.PromoPlans[0].Incident = StoryIncident.Betrayal;
            var result = ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30);
            Assert.That(save.MatchResults[0].EngineState.Rules.OpeningHostility, Is.EqualTo(35));
            Assert.That(save.StoryRelationships, Is.Empty);
            save.Shows[0].Status = ShowStatus.ResultsReviewed;
            new ShowResultApplicationService().Apply(save, result.Id);
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-35));
            Assert.Throws<InvalidOperationException>(() => new ShowResultApplicationService().Apply(save, result.Id));
            Assert.That(save.StoryRelationships.Single().Value, Is.EqualTo(-35));
        }

        [Test]
        public void GoodMatch_ChangesInterestButNotRelationshipOrTension()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var story = new StorylineService().Create(save, "정정당당한 경쟁", "a", "b");
            save.MatchPlans[0].StorylineId = story.Id;
            save.MatchPlans[0].StoryAction = StoryAction.Progress;
            var result = new ShowResultState();
            StorylineService.Evaluate(save, result, new[] { new MatchResultState
            {
                Id = "match-result", MatchPlanId = "match", FinalMatchQuality = 18,
                FinishPerformerId = "a", LoserTargetId = "b"
            } }, Array.Empty<PromoResultState>());
            StorylineService.Apply(save, result, save.CurrentDate);
            Assert.That(story.Tension, Is.EqualTo(25));
            Assert.That(story.Interest, Is.GreaterThan(25));
            Assert.That(story.Fatigue, Is.Zero);
            Assert.That(save.StoryRelationships, Is.Empty);
        }

        [Test]
        public void RepeatedTaunt_DiminishesTensionAndBuildsFatigue_RespectImprovesRelationship()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var story = new StorylineService().Create(save, "라이벌", "a", "b");
            var promo = save.PromoPlans[0];
            promo.ParticipantIds.Add("b"); promo.StorylineId = story.Id;
            promo.StoryAction = StoryAction.Progress; promo.Incident = StoryIncident.Taunt;
            float Run(string id)
            {
                var output = new ShowResultState();
                StorylineService.Evaluate(save, output, Array.Empty<MatchResultState>(),
                    new[] { new PromoResultState { Id = id, PromoPlanId = promo.Id, PromoScore = 80 } });
                StorylineService.Apply(save, output, save.CurrentDate);
                return output.StorylineChanges[0].TensionChange;
            }
            var first = Run("first"); var second = Run("second");
            Assert.That(second, Is.LessThan(first));
            Assert.That(story.Fatigue, Is.GreaterThan(0));
            var before = save.StoryRelationships[0].Value;
            promo.Incident = StoryIncident.Respect;
            Run("respect");
            Assert.That(save.StoryRelationships[0].Value, Is.GreaterThan(before));
            Assert.That(story.History.Last().Incident, Is.EqualTo(StoryIncident.Respect));
        }

        [Test]
        public void DuplicateReversedPair_IsRejectedBeforeApplying()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "a", SecondWrestlerId = "b", Value = 70 });
            save.StoryRelationships.Add(new StoryRelationshipState { FirstWrestlerId = "b", SecondWrestlerId = "a", Value = -30 });
            Assert.Throws<InvalidOperationException>(() => StorylineService.Apply(save, new ShowResultState(), save.CurrentDate));
            Assert.That(save.StoryRelationships[0].Value, Is.EqualTo(70));
        }

        [Test]
        public void EndThenAnotherLinkedEvent_RejectsWholeShowWithoutCommittingStory()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var story = new StorylineService().Create(save, "결말", "a", "b");
            save.PromoPlans[0].ParticipantIds.Add("b");
            save.PromoPlans[0].StorylineId = story.Id;
            save.PromoPlans[0].StoryAction = StoryAction.End;
            save.MatchPlans[0].StorylineId = story.Id;
            save.MatchPlans[0].StoryAction = StoryAction.Progress;
            Assert.Throws<InvalidOperationException>(() => ShowExecutionServiceTests.Service().Execute(save, "show", 73, 500, 30));
            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Active));
            Assert.That(story.History, Is.Empty);
            Assert.That(save.MatchResults, Is.Empty);
            Assert.That(save.PromoResults, Is.Empty);
            Assert.That(save.ShowResults, Is.Empty);
        }

        [Test]
        public void PostMatchBetrayal_IsRecordedBeforeEnding_AndKeepsEnmity()
        {
            var save = ShowExecutionServiceTests.CreateSave();
            var story = new StorylineService().Create(save, "마지막 경기", "a", "b");
            save.MatchPlans[0].StorylineId = story.Id;
            save.MatchPlans[0].StoryAction = StoryAction.End;
            var output = new ShowResultState();
            StorylineService.Evaluate(save, output, new[] { new MatchResultState
            {
                Id = "ending-match", MatchPlanId = "match", FinalMatchQuality = 16,
                FinishPerformerId = "a", LoserTargetId = "b",
                SpotResults = { new SpotResultState { SpotId = "spot_016", ActorId = "a", TargetId = "b", Phase = MatchSpotPhase.PostMatch } }
            } }, Array.Empty<PromoResultState>());
            StorylineService.Apply(save, output, save.CurrentDate);
            Assert.That(story.Status, Is.EqualTo(StorylineStatus.Completed));
            Assert.That(story.History.Last().Incident, Is.EqualTo(StoryIncident.Betrayal));
            Assert.That(save.StoryRelationships[0].Value, Is.EqualTo(-35));
        }
    }
}
