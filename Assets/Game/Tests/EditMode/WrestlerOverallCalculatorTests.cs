using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests.EditMode
{
    public sealed class WrestlerOverallCalculatorTests
    {
        [Test]
        public void Match_UsesSelectedStyleWeights()
        {
            var attributes = MatchAttributes(10f);
            attributes.Brawling = 20f;

            Assert.That(WrestlerOverallCalculator.Match(attributes, "style_007"), Is.EqualTo(11f).Within(.001f));
            Assert.That(WrestlerOverallCalculator.Match(attributes, "style_001"), Is.EqualTo(12.2f).Within(.001f));
        }

        [Test]
        public void Promo_UsesRoleAndDispositionWeights()
        {
            var attributes = new WrestlerAttributesState
            {
                Charisma = 10f, MicWork = 10f, Improvisation = 10f, Acting = 10f,
                FaceWork = 20f, HeelWork = 5f, Comedy = 10f
            };

            var result = WrestlerOverallCalculator.Promo(attributes, KayfabeAlignment.Face, PromoDisposition.Serious);

            Assert.That(result, Is.EqualTo(13.4f).Within(.001f));
        }

        private static WrestlerAttributesState MatchAttributes(float value) => new()
        {
            RingPsychology = value, RingImprovisation = value, SpotWork = value,
            SpecialtyMatches = value, Selling = value, Stamina = value,
            Brawling = value, Power = value, HighFlying = value, Technical = value
        };
    }
}
