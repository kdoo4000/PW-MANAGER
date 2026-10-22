namespace PWManager.Domain.Services
{
    public enum MatchRuleOverride { Default, Allowed, Disabled }

    public readonly struct MatchGimmickRules
    {
        public readonly int MinimumParticipants;
        public readonly int MaximumParticipants;
        public readonly System.Collections.Generic.IReadOnlyList<string> CompatibleMatchTypeIds;
        public readonly float ConditionCostMultiplier;
        public readonly MatchRuleOverride PinfallRule;
        public readonly MatchRuleOverride SubmissionRule;
        public readonly MatchRuleOverride DisqualificationRule;
        public readonly MatchRuleOverride CountOutRule;
        public readonly MatchRuleOverride RopeBreakRule;
        public readonly int CountOutSeconds;
        public readonly bool IronMan, Tornado, Elimination, TwoOutOfThree;
        public readonly System.Collections.Generic.IReadOnlyList<PWManager.Domain.Models.MatchFinishType> AllowedSpecialFinishTypes;

        public MatchGimmickRules(int minimumParticipants, int maximumParticipants,
            System.Collections.Generic.IReadOnlyList<string> compatibleMatchTypeIds, float conditionCostMultiplier,
            MatchRuleOverride disqualificationRule = MatchRuleOverride.Default,
            MatchRuleOverride countOutRule = MatchRuleOverride.Default,
            MatchRuleOverride pinfallRule = MatchRuleOverride.Default,
            MatchRuleOverride submissionRule = MatchRuleOverride.Default,
            System.Collections.Generic.IReadOnlyList<PWManager.Domain.Models.MatchFinishType> allowedSpecialFinishTypes = null,
            MatchRuleOverride ropeBreakRule = MatchRuleOverride.Default, int countOutSeconds = 10,
            bool ironMan = false, bool tornado = false, bool elimination = false, bool twoOutOfThree = false)
        {
            MinimumParticipants = minimumParticipants;
            MaximumParticipants = maximumParticipants;
            CompatibleMatchTypeIds = compatibleMatchTypeIds;
            ConditionCostMultiplier = conditionCostMultiplier;
            PinfallRule = pinfallRule;
            SubmissionRule = submissionRule;
            DisqualificationRule = disqualificationRule;
            CountOutRule = countOutRule;
            RopeBreakRule = ropeBreakRule;
            CountOutSeconds = countOutSeconds > 0 ? countOutSeconds : 10;
            IronMan = ironMan;
            Tornado = tornado;
            Elimination = elimination;
            TwoOutOfThree = twoOutOfThree;
            AllowedSpecialFinishTypes = allowedSpecialFinishTypes;
        }

        public bool IsCompatible(string matchTypeId, int participantCount) =>
            participantCount >= MinimumParticipants && participantCount <= MaximumParticipants &&
            CompatibleMatchTypeIds != null && System.Linq.Enumerable.Contains(CompatibleMatchTypeIds, matchTypeId);

        public void ApplyFormat(PWManager.Domain.Models.MatchPlanState match)
        {
            // Ordinary gimmicks preserve format flags in existing saves.
            if (!IronMan && !Tornado && !Elimination && !TwoOutOfThree) return;
            match.IronMan = IronMan;
            match.Tornado = Tornado;
            match.Elimination = Elimination;
            match.FallsToWin = TwoOutOfThree ? 2 : 1;
        }
    }

    public interface IMatchTypeRules
    {
        bool TryGetParticipantRange(string matchTypeId, out int minimum, out int maximum);
        bool TryGetTeamRules(string matchTypeId, out int minimumTeamCount, out int maximumTeamCount,
            out int minimumMembersPerTeam, out int maximumMembersPerTeam);
        bool TryGetConditionCostMultiplier(string matchTypeId, out float multiplier);
        bool TryGetGimmickRules(string gimmickId, out MatchGimmickRules rules);
    }
}
