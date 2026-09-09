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

        [TestCase(19f, "SS")]
        [TestCase(13f, "B")]
        [TestCase(12f, "C+")]
        [TestCase(11f, "C")]
        [TestCase(10f, "D+")]
        [TestCase(9f, "D")]
        [TestCase(8f, "E+")]
        [TestCase(7f, "E")]
        [TestCase(6f, "F+")]
        [TestCase(5f, "F")]
        [TestCase(4f, "G+")]
        [TestCase(3.99f, "G")]
        public void Grade_UsesConfiguredBoundaries(float value, string expected)
        {
            Assert.That(WrestlerOverallCalculator.Grade(value), Is.EqualTo(expected));
        }

        [TestCase("style_001")]
        [TestCase("style_002")]
        [TestCase("style_003")]
        [TestCase("style_004")]
        [TestCase("style_005")]
        [TestCase("style_006")]
        [TestCase("style_007")]
        public void PlayerBuild_MatchAbilityEqualsWeightedAbility(string styleId)
        {
            foreach (PlayerReputation reputation in System.Enum.GetValues(typeof(PlayerReputation)))
            foreach (PlayerWrestlingType type in System.Enum.GetValues(typeof(PlayerWrestlingType)))
            {
                var build = PlayerCharacterRules.Build(PlayerCareerRole.WrestlerManager, reputation, type, styleId);
                Assert.That(WrestlerOverallCalculator.Match(build.Attributes, styleId), Is.EqualTo(build.MatchAbility).Within(.001f));
                Assert.That(WrestlerOverallCalculator.Grade(WrestlerOverallCalculator.Match(build.Attributes, styleId)),
                    Is.EqualTo(WrestlerOverallCalculator.Grade(build.MatchAbility)));
            }
        }

        [TestCase(PlayerWrestlingType.Worker)]
        [TestCase(PlayerWrestlingType.Balanced)]
        [TestCase(PlayerWrestlingType.Showman)]
        public void PlayerBuild_PromoAbilityUsesNeutralBaseline(PlayerWrestlingType type)
        {
            var build = PlayerCharacterRules.Build(PlayerCareerRole.WrestlerManager, PlayerReputation.Star, type);

            Assert.That(WrestlerOverallCalculator.Promo(build.Attributes, KayfabeAlignment.Tweener, PromoDisposition.Balanced), Is.EqualTo(build.PromoAbility).Within(.001f));
        }

        private static WrestlerAttributesState MatchAttributes(float value) => new()
        {
            RingPsychology = value, RingImprovisation = value, SpotWork = value,
            SpecialtyMatches = value, Selling = value, Stamina = value,
            Brawling = value, Power = value, HighFlying = value, Technical = value
        };
    }
}
