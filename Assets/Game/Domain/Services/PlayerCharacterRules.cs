using System;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class PlayerCharacterBuild
    {
        public float MatchAbility;
        public float PromoAbility;
        public string WrestlingStyleId;
        public WrestlerAttributesState Attributes;
    }

    public static class PlayerCharacterRules
    {
        public static (int Min, int Max) HeightRange(WrestlerGender gender) => gender == WrestlerGender.Male ? (165, 220) : (150, 190);
        public static (int Min, int Max) WeightRange(WrestlerGender gender) => gender == WrestlerGender.Male ? (55, 220) : (42, 150);
        public static bool IsPhysicalProfileValid(WrestlerGender gender, int heightCm, int weightKg)
        {
            var height = HeightRange(gender);
            var weight = WeightRange(gender);
            return heightCm >= height.Min && heightCm <= height.Max && weightKg >= weight.Min && weightKg <= weight.Max;
        }

        public static bool IsStyleAvailable(string styleId, int heightCm, WrestlerGender gender) =>
            styleId != "style_006" || heightCm >= (gender == WrestlerGender.Male ? 195 : 180);

        public static PlayerCharacterBuild Build(PlayerCareerRole role, PlayerReputation reputation, PlayerWrestlingType type, string styleId = null)
        {
            if (role == PlayerCareerRole.ProfessionalManager) type = PlayerWrestlingType.Balanced;
            styleId = string.IsNullOrEmpty(styleId) ? type == PlayerWrestlingType.Worker ? "style_003" : "style_007" : styleId;
            var baseAbility = 6f + (int)reputation * 2f;
            var match = role == PlayerCareerRole.WrestlerManager
                ? baseAbility + (type == PlayerWrestlingType.Worker ? 2f : type == PlayerWrestlingType.Showman ? -2f : 0f)
                : 0f;
            var promo = baseAbility + (role == PlayerCareerRole.WrestlerManager && type == PlayerWrestlingType.Worker ? -2f :
                role == PlayerCareerRole.WrestlerManager && type == PlayerWrestlingType.Showman ? 2f : 0f);
            var a = new WrestlerAttributesState();
            if (role == PlayerCareerRole.WrestlerManager)
            {
                var typeMods = type == PlayerWrestlingType.Worker
                    ? new[] { 2f, 1, 2, 0, 0, 0, 0, 0, 2, 1 }
                    : type == PlayerWrestlingType.Showman ? new[] { 0f, 0, -1, 0, 0, 1, 2, 1, 0, 0 } : new float[10];
                var styleMods = StyleModifiers(styleId);
                a.RingPsychology = Stat(match, typeMods[0] + styleMods[0]); a.RingImprovisation = Stat(match, typeMods[1] + styleMods[1]);
                a.Technical = Stat(match, typeMods[2] + styleMods[2]); a.Brawling = Stat(match, typeMods[3] + styleMods[3]);
                a.Power = Stat(match, typeMods[4] + styleMods[4]); a.HighFlying = Stat(match, typeMods[5] + styleMods[5]);
                a.SpotWork = Stat(match, typeMods[6] + styleMods[6]); a.SpecialtyMatches = Stat(match, typeMods[7] + styleMods[7]);
                a.Selling = Stat(match, typeMods[8] + styleMods[8]); a.Stamina = Stat(match, typeMods[9] + styleMods[9]);
            }
            a.Charisma = Stat(promo, type == PlayerWrestlingType.Showman ? 2 : 0);
            a.MicWork = Stat(promo, type == PlayerWrestlingType.Showman ? 1 : 0);
            a.Improvisation = Stat(promo, type == PlayerWrestlingType.Worker ? 1 : 0);
            a.Acting = Stat(promo, type == PlayerWrestlingType.Showman ? 2 : 0);
            a.FaceWork = Stat(promo, 0); a.HeelWork = Stat(promo, 0);
            a.Comedy = Stat(promo, type == PlayerWrestlingType.Showman ? 1 : 0);
            return new PlayerCharacterBuild { MatchAbility = match, PromoAbility = promo, WrestlingStyleId = styleId, Attributes = a };
        }

        public static long StartingFans(PlayerReputation reputation) => reputation switch
        {
            PlayerReputation.Local => 500, PlayerReputation.Regional => 1500,
            PlayerReputation.National => 2500, PlayerReputation.Star => 5000, _ => 10000
        };

        private static float Stat(float value, float modifier) => Math.Max(1f, Math.Min(20f, value + modifier));

        private static float[] StyleModifiers(string styleId) => styleId switch
        {
            "style_001" => new[] { 0f, 0, -1, 2, 1, -2, 0, 1, 1, 0 },
            "style_002" => new[] { 0f, 0, -1, 0, 3, -2, 0, 0, 1, 0 },
            "style_003" => new[] { 1f, 1, 3, -1, -1, 0, 0, 0, 1, 0 },
            "style_004" => new[] { 0f, 0, 0, -1, -2, 3, 2, 0, 0, 1 },
            "style_005" => new[] { 0f, 1, 2, -1, -2, 2, 2, 0, 0, 0 },
            "style_006" => new[] { 0f, -1, -2, 1, 3, -3, 0, 1, 1, -1 },
            _ => new float[10]
        };
    }
}
