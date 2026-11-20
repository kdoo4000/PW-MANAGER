using System;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;

namespace PWManager.Tests
{
    public sealed class WrestlerFameRulesTests
    {
        private static object Call(string method, params object[] args)
        {
            var type = typeof(GameSave).Assembly.GetType("PWManager.Domain.Services.WrestlerFameRules");
            Assert.That(type, Is.Not.Null, "Personal fame rules must exist independently of card status.");
            try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }

        [TestCase(0L, "무명 선수")]
        [TestCase(99L, "무명 선수")]
        [TestCase(100L, "지역 유명 선수")]
        [TestCase(499L, "지역 유명 선수")]
        [TestCase(500L, "유명 선수")]
        [TestCase(1999L, "유명 선수")]
        [TestCase(2000L, "전국구 스타")]
        [TestCase(4999L, "전국구 스타")]
        [TestCase(5000L, "세계적인 선수")]
        [TestCase(14999L, "세계적인 선수")]
        [TestCase(15000L, "전설적인 선수")]
        [TestCase(long.MaxValue, "전설적인 선수")]
        public void Label_UsesApprovedThresholdsWithoutACap(long value, string expected) => Assert.That(Call("Label", value), Is.EqualTo(expected));

        [TestCase(PlayerReputation.Local, 100L)]
        [TestCase(PlayerReputation.Regional, 500L)]
        [TestCase(PlayerReputation.National, 2000L)]
        [TestCase(PlayerReputation.Star, 5000L)]
        [TestCase(PlayerReputation.Legend, 15000L)]
        public void PlayerInitial_UsesBackground(PlayerReputation background, long expected) => Assert.That(Call("InitialForPlayer", background), Is.EqualTo(expected));

        [Test]
        public void CareerInitial_DoesNotReadStatusOrStyle() => Assert.That(Call("InitialForCareer", 12), Is.EqualTo(120L));

        [TestCase(0L, 10000L, 2d, 0d, 20d)]
        [TestCase(1000L, 10000L, 2d, 0d, 10d)]
        [TestCase(5000L, 10000L, 2d, 0d, 10d / 3d)]
        [TestCase(0L, 0L, 3d, 4d, 0d)]
        [TestCase(0L, 10000L, 2d, 4d, 60d)]
        public void Gain_CombinesReachPerformanceTitleAndDiminishingReturns(long current, long reach, double activity, double title, double expected) =>
            Assert.That((double)Call("CalculateGain", current, reach, activity, title), Is.EqualTo(expected).Within(.000001));

        [TestCase(49f, 10d, 0d)]
        [TestCase(50f, 10d, 1d)]
        [TestCase(75f, 5d, 1d)]
        [TestCase(90f, 1d, .3d)]
        [TestCase(100f, 30d, 3d)]
        public void Activity_UsesPerformanceAndAppearanceMinutes(float score, double minutes, double expected) =>
            Assert.That((double)Call("Activity", score, minutes), Is.EqualTo(expected).Within(.000001));

        [Test]
        public void InvalidValues_AreRejected()
        {
            Assert.Throws<InvalidOperationException>(() => Call("CalculateGain", -1L, 100L, 1d, 0d));
            Assert.Throws<InvalidOperationException>(() => Call("CalculateGain", 0L, -1L, 1d, 0d));
            Assert.Throws<InvalidOperationException>(() => Call("Activity", float.NaN, 10d));
        }
    }
}
