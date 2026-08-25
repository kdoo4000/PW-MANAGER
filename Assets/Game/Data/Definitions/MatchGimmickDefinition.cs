using System.Collections.Generic;
using UnityEngine;
using PWManager.Domain.Services;
using PWManager.Domain.Models;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Match Gimmick")]
    public sealed class MatchGimmickDefinition : StaticDefinition
    {
        public int MinimumParticipants = 2;
        public int MaximumParticipants = 6;
        public List<string> CompatibleMatchTypeIds = new();
        [Min(0f)] public float ConditionCostMultiplier = 1f;
        public MatchRuleOverride PinfallRule;
        public MatchRuleOverride SubmissionRule;
        public MatchRuleOverride DisqualificationRule;
        public MatchRuleOverride CountOutRule;
        public List<MatchFinishType> AllowedSpecialFinishTypes = new();
    }
}
