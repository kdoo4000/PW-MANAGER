using UnityEngine;

namespace PWManager.Data.Definitions
{
    public abstract class StaffTeamLevelDefinition : StaticDefinition
    {
        [Range(1, 5)] public int Level;
        public long RequiredPrestige;
        public long UpgradeCost;
        [TextArea] public string EffectDescription;
    }
}
