using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Wrestling Style")]
    public sealed class WrestlingStyleDefinition : StaticDefinition
    {
        [Range(0, 1)] public float BrawlingWeight;
        [Range(0, 1)] public float PowerWeight;
        [Range(0, 1)] public float HighFlyingWeight;
        [Range(0, 1)] public float TechnicalWeight;
        [Range(0, 1)] public float BrawlingGrowthBonus;
        [Range(0, 1)] public float PowerGrowthBonus;
        [Range(0, 1)] public float HighFlyingGrowthBonus;
        [Range(0, 1)] public float TechnicalGrowthBonus;
        public int MinimumMaleHeightCm;
        public int MinimumFemaleHeightCm;
    }
}
