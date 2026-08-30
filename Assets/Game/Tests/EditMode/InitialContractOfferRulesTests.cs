using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class InitialContractOfferRulesTests
    {
        [Test]
        public void Calculate_UsesAbilityExperienceAndBackground()
        {
            var rookie = Wrestler(10, 8, 0, WrestlerBackground.Rookie);
            var veteran = Wrestler(14, 12, 8, WrestlerBackground.OtherPromotion);

            var rookieOffer = InitialContractOfferRules.Calculate(rookie);
            var veteranOffer = InitialContractOfferRules.Calculate(veteran);

            Assert.That(rookieOffer.MonthlySalary, Is.GreaterThan(0));
            Assert.That(veteranOffer.MonthlySalary, Is.GreaterThan(rookieOffer.MonthlySalary));
            Assert.That(rookieOffer.SigningBonus, Is.EqualTo(rookieOffer.MonthlySalary * 2));
            Assert.That(veteranOffer.SigningBonus, Is.EqualTo(veteranOffer.MonthlySalary * 3));
            Assert.That(veteranOffer.TerminationCost, Is.EqualTo(veteranOffer.MonthlySalary * 4));
        }

        private static WrestlerState Wrestler(float match, float promo, int careerYears, WrestlerBackground background)
        {
            return new WrestlerState
            {
                Identity = new WrestlerIdentityState { CareerYears = careerYears, Background = background },
                Attributes = new WrestlerAttributesState
                {
                    RingPsychology = match, RingImprovisation = match, Technical = match, Brawling = match,
                    Power = match, HighFlying = match, SpotWork = match, SpecialtyMatches = match,
                    Selling = match, Stamina = match, Charisma = promo, MicWork = promo,
                    Improvisation = promo, Acting = promo, FaceWork = promo, HeelWork = promo, Comedy = promo
                }
            };
        }
    }
}
