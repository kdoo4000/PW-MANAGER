using UnityEngine;
using UnityEngine.Serialization;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Match Type")]
    public sealed class MatchTypeDefinition : StaticDefinition
    {
        public int MinimumParticipants = 2;
        public int MaximumParticipants = 2;
        [FormerlySerializedAs("TeamSize")]
        [FormerlySerializedAs("TeamCount")]
        public int MinimumTeamCount;
        public int MaximumTeamCount;
        public int MinimumMembersPerTeam;
        public int MaximumMembersPerTeam;
        [Min(0f)] public float ConditionCostMultiplier = 1f;
    }
}
