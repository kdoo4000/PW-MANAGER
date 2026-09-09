using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Commentary Team Level")]
    public sealed class CommentaryTeamLevelDefinition : StaffTeamLevelDefinition
    {
        public float LiveAudienceReactionMultiplier = 1f;
        public float BroadcastAudienceReactionMultiplier = 1f;
    }
}
