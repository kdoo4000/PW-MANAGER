using System;

namespace PWManager.Domain.Models
{
    public enum ContractType { Wrestler, Manager }
    public enum ContractStatus { PreNegotiation, Negotiating, Active, Expiring, Expired, Overdue, Terminated, Rejected }
    public enum ExpectedRole { Jobber, Midcarder, MainEventer }

    [Serializable]
    public sealed class ContractState
    {
        public string Id;
        public string PersonId;
        public ContractType Type;
        public GameDate StartDate;
        public GameDate EndDate;
        public long SigningBonus;
        public long MonthlySalary;
        public bool HasExpectedRole;
        public ExpectedRole ExpectedRole;
        public ContractStatus Status;
        public long TerminationCost;
    }
}
