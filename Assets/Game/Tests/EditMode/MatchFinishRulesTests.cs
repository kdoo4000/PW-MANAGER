using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;

namespace PWManager.Tests
{
    public sealed class MatchFinishRulesTests
    {
        [TestCase(MatchFinishType.Pinfall)]
        [TestCase(MatchFinishType.RollUp)]
        public void DisabledPinfallRule_BlocksPinfallAndRollUp(MatchFinishType finishType)
        {
            var rules = Rules(pinfall: MatchRuleOverride.Disabled);

            Assert.That(MatchFinishRules.IsAllowed(finishType, false, 2, 0, rules), Is.False);
        }

        [Test]
        public void DisabledSubmissionRule_BlocksSubmission()
        {
            var rules = Rules(submission: MatchRuleOverride.Disabled);

            Assert.That(MatchFinishRules.IsAllowed(MatchFinishType.Submission, false, 2, 0, rules), Is.False);
        }

        [Test]
        public void AllowedDqRule_OverridesMultiSideRestriction()
        {
            var rules = Rules(disqualification: MatchRuleOverride.Allowed);

            Assert.That(MatchFinishRules.IsAllowed(MatchFinishType.Disqualification, false, 3, 0, rules), Is.True);
        }

        [Test]
        public void DefaultDqRule_UsesMultiSideRestriction()
        {
            Assert.That(MatchFinishRules.IsAllowed(MatchFinishType.Disqualification, true, 6, 3), Is.False);
            Assert.That(MatchFinishRules.IsAllowed(MatchFinishType.Disqualification, true, 6, 2), Is.True);
        }

        [TestCase(MatchFinishType.Escape)]
        [TestCase(MatchFinishType.ObjectRetrieval)]
        [TestCase(MatchFinishType.TableBreak)]
        public void SpecialFinish_RequiresExplicitGimmickPermission(MatchFinishType finishType)
        {
            var allowed = new MatchGimmickRules(2, 8, new[] { "matchtype_001" }, 1f,
                allowedSpecialFinishTypes: new[] { finishType });

            Assert.That(MatchFinishRules.IsAllowed(finishType, false, 2, 0), Is.False);
            Assert.That(MatchFinishRules.IsAllowed(finishType, false, 2, 0, allowed), Is.True);
        }

        private static MatchGimmickRules Rules(
            MatchRuleOverride pinfall = MatchRuleOverride.Default,
            MatchRuleOverride submission = MatchRuleOverride.Default,
            MatchRuleOverride disqualification = MatchRuleOverride.Default,
            MatchRuleOverride countOut = MatchRuleOverride.Default) =>
            new(2, 8, new[] { "matchtype_001", "matchtype_002" }, 1f,
                disqualification, countOut, pinfall, submission);
    }
}
