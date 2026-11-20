using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class ContractNegotiationService
    {
        public const int RejectionCooldownDays = 7;
        private readonly Func<string> createId;
        private readonly Random random;

        public ContractNegotiationService(Func<string> createId = null, Random random = null)
        { this.createId = createId ?? EntityId.CreateRuntimeId; this.random = random; }

        public static bool HasCurrentRecruitmentInformation(GameSave save, WrestlerState wrestler)
        {
            if (!save.IsWrestlerDiscovered(wrestler.Id)) return false;
            var report = save.ScoutCandidates?.SingleOrDefault(x => x?.WrestlerId == wrestler.Id);
            return report != null ? !report.IsExpired(save.CurrentDate) :
                !wrestler.Identity.ExpiryDate.HasValue || wrestler.Identity.ExpiryDate.Value.CompareTo(save.CurrentDate) >= 0;
        }

        public static InitialContractOffer Demand(GameSave save, string personId, ContractType type)
        {
            if (save?.Promotion == null) throw new InvalidOperationException("활성 단체가 없습니다.");
            if (type == ContractType.Wrestler)
            {
                var wrestler = save.Wrestlers.SingleOrDefault(x => x?.Id == personId)
                    ?? throw new InvalidOperationException("선수를 찾을 수 없습니다.");
                if (!string.IsNullOrEmpty(wrestler.PromotionId) && wrestler.PromotionId != save.Promotion.Id)
                    throw new InvalidOperationException("다른 단체 소속 선수입니다.");
                if (!save.IsWrestlerDiscovered(wrestler.Id))
                    throw new InvalidOperationException("선수에 대한 영입 정보가 없습니다. 먼저 스카우팅해야 합니다.");
                if (wrestler.Roster.ActivityState == RosterActivityState.Inactive && !HasCurrentRecruitmentInformation(save, wrestler))
                    throw new InvalidOperationException("스카우트 보고서의 유효 기간이 지났습니다. 다시 탐색해야 합니다.");
                return InitialContractOfferRules.Calculate(wrestler);
            }
            if (type != ContractType.Manager) throw new InvalidOperationException("계약 유형이 올바르지 않습니다.");
            var manager = save.Managers.SingleOrDefault(x => x?.Id == personId)
                ?? throw new InvalidOperationException("전문 매니저를 찾을 수 없습니다.");
            if (!string.IsNullOrEmpty(manager.PromotionId) && manager.PromotionId != save.Promotion.Id)
                throw new InvalidOperationException("다른 단체 소속 매니저입니다.");
            return InitialContractOfferRules.Calculate(manager);
        }

        public static float Acceptance(GameSave save, string personId, ContractType type, long signingBonus,
            long monthlySalary, bool hasRole = false, ExpectedRole role = ExpectedRole.Midcarder,
            long? matchAppearanceFee = null, long? promoAppearanceFee = null)
        {
            if (signingBonus < 0 || monthlySalary <= 0 || !Enum.IsDefined(typeof(ExpectedRole), role))
                throw new InvalidOperationException("계약금은 0 이상, 월급은 0보다 커야 합니다.");
            var demand = Demand(save, personId, type);
            var matchFee = matchAppearanceFee ?? demand.MatchAppearanceFee;
            var promoFee = promoAppearanceFee ?? demand.PromoAppearanceFee;
            if (matchFee < 0 || promoFee < 0 || type == ContractType.Manager && (matchFee != 0 || promoFee != 0))
                throw new InvalidOperationException("출연 수당을 확인하세요.");
            var satisfaction = type == ContractType.Wrestler ? save.Wrestlers.Single(x => x.Id == personId).Condition.Satisfaction
                : save.Managers.Single(x => x.Id == personId).Satisfaction;
            var financial = ((double)monthlySalary / demand.MonthlySalary - 1) * 100 +
                ((double)signingBonus / demand.SigningBonus - 1) * 25;
            if (type == ContractType.Wrestler)
                financial += ((double)matchFee / demand.MatchAppearanceFee - 1) * 40 +
                    ((double)promoFee / demand.PromoAppearanceFee - 1) * 20;
            var wrestler = type == ContractType.Wrestler ? save.Wrestlers.Single(x => x.Id == personId) : null;
            var moneySensitivity = 1 + ((wrestler?.Hidden?.MoneyOrientation ?? 10) - 10) / 20d;
            var chance = 70 + financial * moneySensitivity + (satisfaction - 50) * .5;
            if (wrestler?.PromotionId == save.Promotion.Id)
            {
                var current = save.OwnContracts.SingleOrDefault(c => c.PersonId == personId && c.Type == type && c.IsInForce && c.Covers(save.CurrentDate));
                if (current != null)
                {
                    var trust = save.WrestlerBehaviors.SingleOrDefault(s => s.PromotionId == save.Promotion.Id && s.WrestlerId == personId)?.Trust ?? 50;
                    var relationship = Math.Clamp((trust - 25) / 75f, 0, 1) * Math.Clamp(satisfaction / 50, 0, 1);
                    var start = current.StartDate;
                    foreach (var previous in save.OwnContracts.Where(c => c.PersonId == personId && c.Type == type &&
                        (c.IsInForce || c.Status == ContractStatus.Expired) && c.StartDate.CompareTo(start) < 0).OrderByDescending(c => c.EndDate))
                    {
                        if (previous.EndDate.AddDays(1).CompareTo(start) < 0) break;
                        if (previous.StartDate.CompareTo(start) < 0) start = previous.StartDate;
                    }
                    var tenure = Math.Clamp(start.DaysUntil(save.CurrentDate) / 180f, 0, 1);
                    chance += ((wrestler.Hidden?.Loyalty ?? 10) - 10) * relationship * tenure;
                }
            }
            if (type == ContractType.Wrestler && hasRole)
            {
                var status = save.Wrestlers.Single(x => x.Id == personId).Status.StatusValue;
                var expected = wrestler.PromotionId == save.Promotion.Id ? WrestlerBehaviorService.ExpectedRoleAt(save, wrestler, save.CurrentDate) :
                    status >= 80 ? ExpectedRole.MainEventer : status >= 45 ? ExpectedRole.Midcarder : ExpectedRole.Jobber;
                chance -= Math.Max(0, (int)expected - (int)role) * 10 * (1 + ((wrestler.Hidden?.Ambition ?? 10) - 10) / 20f);
            }
            return (float)Math.Max(0, Math.Min(95, chance));
        }

        public static (GameDate Start, GameDate End) Period(GameSave save, string personId, ContractType type)
        {
            var current = save.OwnContracts.SingleOrDefault(x => x?.PersonId == personId && x.Type == type && x.IsInForce &&
                x.StartDate.CompareTo(save.CurrentDate) <= 0 && x.EndDate.CompareTo(save.CurrentDate) >= 0);
            var start = current?.EndDate.AddDays(1) ?? save.CurrentDate;
            if (current == null && save.SeasonPolicy != null && save.SeasonPolicy.StartDate.CompareTo(start) > 0)
                start = save.SeasonPolicy.StartDate;
            return (start, new GameDate(start.Month <= 5 ? start.Year : start.Year + 1, 5, 31));
        }

        public ContractNegotiationState Submit(GameSave save, string personId, ContractType type, long signingBonus,
            long monthlySalary, bool hasRole = false, ExpectedRole role = ExpectedRole.Midcarder,
            long? matchAppearanceFee = null, long? promoAppearanceFee = null)
        {
            if (save?.HasRunningShow == true)
                throw new InvalidOperationException("쇼 진행과 결과 반영을 마친 뒤 계약을 변경할 수 있습니다.");
            var demand = Demand(save, personId, type);
            var matchFee = matchAppearanceFee ?? demand.MatchAppearanceFee;
            var promoFee = promoAppearanceFee ?? demand.PromoAppearanceFee;
            var chance = Acceptance(save, personId, type, signingBonus, monthlySalary, hasRole, role, matchFee, promoFee);
            var history = (save.ContractNegotiations ?? new()).Where(x => x != null && x.PersonId == personId && x.Type == type).ToList();
            var today = history.LastOrDefault(x => x.Date.Equals(save.CurrentDate));
            if (today?.Accepted == true && today.SigningBonus == signingBonus && today.MonthlySalary == monthlySalary &&
                today.HasExpectedRole == hasRole && today.ExpectedRole == role &&
                today.MatchAppearanceFee == matchFee && today.PromoAppearanceFee == promoFee) return today;
            var rejected = history.LastOrDefault(x => !x.Accepted);
            if (rejected != null && save.CurrentDate.CompareTo(rejected.NextAttemptDate) < 0)
                throw new InvalidOperationException($"{Date(rejected.NextAttemptDate)}부터 재협상할 수 있습니다.");
            if (today?.Accepted == true) throw new InvalidOperationException("오늘 이미 계약을 체결했습니다.");
            if (save.Contracts.Any(x => x?.PersonId == personId && x.Type == type && x.Status == ContractStatus.Scheduled))
                throw new InvalidOperationException("이미 예약된 재계약이 있습니다.");
            if (save.Promotion.CalculateCurrentCash(save.Transactions) < signingBonus)
                throw new InvalidOperationException("계약금을 지급할 자금이 부족합니다.");
            var period = Period(save, personId, type);
            if (save.Contracts.Any(x => x?.PersonId == personId && x.Type == type && x.IsInForce &&
                x.StartDate.CompareTo(period.End) <= 0 && period.Start.CompareTo(x.EndDate) <= 0))
                throw new InvalidOperationException("기존 계약과 기간이 겹칩니다.");
            var roll = Math.Min(99.99999f, (float)((random ?? new Random(Seed(save, personId, type))).NextDouble() * 100));
            var result = new ContractNegotiationState { Id = createId(), PersonId = personId, Type = type, Date = save.CurrentDate,
                SigningBonus = signingBonus, MonthlySalary = monthlySalary,
                MatchAppearanceFee = matchFee, PromoAppearanceFee = promoFee, HasExpectedRole = hasRole, ExpectedRole = role,
                AcceptancePercent = chance, RollPercent = roll, Accepted = roll < chance };
            result.NextAttemptDate = result.Accepted ? save.CurrentDate : save.CurrentDate.AddDays(RejectionCooldownDays);
            ContractState contract = null;
            TransactionRecord transaction = null;
            if (result.Accepted)
            {
                contract = new ContractState { Id = createId(), PersonId = personId, PromotionId = save.Promotion.Id, Type = type, StartDate = period.Start, EndDate = period.End,
                    WasRookieWhenHired = type == ContractType.Wrestler && save.Wrestlers.Single(x => x.Id == personId).Status.IsRookie &&
                        !save.OwnContracts.Any(x => x.PersonId == personId && x.Type == type && x.Covers(save.CurrentDate)),
                    SigningBonus = signingBonus, MonthlySalary = monthlySalary,
                    MatchAppearanceFee = matchFee, PromoAppearanceFee = promoFee,
                    HasExpectedRole = hasRole, ExpectedRole = role,
                    Status = period.Start.CompareTo(save.CurrentDate) > 0 ? ContractStatus.Scheduled : ContractStatus.Active };
                result.ContractId = contract.Id;
                if (signingBonus > 0) transaction = new TransactionRecord { Id = createId(), PromotionId = save.Promotion.Id,
                    Date = save.CurrentDate, Type = TransactionType.SigningFee, Amount = -signingBonus, ReasonId = contract.Id };
            }
            var ids = new HashSet<string>((save.ContractNegotiations ?? new()).Select(x => x.Id)
                .Concat(save.Contracts.Select(x => x.Id)).Concat(save.Transactions.Select(x => x.Id)), StringComparer.Ordinal);
            var requiredIds = new List<string> { result.Id };
            if (contract != null) requiredIds.Add(contract.Id);
            if (transaction != null) requiredIds.Add(transaction.Id);
            foreach (var id in requiredIds)
                if (!EntityId.IsValidRuntimeId(id) || !ids.Add(id)) throw new InvalidOperationException("계약 기록 ID가 중복되거나 올바르지 않습니다.");
            var settlement = contract == null ? null : new SalaryPaymentService(createId).PrepareSettlement(save, save.CurrentDate,
                transaction == null ? Array.Empty<TransactionRecord>() : new[] { transaction }, startingContract: contract);
            if (settlement != null)
            {
                var existing = new HashSet<string>(save.Transactions.Select(x => x.Id).Concat(save.SalaryPayments.Select(x => x.Id))
                    .Concat(save.InboxMessages.Select(x => x.Id)), StringComparer.Ordinal);
                if (transaction != null) existing.Add(transaction.Id);
                foreach (var id in settlement.RecordIds.Where(x => !existing.Contains(x)))
                    if (!EntityId.IsValidRuntimeId(id) || !ids.Add(id)) throw new InvalidOperationException("급여 정산 ID가 중복되거나 올바르지 않습니다.");
            }

            save.ContractNegotiations ??= new();
            save.ContractNegotiations.Add(result);
            if (contract == null) return result;
            save.Contracts.Add(contract);
            settlement.Apply(save);
            if (type == ContractType.Wrestler)
            {
                var wrestler = save.Wrestlers.Single(x => x.Id == personId);
                wrestler.PromotionId = save.Promotion.Id;
                if (contract.Status == ContractStatus.Active) wrestler.Roster.ActivityState = RosterActivityState.Active;
                else if (wrestler.Roster.ActivityState == RosterActivityState.Released) wrestler.Roster.ActivityState = RosterActivityState.Inactive;
                wrestler.Identity.ExpiryDate = default;
                save.ScoutCandidates.RemoveAll(x => x?.WrestlerId == personId);
            }
            else
            {
                var manager = save.Managers.Single(x => x.Id == personId);
                manager.PromotionId = save.Promotion.Id;
                if (contract.Status == ContractStatus.Active) manager.ActivityState = RosterActivityState.Active;
                else if (manager.ActivityState == RosterActivityState.Released) manager.ActivityState = RosterActivityState.Inactive;
            }
            return result;
        }

        public static void ValidateState(GameSave save)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var days = new HashSet<string>(StringComparer.Ordinal);
            foreach (var result in save.ContractNegotiations ?? new())
            {
                if (result == null || !EntityId.IsValidRuntimeId(result.Id) || !ids.Add(result.Id) || string.IsNullOrWhiteSpace(result.PersonId) ||
                    !Enum.IsDefined(typeof(ContractType), result.Type) || !Enum.IsDefined(typeof(ExpectedRole), result.ExpectedRole) ||
                    result.SigningBonus < 0 || result.MonthlySalary <= 0 || result.MatchAppearanceFee < 0 || result.PromoAppearanceFee < 0 || !float.IsFinite(result.AcceptancePercent) ||
                    result.AcceptancePercent < 0 || result.AcceptancePercent > 95 || !float.IsFinite(result.RollPercent) ||
                    result.RollPercent < 0 || result.RollPercent >= 100 || result.Accepted != (result.RollPercent < result.AcceptancePercent))
                    throw new InvalidOperationException("계약 협상 기록이 올바르지 않습니다.");
                try { _ = new DateTime(result.Date.Year, result.Date.Month, result.Date.Day); }
                catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("계약 협상 날짜가 올바르지 않습니다."); }
                if (!days.Add($"{result.Type}:{result.PersonId}:{result.Date.Year}:{result.Date.Month}:{result.Date.Day}") ||
                    !result.NextAttemptDate.Equals(result.Accepted ? result.Date : result.Date.AddDays(RejectionCooldownDays)))
                    throw new InvalidOperationException("계약 협상 날짜 또는 재협상 기한이 중복되거나 올바르지 않습니다.");
                if (result.Accepted && !save.OwnContracts.Any(x => x?.Id == result.ContractId && x.PersonId == result.PersonId && x.Type == result.Type))
                    throw new InvalidOperationException("수락한 협상의 계약을 찾을 수 없습니다.");
            }
        }

        private static int Seed(GameSave save, string personId, ContractType type)
        {
            unchecked
            {
                var value = save.WorldSeed;
                foreach (var letter in personId) value = value * 31 + letter;
                value = value * 31 + (int)type;
                value = value * 31 + save.CurrentDate.Year;
                value = value * 31 + save.CurrentDate.Month;
                return value * 31 + save.CurrentDate.Day;
            }
        }

        private static string Date(GameDate date) => $"{date.Year}년 {date.Month}월 {date.Day}일";
    }
}
