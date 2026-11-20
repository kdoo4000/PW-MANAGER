using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Identifiers;
namespace PWManager.Domain.Services
{
    public sealed class MerchandiseService
    {
        private readonly Func<string> createId;
        public MerchandiseService(Func<string> createId = null) => this.createId = createId ?? EntityId.CreateRuntimeId;
        public static void Initialize(GameSave save)
        {
            save.Merchandise ??= new();
            if (!save.Merchandise.IsInitialized) { save.Merchandise.IsInitialized = true; save.Merchandise.LastOnlineDate = save.CurrentDate; }
        }
        public static double FameMultiplier(long fame) => 1 + Math.Sqrt(Math.Max(0, fame) / 500d);
        public static double Reaction(GameSave save, WrestlerState wrestler)
        {
            var audience = save.Promotion.Audience;
            var counts = new[] { audience?.Mark.Followers ?? 0, audience?.Casual.Followers ?? 0, audience?.Hardcore.Followers ?? 0 };
            var reactions = new[] { wrestler.FanReaction.MarkResponse, wrestler.FanReaction.CasualResponse, wrestler.FanReaction.HardcoreResponse };
            var total = counts.Sum(); var result = 0d;
            for (var i = 0; i < 3; i++) result += (total > 0 ? counts[i] / (double)total : 1d / 3) *
                (Math.Clamp(reactions[i].Interest, 0, 100) / 50d) * (.75 + Math.Clamp(reactions[i].Preference, 0, 100) / 200d);
            return result;
        }
        public void ProcessDay(GameSave save)
        {
            Initialize(save); ValidateState(save);
            if (save.Merchandise.LastOnlineDate.DaysUntil(save.CurrentDate) < 7) return;
            var state = save.Merchandise.Copy(); var transactions = new List<TransactionRecord>();
            var key = $"merch-online:{save.CurrentDate.Year:D4}{save.CurrentDate.Month:D2}{save.CurrentDate.Day:D2}";
            foreach (var wrestler in save.Wrestlers.Where(x => Eligible(save, x, save.CurrentDate)))
                Sell(save, state, transactions, key, wrestler, MerchandiseChannel.Online,
                    (save.Promotion.Audience?.TotalFollowers ?? 0) * .002 * FameMultiplier(wrestler.Fame.Value) * Reaction(save, wrestler), save.CurrentDate);
            state.ProcessedKeys.Add(key); state.LastOnlineDate = save.CurrentDate;
            ValidateNewIds(save, transactions);
            var salary = new SalaryPaymentService(createId).PrepareSettlement(save, save.CurrentDate, transactions);
            salary.Apply(save); save.Merchandise = state;
        }
        public Prepared PrepareShow(ResultApplicationContext context)
        {
            var save = context.Save;
            var state = save.Merchandise?.Copy() ?? new MerchandiseState();
            if (!state.IsInitialized) { state.IsInitialized = true; state.LastOnlineDate = save.CurrentDate; }
            var transactions = new List<TransactionRecord>();
            var key = $"merch-show:{context.Show.Id}:{context.ShowResult.ShowVersion}";
            if (state.ProcessedKeys.Contains(key)) throw new InvalidOperationException("굿즈 현장 판매가 이미 정산되었습니다.");
            var scores = new Dictionary<string, (double Total, int Minutes)>();
            foreach (var match in context.MatchResults.Where(x => string.IsNullOrEmpty(x.CancellationReason)))
            {
                var minutes = save.ShowEvents.Single(x => x.Id == match.ShowEventId).PlannedDuration;
                foreach (var id in match.ParticipantProfiles.SelectMany(x => x.MemberIds).Distinct()) AddScore(id, Math.Clamp(match.FinalMatchQuality * 5, 0, 100), minutes);
            }
            foreach (var promo in context.PromoResults.Where(x => string.IsNullOrEmpty(x.CancellationReason)))
            {
                var minutes = save.ShowEvents.Single(x => x.Id == promo.ShowEventId).PlannedDuration;
                foreach (var id in save.PromoPlans.Single(x => x.Id == promo.PromoPlanId).ParticipantIds.Distinct()) AddScore(id, Math.Clamp(promo.PromoScore, 0, 100), minutes);
            }
            foreach (var item in scores)
            {
                var wrestler = save.Wrestlers.Single(x => x.Id == item.Key);
                if (!Eligible(save, wrestler, context.Show.Date) || item.Value.Minutes <= 0) continue;
                var quality = item.Value.Total / item.Value.Minutes;
                Sell(save, state, transactions, key, wrestler, MerchandiseChannel.Venue, context.ShowResult.FinancialSettlement.Attendance * .005 *
                    FameMultiplier(wrestler.Fame.Value) * Reaction(save, wrestler) * (.75 + quality / 200), context.Show.Date);
            }
            state.ProcessedKeys.Add(key); ValidateNewIds(save, transactions);
            return new Prepared(state, transactions);
            void AddScore(string id, double score, int minutes)
            {
                scores.TryGetValue(id, out var value); scores[id] = (value.Total + score * minutes, value.Minutes + minutes);
            }
        }
        public sealed class Prepared
        {
            private readonly MerchandiseState state;
            public IReadOnlyList<TransactionRecord> Transactions { get; }
            internal Prepared(MerchandiseState state, List<TransactionRecord> transactions) { this.state = state; Transactions = transactions; }
            public void Apply(GameSave save) => save.Merchandise = state;
        }
        private static bool Eligible(GameSave save, WrestlerState wrestler, GameDate date) => wrestler.PromotionId == save.Promotion.Id &&
            wrestler.Roster.ActivityState is RosterActivityState.Active or RosterActivityState.Suspended &&
            (save.Player?.WrestlerId == wrestler.Id || save.OwnContracts.Any(x => x.Type == ContractType.Wrestler && x.PersonId == wrestler.Id && x.Covers(date)));
        private void Sell(GameSave save, MerchandiseState state, List<TransactionRecord> transactions, string key, WrestlerState wrestler, MerchandiseChannel channel, double demand, GameDate date)
        {
            if (!double.IsFinite(demand) || demand < 0) throw new InvalidOperationException("굿즈 판매량이 올바르지 않습니다.");
            var remainder = state.Remainders.SingleOrDefault(x => x.WrestlerId == wrestler.Id);
            if (remainder == null) { remainder = new MerchandiseRemainder { WrestlerId = wrestler.Id }; state.Remainders.Add(remainder); }
            var total = demand + (channel == MerchandiseChannel.Online ? remainder.Online : remainder.Venue);
            var units = checked((long)Math.Floor(total));
            if (channel == MerchandiseChannel.Online) remainder.Online = total - units; else remainder.Venue = total - units;
            if (units == 0) return;
            var sale = new MerchandiseSale { Key = key, WrestlerId = wrestler.Id, Date = date, Channel = channel, Units = units,
                Gross = checked(units * 30), Production = checked(units * 8), Selling = checked(units * (channel == MerchandiseChannel.Online ? 4 : 2)), Royalty = checked(units * 6) };
            state.Sales.Add(sale);
            foreach (var entry in new[] { (TransactionType.MerchandiseRevenue, sale.Gross), (TransactionType.MerchandiseProductionCost, -sale.Production),
                (TransactionType.MerchandiseSalesCost, -sale.Selling), (TransactionType.MerchandiseRoyalty, -sale.Royalty) })
                transactions.Add(new TransactionRecord { Id = createId(), PromotionId = save.Promotion.Id, Date = date, Type = entry.Item1, Amount = entry.Item2, ReasonId = key + ":" + wrestler.Id });
        }
        private static void ValidateNewIds(GameSave save, IEnumerable<TransactionRecord> transactions)
        {
            var ids = new HashSet<string>(save.Transactions.Select(x => x.Id), StringComparer.Ordinal);
            foreach (var transaction in transactions) if (string.IsNullOrWhiteSpace(transaction.Id) || !ids.Add(transaction.Id)) throw new InvalidOperationException("굿즈 거래 ID가 중복됩니다.");
        }
        public static void ValidateState(GameSave save)
        {
            var state = save.Merchandise; if (state == null) return;
            if (state.Remainders == null || state.Sales == null || state.ProcessedKeys == null || state.ProcessedKeys.Any(string.IsNullOrWhiteSpace) ||
                state.ProcessedKeys.Distinct().Count() != state.ProcessedKeys.Count || !state.IsInitialized && (state.Remainders.Count > 0 || state.Sales.Count > 0 || state.ProcessedKeys.Count > 0))
                throw new InvalidOperationException("굿즈 정산 정보가 올바르지 않습니다.");
            if (state.IsInitialized && (state.LastOnlineDate.Year < 1 || state.LastOnlineDate.CompareTo(save.CurrentDate) > 0)) throw new InvalidOperationException("굿즈 정산 날짜가 올바르지 않습니다.");
            var wrestlers = save.Wrestlers.Select(x => x.Id).ToHashSet(); var ids = new HashSet<string>();
            foreach (var r in state.Remainders)
                if (r == null || !wrestlers.Contains(r.WrestlerId) || !ids.Add(r.WrestlerId) || !double.IsFinite(r.Online) || !double.IsFinite(r.Venue) || r.Online < 0 || r.Online >= 1 || r.Venue < 0 || r.Venue >= 1)
                    throw new InvalidOperationException("굿즈 소수 판매량이 올바르지 않습니다.");
            var sales = new HashSet<string>();
            var processedKeys = state.ProcessedKeys.ToHashSet(StringComparer.Ordinal);
            var ledger = save.Transactions.Where(x => !x.IsVoided).ToLookup(x => (x.ReasonId, x.Type));
            foreach (var sale in state.Sales)
            {
                if (sale == null || !wrestlers.Contains(sale.WrestlerId) || sale.Units <= 0 || sale.Gross <= 0 || sale.Production < 0 || sale.Selling < 0 || sale.Royalty < 0 ||
                    (decimal)sale.Production + sale.Selling + sale.Royalty > sale.Gross || !Enum.IsDefined(typeof(MerchandiseChannel), sale.Channel) ||
                    !processedKeys.Contains(sale.Key) || !sales.Add(sale.Key + ":" + sale.WrestlerId) || sale.Date.CompareTo(save.CurrentDate) > 0)
                    throw new InvalidOperationException("굿즈 판매 기록이 올바르지 않습니다.");
                var reason = sale.Key + ":" + sale.WrestlerId;
                foreach (var expected in new[] { (TransactionType.MerchandiseRevenue, sale.Gross), (TransactionType.MerchandiseProductionCost, -sale.Production),
                    (TransactionType.MerchandiseSalesCost, -sale.Selling), (TransactionType.MerchandiseRoyalty, -sale.Royalty) })
                {
                    var entries = ledger[(reason, expected.Item1)].ToList();
                    if (entries.Count != 1 || entries[0].PromotionId != save.Promotion.Id || entries[0].Amount != expected.Item2 || !entries[0].Date.Equals(sale.Date))
                        throw new InvalidOperationException("굿즈 거래와 판매 기록이 일치하지 않습니다.");
                }
            }
            foreach (var transaction in save.Transactions.Where(x => x.ReasonId?.StartsWith("merch-", StringComparison.Ordinal) == true &&
                x.Type is TransactionType.MerchandiseRevenue or TransactionType.MerchandiseProductionCost or TransactionType.MerchandiseSalesCost or TransactionType.MerchandiseRoyalty))
                if (!sales.Contains(transaction.ReasonId)) throw new InvalidOperationException("굿즈 거래의 원본 판매 기록이 없습니다.");
        }
    }
}
