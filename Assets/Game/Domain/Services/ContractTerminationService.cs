using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class ContractTerminationQuote
    {
        public long RemainingSalary;
        public long MutualCompensation;
        public float AcceptancePercent;
        public string BlockingReason;
    }

    public sealed class ContractTerminationService
    {
        public const int RejectionCooldownDays = 7;
        private readonly Func<string> createId;
        private readonly Random random;

        public ContractTerminationService(Func<string> createId = null, Random random = null)
        { this.createId = createId ?? EntityId.CreateRuntimeId; this.random = random; }

        public static ContractTerminationQuote Quote(GameSave save, string personId, ContractType type)
        {
            if (save?.Promotion == null) throw new InvalidOperationException("활성 단체가 없습니다.");
            var contracts = ContractsToEnd(save, personId, type);
            if (contracts.Count == 0) throw new InvalidOperationException("해지할 계약이 없습니다.");
            var market = type == ContractType.Wrestler
                ? InitialContractOfferRules.Calculate(save.Wrestlers.Single(x => x.Id == personId)).MonthlySalary
                : InitialContractOfferRules.Calculate(save.Managers.Single(x => x.Id == personId)).MonthlySalary;
            decimal total = 0;
            foreach (var contract in contracts)
            {
                if (contract.MonthlySalary < 0 || contract.StartDate.CompareTo(contract.EndDate) > 0 ||
                    !EntityId.IsValidRuntimeId(contract.Id)) throw new InvalidOperationException("계약 정보가 올바르지 않습니다.");
                var start = save.CurrentDate.AddDays(1);
                if (contract.StartDate.CompareTo(start) > 0) start = contract.StartDate;
                while (start.CompareTo(contract.EndDate) <= 0)
                {
                    var days = DateTime.DaysInMonth(start.Year, start.Month);
                    var end = new GameDate(start.Year, start.Month, days);
                    if (contract.EndDate.CompareTo(end) < 0) end = contract.EndDate;
                    var key = $"salary:{contract.Id}:{start.Year:D4}{start.Month:D2}01";
                    var charged = save.ProcessedIds.Contains(key) || (save.SalaryPayments ?? new()).Any(x => x?.ContractId == contract.Id &&
                        x.Kind == SalaryPaymentKind.Monthly && x.DueDate.Year == start.Year && x.DueDate.Month == start.Month);
                    if (!charged) total += (decimal)contract.MonthlySalary * (start.DaysUntil(end) + 1) / days;
                    start = end.AddDays(1);
                }
            }
            var salary = contracts.OrderBy(x => x.StartDate).First().MonthlySalary;
            var satisfaction = type == ContractType.Wrestler ? save.Wrestlers.Single(x => x.Id == personId).Condition.Satisfaction
                : save.Managers.Single(x => x.Id == personId).Satisfaction;
            if (!float.IsFinite(satisfaction) || satisfaction < 0 || satisfaction > 100)
                throw new InvalidOperationException("만족도가 올바르지 않습니다.");
            var amount = checked((long)decimal.Ceiling(total));
            return new ContractTerminationQuote { RemainingSalary = amount, MutualCompensation = MutualAmount(amount),
                AcceptancePercent = (float)Math.Max(20, Math.Min(95, 70 - ((double)salary / market - 1) * 100 - (satisfaction - 50) * .2)),
                BlockingReason = BlockingReason(save, personId, type, PrepareDeparture(save, personId, type)) };
        }

        public ContractTerminationState Submit(GameSave save, string personId, ContractType type, bool mutual)
        {
            if (save?.Promotion == null) throw new InvalidOperationException("단체를 찾을 수 없습니다.");
            ValidateState(save);
            SalaryPaymentService.ValidateState(save);
            var contracts = ContractsToEnd(save, personId, type);
            var previous = (save.ContractTerminations ?? new()).LastOrDefault(x => x.PersonId == personId && x.Type == type);
            if (contracts.Count == 0 && previous?.Accepted == true && previous.Mutual == mutual && previous.Date.Equals(save.CurrentDate)) return previous;
            var quote = Quote(save, personId, type);
            if (quote.BlockingReason != null) throw new InvalidOperationException(quote.BlockingReason);
            var rejected = (save.ContractTerminations ?? new()).LastOrDefault(x => x.PersonId == personId && x.Type == type && x.Mutual && !x.Accepted &&
                x.ContractIds.Intersect(contracts.Select(c => c.Id)).Any());
            if (mutual && rejected != null && save.CurrentDate.CompareTo(rejected.NextAttemptDate) < 0)
                throw new InvalidOperationException($"{Date(rejected.NextAttemptDate)}부터 다시 제안할 수 있습니다.");
            var amount = mutual ? quote.MutualCompensation : quote.RemainingSalary;
            if (save.Promotion.CalculateCurrentCash(save.Transactions) < amount)
                throw new InvalidOperationException("해지 보상금 전액을 지급할 자금이 부족합니다.");
            var roll = mutual ? Math.Min(99.99999f, (float)((random ?? new Random(Seed(save, personId, type))).NextDouble() * 100)) : 0;
            var result = new ContractTerminationState { Id = createId(), PersonId = personId, Type = type, Date = save.CurrentDate,
                Mutual = mutual, RemainingSalary = quote.RemainingSalary, Compensation = amount,
                AcceptancePercent = mutual ? quote.AcceptancePercent : 100, RollPercent = roll,
                Accepted = !mutual || roll < quote.AcceptancePercent, ContractIds = contracts.Select(x => x.Id).ToList() };
            result.NextAttemptDate = result.Accepted ? save.CurrentDate : save.CurrentDate.AddDays(RejectionCooldownDays);
            TransactionRecord payment = result.Accepted && amount > 0 ? new TransactionRecord { Id = createId(), PromotionId = save.Promotion.Id,
                Date = save.CurrentDate, Type = TransactionType.TerminationFee, Amount = -amount, ReasonId = result.Id } : null;
            var ids = new HashSet<string>(save.Contracts.Select(x => x.Id).Concat(save.Transactions.Select(x => x.Id))
                .Concat((save.ContractNegotiations ?? new()).Select(x => x.Id)).Concat((save.ContractTerminations ?? new()).Select(x => x.Id)), StringComparer.Ordinal);
            var required = new List<string> { result.Id }; if (payment != null) required.Add(payment.Id);
            foreach (var id in required)
                if (!EntityId.IsValidRuntimeId(id) || !ids.Add(id)) throw new InvalidOperationException("해지 기록 ID가 중복되거나 올바르지 않습니다.");
            var departure = PrepareDeparture(save, personId, type);

            (save.ContractTerminations ??= new()).Add(result);
            if (!result.Accepted) return result;
            if (payment != null) save.Transactions.Add(payment);
            foreach (var contract in contracts) contract.Status = ContractStatus.Terminated;
            departure.Apply();
            return result;
        }

        public static void ValidateState(GameSave save)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var result in save.ContractTerminations ?? new())
            {
                if (result == null || !EntityId.IsValidRuntimeId(result.Id) || !ids.Add(result.Id) ||
                    !Enum.IsDefined(typeof(ContractType), result.Type) || result.RemainingSalary < 0 || result.Compensation < 0 ||
                    result.Compensation != (result.Mutual ? MutualAmount(result.RemainingSalary) : result.RemainingSalary) ||
                    !float.IsFinite(result.AcceptancePercent) || !float.IsFinite(result.RollPercent) || result.RollPercent < 0 || result.RollPercent >= 100 ||
                    (result.Mutual ? result.AcceptancePercent < 20 || result.AcceptancePercent > 95 : result.AcceptancePercent != 100 || result.RollPercent != 0) ||
                    result.Accepted != (result.RollPercent < result.AcceptancePercent) || result.ContractIds == null || result.ContractIds.Count == 0 ||
                    result.ContractIds.Distinct().Count() != result.ContractIds.Count || result.ContractIds.Any(id => !save.OwnContracts.Any(x => x?.Id == id &&
                        x.PersonId == result.PersonId && x.Type == result.Type && (!result.Accepted || x.Status == ContractStatus.Terminated))))
                    throw new InvalidOperationException("계약 해지 기록이 올바르지 않습니다.");
                try { _ = new DateTime(result.Date.Year, result.Date.Month, result.Date.Day); }
                catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("계약 해지 날짜가 올바르지 않습니다."); }
                if (!result.NextAttemptDate.Equals(result.Accepted ? result.Date : result.Date.AddDays(RejectionCooldownDays)))
                    throw new InvalidOperationException("해지 재시도 날짜가 올바르지 않습니다.");
                var payments = save.Transactions.Where(x => x?.Type == TransactionType.TerminationFee && x.ReasonId == result.Id).ToList();
                if (result.Accepted && result.Compensation > 0 ? payments.Count != 1 || payments[0].Amount != -result.Compensation || payments[0].IsVoided : payments.Count != 0)
                    throw new InvalidOperationException("해지 보상금 지급 기록이 올바르지 않습니다.");
            }
        }

        private static long MutualAmount(long amount) => checked((long)decimal.Ceiling(amount * .7m));
        private static List<ContractState> ContractsToEnd(GameSave save, string personId, ContractType type) => save.OwnContracts
            .Where(x => x?.PersonId == personId && x.Type == type && (x.IsInForce || x.Status == ContractStatus.Scheduled) &&
                x.EndDate.CompareTo(save.CurrentDate) >= 0).ToList();

        private sealed class Departure
        {
            public HashSet<string> Groups = new();
            public HashSet<string> Stories = new();
            public HashSet<string> Titles = new();
            public Action Apply;
        }

        internal static void ExpireEmployment(GameSave save, string personId, ContractType type) =>
            PrepareDeparture(save, personId, type, RosterActivityState.Inactive).Apply();

        private static Departure PrepareDeparture(GameSave save, string personId, ContractType type, RosterActivityState activity = RosterActivityState.Released)
        {
            var output = new Departure { Apply = () => { } };
            var currentPromotionId = type == ContractType.Manager
                ? save.Managers.Single(x => x.Id == personId).PromotionId
                : save.Wrestlers.Single(x => x.Id == personId).PromotionId;
            if (!string.IsNullOrEmpty(currentPromotionId) && currentPromotionId != save.Promotion.Id) return output;
            if (type == ContractType.Manager)
            {
                var manager = save.Managers.Single(x => x.Id == personId);
                output.Apply = () => manager.ActivityState = activity;
                return output;
            }
            var wrestler = save.Wrestlers.Single(x => x.Id == personId);
            var teams = save.TagTeams.Where(x => x?.Status == TagTeamStatus.Active && x.MemberIds.Contains(personId)).ToList();
            var titleTeamIds = save.TagTeams.Where(x => x?.MemberIds?.Contains(personId) == true).Select(x => x.Id).ToHashSet();
            var stables = save.Stables.Where(x => x?.Status == TagTeamStatus.Active && x.MemberIds.Contains(personId)).ToList();
            output.Groups.UnionWith(teams.Select(x => x.Id));
            output.Groups.UnionWith(stables.Where(x => x.MemberIds.Count - 1 < 3).Select(x => x.Id));
            var titles = save.Championships.Where(x => x != null && !string.IsNullOrEmpty(x.HolderId) &&
                (x.Type == ChampionshipType.Individual ? x.HolderId == personId : titleTeamIds.Contains(x.HolderId))).ToList();
            output.Titles.UnionWith(titles.Select(x => x.Id));
            var stories = save.Storylines.Where(x => x?.Status == StorylineStatus.Active &&
                (x.PartyType == StoryPartyType.Wrestler ? x.FirstWrestlerId == personId || x.SecondWrestlerId == personId :
                    output.Groups.Contains(x.FirstGroupId ?? "") || output.Groups.Contains(x.SecondGroupId ?? ""))).ToList();
            output.Stories.UnionWith(stories.Select(x => x.Id));
            output.Apply = () =>
            {
                foreach (var title in titles) ChampionshipService.SetHolder(save, title, null);
                foreach (var team in teams)
                {
                    team.Status = TagTeamStatus.Inactive;
                    foreach (var member in save.Wrestlers.Where(x => x.Roster.ActiveTagTeamId == team.Id)) member.Roster.ActiveTagTeamId = null;
                }
                foreach (var stable in stables)
                {
                    if (output.Groups.Contains(stable.Id))
                    {
                        stable.Status = TagTeamStatus.Inactive;
                        foreach (var member in save.Wrestlers.Where(x => x.Roster.ActiveStableId == stable.Id)) member.Roster.ActiveStableId = null;
                    }
                    else
                    {
                        stable.MemberIds.Remove(personId);
                        if (stable.LeaderId == personId) stable.LeaderId = stable.MemberIds[0];
                    }
                }
                foreach (var story in stories) { story.Status = StorylineStatus.Completed; story.CompletedDate = save.CurrentDate; }
                wrestler.Roster.ActiveTagTeamId = null; wrestler.Roster.ActiveStableId = null;
                wrestler.Roster.ActivityState = activity;
            };
            return output;
        }

        private static string BlockingReason(GameSave save, string personId, ContractType type, Departure departure)
        {
            if (type == ContractType.Manager ? save.Player?.ManagerId == personId : save.Player?.WrestlerId == personId)
                return "사용자 캐릭터의 계약은 해지할 수 없습니다.";
            if (save.HasRunningShow)
                return "쇼 진행과 결과 반영을 마친 뒤 계약을 변경할 수 있습니다.";
            foreach (var show in save.Shows.Where(x => x != null && x.Status != ShowStatus.Completed &&
                !save.Schedules.Any(s => s.Id == x.ScheduleId && s.Status == ScheduleStatus.Cancelled)))
            foreach (var entry in save.ShowEvents.Where(x => x?.ShowId == show.Id && show.TimelineEventIds.Contains(x.Id)))
            {
                if (entry.EventType == ShowEventType.Match)
                {
                    var match = save.MatchPlans.SingleOrDefault(x => x?.Id == entry.DetailId);
                    if (match != null && ((match.ParticipantIds ?? new()).Contains(personId) ||
                        (match.Sides ?? new()).Any(x => (x?.MemberIds ?? new()).Contains(personId) || departure.Groups.Contains(x?.RepresentedTagTeamId ?? "")) ||
                        departure.Stories.Contains(match.StorylineId ?? "") || departure.Titles.Contains(match.ChampionshipId ?? "")))
                        return "예정된 쇼의 관련 경기 편성을 먼저 수정해야 합니다.";
                }
                else
                {
                    var promo = save.PromoPlans.SingleOrDefault(x => x?.Id == entry.DetailId);
                    if (promo != null && ((promo.ParticipantIds ?? new()).Contains(personId) || (promo.TargetIds ?? new()).Contains(personId) ||
                        promo.IncidentActorId == personId || promo.IncidentTargetId == personId || promo.GroupLeaderId == personId ||
                        departure.Groups.Contains(promo.GroupId ?? "") || departure.Stories.Contains(promo.StorylineId ?? "")))
                        return "예정된 쇼의 관련 프로모 편성을 먼저 수정해야 합니다.";
                }
            }
            return null;
        }

        private static int Seed(GameSave save, string personId, ContractType type)
        {
            unchecked
            {
                var seed = save.WorldSeed ^ 0x5445524D;
                foreach (var letter in personId) seed = seed * 31 + letter;
                seed = seed * 31 + (int)type;
                seed = seed * 31 + save.CurrentDate.Year;
                seed = seed * 31 + save.CurrentDate.Month;
                return seed * 31 + save.CurrentDate.Day;
            }
        }
        private static string Date(GameDate date) => $"{date.Year}.{date.Month:D2}.{date.Day:D2}";
    }
}
