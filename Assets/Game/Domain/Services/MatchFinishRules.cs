using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public static class MatchFinishRules
    {
        public static bool IsAllowed(MatchFinishType finishType, bool isTeamMatch,
            int participantCount, int teamCount, MatchGimmickRules? gimmickRules = null)
        {
            if (finishType is MatchFinishType.Escape or MatchFinishType.ObjectRetrieval or MatchFinishType.TableBreak)
                return gimmickRules?.AllowedSpecialFinishTypes != null &&
                    System.Linq.Enumerable.Contains(gimmickRules.Value.AllowedSpecialFinishTypes, finishType);
            var ruleOverride = GetOverride(finishType, gimmickRules);
            if (ruleOverride != MatchRuleOverride.Default) return ruleOverride == MatchRuleOverride.Allowed;
            if (finishType is not (MatchFinishType.Disqualification or MatchFinishType.CountOut)) return true;
            return isTeamMatch ? teamCount < 3 : participantCount < 3;
        }

        private static MatchRuleOverride GetOverride(MatchFinishType finishType, MatchGimmickRules? gimmickRules)
        {
            if (!gimmickRules.HasValue) return MatchRuleOverride.Default;
            return finishType switch
            {
                MatchFinishType.Pinfall or MatchFinishType.RollUp => gimmickRules.Value.PinfallRule,
                MatchFinishType.Submission => gimmickRules.Value.SubmissionRule,
                MatchFinishType.Disqualification => gimmickRules.Value.DisqualificationRule,
                MatchFinishType.CountOut => gimmickRules.Value.CountOutRule,
                _ => MatchRuleOverride.Default
            };
        }
    }
}
