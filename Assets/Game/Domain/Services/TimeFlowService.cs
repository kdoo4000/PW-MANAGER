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

            var result = new TimeFlowResult { State = TimeFlowState.Completed, ReachedDate = save.CurrentDate };
            while (save.CurrentDate.CompareTo(targetDate) < 0)
            {
                var nextDate = save.CurrentDate.AddDays(1);
                if (!CanPaySalaries(save, nextDate, out var required))
                {
                    result.State = TimeFlowState.Blocked;
                    result.BlockingReason = $"Insufficient cash for salaries due on {nextDate.Year:D4}-{nextDate.Month:D2}-{nextDate.Day:D2}. Required: {required}.";
                    return result;
                }

                save.CurrentDate = nextDate;
                ProcessContracts(save, nextDate, result.ProcessedIds);
                result.ProcessedDays++;
                result.ReachedDate = nextDate;
            }
            return result;
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
