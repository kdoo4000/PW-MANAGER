using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Medical Team Level")]
    public sealed class MedicalTeamLevelDefinition : StaffTeamLevelDefinition
    {
        public float RecoveryDurationMultiplier = 1f;
        public float InjuryAggravationMultiplier = 1f;
    }
}
