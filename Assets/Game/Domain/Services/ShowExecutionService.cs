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

        public ShowResultState Execute(GameSave save, string showId, int resultSeed)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            var show = save.Shows.SingleOrDefault(x => x?.Id == showId)
                ?? throw new ArgumentException("Show was not found.", nameof(showId));
            if (show.Status != ShowStatus.Confirmed || show.ShowVersion < 1)
                throw new InvalidOperationException("Only a confirmed show version can be executed.");
            if (save.ShowResults.Any(x => x?.ShowId == show.Id && x.ShowVersion == show.ShowVersion))
                throw new InvalidOperationException("This show version has already been executed.");

            var issues = planningService.Validate(save, showId).Where(x => x.Severity == ShowValidationSeverity.Error).ToList();
            if (issues.Count > 0)
                throw new InvalidOperationException(string.Join(" | ", issues.Select(x => x.Message)));

            var eventIds = show.TimelineEventIds ?? new List<string>();
            if (eventIds.Count == 0 || eventIds.Distinct(StringComparer.Ordinal).Count() != eventIds.Count)
                throw new InvalidOperationException("Show timeline must contain unique events.");

            var matchResults = new List<MatchResultState>();
            var promoResults = new List<PromoResultState>();
            var timelineResultIds = new List<string>();
            foreach (var eventId in eventIds)
            {
                var showEvent = save.ShowEvents.SingleOrDefault(x => x?.Id == eventId && x.ShowId == show.Id)
                    ?? throw new InvalidOperationException("Show timeline references a missing event.");
                var eventSeed = CreateEventSeed(resultSeed, show.ShowVersion, showEvent.Id);
                if (showEvent.EventType == ShowEventType.Match)
                {
                    var result = matchEvaluator.EvaluateTechnical(save, showEvent.Id, 0f, eventSeed);
                    matchResults.Add(result);
                    timelineResultIds.Add(result.Id);
                }
                else if (showEvent.EventType == ShowEventType.Promo)
                {
                    var result = promoEvaluator.Evaluate(save, CreatePromoInput(save, showEvent, eventSeed));
                    promoResults.Add(result);
                    timelineResultIds.Add(result.Id);
                }
                else
                {
                    throw new InvalidOperationException("Show event type is not supported.");
                }
            }

            var showResult = new ShowResultState
            {
                Id = createId(), ShowId = show.Id, ShowVersion = show.ShowVersion,
                TimelineResultIds = timelineResultIds,
                MatchResultIds = matchResults.Select(x => x.Id).ToList(),
                PromoResultIds = promoResults.Select(x => x.Id).ToList(),
                FinancialSettlement = new FinancialSettlementState { Revenue = 0, Cost = 0, NetIncome = 0 }
            };
            save.MatchResults.AddRange(matchResults);
            save.PromoResults.AddRange(promoResults);
            save.ShowResults.Add(showResult);
            show.Status = ShowStatus.Completed;
            return showResult;
        }

        private static PromoEvaluationInput CreatePromoInput(GameSave save, ShowEventState showEvent, int resultSeed)
        {
            var promo = save.PromoPlans.SingleOrDefault(x => x?.Id == showEvent.DetailId)
                ?? throw new InvalidOperationException("Promo plan was not found.");
            var performances = promo.ParticipantIds.Select(id => new PromoParticipantPerformance
            {
                WrestlerId = id,
                Score = Math.Max(0f, Math.Min(100f, save.Wrestlers.Single(x => x.Id == id).Attributes.PromoOverall * 5f))
            }).ToList();
            var isProductionOnly = promo.Presentation == PromoPresentation.VideoPackage ||
                promo.Purpose == PromoPurpose.SponsorAdvertisement;
            return new PromoEvaluationInput
            {
                ShowEventId = showEvent.Id,
                PrimarySpeakerId = isProductionOnly ? null : promo.ParticipantIds.FirstOrDefault(),
                ParticipantPerformances = performances,
                ProductionPerformance = performances.Count == 0 ? 0f : performances.Average(x => x.Score),
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
