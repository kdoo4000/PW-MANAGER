using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Scout Team Level")]
    public sealed class ScoutTeamLevelDefinition : StaffTeamLevelDefinition
    {
        public int CandidateCount;
        [Range(0, 4)] public int KnowledgeLevel;
        [Range(0f, 1f)] public float ExactValueChance;
        [Range(0f, 2f)] public float ValueErrorRange;
        [Range(0f, 1f)] public float HighTierCandidateRate;
    }
}
