using System.Collections.Generic;
using SaintsField;
using SaintsField.Playa;
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
        [ListDrawerSettings] public List<string> CompatibleMatchTypeIds = new();
        [Min(0f)] public float ConditionCostMultiplier = 1f;
        [EnumToggleButtons] public MatchRuleOverride PinfallRule;
        [EnumToggleButtons] public MatchRuleOverride SubmissionRule;
        [EnumToggleButtons] public MatchRuleOverride DisqualificationRule;
        [EnumToggleButtons] public MatchRuleOverride CountOutRule;
        [ListDrawerSettings] public List<MatchFinishType> AllowedSpecialFinishTypes = new();
    }
}
