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
            var baseAbility = 10f + (int)reputation * 1.5f;
            var match = role == PlayerCareerRole.WrestlerManager
                ? baseAbility + (type == PlayerWrestlingType.Worker ? 2f : type == PlayerWrestlingType.Showman ? -2f : 0f)
                : 0f;
            var promo = baseAbility + (role == PlayerCareerRole.WrestlerManager && type == PlayerWrestlingType.Worker ? -2f :
                role == PlayerCareerRole.WrestlerManager && type == PlayerWrestlingType.Showman ? 2f : 0f);
            var a = new WrestlerAttributesState();
            if (role == PlayerCareerRole.WrestlerManager)
                a = MatchAttributes(match, MatchModifiers(type, styleId));
            ApplyPromoAttributes(a, promo, PromoModifiers(type));
            return new PlayerCharacterBuild { MatchAbility = match, PromoAbility = promo, WrestlingStyleId = styleId, Attributes = a };
        }

        public static long StartingFans(PlayerReputation reputation) => reputation switch
        {
            PlayerReputation.Local => 500, PlayerReputation.Regional => 1500,
            PlayerReputation.National => 2500, PlayerReputation.Star => 5000, _ => 10000
        };

        private static WrestlerAttributesState MatchAttributes(float value, float[] offsets) => new()
        {
            RingPsychology = value + offsets[0], RingImprovisation = value + offsets[1], Technical = value + offsets[2],
            Brawling = value + offsets[3], Power = value + offsets[4], HighFlying = value + offsets[5],
            SpotWork = value + offsets[6], SpecialtyMatches = value + offsets[7], Selling = value + offsets[8], Stamina = value + offsets[9]
        };

        private static void ApplyPromoAttributes(WrestlerAttributesState a, float value, float[] offsets)
        {
            a.Charisma = value + offsets[0]; a.MicWork = value + offsets[1]; a.Improvisation = value + offsets[2];
            a.Acting = value + offsets[3]; a.FaceWork = value + offsets[4]; a.HeelWork = value + offsets[5]; a.Comedy = value + offsets[6];
        }

        private static float[] MatchModifiers(PlayerWrestlingType type, string styleId) => (type, styleId) switch
        {
            (PlayerWrestlingType.Worker, "style_001") => new[] { .7f, -.3f, -.3f, .7f, -.3f, -3.3f, -1.3f, -.3f, 1.7f, -.3f },
            (PlayerWrestlingType.Worker, "style_002") => new[] { .58f, -.42f, -.42f, -1.42f, 1.58f, -3.42f, -1.42f, -1.42f, 1.58f, -.42f },
            (PlayerWrestlingType.Worker, "style_003") => new[] { .709677f, .064516f, 2f, -1.870968f, -1.870968f, -1.225806f, -1.225806f, -1.225806f, .709677f, -.580645f },
            (PlayerWrestlingType.Worker, "style_004") => new[] { .28f, -.72f, .28f, -2.72f, -3.72f, 1.28f, .28f, -1.72f, .28f, .28f },
            (PlayerWrestlingType.Worker, "style_005") => new[] { .113208f, .113208f, 2f, -2.716981f, -3.660377f, .113208f, .113208f, -1.773585f, .113208f, -.830189f },
            (PlayerWrestlingType.Worker, "style_006") => new[] { .62f, -1.38f, -1.38f, -.38f, 1.62f, -4.38f, -1.38f, -.38f, 1.62f, -1.38f },
            (PlayerWrestlingType.Worker, _) => new[] { 1.2f, .2f, 1.2f, -.8f, -.8f, -.8f, -.8f, -.8f, 1.2f, .2f },
            (PlayerWrestlingType.Showman, "style_001") => new[] { -.86f, -.86f, -2.86f, 1.14f, .14f, -1.86f, 1.14f, 1.14f, .14f, -.86f },
            (PlayerWrestlingType.Showman, "style_002") => new[] { -.84f, -.84f, -2.84f, -.84f, 2.16f, -1.84f, 1.16f, .16f, .16f, -.84f },
            (PlayerWrestlingType.Showman, "style_003") => new[] { -.02f, -.02f, .98f, -2.02f, -2.02f, -.02f, .98f, -.02f, -.02f, -1.02f },
            (PlayerWrestlingType.Showman, "style_004") => new[] { -1.28f, -1.28f, -2.28f, -2.28f, -3.28f, 2.72f, 2.72f, -.28f, -1.28f, -.28f },
            (PlayerWrestlingType.Showman, "style_005") => new[] { -1.3f, -.3f, -.3f, -2.3f, -3.3f, 1.7f, 2.7f, -.3f, -1.3f, -1.3f },
            (PlayerWrestlingType.Showman, "style_006") => new[] { -.9f, -1.9f, -3.9f, .1f, 2.1f, -2.9f, 1.1f, 1.1f, .1f, -1.9f },
            (PlayerWrestlingType.Showman, _) => new[] { -.3f, -.3f, -1.3f, -.3f, -.3f, .7f, 1.7f, .7f, -.3f, -.3f },
            (_, "style_001") => new[] { -.58f, -.58f, -1.58f, 1.42f, .42f, -2.58f, -.58f, .42f, .42f, -.58f },
            (_, "style_002") => new[] { -.62f, -.62f, -1.62f, -.62f, 2.38f, -2.62f, -.62f, -.62f, .38f, -.62f },
            (_, "style_003") => new[] { .14f, .14f, 2.14f, -1.86f, -1.86f, -.86f, -.86f, -.86f, .14f, -.86f },
            (_, "style_004") => new[] { -.88f, -.88f, -.88f, -1.88f, -2.88f, 2.12f, 1.12f, -.88f, -.88f, .12f },
            (_, "style_005") => new[] { -.96f, .04f, 1.04f, -1.96f, -2.96f, 1.04f, 1.04f, -.96f, -.96f, -.96f },
            (_, "style_006") => new[] { -.66f, -1.66f, -2.66f, .34f, 2.34f, -3.66f, -.66f, .34f, .34f, -1.66f },
            _ => new float[10]
        };

        private static float[] PromoModifiers(PlayerWrestlingType type) => type switch
        {
            PlayerWrestlingType.Worker => new[] { -.142857f, -.142857f, .857143f, -.142857f, -.142857f, -.142857f, -.142857f },
            PlayerWrestlingType.Showman => new[] { 1.185714f, .185714f, -.814286f, 1.185714f, -.814286f, -.814286f, .185714f },
            _ => new float[7]
        };
    }
}
