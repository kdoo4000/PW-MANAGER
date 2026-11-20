using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class TrainingService
    {
        private static readonly FieldInfo[] Fields = Enum.GetNames(typeof(WrestlerAttribute)).Select(x => typeof(AttributeProgressState).GetField(x)).ToArray();
        private readonly Func<string> createId;
        private readonly Random random;
        public TrainingService(Func<string> createId = null, Random random = null) { this.createId = createId ?? EntityId.CreateRuntimeId; this.random = random; }
        public static void Initialize(GameSave save)
        {
            if (save.Training?.IsInitialized == true) return;
            save.Training = new TrainingState { IsInitialized = true, WeekStartDate = save.CurrentDate, LastProcessedDate = save.CurrentDate };
        }
        public static (int Recovery, int Safety, int Capacity, long Cost, long Required) Facility(int level) => level switch
        {
            1 => (0, 50, 20, 0, 0), 2 => (25, 65, 30, 25000, 300),
            3 => (50, 75, 50, 100000, 2500), 4 => (75, 90, 80, 400000, 5500),
            5 => (100, 100, 120, 1500000, 20000), _ => throw new InvalidOperationException("연습시설 등급이 올바르지 않습니다.")
        };
        public static (float Efficiency, long Cost, long Required) Trainer(int level) => level switch
        {
            1 => (1f, 0, 0), 2 => (1.05f, 10000, 300), 3 => (1.1f, 50000, 2500),
            4 => (1.15f, 200000, 5500), 5 => (1.2f, 1000000, 20000), _ => throw new InvalidOperationException("트레이너 등급이 올바르지 않습니다.")
        };
        public static bool Eligible(GameSave save, WrestlerState w) => w != null && w.PromotionId == save.Promotion.Id &&
            w.Roster.ActivityState is RosterActivityState.Active or RosterActivityState.Suspended &&
            (save.Player?.WrestlerId == w.Id || save.OwnContracts.Any(x => x.PersonId == w.Id && x.Type == ContractType.Wrestler && x.Covers(save.CurrentDate)));

        public void SetPlan(GameSave save, string wrestlerId, TrainingMode mode, WrestlerAttribute focus)
        {
            if (save?.Training?.IsInitialized != true || save.HasRunningShow || !Enum.IsDefined(typeof(TrainingMode), mode) || !Enum.IsDefined(typeof(WrestlerAttribute), focus))
                throw new InvalidOperationException("훈련 계획을 변경할 수 없습니다.");
            var w = save.Wrestlers.SingleOrDefault(x => x.Id == wrestlerId);
            if (!Eligible(save, w)) throw new InvalidOperationException("계약 중인 로스터 선수만 훈련할 수 있습니다.");
            if (w.Condition.InjuryStatus == InjuryStatus.Severe && mode != TrainingMode.Rest) throw new InvalidOperationException("중상 선수는 휴식해야 합니다.");
            var count = save.Training.Plans.Count(p => p.WrestlerId != wrestlerId && p.Mode != TrainingMode.Rest &&
                save.Wrestlers.Any(a => a.Id == p.WrestlerId && Eligible(save, a) && a.Condition.InjuryStatus != InjuryStatus.Severe));
            if (mode != TrainingMode.Rest && count + 1 > Facility(save.Training.FacilityLevel).Capacity * 1.5)
                throw new InvalidOperationException("연습시설 수용량의 150%를 초과할 수 없습니다.");
            save.Training.Plans.RemoveAll(x => x.WrestlerId == wrestlerId);
            save.Training.Plans.Add(new() { WrestlerId = wrestlerId, Mode = mode, Focus = focus });
        }

        public void Upgrade(GameSave save) => Upgrade(save, false);
        public void UpgradeTrainer(GameSave save) => Upgrade(save, true);

        private void Upgrade(GameSave save, bool trainer)
        {
            if (save?.Training?.IsInitialized != true || save.HasRunningShow) throw new InvalidOperationException("훈련 업그레이드를 진행할 수 없습니다.");
            var level = trainer ? save.Training.TrainerLevel : save.Training.FacilityLevel;
            if (level >= 5) throw new InvalidOperationException("최대 등급입니다.");
            var next = trainer ? (Cost: Trainer(level + 1).Cost, Required: Trainer(level + 1).Required) : (Cost: Facility(level + 1).Cost, Required: Facility(level + 1).Required);
            if (PromotionProgressionRules.ActivePrestige(save) < next.Required) throw new InvalidOperationException("업그레이드에 필요한 시즌 명성 자격이 부족합니다.");
            if (save.Promotion.CalculateCurrentCash(save.Transactions) < next.Cost) throw new InvalidOperationException("업그레이드 비용을 지급할 자금이 부족합니다.");
            var transaction = new TransactionRecord { Id = createId(), PromotionId = save.Promotion.Id, Date = save.CurrentDate,
                Type = trainer ? TransactionType.TrainerUpgrade : TransactionType.TrainingFacilityUpgrade, Amount = -next.Cost,
                ReasonId = trainer ? $"trainer:level:{level + 1}" : $"training:level:{level + 1}" };
            WrestlerGrowthService.ValidateNewIds(save, new[] { transaction.Id });
            save.Transactions.Add(transaction);
            if (trainer) save.Training.TrainerLevel++; else save.Training.FacilityLevel++;
        }

        public void ProcessDay(GameSave save)
        {
            if (save.Training?.IsInitialized != true || save.Training.LastProcessedDate.Equals(save.CurrentDate)) return;
            ValidateState(save);
            if (!save.Training.LastProcessedDate.AddDays(1).Equals(save.CurrentDate)) throw new InvalidOperationException("훈련 날짜는 하루씩 처리해야 합니다.");
            var copy = save.CopyForGrowthSimulation(); copy.Training = save.Training.Copy();
            var state = copy.Training; var facility = Facility(state.FacilityLevel);
            var active = copy.Wrestlers.Where(x => Eligible(copy, x)).ToList();
            var count = state.Plans.Count(p => p.Mode != TrainingMode.Rest && active.Any(w => w.Id == p.WrestlerId && w.Condition.InjuryStatus != InjuryStatus.Severe));
            var load = count == 0 ? 1f : Math.Min(1f, (float)facility.Capacity / count);
            foreach (var w in active)
            {
                var before = w.Condition.Condition;
                w.Condition.Condition = Math.Min(100, before + facility.Recovery / 25);
                var plan = state.Plans.FirstOrDefault(x => x.WrestlerId == w.Id);
                if (plan == null) continue;
                var result = state.Pending.FirstOrDefault(x => x.WrestlerId == w.Id);
                if (result == null) { result = new() { WrestlerId = w.Id }; state.Pending.Add(result); }
                var mode = w.Condition.InjuryStatus == InjuryStatus.Severe ? TrainingMode.Rest : plan.Mode;
                if (mode == TrainingMode.Rest)
                { result.RestDays++; w.Condition.Condition = Math.Min(100, w.Condition.Condition + 4f / 7); }
                else
                {
                    result.TrainingDays++;
                    var effort = WrestlerHiddenRules.TrainingEffort(w);
                    var xp = 4f / 7 * effort * Trainer(state.TrainerLevel).Efficiency * load;
                    result.Experience += xp;
                    for (var i = 0; i < 17; i++)
                    {
                        var amount = mode == TrainingMode.Normal ? xp * .12f : i == (int)plan.Focus ? xp * .75f : 0;
                        Fields[i].SetValue(result.Progress, (float)Fields[i].GetValue(result.Progress) + amount);
                    }
                    var risk = (mode == TrainingMode.Focused ? .01 : .003) / 7 * (1 + Math.Max(0, 50 - w.Condition.Condition) / 50) *
                        (1 - (facility.Safety - 50) * .001) * WrestlerHiddenRules.InjuryRiskMultiplier(w.Hidden?.InjuryProneness ?? 10);
                    if (w.Condition.InjuryStatus != InjuryStatus.None) risk *= StaffTeamEffects.For(save, StaffDepartmentType.Medical).Aggravation;
                    w.Condition.Condition = Math.Max(0, w.Condition.Condition - (mode == TrainingMode.Focused ? 10f : 6f) / 7 * effort);
                    if ((random ?? new Random(Seed(copy.WorldSeed, w.Id, copy.CurrentDate))).NextDouble() < risk)
                    {
                        var recovery = Math.Max(1, (int)Math.Ceiling(7 * StaffTeamEffects.For(save, StaffDepartmentType.Medical).Recovery));
                        MatchHealth.ApplyActualInjury(w, InjuryStatus.Minor, BodyRegion.Body, copy.CurrentDate, recovery); result.Injured = true;
                    }
                }
                result.ConditionChange += w.Condition.Condition - before;
            }
            state.LastProcessedDate = copy.CurrentDate;
            var newIds = new List<string>();
            if (state.WeekStartDate.DaysUntil(copy.CurrentDate) >= 7 || PromotionQuarterService.EndOfQuarter(copy.CurrentDate).Equals(copy.CurrentDate))
            {
                if (state.Pending.Count > 0)
                {
                    var week = new TrainingWeekState { Id = createId(), StartDate = state.WeekStartDate.AddDays(1), EndDate = copy.CurrentDate, Results = state.Pending };
                    state.History.Add(week); newIds.Add(week.Id);
                    var growth = new WrestlerGrowthService(createId).PrepareTraining(copy, week); newIds.AddRange(growth.NewRecordIds); growth.Apply();
                }
                state.Pending = new(); state.WeekStartDate = copy.CurrentDate;
            }
            ValidateState(copy); WrestlerGrowthService.ValidateNewIds(save, newIds);
            foreach (var w in save.Wrestlers)
            {
                var next = copy.Wrestlers.Single(x => x.Id == w.Id); w.Condition = next.Condition; w.Growth = next.Growth;
            }
            save.ExperienceRecords = copy.ExperienceRecords; save.Training = state;
        }

        public static void ValidateState(GameSave save)
        {
            var state = save.Training; if (state == null) return;
            if (!state.IsInitialized)
            {
                if (state.Plans?.Count > 0 || state.Pending?.Count > 0 || state.History?.Count > 0 || state.WeekStartDate.Year != 0 || state.LastProcessedDate.Year != 0)
                    throw new InvalidOperationException("미초기화 훈련 데이터에 활동 기록이 있습니다.");
                return;
            }
            if (state.FacilityLevel < 1 || state.FacilityLevel > 5 || state.TrainerLevel < 1 || state.TrainerLevel > 5 || !ValidDate(state.WeekStartDate) || !ValidDate(state.LastProcessedDate) ||
                state.WeekStartDate.CompareTo(state.LastProcessedDate) > 0 || state.LastProcessedDate.CompareTo(save.CurrentDate) > 0 ||
                state.WeekStartDate.DaysUntil(state.LastProcessedDate) > 6 ||
                state.Plans == null || state.Pending == null || state.History == null || state.Plans.Any(x => x == null) ||
                state.Plans.Select(x => x.WrestlerId).Distinct().Count() != state.Plans.Count)
                throw new InvalidOperationException("훈련 운영 상태가 올바르지 않습니다.");
            foreach (var p in state.Plans)
                if (!save.Wrestlers.Any(w => w.Id == p.WrestlerId) || !Enum.IsDefined(typeof(TrainingMode), p.Mode) || !Enum.IsDefined(typeof(WrestlerAttribute), p.Focus))
                    throw new InvalidOperationException("훈련 계획이 올바르지 않습니다.");
            var ids = new HashSet<string>(); GameDate? previousEnd = null;
            foreach (var week in state.History)
            {
                if (week == null || !EntityId.IsValidRuntimeId(week.Id) || !ids.Add(week.Id) || !ValidDate(week.StartDate) || !ValidDate(week.EndDate) ||
                    week.StartDate.DaysUntil(week.EndDate) is < 0 or > 6 || week.EndDate.CompareTo(state.LastProcessedDate) > 0 ||
                    previousEnd.HasValue && week.StartDate.CompareTo(previousEnd.Value) <= 0)
                    throw new InvalidOperationException("주간 훈련 기록이 올바르지 않습니다.");
                ValidateContributions(save, week.Results, week.StartDate.DaysUntil(week.EndDate) + 1); previousEnd = week.EndDate;
            }
            if (previousEnd.HasValue && state.WeekStartDate.CompareTo(previousEnd.Value) < 0)
                throw new InvalidOperationException("훈련 정산 이후의 집계 시작일이 올바르지 않습니다.");
            ValidateContributions(save, state.Pending, state.WeekStartDate.DaysUntil(state.LastProcessedDate));
        }
        private static void ValidateContributions(GameSave save, List<TrainingContributionState> rows, int days)
        {
            if (rows == null || rows.Any(x => x == null) || rows.Select(x => x.WrestlerId).Distinct().Count() != rows.Count)
                throw new InvalidOperationException("훈련 결과가 올바르지 않습니다.");
            foreach (var r in rows)
                if (!save.Wrestlers.Any(w => w.Id == r.WrestlerId) || r.TrainingDays < 0 || r.RestDays < 0 || r.TrainingDays + r.RestDays > days ||
                    !float.IsFinite(r.Experience) || r.Experience < 0 || !float.IsFinite(r.ConditionChange) || r.Progress == null ||
                    Fields.Any(f => !float.IsFinite((float)f.GetValue(r.Progress)) || (float)f.GetValue(r.Progress) < 0))
                    throw new InvalidOperationException("훈련 결과의 경험·체력·일수가 올바르지 않습니다.");
        }
        private static bool ValidDate(GameDate date) { try { _ = new DateTime(date.Year, date.Month, date.Day); return true; } catch (ArgumentOutOfRangeException) { return false; } }
        private static int Seed(int seed, string id, GameDate date) { unchecked { foreach (var c in id) seed = seed * 31 + c; return seed * 397 ^ date.Year * 10000 ^ date.Month * 100 ^ date.Day; } }
    }
}
