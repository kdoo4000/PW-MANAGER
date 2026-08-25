using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class PromotionState
    {
        public string Id;
        public string Name;
        public long InitialCash;
        public long PromotionPrestige;
        public List<string> UnlockedIds = new();
        public List<string> AppliedPrestigeReasonIds = new();
        public string SeasonPolicyId;

        public long CalculateCurrentCash(IEnumerable<TransactionRecord> transactions)
        {
            var currentCash = InitialCash;
            if (transactions == null) return currentCash;

            foreach (var transaction in transactions)
            {
                if (transaction != null && !transaction.IsVoided && transaction.PromotionId == Id)
                {
                    currentCash += transaction.Amount;
                }
            }

            return currentCash;
        }
    }
}
