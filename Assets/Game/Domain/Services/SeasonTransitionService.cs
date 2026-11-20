using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class SeasonPpvSetup
    {
        public int Month;
        public string Title;
        public int DurationMinutes = 120;
        public ScheduledShowType Type;
    }

    public sealed class SeasonSetupRequest
    {
        public string SourcePolicyId;
        public RegularShowFrequency RegularFrequency;
        public PpvFrequency PpvFrequency;
        public string VenueId;
        public long ProductionCost;
        public long VenueUnlockCost, VenueRequiredPrestige;
        public string RegularShowName;
        public int RegularDurationMinutes;
        public List<SeasonPpvSetup> Ppvs = new();
        public bool CancelMissedShows;
    }

    public sealed class SeasonTransitionService
    {
        private readonly Func<string> createId;
        public SeasonTransitionService(Func<string> createId = null) => this.createId = createId ?? EntityId.CreateRuntimeId;
        public static bool RequiresSetup(GameSave save) => !string.IsNullOrEmpty(save?.SeasonPolicy?.Id) &&
            save.CurrentDate.CompareTo(save.SeasonPolicy.EndDate) >= 0;
        public static bool CanClose(GameSave save) => RequiresSetup(save) && IncompleteShows(save).Count == 0;
        public static bool CanConfigure(GameSave save) => RequiresSetup(save) && IncompleteShows(save).All(x => x.Date.CompareTo(save.CurrentDate) < 0);
        public static long ProjectedClosingPrestige(GameSave save) => new WrestlerCareerService().PrepareDue(save, IncompleteShows(save).Select(x => x.Id)).ProjectedPrestige;

        public static List<ShowState> IncompleteShows(GameSave save) => save.Shows.Where(x => x != null &&
            InPeriod(x.Date, save.SeasonPolicy) && x.Status != ShowStatus.Completed && !Cancelled(save, x)).ToList();

        public static IReadOnlyList<int> PpvMonths(PpvFrequency frequency)
        {
            var interval = frequency switch { PpvFrequency.EveryFourMonths => 4, PpvFrequency.Quarterly => 3,
                PpvFrequency.Bimonthly => 2, PpvFrequency.Monthly => 1, _ => throw new InvalidOperationException("PPV 빈도가 올바르지 않습니다.") };
            return Enumerable.Range(1, 12 / interval).Select(x => (4 + x * interval) % 12 + 1).ToList();
        }

        public static SeasonSetupRequest DefaultSettings(GameSave save)
        {
            var policy = save.SeasonPolicy ?? throw new InvalidOperationException("시즌 정책이 없습니다.");
            var venue = save.VenueContracts.Single(x => x.Id == policy.RegularVenueContractId);
            return new SeasonSetupRequest { SourcePolicyId = policy.Id, RegularFrequency = policy.RegularShowFrequency,
                PpvFrequency = policy.PpvFrequency, VenueId = venue.VenueId, ProductionCost = venue.ProductionCost,
                RegularShowName = policy.RegularShowName ?? "정규 쇼", RegularDurationMinutes = policy.RegularShowDurationMinutes,
                Ppvs = Enumerable.Range(1, 12).Select(month =>
                {
                    var title = (policy.PpvShowTitles ?? new()).FirstOrDefault(x => x.Month == month);
                    var schedule = save.Schedules.FirstOrDefault(x => x != null && InPeriod(x.Date, policy) &&
                        x.Date.Month == month && x.ShowType != ScheduledShowType.Regular);
                    return new SeasonPpvSetup { Month = month, Title = title?.Title ?? $"{month}월 PPV",
                        DurationMinutes = title?.DurationMinutes ?? 120, Type = schedule?.ShowType ?? ScheduledShowType.PpvRegular };
                }).ToList() };
        }

        public SeasonResultState GetOrCreateResult(GameSave save)
        {
            EnsureCanConfigure(save);
            ValidateState(save);
            var existing = save.SeasonResults.FirstOrDefault(x => x.Policy.Id == save.SeasonPolicy.Id);
            var result = BuildResult(save, save.SeasonPolicy, existing?.Id ?? createId());
            if (existing == null) ValidateNewIds(save, new[] { result.Id });
            ReplaceResult(save, result);
            return result;
        }

        public SeasonPolicyState ConfirmNextSeason(GameSave save, SeasonSetupRequest request)
        {
            if (request == null) throw new InvalidOperationException("다음 시즌 설정이 필요합니다.");
            ValidateState(save);
            var recorded = save.SeasonResults.FirstOrDefault(x => x.Policy.Id == request.SourcePolicyId);
            if (!string.IsNullOrEmpty(recorded?.NextSeasonPolicyId))
                return save.SeasonPolicy.Id == recorded.NextSeasonPolicyId ? save.SeasonPolicy :
                    save.SeasonResults.Single(x => x.Policy.Id == recorded.NextSeasonPolicyId).Policy;
            EnsureCanConfigure(save);
            if (request.SourcePolicyId != save.SeasonPolicy.Id) throw new InvalidOperationException("다른 시즌의 설정입니다. 창을 다시 여세요.");
            if (!Enum.IsDefined(typeof(RegularShowFrequency), request.RegularFrequency) || !Enum.IsDefined(typeof(PpvFrequency), request.PpvFrequency) ||
                string.IsNullOrWhiteSpace(request.VenueId) || !EntityId.IsValidStaticId(request.VenueId) || request.ProductionCost < 0 ||
                string.IsNullOrWhiteSpace(request.RegularShowName) || !ValidDuration(request.RegularDurationMinutes) || request.Ppvs == null ||
                request.Ppvs.Any(x => x == null || x.Month < 1 || x.Month > 12 || string.IsNullOrWhiteSpace(x.Title) || !ValidDuration(x.DurationMinutes) ||
                    !Enum.IsDefined(typeof(ScheduledShowType), x.Type) || x.Type == ScheduledShowType.Regular) ||
                request.Ppvs.Select(x => x.Month).Distinct().Count() != request.Ppvs.Count)
                throw new InvalidOperationException("쇼 제목·시간·빈도·경기장 설정을 확인하세요. 시간은 60분 단위입니다.");
            var incomplete = IncompleteShows(save);
            if (incomplete.Count > 0 && (!request.CancelMissedShows || incomplete.Any(x => x.Date.CompareTo(save.CurrentDate) >= 0)))
                throw new InvalidOperationException("지난 미완료 쇼를 취소하는 데 동의해야 다음 시즌을 준비할 수 있습니다.");
            if (save.Shows.Any(x => save.IsRunningShow(x) && !incomplete.Contains(x)))
                throw new InvalidOperationException("진행 중인 쇼를 먼저 마쳐야 합니다.");
            var career = new WrestlerCareerService(createId).PrepareDue(save, incomplete.Select(x => x.Id));
            var unlocks = PromotionProgressionRules.Unlocks(save, career.ProjectedPrestige);
            var nextTier = Enumerable.Range(1, 8).Where(x => unlocks.Contains(PromotionProgressionRules.TierId(x))).DefaultIfEmpty(0).Max();
            var usedMonths = PpvMonths(request.PpvFrequency);
            PromotionProgressionRules.ValidateOperations(nextTier, request.RegularFrequency, request.PpvFrequency,
                request.Ppvs.Where(x => usedMonths.Contains(x.Month)).Select(x => x.Type));
            if (request.VenueUnlockCost < 0 || request.VenueRequiredPrestige < 0)
                throw new InvalidOperationException("경기장 등급 해금 조건이 올바르지 않습니다.");
            var unlockVenue = request.VenueUnlockCost > 0 && !save.Promotion.UnlockedIds.Contains(request.VenueId) &&
                !save.VenueContracts.Any(x => x.VenueId == request.VenueId);
            if (unlockVenue && career.ProjectedPrestige < request.VenueRequiredPrestige)
                throw new InvalidOperationException("경기장 등급 해금에 필요한 단체 명성이 부족합니다.");
            if (unlockVenue && save.Promotion.CalculateCurrentCash(save.Transactions) < request.VenueUnlockCost)
                throw new InvalidOperationException("경기장 등급 해금 비용를 지급할 자금이 부족합니다.");
            TransactionRecord venueUnlock = unlockVenue ? new() { Id = createId(), PromotionId = save.Promotion.Id,
                Date = save.CurrentDate, Type = TransactionType.VenueUnlock, Amount = -request.VenueUnlockCost, ReasonId = request.VenueId } : null;
            if (unlockVenue) unlocks.Add(request.VenueId);

            var year = Math.Max(save.SeasonPolicy.EndDate.Year, save.CurrentDate.Month >= 6 ? save.CurrentDate.Year : save.CurrentDate.Year - 1);
            if (year >= 9999) throw new InvalidOperationException("다음 시즌을 생성할 수 있는 연도 범위를 벗어났습니다.");
            if (new GameDate(year + 1, 5, 31).CompareTo(save.CurrentDate) <= 0) year++;
            if (year >= 9999) throw new InvalidOperationException("다음 시즌을 생성할 수 있는 연도 범위를 벗어났습니다.");
            var venue = new VenueContractState { Id = createId(), VenueId = request.VenueId, ContractType = VenueContractType.RegularSeason,
                StartDate = new GameDate(year, 6, 1), EndDate = new GameDate(year + 1, 5, 31), ProductionCost = request.ProductionCost,
                Status = VenueContractStatus.Reserved };
            ValidateNewIds(save, new[] { venue.Id });
            var generated = new SeasonScheduleGenerator(createId).Generate(new SeasonScheduleGenerationRequest { SeasonStartYear = year,
                RegularShowFrequency = request.RegularFrequency, PpvFrequency = request.PpvFrequency,
                RegularVenueContractId = venue.Id, InitialStatus = ScheduleStatus.Confirmed });
            var policy = generated.Policy;
            policy.RegularShowName = request.RegularShowName.Trim(); policy.RegularShowDurationMinutes = request.RegularDurationMinutes;
            foreach (var schedule in generated.Schedules.Where(x => x.ShowType != ScheduledShowType.Regular))
            {
                var setup = request.Ppvs.FirstOrDefault(x => x.Month == schedule.Date.Month) ?? new SeasonPpvSetup
                    { Month = schedule.Date.Month, Title = $"{schedule.Date.Month}월 PPV", Type = ScheduledShowType.PpvRegular };
                schedule.ShowType = setup.Type;
                policy.PpvShowTitles.Add(new PpvTitlePolicyState { Month = setup.Month, Title = setup.Title.Trim(), DurationMinutes = setup.DurationMinutes });
            }
            if (generated.Schedules.Count(x => x.ShowType == ScheduledShowType.PpvSignature) > 1 ||
                policy.PpvShowTitles.Select(x => x.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() != policy.PpvShowTitles.Count)
                throw new InvalidOperationException("대표 PPV는 최대 하나이며, 같은 시즌의 PPV 제목은 서로 달라야 합니다.");
            var schedules = generated.Schedules.Where(x => x.Date.CompareTo(save.CurrentDate) >= 0).ToList();
            policy.SignaturePpvScheduleId = schedules.SingleOrDefault(x => x.ShowType == ScheduledShowType.PpvSignature)?.Id;
            var shows = new List<ShowState>(); var sequence = 0;
            foreach (var schedule in schedules)
            {
                var regular = schedule.ShowType == ScheduledShowType.Regular;
                if (regular) sequence++;
                var ppv = regular ? null : policy.PpvShowTitles.Single(x => x.Month == schedule.Date.Month);
                var duration = regular ? policy.RegularShowDurationMinutes : ppv.DurationMinutes;
                shows.Add(new ShowState { Id = createId(), ScheduleId = schedule.Id, Date = schedule.Date, ShowType = schedule.ShowType,
                    Name = ShowNamingRules.Compose(save.Promotion.GetDisplayAbbreviation(), regular ? policy.RegularShowName : ppv.Title,
                        schedule.ShowType, sequence, schedule.Date.Year), VenueContractId = venue.Id, DurationLimit = duration,
                    EstimatedCost = venue.CalculateProductionCost(duration), Status = ShowStatus.Draft });
            }
            var reportSource = save;
            if (venueUnlock != null)
            {
                reportSource = save.CopyForCareerSimulation(); reportSource.Transactions = new(save.Transactions) { venueUnlock };
            }
            var result = BuildResult(reportSource, save.SeasonPolicy, recorded?.Id ?? createId(), incomplete.Select(x => x.Id).ToHashSet());
            result.NextSeasonPolicyId = policy.Id;
            result.NewUnlockIds = unlocks.Except(save.Promotion.UnlockedIds).ToList();
            var ids = new List<string> { venue.Id, policy.Id };
            if (recorded == null) ids.Add(result.Id);
            if (venueUnlock != null) ids.Add(venueUnlock.Id);
            ids.AddRange(generated.Schedules.Select(x => x.Id)); ids.AddRange(shows.Select(x => x.Id));
            var growth = new WrestlerGrowthService(createId).PrepareDue(save, incomplete.Select(x => x.Id), career.MomentumForGrowth);
            ids.AddRange(career.NewRecordIds);
            ids.AddRange(growth.NewRecordIds);
            ValidateNewIds(save, ids);

            foreach (var show in incomplete) save.Schedules.Single(x => x.Id == show.ScheduleId).Status = ScheduleStatus.Cancelled;
            foreach (var message in save.InboxMessages.Where(x => x != null && x.Priority == InboxPriority.Required &&
                (x.SourceEventId == $"season-end:{save.SeasonPolicy.Id}" || incomplete.Any(s => x.TargetId == s.Id || x.SourceEventId == $"show-card-required:{s.ScheduleId}"))))
                message.Status = InboxMessageStatus.Resolved;
            if (venueUnlock != null) save.Transactions.Add(venueUnlock);
            ReplaceResult(save, result); save.VenueContracts.Add(venue); save.Schedules.AddRange(schedules); save.Shows.AddRange(shows);
            save.SeasonPolicy = policy; save.Promotion.SeasonPolicyId = policy.Id;
            career.Apply(); growth.Apply();
            new SponsorMissionService(createId).ProcessDay(save);
            save.Promotion.UnlockedIds = unlocks;
            result.Prestige = save.Promotion.PromotionPrestige;
            RefreshClosingResults(save);
            return policy;
        }

        public static void RefreshClosingResults(GameSave save)
        {
            foreach (var result in save.SeasonResults.Where(x => !string.IsNullOrEmpty(x.NextSeasonPolicyId) && x.Policy.EndDate.Equals(save.CurrentDate)).ToList())
            {
                var refreshed = BuildResult(save, result.Policy, result.Id); refreshed.NextSeasonPolicyId = result.NextSeasonPolicyId;
                refreshed.NewUnlockIds = new(result.NewUnlockIds ?? new());
                ReplaceResult(save, refreshed);
            }
        }

        public static void ValidateState(GameSave save)
        {
            var ids = new HashSet<string>(); var policyIds = new HashSet<string>();
            foreach (var result in save.SeasonResults ?? new())
            {
                if (result == null || !EntityId.IsValidRuntimeId(result.Id) || !ids.Add(result.Id) || result.Policy == null ||
                    !EntityId.IsValidRuntimeId(result.Policy.Id) || !policyIds.Add(result.Policy.Id) || result.Income < 0 || result.Expenses < 0 ||
                    result.OutstandingSalary < 0 || result.Followers < 0 || result.Prestige < 0 || result.CompletedShows < 0 || result.CancelledShows < 0 ||
                    result.CompletedPpvs < 0 || result.CompletedPpvs > result.CompletedShows || result.Policy.StartDate.Month != 6 || result.Policy.StartDate.Day != 1 ||
                    result.Policy.EndDate.Month != 5 || result.Policy.EndDate.Day != 31 || result.Policy.EndDate.Year != result.Policy.StartDate.Year + 1 ||
                    result.ObservedDate.CompareTo(result.Policy.EndDate) < 0)
                    throw new InvalidOperationException("시즌 결산 기록이 올바르지 않습니다.");
                try
                {
                    _ = new DateTime(result.ObservedDate.Year, result.ObservedDate.Month, result.ObservedDate.Day);
                    _ = new DateTime(result.Policy.StartDate.Year, result.Policy.StartDate.Month, result.Policy.StartDate.Day);
                    _ = new DateTime(result.Policy.EndDate.Year, result.Policy.EndDate.Month, result.Policy.EndDate.Day);
                }
                catch (ArgumentOutOfRangeException) { throw new InvalidOperationException("시즌 결산 날짜가 올바르지 않습니다."); }
                if (string.IsNullOrEmpty(result.NextSeasonPolicyId))
                {
                    if (result.Policy.Id != save.SeasonPolicy?.Id) throw new InvalidOperationException("미확정 결산이 현재 시즌과 다릅니다.");
                }
                else
                {
                    var next = save.SeasonPolicy?.Id == result.NextSeasonPolicyId ? save.SeasonPolicy :
                        (save.SeasonResults ?? new()).FirstOrDefault(x => x?.Policy?.Id == result.NextSeasonPolicyId)?.Policy;
                    if (next == null || next.StartDate.CompareTo(result.Policy.EndDate) <= 0)
                        throw new InvalidOperationException("다음 시즌 연결이 올바르지 않습니다.");
                }
            }
        }

        private static SeasonResultState BuildResult(GameSave save, SeasonPolicyState policy, string id, HashSet<string> cancelled = null)
        {
            var transactions = save.Transactions.Where(x => x != null && !x.IsVoided && x.PromotionId == save.Promotion.Id).ToList();
            var period = transactions.Where(x => InPeriod(x.Date, policy)).ToList();
            var shows = save.Shows.Where(x => x != null && InPeriod(x.Date, policy)).ToList();
            var complete = shows.Where(x => x.Status == ShowStatus.Completed && !Cancelled(save, x)).ToList();
            var debt = (save.SalaryPayments ?? new()).Where(x => x.DueDate.CompareTo(policy.EndDate) <= 0).Sum(payment =>
                Math.Max(0, checked(payment.Amount + transactions.Where(x => x.ReasonId == payment.Id && x.Date.CompareTo(policy.EndDate) <= 0 &&
                    x.Type is TransactionType.Salary or TransactionType.LatePayment or TransactionType.MatchAppearanceFee or TransactionType.PromoAppearanceFee).Sum(x => x.Amount))));
            return new SeasonResultState { Id = id, Policy = policy.Copy(), ObservedDate = save.CurrentDate,
                CompletedShows = complete.Count, CompletedPpvs = complete.Count(x => x.ShowType != ScheduledShowType.Regular),
                CancelledShows = shows.Count(x => Cancelled(save, x) || cancelled?.Contains(x.Id) == true),
                Income = period.Where(x => x.Amount > 0).Sum(x => x.Amount), Expenses = checked(-period.Where(x => x.Amount < 0).Sum(x => x.Amount)),
                ClosingCash = checked(save.Promotion.InitialCash + transactions.Where(x => x.Date.CompareTo(policy.EndDate) <= 0).Sum(x => x.Amount)),
                OutstandingSalary = debt, Followers = save.Promotion.Audience?.TotalFollowers ?? 0, Prestige = save.Promotion.PromotionPrestige };
        }

        private static void EnsureCanConfigure(GameSave save)
        {
            if (!RequiresSetup(save)) throw new InvalidOperationException("아직 시즌 종료일이 아닙니다.");
            if (!CanConfigure(save))
                throw new InvalidOperationException("시즌의 마지막 쇼를 먼저 마쳐야 합니다.");
        }
        private static bool InPeriod(GameDate date, SeasonPolicyState policy) => date.CompareTo(policy.StartDate) >= 0 && date.CompareTo(policy.EndDate) <= 0;
        private static bool Cancelled(GameSave save, ShowState show) => save.Schedules.Any(x => x?.Id == show.ScheduleId && x.Status == ScheduleStatus.Cancelled);
        private static bool ValidDuration(int duration) => duration > 0 && duration % 60 == 0;
        private static void ReplaceResult(GameSave save, SeasonResultState result)
        {
            var index = save.SeasonResults.FindIndex(x => x.Policy.Id == result.Policy.Id);
            if (index < 0) save.SeasonResults.Add(result); else save.SeasonResults[index] = result;
        }
        private static void ValidateNewIds(GameSave save, IEnumerable<string> generated)
        {
            var ids = new HashSet<string>(save.Shows.Select(x => x.Id).Concat(save.Schedules.Select(x => x.Id)).Concat(save.VenueContracts.Select(x => x.Id))
                .Concat(save.Contracts.Select(x => x.Id)).Concat(save.Transactions.Select(x => x.Id)).Concat(save.InboxMessages.Select(x => x.Id))
                .Concat(save.Wrestlers.Select(x => x.Id)).Concat(save.Managers.Select(x => x.Id))
                .Concat(save.TagTeams.Select(x => x.Id)).Concat(save.Stables.Select(x => x.Id)).Concat(save.Championships.Select(x => x.Id))
                .Concat(save.ContractNegotiations.Select(x => x.Id)).Concat(save.ContractTerminations.Select(x => x.Id))
                .Concat(save.SeasonResults.Select(x => x.Id)).Concat(save.SeasonResults.Select(x => x.Policy.Id))
                .Concat(save.ExperienceRecords.Select(x => x.Id)).Concat(save.GrowthQuarterResults.Select(x => x.Id))
                .Concat(save.CareerEvents.Select(x => x.Id))
                .Concat(new[] { save.Promotion.Id, save.SeasonPolicy.Id }), StringComparer.Ordinal);
            foreach (var id in generated)
                if (!EntityId.IsValidRuntimeId(id) || !ids.Add(id)) throw new InvalidOperationException("시즌 기록 ID가 중복되거나 올바르지 않습니다.");
        }
    }
}
