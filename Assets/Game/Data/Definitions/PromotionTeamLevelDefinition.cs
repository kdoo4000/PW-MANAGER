using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Promotion Team Level")]
    public sealed class PromotionTeamLevelDefinition : StaffTeamLevelDefinition
    {
        public float TicketDemandMultiplier = 1f;
        public float PositiveFanGainMultiplier = 1f;
    }
}
