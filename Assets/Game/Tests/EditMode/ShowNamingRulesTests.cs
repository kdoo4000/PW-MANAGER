using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class ShowNamingRulesTests
    {
        [TestCase(ScheduledShowType.Regular, 1, 2026, "WWE RAW #1")]
        [TestCase(ScheduledShowType.PpvMajor, 0, 2026, "WWE 로얄럼블 2026")]
        public void Compose_AddsPromotionAndTypeSuffix(ScheduledShowType type, int sequence, int year, string expected)
        {
            var title = type == ScheduledShowType.Regular ? "RAW" : "로얄럼블";
            Assert.That(ShowNamingRules.Compose("WWE", title, type, sequence, year), Is.EqualTo(expected));
        }
    }
}
