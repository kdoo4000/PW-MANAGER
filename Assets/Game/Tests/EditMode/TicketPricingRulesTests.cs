using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class TicketPricingRulesTests
    {
        [TestCase(ScheduledShowType.Regular, 150)]
        [TestCase(ScheduledShowType.PpvRegular, 225)]
        [TestCase(ScheduledShowType.PpvMajor, 300)]
        [TestCase(ScheduledShowType.PpvSignature, 450)]
        public void ReferencePrice_UsesVenueBaseAndShowType(ScheduledShowType showType, long expected)
        {
            Assert.That(TicketPricingRules.GetReferencePrice(150, showType), Is.EqualTo(expected));
        }

        [Test]
        public void PlayerPriceRange_IsSixtyToOneHundredSixtyPercent()
        {
            Assert.That(TicketPricingRules.GetPlayerPriceRange(450), Is.EqualTo((270L, 720L)));
        }

        [TestCase(60, "1.30")]
        [TestCase(80, "1.15")]
        [TestCase(100, "1.00")]
        [TestCase(120, "0.85")]
        [TestCase(140, "0.70")]
        [TestCase(160, "0.55")]
        public void DemandMultiplier_UsesLinearPriceCurve(long actualPrice, string expected)
        {
            Assert.That(TicketPricingRules.GetDemandMultiplier(actualPrice, 100),
                Is.EqualTo(decimal.Parse(expected)));
        }

        [Test]
        public void PriceAdjustedDemand_FloorsFractionalAudience()
        {
            Assert.That(TicketPricingRules.GetPriceAdjustedDemand(999, 120, 100), Is.EqualTo(849));
        }

        [TestCase(59)]
        [TestCase(161)]
        public void DemandMultiplier_RejectsPriceOutsidePlayerRange(long actualPrice)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => TicketPricingRules.GetDemandMultiplier(actualPrice, 100));
        }

        [Test]
        public void ExpectedAttendanceRange_AppliesVarianceAndVenueCapacity()
        {
            Assert.That(TicketPricingRules.GetExpectedAttendanceRange(1000, 100, 100, 980),
                Is.EqualTo((950L, 980L)));
        }

        [Test]
        public void ActualAttendance_AppliesStoredVarianceAfterPriceDemand()
        {
            Assert.That(TicketPricingRules.GetActualAttendance(1000, 120, 100, 1000, 10200),
                Is.EqualTo(867));
        }

        [Test]
        public void AttendanceVarianceRoll_IsDeterministicForSavedRandomSeed()
        {
            var first = TicketPricingRules.RollAttendanceVarianceBasisPoints(new System.Random(12345));
            var second = TicketPricingRules.RollAttendanceVarianceBasisPoints(new System.Random(12345));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.InRange(9500, 10500));
        }
    }
}
