using SaintsField;
using UnityEngine;

namespace PWManager.Data.Definitions
{
    public enum MoveRequiredStat { None, Brawling, Power, Technical, HighFlying }
    public enum MoveSellingDifficulty { Easy = 7, Normal = 10, Hard = 13, VeryHard = 16 }

    [CreateAssetMenu(menuName = "PW Manager/Static/Move")]
    public sealed class MoveDefinition : StaticDefinition
    {
        [Range(0, 1)] public float BrawlingWeight;
        [Range(0, 1)] public float PowerWeight;
        [Range(0, 1)] public float TechnicalWeight;
        [Range(0, 1)] public float HighFlyingWeight;
        [Range(1, 20)] public float ExecutionDifficulty = 1;
        [EnumToggleButtons] public MoveSellingDifficulty SellingDifficulty = MoveSellingDifficulty.Easy;
        [EnumToggleButtons] public MoveRequiredStat RequiredStat;
        [Range(0, 20)] public float RequiredStatValue;
        public string ScenarioGroupId;
    }
}
