using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class CriticReviewCalculatorTests
    {
        [TestCase(0f, .25f)]
        [TestCase(36f, 2.5f)]
        [TestCase(84f, 4f)]
        [TestCase(90f, 4.25f)]
        [TestCase(100f, 5f)]
        [TestCase(104f, 6f)]
        [TestCase(105f, 7f)]
        public void Stars_UsesConfirmedUnevenThresholds(float score, float expected)
        {
            Assert.That(CriticReviewCalculator.Stars(score), Is.EqualTo(expected));
        }

        [Test]
        public void Match_NeutralStory_UsesTechnicalScoreOnly()
        {
            var review = CriticReviewCalculator.Match(18f);

            Assert.That(review.FinalScore, Is.EqualTo(90f));
            Assert.That(review.DisplayedStars, Is.EqualTo(4.25f));
        }

        [Test]
        public void Show_UsesMatchAveragesAndNeutralMissingInputs()
        {
            var review = CriticReviewCalculator.Show(new[]
            {
                new CriticReviewState { FinalScore = 80f },
                new CriticReviewState { FinalScore = 100f }
            });

            Assert.That(review.FinalScore, Is.EqualTo(80f).Within(.001f));
            Assert.That(review.DisplayedStars, Is.EqualTo(3.75f));
        }
    }
}
