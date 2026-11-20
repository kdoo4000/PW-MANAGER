using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class SalaryPaymentService
    {
        public const float InitialPenalty = 10f;
        public const float WeeklyPenalty = 5f;
        private readonly Func<string> createId;

        public SalaryPaymentService(Func<string> createId = null) => this.createId = createId ?? EntityId.CreateRuntimeId;

        public static long Outstanding(GameSave save)
        {
            if (save == null) return 0;
            var contractIds = save.OwnContracts.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            return (save.SalaryPayments ?? new()).Where(x => x != null && contractIds.Contains(x.ContractId)).Sum(x => x.RemainingAmount);
        }

        public static long EstimateAppearanceFees(GameSave save, ShowState show)
        {
            if (show.Status == ShowStatus.Completed) return save.SalaryPayments.Where(x => x.ShowId == show.Id).Sum(x => x.Amount);
            var events = save.ShowEvents.Where(x => show.TimelineEventIds.Contains(x.Id)).ToList();
            var matches = save.MatchPlans.Where(x => events.Any(e => e.EventType == ShowEventType.Match && e.DetailId == x.Id));
            var matchIds = matches.SelectMany(x => x.Sides.Count > 0 ? x.Sides.SelectMany(s => s.MemberIds) : x.ParticipantIds).ToHashSet();
            var promoIds = save.PromoPlans.Where(x => events.Any(e => e.EventType == ShowEventType.Promo && e.DetailId == x.Id))
                .SelectMany(x => x.ParticipantIds).ToHashSet();
            return matchIds.Union(promoIds).Sum(id =>
            {
                var contract = save.OwnContracts.SingleOrDefault(x => x.Type == ContractType.Wrestler && x.PersonId == id && x.Covers(show.Date));
                return contract == null ? 0 : Math.Max(matchIds.Contains(id) ? contract.MatchAppearanceFee : 0,
                    promoIds.Contains(id) ? contract.PromoAppearanceFee : 0);
            });
        }

        public Settlement PrepareSettlement(GameSave save, GameDate date, IEnumerable<TransactionRecord> incomeAndCosts,
            ResultApplicationContext show = null, ContractState startingContract = null)
        {
            ValidateState(save);
            var prepared = new GameSave { Promotion = save.Promotion, Player = save.Player, CurrentDate = save.CurrentDate,
                Wrestlers = save.Wrestlers, Managers = save.Managers,
                Contracts = save.Contracts.Select(x => x?.Copy()).ToList(),
                Shows = save.Shows,
                ProcessedIds = new List<string>(save.ProcessedIds),
                SalaryPayments = (save.SalaryPayments ?? new()).Select(x => x.Copy()).ToList(),
                Transactions = save.Transactions.Concat(incomeAndCosts).ToList(),
                InboxMessages = new List<InboxMessageState>(save.InboxMessages) };
            if (startingContract != null)
            {
                if (!string.IsNullOrEmpty(startingContract.PromotionId) && startingContract.PromotionId != save.Promotion.Id)
                    throw new InvalidOperationException("다른 단체의 계약은 정산할 수 없습니다.");
                prepared.Contracts.Add(startingContract.Copy());
                if (startingContract.StartDate.Equals(date)) AddMonthlyPayments(prepared, date, onlyContractId: startingContract.Id);
            }
            if (show != null) AddAppearancePayments(prepared, show);
            if (startingContract != null && startingContract.StartDate.CompareTo(date) > 0)
                return new Settlement(prepared, new List<(ContractState, float)>());
            SettleOutstanding(prepared, date);
            var penalties = new List<(ContractState Contract, float Amount)>();
            ProcessPenalties(prepared, date, (contract, amount) => penalties.Add((contract, amount)));
            return new Settlement(prepared, penalties);
        }

        public sealed class Settlement
        {
            private readonly GameSave prepared;
            private readonly List<(ContractState Contract, float Amount)> penalties;
            internal Settlement(GameSave prepared, List<(ContractState Contract, float Amount)> penalties)
            { this.prepared = prepared; this.penalties = penalties; }
            internal IEnumerable<string> RecordIds => prepared.Transactions.Select(x => x.Id).Concat(prepared.InboxMessages.Select(x => x.Id))
                .Concat(prepared.SalaryPayments.Select(x => x.Id));

            public void Apply(GameSave save)
            {
                save.Transactions = prepared.Transactions;
                save.SalaryPayments = prepared.SalaryPayments;
                save.InboxMessages = prepared.InboxMessages;
                save.ProcessedIds = prepared.ProcessedIds;
                foreach (var penalty in penalties) ChangeSatisfaction(save, penalty.Contract, penalty.Amount);
                foreach (var contract in save.OwnContracts.Where(x => x != null))
                {
                    var result = prepared.Contracts.Single(x => x?.Id == contract.Id);
                    contract.Status = result.Status;
                    contract.LastSalaryPenaltyDate = result.LastSalaryPenaltyDate;
                }
            }
        }

        public void ProcessDay(GameSave save, GameDate date, ICollection<string> processedThisRun = null)
        {
            ValidateState(save);
            save.SalaryPayments ??= new();
            AddMonthlyPayments(save, date, processedThisRun);

            SettleOutstanding(save, date);
            ProcessPenalties(save, date);
        }

        private void AddMonthlyPayments(GameSave save, GameDate date, ICollection<string> processedThisRun = null, string onlyContractId = null)
        {
            foreach (var contract in save.OwnContracts.Where(x => x != null && IsPayable(x.Status) && x.MonthlySalary > 0 &&
                (onlyContractId == null || x.Id == onlyContractId) &&
                (date.Day == 1 || x.StartDate.Equals(date)) && x.StartDate.CompareTo(date) <= 0 &&
                x.EndDate.CompareTo(date) >= 0).OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var key = $"salary:{contract.Id}:{date.Year:D4}{date.Month:D2}01";
                if (save.ProcessedIds.Contains(key)) continue;
                var days = DateTime.DaysInMonth(date.Year, date.Month);
                var end = new GameDate(date.Year, date.Month, days);
                if (contract.EndDate.CompareTo(end) < 0) end = contract.EndDate;
                var amount = (long)Math.Ceiling((decimal)contract.MonthlySalary * (date.DaysUntil(end) + 1) / days);
                save.SalaryPayments.Add(new SalaryPaymentState { Id = PaymentId(save), ContractId = contract.Id,
                    DueDate = date, Amount = amount, RemainingAmount = amount });
                save.ProcessedIds.Add(key);
                processedThisRun?.Add(key);
            }
        }

        private void ProcessPenalties(GameSave save, GameDate date, Action<ContractState, float> changeSatisfaction = null)
        {
            changeSatisfaction ??= (contract, penalty) => ChangeSatisfaction(save, contract, penalty);
            var inbox = new InboxService(createId);
            foreach (var payment in save.SalaryPayments.Where(x => x.RemainingAmount > 0 && !x.WasOverdue && x.DueDate.CompareTo(date) <= 0))
            {
                var contract = save.Contracts.Single(x => x.Id == payment.ContractId);
                payment.WasOverdue = true;
                changeSatisfaction(contract, InitialPenalty);
                if (!contract.LastSalaryPenaltyDate.HasValue) contract.LastSalaryPenaltyDate = new OptionalGameDate(date);
                inbox.Publish(save, $"salary-overdue:{payment.Id}", InboxMessageType.Information, InboxPriority.Important,
                    "계약 담당", $"{PersonName(save, contract)}의 급여가 미지급되었습니다",
                    $"급여 ${payment.Amount:N0} 중 ${payment.RemainingAmount:N0} 미지급 · 만족도 −{InitialPenalty:0}\n" +
                    "가용 자금으로 오래된 급여부터 자동 상환합니다. 연체가 이어지면 7일마다 만족도가 5 감소합니다.",
                    Target(contract), contract.PersonId);
            }

            foreach (var contract in save.OwnContracts.Where(x => x != null &&
                save.SalaryPayments.Any(p => p.ContractId == x.Id && p.RemainingAmount > 0)))
            {
                if (IsPayable(contract.Status) && date.CompareTo(contract.EndDate) <= 0) contract.Status = ContractStatus.Overdue;
                var weeks = contract.LastSalaryPenaltyDate.Value.DaysUntil(date) / 7;
                if (weeks <= 0) continue;
                var penalty = weeks * WeeklyPenalty;
                changeSatisfaction(contract, penalty);
                contract.LastSalaryPenaltyDate = new OptionalGameDate(contract.LastSalaryPenaltyDate.Value.AddDays(weeks * 7));
                inbox.Publish(save, $"salary-penalty:{contract.Id}:{contract.LastSalaryPenaltyDate.Value.Year:D4}" +
                    $"{contract.LastSalaryPenaltyDate.Value.Month:D2}{contract.LastSalaryPenaltyDate.Value.Day:D2}",
                    InboxMessageType.Information, InboxPriority.Important, "계약 담당",
                    $"{PersonName(save, contract)}의 급여 연체가 이어지고 있습니다",
                    $"미지급 급여 ${save.SalaryPayments.Where(p => p.ContractId == contract.Id).Sum(p => p.RemainingAmount):N0} · 만족도 −{penalty:0}",
                    Target(contract), contract.PersonId);
            }
        }

        private void AddAppearancePayments(GameSave save, ResultApplicationContext context)
        {
            var matchIds = context.MatchResults.Where(x => string.IsNullOrEmpty(x.CancellationReason))
                .SelectMany(x => x.ParticipantProfiles).SelectMany(x => x.MemberIds).ToHashSet(StringComparer.Ordinal);
            var promoIds = context.PromoResults.Where(x => string.IsNullOrEmpty(x.CancellationReason))
                .SelectMany(x => context.Save.PromoPlans.Single(p => p.Id == x.PromoPlanId).ParticipantIds).ToHashSet(StringComparer.Ordinal);
            foreach (var personId in matchIds.Union(promoIds).OrderBy(x => x, StringComparer.Ordinal))
            {
                var contract = save.OwnContracts.SingleOrDefault(x => x.Type == ContractType.Wrestler && x.PersonId == personId && x.Covers(context.Show.Date));
                if (contract == null) continue;
                var match = matchIds.Contains(personId) ? contract.MatchAppearanceFee : 0;
                var promo = promoIds.Contains(personId) ? contract.PromoAppearanceFee : 0;
                var fee = Math.Max(match, promo);
                if (fee <= 0) continue;
                if (save.SalaryPayments.Any(x => x.ContractId == contract.Id && x.ShowId == context.Show.Id))
                    throw new InvalidOperationException("같은 쇼의 출연료를 중복 정산할 수 없습니다.");
                save.SalaryPayments.Add(new SalaryPaymentState { Id = PaymentId(save), ContractId = contract.Id,
                    ShowId = context.Show.Id, Kind = match >= promo ? SalaryPaymentKind.MatchAppearance : SalaryPaymentKind.PromoAppearance,
                    DueDate = context.Show.Date, Amount = fee, RemainingAmount = fee });
            }
        }

        private string PaymentId(GameSave save)
        {
            var id = createId();
            if (!EntityId.IsValidRuntimeId(id) || save.SalaryPayments.Any(x => x.Id == id) ||
                save.Transactions.Any(x => x.Id == id) || save.Contracts.Any(x => x.Id == id) || save.InboxMessages.Any(x => x.Id == id))
                throw new InvalidOperationException("Salary payment ID must be unique.");
            return id;
        }

        public void SettleOutstanding(GameSave save, GameDate date)
        {
            ValidateState(save);
            var available = Math.Max(0, save.Promotion.CalculateCurrentCash(save.Transactions));
            var pending = (save.SalaryPayments ?? new()).Where(x => x.RemainingAmount > 0 && x.DueDate.CompareTo(date) <= 0)
                .OrderBy(x => x.DueDate).ThenBy(x => x.ContractId, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            var transactions = new List<(SalaryPaymentState Payment, TransactionRecord Transaction)>();
            var ids = new HashSet<string>(save.Transactions.Select(x => x.Id).Concat(save.SalaryPayments.Select(x => x.Id))
                .Concat(save.Contracts.Select(x => x.Id)).Concat(save.InboxMessages.Select(x => x.Id)), StringComparer.Ordinal);
            foreach (var payment in pending)
            {
                var amount = Math.Min(available, payment.RemainingAmount);
                if (amount == 0) break;
                var id = createId();
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new InvalidOperationException("Salary transaction ID must be unique.");
                transactions.Add((payment, new TransactionRecord { Id = id, PromotionId = save.Promotion.Id, Date = date,
                    Type = payment.WasOverdue ? TransactionType.LatePayment : payment.Kind switch
                    {
                        SalaryPaymentKind.MatchAppearance => TransactionType.MatchAppearanceFee,
                        SalaryPaymentKind.PromoAppearance => TransactionType.PromoAppearanceFee,
                        _ => TransactionType.Salary
                    },
                    Amount = -amount, ReasonId = payment.Id }));
                available -= amount;
            }
            foreach (var entry in transactions)
            {
                entry.Payment.RemainingAmount += entry.Transaction.Amount;
                save.Transactions.Add(entry.Transaction);
            }

            foreach (var contract in save.OwnContracts.Where(x => x != null))
            {
                if ((save.SalaryPayments ?? new()).Any(x => x.ContractId == contract.Id && x.RemainingAmount > 0)) continue;
                var settled = pending.LastOrDefault(x => x.ContractId == contract.Id && x.WasOverdue);
                if (settled != null)
                    new InboxService(createId).Publish(save, $"salary-cleared:{settled.Id}", InboxMessageType.Information, InboxPriority.Normal,
                        "계약 담당", $"{PersonName(save, contract)}의 미지급 급여를 모두 상환했습니다",
                        "미지급 급여가 모두 지급되었습니다. 연체로 감소한 만족도는 자동 회복되지 않습니다.", Target(contract), contract.PersonId);
                contract.LastSalaryPenaltyDate = default;
                if (contract.Status == ContractStatus.Overdue)
                    contract.Status = date.CompareTo(contract.EndDate) > 0 ? ContractStatus.Expired :
                        date.DaysUntil(contract.EndDate) <= 30 ? ContractStatus.Expiring : ContractStatus.Active;
            }
        }

        public static void ValidateState(GameSave save)
        {
            if (save?.Promotion == null) throw new InvalidOperationException("Salary payments require a promotion.");
            var ownContractIds = save.OwnContracts.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var periods = new HashSet<string>(StringComparer.Ordinal);
            foreach (var payment in save.SalaryPayments ?? new())
            {
                if (payment == null || !EntityId.IsValidRuntimeId(payment.Id) || !ids.Add(payment.Id) ||
                    !ownContractIds.Contains(payment.ContractId) || payment.Amount <= 0 ||
                    payment.RemainingAmount < 0 || payment.RemainingAmount > payment.Amount)
                    throw new InvalidOperationException("Salary payment contains invalid amounts, ID or contract reference.");
                try { _ = new DateTime(payment.DueDate.Year, payment.DueDate.Month, payment.DueDate.Day); }
                catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("Salary payment due date is invalid."); }
                if (!Enum.IsDefined(typeof(SalaryPaymentKind), payment.Kind) ||
                    payment.Kind == SalaryPaymentKind.Monthly && !string.IsNullOrEmpty(payment.ShowId) ||
                    payment.Kind != SalaryPaymentKind.Monthly && !(save.Shows ?? new()).Any(x => x.Id == payment.ShowId && x.Date.Equals(payment.DueDate)))
                    throw new InvalidOperationException("Salary payment kind or show is invalid.");
                var period = payment.Kind == SalaryPaymentKind.Monthly
                    ? $"{payment.ContractId}:monthly:{payment.DueDate.Year:D4}{payment.DueDate.Month:D2}"
                    : $"{payment.ContractId}:show:{payment.ShowId}";
                if (!periods.Add(period))
                    throw new InvalidOperationException("A salary period cannot be charged twice.");
                if (payment.WasOverdue && payment.RemainingAmount > 0 &&
                    !save.Contracts.Single(x => x.Id == payment.ContractId).LastSalaryPenaltyDate.HasValue)
                    throw new InvalidOperationException("Overdue salary requires a penalty date.");
            }
            foreach (var contract in save.Contracts ?? new())
            {
                if (contract?.LastSalaryPenaltyDate.HasValue != true) continue;
                var date = contract.LastSalaryPenaltyDate.Value;
                try { _ = new DateTime(date.Year, date.Month, date.Day); }
                catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("Salary penalty date is invalid."); }
            }
        }

        private static void ChangeSatisfaction(GameSave save, ContractState contract, float penalty)
        {
            if (contract.Type == ContractType.Wrestler)
            {
                var wrestler = save.Wrestlers.FirstOrDefault(x => x?.Id == contract.PersonId);
                if (wrestler?.Condition != null) wrestler.Condition.Satisfaction = Math.Max(0, wrestler.Condition.Satisfaction - penalty);
            }
            else
            {
                var manager = save.Managers.FirstOrDefault(x => x?.Id == contract.PersonId);
                if (manager != null) manager.Satisfaction = Math.Max(0, manager.Satisfaction - penalty);
            }
        }

        private static string PersonName(GameSave save, ContractState contract) => contract.Type == ContractType.Wrestler
            ? save.Wrestlers.FirstOrDefault(x => x?.Id == contract.PersonId)?.Identity?.RingName ?? "선수"
            : save.Managers.FirstOrDefault(x => x?.Id == contract.PersonId)?.Name ??
                (save.Player?.ManagerId == contract.PersonId ? save.Player.Name : "전문 매니저");

        private static InboxTargetType Target(ContractState contract) => contract.Type == ContractType.Wrestler ? InboxTargetType.Wrestler : InboxTargetType.None;
        private static bool IsPayable(ContractStatus status) => status is ContractStatus.Active or ContractStatus.Expiring or ContractStatus.Overdue;
    }
}
