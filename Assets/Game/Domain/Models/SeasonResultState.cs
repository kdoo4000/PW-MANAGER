using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class SeasonResultState
    {
        public string Id;
        public SeasonPolicyState Policy;
        public GameDate ObservedDate;
        public int CompletedShows;
        public int CompletedPpvs;
        public int CancelledShows;
        public long Income;
        public long Expenses;
        public long ClosingCash;
        public long OutstandingSalary;
        public long Followers;
        public long Prestige;
        public string NextSeasonPolicyId;
        public List<string> NewUnlockIds = new();
    }
}
