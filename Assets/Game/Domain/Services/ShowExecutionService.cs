using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class ShowExecutionService
    {
        private readonly ShowPlanningService planningService;
        private readonly MatchEvaluator matchEvaluator;
        private readonly PromoEvaluator promoEvaluator;
        private readonly Func<string> createId;

        public ShowExecutionService(
            ShowPlanningService planningService, MatchEvaluator matchEvaluator, PromoEvaluator promoEvaluator,
            Func<string> createId = null)
        {
            this.planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
            this.matchEvaluator = matchEvaluator ?? throw new ArgumentNullException(nameof(matchEvaluator));
            this.promoEvaluator = promoEvaluator ?? throw new ArgumentNullException(nameof(promoEvaluator));
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public ShowResultState Execute(GameSave save, string showId, int resultSeed, int venueCapacity, long venueBaseTicketPrice)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            var show = save.Shows.SingleOrDefault(x => x?.Id == showId)
                ?? throw new ArgumentException("Show was not found.", nameof(showId));
            if (show.Status != ShowStatus.InProgress || show.ShowVersion < 1)
                throw new InvalidOperationException("Only an in-progress show version can be executed.");
            if (save.ShowResults.Any(x => x?.ShowId == show.Id && x.ShowVersion == show.ShowVersion))
                throw new InvalidOperationException("This show version has already been executed.");

            var referencePrice = TicketPricingRules.GetReferencePrice(venueBaseTicketPrice, show.ShowType);
            var ticketUnitPrice = TicketPricingRules.GetActualPrice(referencePrice, show.TicketPricePercent);
            var ticketSalesCount = TicketPricingRules.GetActualAttendance(
                FanAudienceService.BaseDemand(save, show, referencePrice), ticketUnitPrice, referencePrice, venueCapacity, show.AttendanceVarianceBasisPoints);
            var ticketRevenue = checked(ticketUnitPrice * ticketSalesCount);

            var issues = planningService.Validate(save, showId).Where(x => x.Severity == ShowValidationSeverity.Error).ToList();
            if (issues.Count > 0)
                throw new InvalidOperationException(string.Join(" | ", issues.Select(x => x.Message)));

            var eventIds = show.TimelineEventIds ?? new List<string>();
            if (eventIds.Count == 0 || eventIds.Distinct(StringComparer.Ordinal).Count() != eventIds.Count)
                throw new InvalidOperationException("Show timeline must contain unique events.");

            var matchResults = new List<MatchResultState>();
            var promoResults = new List<PromoResultState>();
            var timelineResultIds = new List<string>();
            var storySimulation = save.CopyForStorySimulation();
            var pendingStories = new ShowResultState();
            foreach (var eventId in eventIds)
            {
                var showEvent = save.ShowEvents.SingleOrDefault(x => x?.Id == eventId && x.ShowId == show.Id)
                    ?? throw new InvalidOperationException("Show timeline references a missing event.");
                var eventSeed = CreateEventSeed(resultSeed, show.ShowVersion, showEvent.Id);
                var eventStories = new ShowResultState();
                GroupResultChangeState groupChange = null;
                if (showEvent.EventType == ShowEventType.Match)
                {
                    var result = matchEvaluator.EvaluateTechnical(storySimulation, showEvent.Id, 0f, eventSeed, matchResults);
                    var plan = save.MatchPlans.Single(x => x.Id == result.MatchPlanId);
                    var narration = new MatchNarrationService();
                    result.SimulationBeats = narration.CreateBeats(plan, result, save);
                    result.NarrativeLines = narration.Narrate(plan, result, id =>
                    {
                        var wrestler = save.Wrestlers.FirstOrDefault(x => x?.Id == id);
                        return wrestler == null ? "선수" : string.IsNullOrWhiteSpace(wrestler.Identity.RingName) ? wrestler.Identity.LegalName : wrestler.Identity.RingName;
                    }, save);
                    matchResults.Add(result);
                    foreach (var change in result.WrestlerConditionChanges)
                    {
                        var wrestler = storySimulation.Wrestlers.Single(w => w.Id == change.WrestlerId);
                        wrestler.Condition.Condition = Math.Clamp(wrestler.Condition.Condition + change.ConditionDelta, 0, 100);
                    }
                    MatchHealth.ApplyResult(storySimulation, result, show.Date);
                    timelineResultIds.Add(result.Id);
                    if (string.IsNullOrEmpty(result.CancellationReason)) StorylineService.Evaluate(storySimulation, eventStories, new[] { result }, Array.Empty<PromoResultState>());
                }
                else if (showEvent.EventType == ShowEventType.Promo)
                {
                    var promoPlan = storySimulation.PromoPlans.Single(p => p.Id == showEvent.DetailId);
                    var cancelled = promoPlan.ParticipantIds.Any(id => storySimulation.Wrestlers.Single(w => w.Id == id).Condition.Availability == WrestlerAvailability.Unavailable);
                    var result = cancelled ? new PromoResultState { Id = createId(), ShowId = show.Id, ShowVersion = show.ShowVersion,
                        ShowEventId = showEvent.Id, PromoPlanId = promoPlan.Id, CancellationReason = "실제 부상으로 프로모 취소", ResultSeed = eventSeed } :
                        promoEvaluator.Evaluate(storySimulation, CreatePromoInput(storySimulation, showEvent, eventSeed));
                    promoResults.Add(result);
                    timelineResultIds.Add(result.Id);
                    if (!cancelled)
                    {
                        StorylineService.Evaluate(storySimulation, eventStories, Array.Empty<MatchResultState>(), new[] { result });
                        groupChange = WrestlerGroupService.FromPromo(storySimulation, promoPlan, result.Id);
                    }
                    result.RelationshipChanges.AddRange(eventStories.RelationshipChanges.Select(x => new ResultChangeState
                    {
                        TargetId = x.SecondWrestlerId, ValueKey = "관계", Amount = x.ValueChange,
                        Reason = StorylineService.PromoIncident(save.PromoPlans.Single(p => p.Id == result.PromoPlanId)).ToString()
                    }));
                }
                else
                {
                    throw new InvalidOperationException("Show event type is not supported.");
                }
                StorylineService.Apply(storySimulation, eventStories, show.Date);
                if (groupChange != null)
                {
                    WrestlerGroupService.ApplyPromoChange(storySimulation, groupChange);
                    pendingStories.GroupChanges.Add(groupChange);
                }
                pendingStories.StorylineChanges.AddRange(eventStories.StorylineChanges);
                pendingStories.RelationshipChanges.AddRange(eventStories.RelationshipChanges);
                pendingStories.StoryCandidates.AddRange(eventStories.StoryCandidates);
            }

            var showResult = new ShowResultState
            {
                Id = createId(), ShowId = show.Id, ShowVersion = show.ShowVersion,
                TimelineResultIds = timelineResultIds,
                StorylineChanges = pendingStories.StorylineChanges,
                RelationshipChanges = pendingStories.RelationshipChanges,
                StoryCandidates = pendingStories.StoryCandidates,
                GroupChanges = pendingStories.GroupChanges,
                MatchResultIds = matchResults.Select(x => x.Id).ToList(),
                PromoResultIds = promoResults.Select(x => x.Id).ToList(),
                ShowEvaluation = CreateShowEvaluation(save, eventIds, matchResults, promoResults),
                FinancialSettlement = new FinancialSettlementState
                {
                    Attendance = ticketSalesCount,
                    TicketPrice = ticketUnitPrice,
                    Revenue = ticketRevenue,
                    Cost = show.EstimatedCost,
                    NetIncome = ticketRevenue - show.EstimatedCost
                }
            };
            FanAudienceService.Evaluate(save, show, showResult, matchResults, promoResults);
            StorylineService.Validate(save, showResult);
            foreach (var change in showResult.StorylineChanges)
            {
                var story = save.Storylines.Single(x => x.Id == change.StorylineId);
                showResult.StoryChanges.Add(new ResultChangeState { TargetId = story.Id, ValueKey = "긴장도",
                    Amount = change.TensionChange, Reason = story.Name });
                showResult.StoryChanges.Add(new ResultChangeState { TargetId = story.Id, ValueKey = "관심도",
                    Amount = change.InterestChange, Reason = story.Name });
                showResult.StoryChanges.Add(new ResultChangeState { TargetId = story.Id, ValueKey = "피로도",
                    Amount = change.FatigueChange, Reason = story.Name });
            }
            save.MatchResults.AddRange(matchResults);
            save.PromoResults.AddRange(promoResults);
            save.ShowResults.Add(showResult);
            show.Status = ShowStatus.ResultReview;
            new InboxService(createId).Publish(save, $"show-result-review:{show.Id}:{show.ShowVersion}",
                InboxMessageType.Decision, InboxPriority.Required, "분석팀", $"{show.Name} 결과를 확인하세요",
                "쇼가 종료되었습니다. 결과를 확인해야 일정을 마칠 수 있습니다.", InboxTargetType.Show, show.Id, save.CurrentDate);
            return showResult;
        }

        private static ShowEvaluationState CreateShowEvaluation(
            GameSave save, IReadOnlyCollection<string> eventIds,
            IReadOnlyCollection<MatchResultState> matchResults, IReadOnlyCollection<PromoResultState> promoResults)
        {
            var matchScores = matchResults.ToDictionary(x => x.ShowEventId, x => x.FinalMatchQuality * 5f, StringComparer.Ordinal);
            var promoScores = promoResults.ToDictionary(x => x.ShowEventId, x => x.PromoScore, StringComparer.Ordinal);
            var weightedScore = 0f;
            var totalDuration = 0;
            foreach (var eventId in eventIds)
            {
                var showEvent = save.ShowEvents.Single(x => x.Id == eventId);
                if (matchResults.Any(x => x.ShowEventId == eventId && !string.IsNullOrEmpty(x.CancellationReason)) ||
                    promoResults.Any(x => x.ShowEventId == eventId && !string.IsNullOrEmpty(x.CancellationReason))) continue;
                var score = showEvent.EventType == ShowEventType.Match ? matchScores[eventId] : promoScores[eventId];
                weightedScore += score * showEvent.PlannedDuration;
                totalDuration += showEvent.PlannedDuration;
            }
            var showScore = totalDuration == 0 ? 0f : weightedScore / totalDuration;
            return new ShowEvaluationState
            {
                Score = showScore,
                CriticReview = CriticReviewCalculator.Show(matchResults.Where(x => string.IsNullOrEmpty(x.CancellationReason) && x.CriticReview != null).Select(x => x.CriticReview).ToList()),
                EvaluationReasons = new List<EvaluationReasonState>
                {
                    new() { Code = "show.event-quality", Contribution = showScore }
                }
            };
        }

        private static PromoEvaluationInput CreatePromoInput(GameSave save, ShowEventState showEvent, int resultSeed)
        {
            var promo = save.PromoPlans.SingleOrDefault(x => x?.Id == showEvent.DetailId)
                ?? throw new InvalidOperationException("Promo plan was not found.");
            var performances = promo.ParticipantIds.Select(id => new PromoParticipantPerformance
            {
                WrestlerId = id,
                Score = Math.Max(0f, Math.Min(100f, WrestlerOverallCalculator.Promo(save.Wrestlers.Single(x => x.Id == id)) * 5f))
            }).ToList();
            var isProductionOnly = promo.Presentation == PromoPresentation.VideoPackage ||
                promo.Purpose == PromoPurpose.SponsorAdvertisement;
            var story = (save.Storylines ?? new()).FirstOrDefault(x => x.Id == promo.StorylineId && x.Status == StorylineStatus.Active);
            return new PromoEvaluationInput
            {
                ShowEventId = showEvent.Id,
                PrimarySpeakerId = isProductionOnly ? null : promo.ParticipantIds.FirstOrDefault(),
                ParticipantPerformances = performances,
                ProductionPerformance = performances.Count == 0 ? 0f : performances.Average(x => x.Score),
                // ponytail: bounded initial story-context modifier; tune weights after multi-show playtesting.
                StoryContext = story == null ? 0f : Math.Max(-10f, Math.Min(15f,
                    (story.Interest - 25f) / 10f + (story.Tension - 25f) / 20f - story.Fatigue / 10f)),
                ResultSeed = resultSeed
            };
        }

        private static int CreateEventSeed(int resultSeed, int showVersion, string eventId)
        {
            unchecked
            {
                var hash = 2166136261u ^ (uint)resultSeed;
                hash = (hash ^ (uint)showVersion) * 16777619u;
                foreach (var character in eventId ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7fffffffu);
            }
        }
    }
}
