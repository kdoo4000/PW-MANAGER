using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public enum TimeFlowState { Completed, Blocked }

    public sealed class TimeFlowResult
    {
        public TimeFlowState State;
        public GameDate ReachedDate;
        public int ProcessedDays;
        public string BlockingReason;
        public List<string> ProcessedIds = new();
    }

    public sealed class InboxService
    {
        private readonly Func<string> createId;

        public InboxService(Func<string> createId = null) => this.createId = createId ?? EntityId.CreateRuntimeId;

        public InboxMessageState Publish(GameSave save, string sourceEventId, InboxMessageType type, InboxPriority priority,
            string sender, string subject, string body, InboxTargetType targetType = InboxTargetType.None,
            string targetId = null, GameDate? dueDate = null)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (string.IsNullOrWhiteSpace(sourceEventId)) throw new ArgumentException("Source event id is required.", nameof(sourceEventId));
            save.InboxMessages ??= new List<InboxMessageState>();
            var existing = save.InboxMessages.FirstOrDefault(x => x?.SourceEventId == sourceEventId);
            if (existing != null) return existing;
            var message = new InboxMessageState
            {
                Id = createId(), SourceEventId = sourceEventId, CreatedDate = save.CurrentDate,
                Type = type, Priority = priority, Status = InboxMessageStatus.Unread,
                Sender = sender, Subject = subject, Body = body, TargetType = targetType, TargetId = targetId,
                HasDueDate = dueDate.HasValue, DueDate = dueDate ?? default
            };
            save.InboxMessages.Add(message);
            return message;
        }

        public void EnsureForCurrentDate(GameSave save)
        {
            if (save?.Promotion == null) return;
            save.InboxMessages ??= new List<InboxMessageState>();
            ResolveCompletedShowDecisions(save);
            var dateKey = DateKey(save.CurrentDate);
            var active = (save.Wrestlers ?? new List<WrestlerState>()).Count(x => x?.Roster?.PromotionId == save.Promotion.Id && x.Roster.ActivityState == RosterActivityState.Active);
            var cash = save.Promotion.CalculateCurrentCash(save.Transactions ?? new List<TransactionRecord>());
            Publish(save, $"daily-brief:{dateKey}", InboxMessageType.Information, InboxPriority.Normal, "운영실",
                $"{save.CurrentDate.Month}월 {save.CurrentDate.Day}일 단체 운영 브리핑",
                $"활동 선수 {active}명 · 현재 자금 ${cash:N0} · 단체 명성 {save.Promotion.PromotionPrestige:N0}");

            foreach (var schedule in (save.Schedules ?? new List<ScheduleState>()).Where(x => x != null && x.Status == ScheduleStatus.Confirmed && x.BookingDeadline.CompareTo(save.CurrentDate) <= 0))
            {
                var show = (save.Shows ?? new List<ShowState>()).SingleOrDefault(x => x?.ScheduleId == schedule.Id);
                if (show != null && show.Status is ShowStatus.Confirmed or ShowStatus.InProgress or ShowStatus.ResultReview or ShowStatus.ResultsReviewed or ShowStatus.Completed) continue;
                Publish(save, $"show-card-required:{schedule.Id}", InboxMessageType.Decision, InboxPriority.Required, "쇼 운영팀",
                    schedule.Date.Equals(save.CurrentDate) ? "오늘 쇼의 편성을 확정해야 합니다." : "오늘 마감되는 쇼의 편성을 확정해야 합니다.",
                    $"{schedule.Date.Month}월 {schedule.Date.Day}일 쇼의 편성 마감일이 도래했습니다.",
                    InboxTargetType.Show, show?.Id, save.CurrentDate);
            }

            foreach (var contract in save.OwnContracts.Where(x => x != null && x.IsInForce))
            {
                var days = save.CurrentDate.DaysUntil(contract.EndDate);
                if (days < 0 || days > 28) continue;
                var wrestler = (save.Wrestlers ?? new List<WrestlerState>()).FirstOrDefault(x => x?.Id == contract.PersonId);
                var name = wrestler?.Identity?.RingName ?? wrestler?.Identity?.LegalName ?? "선수";
                Publish(save, $"contract-expiry-warning:{contract.Id}:28", InboxMessageType.Navigation, InboxPriority.Important, "계약 담당",
                    $"{name}의 계약이 {days}일 후 만료됩니다", "계약 조건과 선수의 향후 활용 계획을 확인하세요.", InboxTargetType.Wrestler, contract.PersonId);
            }
            if (SeasonTransitionService.CanConfigure(save))
                Publish(save, $"season-end:{save.SeasonPolicy.Id}", InboxMessageType.Decision, InboxPriority.Required, "운영실",
                    "시즌 결산과 다음 시즌 설정을 확인하세요", "시즌 결과와 계약 상태를 확인한 뒤 다음 시즌 운영 설정을 확정하세요.",
                    InboxTargetType.Season, save.SeasonPolicy.Id, save.CurrentDate);
        }

        public InboxMessageState GetBlockingMessage(GameSave save) => (save?.InboxMessages ?? new List<InboxMessageState>())
            .Where(x => x != null && x.Priority == InboxPriority.Required && (x.Status == InboxMessageStatus.Unread || x.Status == InboxMessageStatus.Read) && (!x.HasDueDate || x.DueDate.CompareTo(save.CurrentDate) <= 0))
            .OrderBy(x => x.CreatedDate.Year).ThenBy(x => x.CreatedDate.Month).ThenBy(x => x.CreatedDate.Day).FirstOrDefault();

        public void MarkRead(InboxMessageState message) { if (message?.Status == InboxMessageStatus.Unread) message.Status = InboxMessageStatus.Read; }

        public void ResolveBySource(GameSave save, string sourceEventId)
        {
            var message = (save?.InboxMessages ?? new List<InboxMessageState>()).FirstOrDefault(x => x?.SourceEventId == sourceEventId);
            if (message != null) message.Status = InboxMessageStatus.Resolved;
        }

        private static void ResolveCompletedShowDecisions(GameSave save)
        {
            foreach (var message in save.InboxMessages.Where(x => x != null && x.SourceEventId?.StartsWith("show-card-required:", StringComparison.Ordinal) == true && x.Status != InboxMessageStatus.Resolved))
            {
                var scheduleId = message.SourceEventId.Substring("show-card-required:".Length);
                var show = (save.Shows ?? new List<ShowState>()).FirstOrDefault(x => x?.ScheduleId == scheduleId);
                if (show != null && show.Status is not (ShowStatus.Draft or ShowStatus.Preparing or ShowStatus.Review) ||
                    save.Schedules.Any(x => x.Id == scheduleId && x.Status == ScheduleStatus.Cancelled)) message.Status = InboxMessageStatus.Resolved;
            }
        }

        private static string DateKey(GameDate date) => $"{date.Year:D4}{date.Month:D2}{date.Day:D2}";
    }

    public sealed class TimeFlowService
    {
        public const int MaxAdvanceDays = 30;
        private readonly Func<string> createId;
        private readonly Action<GameSave> processScouting;

        public TimeFlowService(Func<string> createId = null, Action<GameSave> processScouting = null)
        {
            this.createId = createId ?? EntityId.CreateRuntimeId;
            this.processScouting = processScouting;
        }

        public TimeFlowResult AdvanceTo(GameSave save, GameDate targetDate)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (save.Promotion == null) throw new ArgumentException("Promotion is required.", nameof(save));
            WrestlerBehaviorService.ValidateState(save);
            var days = save.CurrentDate.DaysUntil(targetDate);
            if (days < 1) throw new ArgumentException("Target date must be after the current date.", nameof(targetDate));
            if (days > MaxAdvanceDays) throw new ArgumentException($"Time can advance at most {MaxAdvanceDays} days.", nameof(targetDate));

            var inbox = new InboxService(createId);
            SeasonTransitionService.RefreshClosingResults(save);
            FanAudienceService.Initialize(save);
            inbox.EnsureForCurrentDate(save);
            var result = new TimeFlowResult { State = TimeFlowState.Completed, ReachedDate = save.CurrentDate };
            while (save.CurrentDate.CompareTo(targetDate) < 0)
            {
                var blockingReason = inbox.GetBlockingMessage(save)?.Subject ?? GetBlockingReasonForToday(save);
                if (blockingReason != null)
                {
                    result.State = TimeFlowState.Blocked;
                    result.BlockingReason = blockingReason;
                    return result;
                }
                var nextDate = save.CurrentDate.AddDays(1);
                var career = new WrestlerCareerService(createId).PrepareDue(save, rookieDate: nextDate);
                var growth = new WrestlerGrowthService(createId).PrepareDue(save, projectedMomentum: career.MomentumForGrowth);
                var behavior = new WrestlerBehaviorService(createId).PrepareDay(save, nextDate);
                WrestlerGrowthService.ValidateNewIds(save, career.NewRecordIds.Concat(growth.NewRecordIds).Concat(behavior.NewRecordIds));
                MerchandiseService.Initialize(save);
                career.Apply(); growth.Apply();
                new SponsorMissionService(createId).ProcessDay(save);
                save.CurrentDate = nextDate;
                ProcessContracts(save, nextDate, result.ProcessedIds);
                new MerchandiseService(createId).ProcessDay(save);
                new SalaryPaymentService(createId).ProcessDay(save, nextDate, result.ProcessedIds);
                foreach (var wrestler in save.Wrestlers.Where(x => x?.Condition != null)) MatchHealth.Recover(wrestler, nextDate);
                foreach (var wrestler in save.Wrestlers.Where(x => x?.Condition != null && x.Condition.InjuryStatus == InjuryStatus.None))
                    wrestler.Condition.Condition = Math.Min(100f, wrestler.Condition.Condition + 3f);
                FanAudienceService.AdvanceWeek(save, nextDate);
                new TrainingService(createId).ProcessDay(save);
                processScouting?.Invoke(save);
                behavior.Apply();
                new SponsorMissionService(createId).ProcessDay(save);
                inbox.EnsureForCurrentDate(save);
                result.ProcessedDays++;
                result.ReachedDate = nextDate;
            }
            return result;
        }

        public static string GetBlockingReasonForToday(GameSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            foreach (var schedule in (save.Schedules ?? new List<ScheduleState>())
                .Where(x => x != null && x.Status == ScheduleStatus.Confirmed && x.BookingDeadline.CompareTo(save.CurrentDate) <= 0)
                .OrderBy(x => x.Date.Year).ThenBy(x => x.Date.Month).ThenBy(x => x.Date.Day))
            {
                var show = (save.Shows ?? new List<ShowState>()).SingleOrDefault(x => x?.ScheduleId == schedule.Id);
                if (show == null || show.Status is ShowStatus.Draft or ShowStatus.Preparing or ShowStatus.Review)
                    return schedule.Date.Equals(save.CurrentDate)
                        ? "오늘 쇼의 편성을 확정해야 합니다."
                        : "오늘 마감되는 쇼의 편성을 확정해야 합니다.";
            }
            if (save.Shows.Any(x => x != null && x.Date.Equals(save.CurrentDate) && x.Status != ShowStatus.Completed && !save.IsCancelledShow(x)))
                return "오늘 쇼의 결과를 먼저 반영해야 합니다.";
            return SeasonTransitionService.RequiresSetup(save) ? "시즌 결산과 다음 시즌 설정을 먼저 완료해야 합니다." : null;
        }

        private void ProcessContracts(GameSave save, GameDate date, List<string> processedThisRun)
        {
            // shortcut: external contract lifecycles wait for external promotion simulation.
            var expired = new List<ContractState>();
            foreach (var contract in save.OwnContracts.Where(x => x != null))
            {
                if (contract.Status == ContractStatus.Scheduled && date.CompareTo(contract.StartDate) >= 0)
                {
                    contract.Status = ContractStatus.Active;
                    if (contract.Type == ContractType.Wrestler)
                    {
                        var wrestler = save.Wrestlers.SingleOrDefault(x => x.Id == contract.PersonId);
                        if (wrestler != null)
                        {
                            wrestler.PromotionId = save.Promotion.Id;
                            if (wrestler.Roster.ActivityState != RosterActivityState.Suspended) wrestler.Roster.ActivityState = RosterActivityState.Active;
                            wrestler.Identity.ExpiryDate = default; save.ScoutCandidates.RemoveAll(x => x.WrestlerId == wrestler.Id);
                        }
                    }
                    else
                    {
                        var manager = save.Managers.SingleOrDefault(x => x.Id == contract.PersonId);
                        if (manager != null)
                        {
                            manager.PromotionId = save.Promotion.Id;
                            if (manager.ActivityState != RosterActivityState.Suspended) manager.ActivityState = RosterActivityState.Active;
                        }
                    }
                }
                if (contract.Status == ContractStatus.Active && date.DaysUntil(contract.EndDate) <= 30 && date.CompareTo(contract.EndDate) <= 0)
                    contract.Status = ContractStatus.Expiring;

                if (date.CompareTo(contract.EndDate) > 0 && IsPayable(contract.Status))
                {
                    contract.Status = ContractStatus.Expired;
                    expired.Add(contract);
                    var sourceId = $"contract-expiry:{contract.Id}:{DateKey(date)}";
                    if (!save.ProcessedIds.Contains(sourceId))
                    {
                        save.ProcessedIds.Add(sourceId);
                        processedThisRun.Add(sourceId);
                    }
                }
            }
            foreach (var contract in expired)
                if (!save.OwnContracts.Any(x => x.PersonId == contract.PersonId && x.Type == contract.Type && x.Covers(date)) &&
                    (contract.Type == ContractType.Wrestler ? save.Wrestlers.Any(x => x.Id == contract.PersonId) : save.Managers.Any(x => x.Id == contract.PersonId)))
                    ContractTerminationService.ExpireEmployment(save, contract.PersonId, contract.Type);
        }

        private static bool IsPayable(ContractStatus status) =>
            status is ContractStatus.Active or ContractStatus.Expiring or ContractStatus.Overdue;

        private static string DateKey(GameDate date) => $"{date.Year:D4}{date.Month:D2}{date.Day:D2}";
    }
}
