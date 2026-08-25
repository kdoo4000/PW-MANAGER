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
        public readonly System.Collections.Generic.IReadOnlyList<PWManager.Domain.Models.MatchFinishType> AllowedSpecialFinishTypes;

        public MatchGimmickRules(int minimumParticipants, int maximumParticipants,
            System.Collections.Generic.IReadOnlyList<string> compatibleMatchTypeIds, float conditionCostMultiplier,
            MatchRuleOverride disqualificationRule = MatchRuleOverride.Default,
            MatchRuleOverride countOutRule = MatchRuleOverride.Default,
            MatchRuleOverride pinfallRule = MatchRuleOverride.Default,
            MatchRuleOverride submissionRule = MatchRuleOverride.Default,
            System.Collections.Generic.IReadOnlyList<PWManager.Domain.Models.MatchFinishType> allowedSpecialFinishTypes = null)
        {
            MinimumParticipants = minimumParticipants;
            MaximumParticipants = maximumParticipants;
            CompatibleMatchTypeIds = compatibleMatchTypeIds;
            ConditionCostMultiplier = conditionCostMultiplier;
            PinfallRule = pinfallRule;
            SubmissionRule = submissionRule;
            DisqualificationRule = disqualificationRule;
            CountOutRule = countOutRule;
            AllowedSpecialFinishTypes = allowedSpecialFinishTypes;
        }

        public bool IsCompatible(string matchTypeId, int participantCount) =>
            participantCount >= MinimumParticipants && participantCount <= MaximumParticipants &&
            CompatibleMatchTypeIds != null && System.Linq.Enumerable.Contains(CompatibleMatchTypeIds, matchTypeId);
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
