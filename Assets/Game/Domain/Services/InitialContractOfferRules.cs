using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public readonly struct InitialContractOffer
    {
        public readonly long SigningBonus;
        public readonly long MonthlySalary;
        public readonly long TerminationCost;
        public readonly long MatchAppearanceFee;
        public readonly long PromoAppearanceFee;

        public InitialContractOffer(long signingBonus, long monthlySalary, long terminationCost, long matchAppearanceFee = 0, long promoAppearanceFee = 0)
        {
            SigningBonus = signingBonus;
            MonthlySalary = monthlySalary;
            TerminationCost = terminationCost;
            MatchAppearanceFee = matchAppearanceFee;
            PromoAppearanceFee = promoAppearanceFee;
        }
    }

    public static class InitialContractOfferRules
    {
        public static InitialContractOffer Calculate(WrestlerState wrestler)
        {
            if (wrestler == null) throw new ArgumentNullException(nameof(wrestler));
            var ability = WrestlerOverallCalculator.Match(wrestler) * 0.65f + WrestlerOverallCalculator.Promo(wrestler) * 0.35f;
            var experience = Math.Min(10, wrestler.Identity.CareerYears) * 35;
            var signingMultiplier = wrestler.Identity.Background == WrestlerBackground.Veteran ? 3 : 2;
            var reference = Calculate(ability, experience, signingMultiplier).MonthlySalary;
            var reputation = 1 + Math.Sqrt(Math.Max(0, wrestler.Fame?.Value ?? 0) / 500d);
            var monthly = RoundToTen(reference * .08 * reputation);
            var match = RoundToTen(reference * .12 * reputation);
            var promo = RoundToTen(reference * .06 * reputation);
            return new InitialContractOffer((monthly + match) * signingMultiplier, monthly, 0, match, promo);
        }

        public static InitialContractOffer Calculate(ManagerState manager)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            return Calculate(manager.Attributes.ManagerOverall, 0, 2);
        }

        private static InitialContractOffer Calculate(float ability, int experience, int signingMultiplier)
        {
            var monthly = RoundToHundred(250 + ability * ability * 7 + experience);
            return new InitialContractOffer(monthly * signingMultiplier, monthly, 0);
        }

        private static long RoundToHundred(double value) => (long)(Math.Ceiling(value / 100d) * 100);
        private static long RoundToTen(double value) => Math.Max(10, (long)(Math.Ceiling(value / 10d) * 10));
    }
}
