using System;
using System.Collections.Generic;
using System.Linq;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class PromotionState
    {
        public string Id;
        public string Name;
        public string Abbreviation;
        public long InitialCash;
        public long PromotionPrestige;
        public List<string> UnlockedIds = new();
        public List<string> AppliedPrestigeReasonIds = new();
        public string SeasonPolicyId;

        public string GetDisplayAbbreviation() => string.IsNullOrWhiteSpace(Abbreviation)
            ? CreateAbbreviation(Name)
            : Abbreviation.Trim().ToUpperInvariant();

        public static string CreateAbbreviation(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "PW";
            var words = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
                return string.Concat(words.Take(6).Select(x => char.ToUpperInvariant(x[0])));
            var compact = new string(name.Where(char.IsLetterOrDigit).Take(6).ToArray());
            return string.IsNullOrEmpty(compact) ? "PW" : compact.ToUpperInvariant();
        }

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
