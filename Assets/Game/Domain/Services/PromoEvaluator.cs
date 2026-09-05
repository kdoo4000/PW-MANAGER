using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class PromoParticipantPerformance
    {
        public string WrestlerId;
        public float Score;
    }

    public sealed class PromoEvaluationInput
    {
        public string ShowEventId;
        public string PrimarySpeakerId;
        public List<PromoParticipantPerformance> ParticipantPerformances = new();
        public float ProductionPerformance;
        public float RoleFit;
        public float PurposeFit;
        public float StoryContext;
        public float DurationFit;
        public float Freshness;
        public float ResultVariance;
        public int ResultSeed;
    }

    public sealed class PromoEvaluator
    {
        private readonly Func<string> createId;

        public PromoEvaluator(Func<string> createId = null)
        {
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public PromoResultState Evaluate(GameSave save, PromoEvaluationInput input)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (input == null) throw new ArgumentNullException(nameof(input));
            var showEvent = save.ShowEvents.SingleOrDefault(x => x?.Id == input.ShowEventId)
                ?? throw new ArgumentException("Show event was not found.", nameof(input));
            if (showEvent.EventType != ShowEventType.Promo)
                throw new ArgumentException("Show event is not a promo.", nameof(input));
            var show = save.Shows.SingleOrDefault(x => x?.Id == showEvent.ShowId)
                ?? throw new InvalidOperationException("Promo references a missing show.");
            if (show.ShowVersion < 1)
                throw new InvalidOperationException("Promo evaluation requires a confirmed show version.");
            var promo = save.PromoPlans.SingleOrDefault(x => x?.Id == showEvent.DetailId)
                ?? throw new InvalidOperationException("Promo plan was not found.");
            ValidatePlan(save, promo);
            ValidateRange(input.RoleFit, -10f, 10f, nameof(input.RoleFit));
            ValidateRange(input.PurposeFit, -10f, 10f, nameof(input.PurposeFit));
            ValidateRange(input.StoryContext, -10f, 15f, nameof(input.StoryContext));
            ValidateRange(input.DurationFit, -10f, 5f, nameof(input.DurationFit));
            ValidateRange(input.Freshness, -15f, 5f, nameof(input.Freshness));

            var basePerformance = CalculateBasePerformance(promo, input);
            var score = Clamp(basePerformance + input.RoleFit + input.PurposeFit + input.StoryContext +
                input.DurationFit + input.Freshness + input.ResultVariance, 0f, 100f);

            return new PromoResultState
            {
                Id = createId(),
                ShowId = show.Id,
                ShowVersion = show.ShowVersion,
                ShowEventId = showEvent.Id,
                PromoPlanId = promo.Id,
                PromoScore = score,
                EvaluationReasons = new List<EvaluationReasonState>
                {
                    new() { Code = "promo.base-performance", Contribution = basePerformance },
                    new() { Code = "promo.role-fit", Contribution = input.RoleFit },
                    new() { Code = "promo.purpose-fit", Contribution = input.PurposeFit },
                    new() { Code = "promo.story-context", Contribution = input.StoryContext },
                    new() { Code = "promo.duration-fit", Contribution = input.DurationFit },
                    new() { Code = "promo.freshness", Contribution = input.Freshness },
                    new() { Code = "promo.result-variance", Contribution = input.ResultVariance }
                },
                ResultSeed = input.ResultSeed
            };
        }

        private static float CalculateBasePerformance(PromoPlanState promo, PromoEvaluationInput input)
        {
            var performances = input.ParticipantPerformances ?? new List<PromoParticipantPerformance>();
            foreach (var performance in performances)
            {
                if (performance == null || !promo.ParticipantIds.Contains(performance.WrestlerId))
                    throw new ArgumentException("Participant performance references a non-participant.", nameof(input));
                ValidateRange(performance.Score, 0f, 100f, nameof(performance.Score));
            }
            if (performances.Select(x => x.WrestlerId).Distinct(StringComparer.Ordinal).Count() != performances.Count)
                throw new ArgumentException("Participant performances must be unique.", nameof(input));

            if (string.IsNullOrEmpty(input.PrimarySpeakerId))
            {
                if (promo.Presentation != PromoPresentation.VideoPackage && promo.Purpose != PromoPurpose.SponsorAdvertisement)
                    throw new ArgumentException("A spoken promo requires a primary speaker.", nameof(input));
                ValidateRange(input.ProductionPerformance, 0f, 100f, nameof(input.ProductionPerformance));
                return input.ProductionPerformance;
            }

            var primary = performances.SingleOrDefault(x => x.WrestlerId == input.PrimarySpeakerId)
                ?? throw new ArgumentException("Primary speaker performance is missing.", nameof(input));
            if (performances.Count == 1) return primary.Score;
            var supportingAverage = performances.Where(x => x.WrestlerId != input.PrimarySpeakerId).Average(x => x.Score);
            return primary.Score * 0.60f + supportingAverage * 0.40f;
        }

        private static void ValidatePlan(GameSave save, PromoPlanState promo)
        {
            if (promo.ParticipantIds == null || promo.ParticipantIds.Count == 0 ||
                promo.ParticipantIds.Distinct(StringComparer.Ordinal).Count() != promo.ParticipantIds.Count)
                throw new InvalidOperationException("Promo requires unique participants.");
            foreach (var id in promo.ParticipantIds)
                if (save.Wrestlers.All(x => x?.Id != id))
                    throw new InvalidOperationException($"Promo participant was not found: {id}");
            if (promo.Purpose == PromoPurpose.SponsorAdvertisement && string.IsNullOrWhiteSpace(promo.SponsorRequirementId))
                throw new InvalidOperationException("Sponsor advertisement requires a sponsor requirement.");
        }

        private static void ValidateRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name, $"Value must be finite and between {minimum} and {maximum}.");
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
