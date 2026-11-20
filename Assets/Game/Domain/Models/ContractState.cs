using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    public enum ContractType { Wrestler, Manager }
    public enum ContractStatus { PreNegotiation, Negotiating, Active, Expiring, Expired, Overdue, Terminated, Rejected, Scheduled }
    public enum ExpectedRole { Jobber, Midcarder, MainEventer }
    public enum SalaryPaymentKind { Monthly, MatchAppearance, PromoAppearance }

    [Serializable]
    public sealed class ContractState
    {
        public string Id;
        public string PersonId;
        public string PromotionId;
        public ContractType Type;
        public GameDate StartDate;
        public GameDate EndDate;
        public long SigningBonus;
        public long MonthlySalary;
        public long MatchAppearanceFee;
        public long PromoAppearanceFee;
        public bool HasExpectedRole;
        public ExpectedRole ExpectedRole;
        public ContractStatus Status;
        public bool WasRookieWhenHired;
        // Retained for old saves; release costs are quoted from remaining salary.
        public long TerminationCost;
        public OptionalGameDate LastSalaryPenaltyDate;
        public bool IsInForce => Status is ContractStatus.Active or ContractStatus.Expiring or ContractStatus.Overdue;
        public bool Covers(GameDate date) => (IsInForce || Status == ContractStatus.Scheduled) &&
            StartDate.CompareTo(date) <= 0 && EndDate.CompareTo(date) >= 0;

        public ContractState Copy() => (ContractState)MemberwiseClone();
    }

    [Serializable]
    public sealed class SalaryPaymentState
    {
        public string Id;
        public string ContractId;
        public GameDate DueDate;
        public SalaryPaymentKind Kind;
        public string ShowId;
        public long Amount;
        public long RemainingAmount;
        public bool WasOverdue;
        public SalaryPaymentState Copy() => (SalaryPaymentState)MemberwiseClone();
    }

    [Serializable]
    public sealed class ContractNegotiationState
    {
        public string Id;
        public string PersonId;
        public ContractType Type;
        public GameDate Date;
        public GameDate NextAttemptDate;
        public long SigningBonus;
        public long MonthlySalary;
        public long MatchAppearanceFee;
        public long PromoAppearanceFee;
        public bool HasExpectedRole;
        public ExpectedRole ExpectedRole;
        public float AcceptancePercent;
        public float RollPercent;
        public bool Accepted;
        public string ContractId;
    }

    [Serializable]
    public sealed class ContractTerminationState
    {
        public string Id;
        public string PersonId;
        public ContractType Type;
        public GameDate Date;
        public GameDate NextAttemptDate;
        public bool Mutual;
        public long RemainingSalary;
        public long Compensation;
        public float AcceptancePercent;
        public float RollPercent;
        public bool Accepted;
        public List<string> ContractIds = new();
    }
}
