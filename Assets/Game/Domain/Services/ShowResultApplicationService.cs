using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class ShowResultApplicationService
    {
        private readonly Func<string> createId;

        public ShowResultApplicationService(Func<string> createId = null)
        {
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public void Apply(GameSave save, string showResultId)
        {
            var context = ResultApplicationContext.Create(save, showResultId);
            var schedule = (save.Schedules ?? new List<ScheduleState>()).SingleOrDefault(x => x?.Id == context.Show.ScheduleId)
                ?? throw new InvalidOperationException("Show references a missing schedule.");
            if (context.Show.Status != ShowStatus.Completed)
                throw new InvalidOperationException("Only a completed show result can be applied.");
            if (schedule.Status != ScheduleStatus.Confirmed)
                throw new InvalidOperationException("Only a confirmed schedule can be completed.");

            var wrestlers = (save.Wrestlers ?? new List<WrestlerState>()).Where(x => x != null)
                .ToDictionary(x => x.Id, StringComparer.Ordinal);
            var conditionDeltas = CollectConditionDeltas(context, wrestlers);
            var appearanceIds = CollectAppearanceIds(context, wrestlers);
            var transactions = CreateSettlementTransactions(context, save.Transactions ?? new List<TransactionRecord>());

            // Everything above is validation and preparation. Mutation starts only after the full result is valid.
            foreach (var pair in conditionDeltas)
                wrestlers[pair.Key].Condition.Condition = Clamp(wrestlers[pair.Key].Condition.Condition + pair.Value, 0f, 100f);
            foreach (var wrestlerId in appearanceIds)
            {
                var wrestler = wrestlers[wrestlerId];
                wrestler.Roster.LastAppearanceDate = new OptionalGameDate(context.Show.Date);
            }
            foreach (var wrestlerId in conditionDeltas.Keys)
            {
                var wrestler = wrestlers[wrestlerId];
                wrestler.Roster.LastMatchDate = new OptionalGameDate(context.Show.Date);
                wrestler.Status.OfficialMatchCount++;
            }
            save.Transactions.AddRange(transactions);
            schedule.Status = ScheduleStatus.Completed;
            save.ProcessedIds.Add(context.ApplicationKey);
        }

        private static Dictionary<string, float> CollectConditionDeltas(
            ResultApplicationContext context, IReadOnlyDictionary<string, WrestlerState> wrestlers)
        {
            var deltas = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var matchResult in context.MatchResults)
            foreach (var change in matchResult.WrestlerConditionChanges ?? new List<WrestlerConditionChangeData>())
            {
                if (change == null || change.SourceResultId != matchResult.Id || string.IsNullOrWhiteSpace(change.WrestlerId) ||
                    !wrestlers.ContainsKey(change.WrestlerId))
                    throw new InvalidOperationException("Match condition change references invalid result or wrestler.");
                if (float.IsNaN(change.ConditionDelta) || float.IsInfinity(change.ConditionDelta))
                    throw new InvalidOperationException("Match condition change must be finite.");
                deltas.TryGetValue(change.WrestlerId, out var current);
                deltas[change.WrestlerId] = current + change.ConditionDelta;
            }
            return deltas;
        }

        private static HashSet<string> CollectAppearanceIds(
            ResultApplicationContext context, IReadOnlyDictionary<string, WrestlerState> wrestlers)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var matchResult in context.MatchResults)
            foreach (var profile in matchResult.ParticipantProfiles ?? new List<MatchParticipantProfileState>())
            foreach (var wrestlerId in profile?.MemberIds ?? new List<string>())
                ids.Add(wrestlerId);
            foreach (var promoResult in context.PromoResults)
            {
                var promo = (context.Save.PromoPlans ?? new List<PromoPlanState>()).SingleOrDefault(x => x?.Id == promoResult.PromoPlanId)
                    ?? throw new InvalidOperationException("Promo result references a missing promo plan.");
                foreach (var wrestlerId in promo.ParticipantIds ?? new List<string>()) ids.Add(wrestlerId);
            }
            if (ids.Any(x => string.IsNullOrWhiteSpace(x) || !wrestlers.ContainsKey(x)))
                throw new InvalidOperationException("Show result appearance references a missing wrestler.");
            return ids;
        }

        private List<TransactionRecord> CreateSettlementTransactions(ResultApplicationContext context, IEnumerable<TransactionRecord> existing)
        {
            var settlement = context.ShowResult.FinancialSettlement;
            var entries = new List<(TransactionType Type, long Amount)>();
            if (settlement.Revenue > 0) entries.Add((TransactionType.TicketRevenue, settlement.Revenue));
            if (settlement.Cost > 0) entries.Add((TransactionType.VenueCost, -settlement.Cost));

            var ids = new HashSet<string>((existing ?? Array.Empty<TransactionRecord>()).Where(x => x != null).Select(x => x.Id), StringComparer.Ordinal);
            var transactions = new List<TransactionRecord>();
            foreach (var entry in entries)
            {
                var id = createId();
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new InvalidOperationException("Settlement transaction ID must be unique.");
                transactions.Add(new TransactionRecord
                {
                    Id = id,
                    PromotionId = context.Save.Promotion.Id,
                    Date = context.Show.Date,
                    Type = entry.Type,
                    Amount = entry.Amount,
                    ReasonId = context.ApplicationKey
                });
            }
            return transactions;
        }

        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
