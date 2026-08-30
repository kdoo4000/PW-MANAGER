using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public readonly struct InitialContractOffer
    {
        public readonly long SigningBonus;
        public readonly long MonthlySalary;
        public readonly long TerminationCost;

        public InitialContractOffer(long signingBonus, long monthlySalary, long terminationCost)
        {
            SigningBonus = signingBonus;
            MonthlySalary = monthlySalary;
            TerminationCost = terminationCost;
        }
    }

    public static class InitialContractOfferRules
    {
        public static InitialContractOffer Calculate(WrestlerState wrestler)
        {
            if (wrestler == null) throw new ArgumentNullException(nameof(wrestler));
            var ability = WrestlerOverallCalculator.Match(wrestler) * 0.65f + WrestlerOverallCalculator.Promo(wrestler) * 0.35f;
            var experience = Math.Min(10, wrestler.Identity.CareerYears) * 35;
            var monthly = RoundToHundred(250 + ability * ability * 7 + experience);
            var signingMultiplier = wrestler.Identity.Background == WrestlerBackground.OtherPromotion ? 3 : 2;
            return new InitialContractOffer(monthly * signingMultiplier, monthly, monthly * 4);
        }

        private static long RoundToHundred(double value) => (long)(Math.Ceiling(value / 100d) * 100);
    }
}
