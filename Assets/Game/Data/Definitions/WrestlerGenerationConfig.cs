using UnityEngine;

namespace PWManager.Data.Definitions
{
    [CreateAssetMenu(menuName = "PW Manager/Balance/Wrestler Generation")]
    public sealed class WrestlerGenerationConfig : ScriptableObject
    {
        [Min(1)] public int InitialMaleCandidateCount = 12;
        [Min(1)] public int InitialFemaleCandidateCount = 12;
        [Min(0)] public int WorldMaleCount = 200;
        [Min(0)] public int WorldFemaleCount = 200;
        [Min(1)] public int CandidateRetentionWeeks = 52;
        [Min(1)] public int StyleChangeCooldownWeeks = 52;
    }
}
