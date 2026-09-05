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
            var active = (save.Wrestlers ?? new List<WrestlerState>()).Count(x => x?.Roster?.ActivityState == RosterActivityState.Active);
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

            foreach (var contract in (save.Contracts ?? new List<ContractState>()).Where(x => x != null && x.Status is ContractStatus.Active or ContractStatus.Expiring))
            {
                var days = save.CurrentDate.DaysUntil(contract.EndDate);
                if (days < 0 || days > 28) continue;
                var wrestler = (save.Wrestlers ?? new List<WrestlerState>()).FirstOrDefault(x => x?.Id == contract.PersonId);
                var name = wrestler?.Identity?.RingName ?? wrestler?.Identity?.LegalName ?? "선수";
                Publish(save, $"contract-expiry-warning:{contract.Id}:28", InboxMessageType.Navigation, InboxPriority.Important, "계약 담당",
                    $"{name}의 계약이 {days}일 후 만료됩니다", "계약 조건과 선수의 향후 활용 계획을 확인하세요.", InboxTargetType.Wrestler, contract.PersonId);
            }
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
                if (show != null && show.Status is not (ShowStatus.Draft or ShowStatus.Preparing or ShowStatus.Review)) message.Status = InboxMessageStatus.Resolved;
            }
        }

        private static string DateKey(GameDate date) => $"{date.Year:D4}{date.Month:D2}{date.Day:D2}";
    }

    public sealed class TimeFlowService
    {
        public const int MaxAdvanceDays = 30;
        private readonly Func<string> createId;

        public TimeFlowService(Func<string> createId = null)
        {
            this.createId = createId ?? EntityId.CreateRuntimeId;
        }

        public TimeFlowResult AdvanceTo(GameSave save, GameDate targetDate)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (save.Promotion == null) throw new ArgumentException("Promotion is required.", nameof(save));
            var days = save.CurrentDate.DaysUntil(targetDate);
            if (days < 1) throw new ArgumentException("Target date must be after the current date.", nameof(targetDate));
            if (days > MaxAdvanceDays) throw new ArgumentException($"Time can advance at most {MaxAdvanceDays} days.", nameof(targetDate));

            var inbox = new InboxService(createId);
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
                if (!CanPaySalaries(save, nextDate, out var required))
                {
                    result.State = TimeFlowState.Blocked;
                    result.BlockingReason = $"Insufficient cash for salaries due on {nextDate.Year:D4}-{nextDate.Month:D2}-{nextDate.Day:D2}. Required: {required}.";
                    return result;
                }

                save.CurrentDate = nextDate;
                ProcessContracts(save, nextDate, result.ProcessedIds);
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
            return null;
        }

        private void ProcessContracts(GameSave save, GameDate date, List<string> processedThisRun)
        {
            foreach (var contract in save.Contracts.Where(x => x != null))
            {
                if (contract.Status == ContractStatus.Active && date.DaysUntil(contract.EndDate) <= 30 && date.CompareTo(contract.EndDate) <= 0)
                    contract.Status = ContractStatus.Expiring;

                if (date.Day == 1 && contract.MonthlySalary > 0 &&
                    contract.StartDate.CompareTo(date) <= 0 && contract.EndDate.CompareTo(date) >= 0 && IsPayable(contract.Status))
                {
                    var sourceId = SalarySourceId(contract.Id, date);
                    if (!save.ProcessedIds.Contains(sourceId))
                    {
                        save.Transactions.Add(new TransactionRecord
                        {
                            Id = createId(), PromotionId = save.Promotion.Id, Date = date,
                            Type = TransactionType.Salary, Amount = -contract.MonthlySalary, ReasonId = contract.Id
                        });
                        save.ProcessedIds.Add(sourceId);
                        processedThisRun.Add(sourceId);
                    }
                }

                if (date.Equals(contract.EndDate) && IsPayable(contract.Status))
                {
                    contract.Status = ContractStatus.Expired;
                    var sourceId = $"contract-expiry:{contract.Id}:{DateKey(date)}";
                    if (!save.ProcessedIds.Contains(sourceId))
                    {
                        save.ProcessedIds.Add(sourceId);
                        processedThisRun.Add(sourceId);
                    }
                }
            }
        }

        private static bool CanPaySalaries(GameSave save, GameDate date, out long required)
        {
            required = 0;
            if (date.Day != 1) return true;
            foreach (var contract in save.Contracts.Where(x => x != null && x.MonthlySalary > 0 && IsPayable(x.Status)))
            {
                if (contract.StartDate.CompareTo(date) <= 0 && contract.EndDate.CompareTo(date) >= 0 &&
                    !save.ProcessedIds.Contains(SalarySourceId(contract.Id, date))) required += contract.MonthlySalary;
            }
            return save.Promotion.CalculateCurrentCash(save.Transactions) >= required;
        }

        private static bool IsPayable(ContractStatus status) =>
            status is ContractStatus.Active or ContractStatus.Expiring or ContractStatus.Overdue;

        private static string SalarySourceId(string contractId, GameDate date) =>
            $"salary:{contractId}:{DateKey(date)}";

        private static string DateKey(GameDate date) => $"{date.Year:D4}{date.Month:D2}{date.Day:D2}";
    }
}
