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
        public PWManager.Domain.Services.MoveKind Kind;
        public PWManager.Domain.Services.BodyRegion TargetRegion = PWManager.Domain.Services.BodyRegion.Body;
        public bool Enabled = true;
        public bool CanUseGenerally;
        public bool CanBeSignature = true;
        public bool CanBeFinisher = true;
        public string TechniqueKey;
        public string[] AllowedStyleIds = System.Array.Empty<string>();
        public PWManager.Domain.Services.MatchActionType Action;
        public PWManager.Domain.Services.MoveSituation Situation;
        public PWManager.Domain.Services.MoveMotionProfile MotionProfile;
        public PWManager.Domain.Services.MoveExecutionStyle ExecutionStyle;
        public PWManager.Domain.Services.MatchPosture EndPosture;
        public float TravelDistance = .15f;

        public bool CanAssign(PWManager.Domain.Models.WrestlerState wrestler, bool finisher)
        {
            if (!Enabled || !(finisher ? CanBeFinisher : CanBeSignature) ||
                AllowedStyleIds.Length > 0 && !System.Array.Exists(AllowedStyleIds, id => id == wrestler.Presentation.WrestlingStyleId)) return false;
            var a = wrestler.Attributes;
            return RequiredStat switch
            {
                MoveRequiredStat.None => true, MoveRequiredStat.Brawling => a.Brawling >= RequiredStatValue,
                MoveRequiredStat.Power => a.Power >= RequiredStatValue, MoveRequiredStat.Technical => a.Technical >= RequiredStatValue,
                MoveRequiredStat.HighFlying => a.HighFlying >= RequiredStatValue, _ => false
            };
        }
    }
}
