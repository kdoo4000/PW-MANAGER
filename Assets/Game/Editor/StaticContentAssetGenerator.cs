using System.Collections.Generic;
using System.IO;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEditor;
using UnityEngine;

namespace PWManager.Editor
{
    public static class StaticContentAssetGenerator
    {
        private const string Root = "Assets/Game/Data/Static";
        private static readonly Dictionary<string, string> EnglishAssetNames = new()
        {
            { "style_001", "Brawler" }, { "style_002", "Powerhouse" }, { "style_003", "Technician" },
            { "style_004", "HighFlyer" }, { "style_005", "LuchaLibre" }, { "style_006", "Giant" }, { "style_007", "AllRounder" },
            { "trait_001", "Fragile" }, { "trait_002", "IronBody" }, { "trait_003", "FastRecovery" },
            { "trait_004", "SlowRecovery" }, { "trait_005", "FastLearner" }, { "trait_006", "HardWorker" },
            { "trait_007", "Lazy" }, { "trait_008", "Wonderkid" }, { "trait_009", "LateBloomer" },
            { "trait_010", "ClutchPerformer" }, { "trait_011", "Choker" }, { "trait_012", "Consistent" },
            { "trait_013", "WildCard" }, { "trait_014", "TagSpecialist" }, { "trait_015", "SinglesSpecialist" },
            { "trait_016", "Rebel" }, { "trait_017", "Professional" }, { "trait_018", "Leadership" },
            { "trait_019", "RoleModel" }, { "trait_020", "Troublemaker" }, { "trait_021", "Loyal" },
            { "staff_001", "MedicalTeam" }, { "staff_002", "ScoutTeam" },
            { "staff_003", "PromotionTeam" }, { "staff_004", "CommentaryTeam" },
            { "venue_001", "Studio" }, { "venue_002", "CommunityGym" },
            { "venue_003", "SmallArena" }, { "venue_004", "MediumArena" },
            { "venue_005", "LargeArena" }, { "venue_006", "Stadium" },
            { "matchtype_001", "Individual" }, { "matchtype_002", "TagTeam" },
            { "gimmick_001", "SteelCage" }, { "gimmick_002", "Ladder" },
            { "gimmick_003", "Tables" }, { "gimmick_004", "Hardcore" }
        };

        private static readonly Dictionary<string, string> KoreanNames = new()
        {
            { "matchtype_001", "개인전" }, { "matchtype_002", "태그팀 경기" },
            { "gimmick_000", "기본 경기" }, { "gimmick_001", "스틸 케이지" },
            { "gimmick_002", "래더 매치" }, { "gimmick_003", "테이블 매치" }, { "gimmick_004", "하드코어" },
            { "venue_001", "스튜디오" }, { "venue_002", "커뮤니티 체육관" },
            { "venue_003", "소형 아레나" }, { "venue_004", "중형 아레나" },
            { "venue_005", "대형 아레나" }, { "venue_006", "스타디움" },
            { "move_001", "러닝 니 스트라이크" }, { "move_002", "디스커스 래리어트" },
            { "move_003", "스피닝 백피스트" }, { "move_004", "스피어" },
            { "move_005", "싯아웃 파워밤" }, { "move_006", "초크슬램" },
            { "move_007", "스파인버스터" }, { "move_008", "파일드라이버" },
            { "move_009", "저먼 수플렉스" }, { "move_010", "드래곤 수플렉스" },
            { "move_011", "크로스페이스" }, { "move_012", "앵클 락" },
            { "move_013", "프로그 스플래시" }, { "move_014", "문설트" },
            { "move_015", "슈팅 스타 프레스" }, { "move_016", "450 스플래시" },
            { "move_017", "스프링보드 커터" }, { "move_018", "다이빙 엘보 드롭" },
            { "move_019", "토네이도 DDT" }, { "move_020", "슈퍼킥" },
            { "move_021", "바이시클 킥" }, { "move_022", "데스 밸리 드라이버" },
            { "move_023", "브리징 수플렉스" }, { "move_024", "코크스크루 센턴" }
        };

        private static readonly Dictionary<string, string> EnglishDisplayNames = new()
        {
            { "style_004", "High Flyer" }, { "style_005", "Lucha Libre" }, { "style_007", "All-Rounder" },
            { "trait_002", "Iron Body" }, { "trait_003", "Fast Recovery" }, { "trait_004", "Slow Recovery" },
            { "trait_005", "Fast Learner" }, { "trait_006", "Hard Worker" }, { "trait_009", "Late Bloomer" },
            { "trait_010", "Clutch Performer" }, { "trait_013", "Wild Card" }, { "trait_014", "Tag Specialist" },
            { "trait_015", "Singles Specialist" }, { "trait_019", "Role Model" },
            { "staff_001", "Medical Team" }, { "staff_002", "Scouting Team" },
            { "staff_003", "Promotion Team" }, { "staff_004", "Commentary Team" },
            { "venue_002", "Community Gym" }, { "venue_003", "Small Arena" },
            { "venue_004", "Medium Arena" }, { "venue_005", "Large Arena" },
            { "matchtype_002", "Tag Team" }, { "gimmick_001", "Steel Cage" }
        };

        [MenuItem("PW Manager/Generate Confirmed Static Content")]
        public static void Generate()
        {
            Directory.CreateDirectory(Root + "/Wrestlers/Styles");
            Directory.CreateDirectory(Root + "/Wrestlers/Traits");
            Directory.CreateDirectory(Root + "/Wrestlers/Moves");
            Directory.CreateDirectory(Root + "/Wrestlers/Names");
            Directory.CreateDirectory(Root + "/Venues");
            Directory.CreateDirectory(Root + "/Staff");
            AssetDatabase.DeleteAsset(Root + "/Staff/Levels");
            Directory.CreateDirectory(Root + "/Staff/MedicalTeam");
            Directory.CreateDirectory(Root + "/Staff/ScoutTeam");
            Directory.CreateDirectory(Root + "/Staff/PromotionTeam");
            Directory.CreateDirectory(Root + "/Staff/CommentaryTeam");
            Directory.CreateDirectory(Root + "/MatchTypes");
            Directory.CreateDirectory(Root + "/MatchGimmicks");
            Directory.CreateDirectory("Assets/Game/Data/Balance");

            var styles = CreateStyles();
            var traits = CreateTraits();
            var moves = CreateMoves();
            var venues = CreateVenues();
            var staff = CreateStaff();
            var medicalLevels = CreateMedicalLevels();
            var scoutLevels = CreateScoutLevels();
            var promotionLevels = CreatePromotionLevels();
            var commentaryLevels = CreateCommentaryLevels();
            var matchTypes = CreateMatchTypes();
            var matchGimmicks = CreateMatchGimmicks();
            var catalog = LoadOrCreate<StaticContentCatalog>(Root + "/GameStaticContentCatalog.asset");
            catalog.NamePool = LoadOrCreate<NamePoolDefinition>(Root + "/Wrestlers/Names/NamePool.asset");
            PopulatePrototypeNames(catalog.NamePool);
            catalog.WrestlerGeneration = LoadOrCreate<WrestlerGenerationConfig>("Assets/Game/Data/Balance/WrestlerGenerationConfig.asset");
            catalog.WrestlingStyles = styles;
            catalog.Traits = traits;
            catalog.Moves = moves;
            catalog.StaffDepartments = staff;
            catalog.MedicalTeamLevels = medicalLevels;
            catalog.ScoutTeamLevels = scoutLevels;
            catalog.PromotionTeamLevels = promotionLevels;
            catalog.CommentaryTeamLevels = commentaryLevels;
            catalog.MatchTypes = matchTypes;
            catalog.MatchGimmicks = matchGimmicks;
            catalog.Venues = venues;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Generated confirmed static content: {styles.Count} styles, {traits.Count} traits, {moves.Count} moves, {venues.Count} venues, {staff.Count} staff departments, {medicalLevels.Count + scoutLevels.Count + promotionLevels.Count + commentaryLevels.Count} team levels, {matchTypes.Count} match types, {matchGimmicks.Count} match gimmicks.");
        }

        private static void PopulatePrototypeNames(NamePoolDefinition names)
        {
            AddMissing(names.MaleGivenNames, new[]
            {
                "Adrian", "Blake", "Caleb", "Damon", "Elias", "Felix",
                "Grant", "Jace", "Logan", "Marcus", "Nolan", "Victor",
                "Aaron", "Brendan", "Cedric", "Desmond", "Evan", "Gavin",
                "Isaac", "Jonah", "Leon", "Miles", "Oscar", "Preston",
                "Roman", "Silas", "Tristan", "Wesley", "Xavier", "Zane"
            });
            AddMissing(names.FemaleGivenNames, new[]
            {
                "Aria", "Bianca", "Carmen", "Dahlia", "Elena", "Freya",
                "Iris", "Jade", "Maya", "Nina", "Sierra", "Talia",
                "Amara", "Brielle", "Celeste", "Delilah", "Eva", "Georgia",
                "Hazel", "Kiara", "Lana", "Marina", "Naomi", "Ophelia",
                "Phoebe", "Raina", "Selene", "Vera", "Willow", "Zara"
            });
            AddMissing(names.FamilyNames, new[]
            {
                "Archer", "Bennett", "Cole", "Cross", "Dalton", "Everett", "Frost", "Graves", "Hartley", "Hayes",
                "Knight", "Mercer", "Monroe", "Palmer", "Quinn", "Reed", "Sawyer", "Sterling", "Vale", "Wolfe",
                "Abbott", "Barrett", "Calloway", "Donovan", "Ellis", "Fairchild", "Gallagher", "Hawthorne", "Irving", "Jennings",
                "Kendrick", "Langley", "Maddox", "Nash", "Ortega", "Prescott", "Ramsey", "Shepherd", "Thornton", "Underwood",
                "Vaughn", "Walker", "York", "Zimmerman", "Blackwood", "Carver", "Drake", "Emerson", "Ford", "Holland"
            });
            AddMissing(names.KoreanMaleGivenNames, new[] { "Min-jun", "Seo-jun", "Ji-hoon" });
            AddMissing(names.KoreanFemaleGivenNames, new[] { "Seo-yeon", "Ji-woo", "Ha-eun" });
            AddMissing(names.KoreanFamilyNames, new[] { "Kim", "Lee", "Park" });
            AddMissing(names.JapaneseMaleGivenNames, new[] { "Haruto", "Ren", "Kaito" });
            AddMissing(names.JapaneseFemaleGivenNames, new[] { "Yui", "Aoi", "Sakura" });
            AddMissing(names.JapaneseFamilyNames, new[] { "Sato", "Suzuki", "Tanaka" });
            EditorUtility.SetDirty(names);
        }

        private static void AddMissing(ICollection<string> target, IEnumerable<string> values)
        {
            var existing = new HashSet<string>(target, System.StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
                if (existing.Add(value)) target.Add(value);
        }

        private static List<WrestlingStyleDefinition> CreateStyles() => new()
        {
            Style("style_001", "브롤러", .55f, .20f, .10f, .15f, .20f, 0, 0, 0),
            Style("style_002", "파워하우스", .15f, .55f, .05f, .25f, 0, .20f, 0, 0),
            Style("style_003", "테크니션", .10f, .15f, .20f, .55f, 0, 0, 0, .20f),
            Style("style_004", "하이플라이어", .10f, .05f, .55f, .30f, 0, 0, .20f, 0),
            Style("style_005", "루차 리브레", .05f, .05f, .50f, .40f, 0, 0, .10f, .10f),
            Style("style_006", "자이언트", .30f, .55f, 0, .15f, .08f, .12f, 0, 0, 195, 180),
            Style("style_007", "올라운더", .25f, .25f, .25f, .25f, .05f, .05f, .05f, .05f)
        };

        private static List<TraitDefinition> CreateTraits()
        {
            var values = new List<TraitDefinition>
            {
                Trait("trait_001", "유리몸", TraitPolarity.Negative, "경기 중 부상 발생 확률 20% 증가"),
                Trait("trait_002", "강철몸", TraitPolarity.Positive, "경기 중 부상 발생 확률 20% 감소"),
                Trait("trait_003", "빠른 회복", TraitPolarity.Positive, "체력 및 부상 회복 기간 25% 감소"),
                Trait("trait_004", "느린 회복", TraitPolarity.Negative, "체력 및 부상 회복 기간 25% 증가"),
                Trait("trait_005", "빠른 학습", TraitPolarity.Positive, "모든 경험치 획득량 20% 증가"),
                Trait("trait_006", "노력파", TraitPolarity.Mixed, "훈련 경험치와 훈련 체력 소모량 20% 증가"),
                Trait("trait_007", "게으름", TraitPolarity.Mixed, "훈련 경험치와 훈련 체력 소모량 20% 감소"),
                Trait("trait_008", "원더키드", TraitPolarity.Mixed, "젊은 시기 성장 가속, 30대 이후 성장률 감소와 빠른 노화"),
                Trait("trait_009", "늦게 핀 꽃", TraitPolarity.Mixed, "젊은 시기 성장 둔화, 30대 이후 성장 가능 및 노화 능력 감소 없음"),
                Trait("trait_010", "강심장", TraitPolarity.Positive, "중요 경기에서 유효 경기 능력 +1.0"),
                Trait("trait_011", "새가슴", TraitPolarity.Negative, "중요 경기에서 유효 경기 능력 -1.0"),
                Trait("trait_012", "꾸준함", TraitPolarity.Positive, "경기·프로모 개인 수행 편차를 -0.25~+0.25로 축소"),
                Trait("trait_013", "주사위", TraitPolarity.Mixed, "경기·프로모 개인 수행 편차를 -1.0~+1.0으로 확대"),
                Trait("trait_014", "태그 선호", TraitPolarity.Mixed, "태그 +0.5, 싱글 -0.5"),
                Trait("trait_015", "싱글 선호", TraitPolarity.Mixed, "싱글 +0.5, 태그 -0.5"),
                Trait("trait_016", "반항아", TraitPolarity.Negative, "불리한 지시에 따른 불만 증가량 20% 상승"),
                Trait("trait_017", "프로페셔널", TraitPolarity.Positive, "업무상 불리한 지시로 인한 불만 증가 방지"),
                Trait("trait_018", "리더십", TraitPolarity.Mixed, "만족도와 라커룸 사건이 주변 선수에게 영향"),
                Trait("trait_019", "모범생", TraitPolarity.Positive, "부정적 라커룸 이벤트 발생 확률 감소"),
                Trait("trait_020", "문제아", TraitPolarity.Negative, "부정적 라커룸 이벤트 발생 확률 증가"),
                Trait("trait_021", "충성심", TraitPolarity.Positive, "비용 지급 가능 시 재계약 의향 자동 성공")
            };
            Conflict(values, 1, 2); Conflict(values, 3, 4); Conflict(values, 6, 7); Conflict(values, 8, 9);
            Conflict(values, 10, 11); Conflict(values, 12, 13); Conflict(values, 14, 15); Conflict(values, 16, 17); Conflict(values, 19, 20);
            return values;
        }

        private static List<StaffDepartmentDefinition> CreateStaff() => new()
        {
            Staff("staff_001", "의료팀", StaffDepartmentType.Medical, 1),
            Staff("staff_002", "스카우트팀", StaffDepartmentType.Scout, 1),
            Staff("staff_003", "홍보팀", StaffDepartmentType.Promotion, 1),
            Staff("staff_004", "해설진", StaffDepartmentType.Commentary, 0)
        };

        private static List<MedicalTeamLevelDefinition> CreateMedicalLevels() => new()
        {
            MedicalLevel(1, 0, 0, "기본 부상 회복 및 악화 방지", 1f, 1f), MedicalLevel(2, 300, 10_000, "회복 기간 5% 감소 · 악화 확률 10% 감소", .95f, .90f),
            MedicalLevel(3, 2_500, 50_000, "회복 기간 10% 감소 · 악화 확률 20% 감소", .90f, .80f), MedicalLevel(4, 5_500, 250_000, "회복 기간 15% 감소 · 악화 확률 30% 감소", .85f, .70f),
            MedicalLevel(5, 20_000, 1_000_000, "회복 기간 20% 감소 · 악화 확률 40% 감소", .80f, .60f)
        };

        private static List<ScoutTeamLevelDefinition> CreateScoutLevels() => new()
        {
            ScoutLevel(1, 0, 0, "후보 6명 · 종합 능력 추정", 6, 1, 0f, 2f, .05f), ScoutLevel(2, 300, 10_000, "후보 8명 · 성장 2단계 추정", 8, 2, 0f, 1.5f, .10f),
            ScoutLevel(3, 2_500, 50_000, "후보 10명 · 등급 정확도 60%", 10, 3, .60f, 1f, .15f), ScoutLevel(4, 5_500, 250_000, "후보 12명 · 등급 정확도 85%", 12, 4, .85f, .5f, .25f),
            ScoutLevel(5, 20_000, 1_000_000, "후보 15명 · 모든 정보 정확히 공개", 15, 4, 1f, 0, .35f)
        };

        private static List<PromotionTeamLevelDefinition> CreatePromotionLevels() => new()
        {
            PromotionLevel(1, 0, 0, "기본 티켓 수요와 팬 증가", 1f, 1f), PromotionLevel(2, 300, 10_000, "티켓 수요 5% · 팬 증가 3% 향상", 1.05f, 1.03f),
            PromotionLevel(3, 2_500, 50_000, "티켓 수요 10% · 팬 증가 6% 향상", 1.10f, 1.06f), PromotionLevel(4, 5_500, 250_000, "티켓 수요 15% · 팬 증가 9% 향상", 1.15f, 1.09f),
            PromotionLevel(5, 20_000, 1_000_000, "티켓 수요 20% · 팬 증가 12% 향상", 1.20f, 1.12f)
        };

        private static List<CommentaryTeamLevelDefinition> CreateCommentaryLevels() => new()
        {
            CommentaryLevel(1, 1_000, 25_000, "현장 반응 2% · 방송 반응 3% 향상", 1.02f, 1.03f), CommentaryLevel(2, 2_500, 100_000, "현장 반응 4% · 방송 반응 6% 향상", 1.04f, 1.06f),
            CommentaryLevel(3, 11_000, 500_000, "현장 반응 6% · 방송 반응 9% 향상", 1.06f, 1.09f), CommentaryLevel(4, 20_000, 2_000_000, "현장 반응 8% · 방송 반응 12% 향상", 1.08f, 1.12f),
            CommentaryLevel(5, 34_000, 8_000_000, "현장 반응 10% · 방송 반응 15% 향상", 1.10f, 1.15f)
        };

        private static List<MatchTypeDefinition> CreateMatchTypes() => new()
        {
            MatchType("matchtype_001", "Individual", 2, 6, 0, 0, 0, 0, 1f),
            MatchType("matchtype_002", "Tag Team", 4, 8, 2, 4, 2, 4, .8f)
        };

        private static MatchTypeDefinition MatchType(string id, string name, int minimum, int maximum,
            int minimumTeamCount, int maximumTeamCount, int minimumMembersPerTeam, int maximumMembersPerTeam,
            float conditionCostMultiplier)
        {
            if (id == "matchtype_001")
            {
                const string oldPath = Root + "/MatchTypes/matchtype_001_Singles.asset";
                const string newPath = Root + "/MatchTypes/matchtype_001_Individual.asset";
                if (AssetDatabase.LoadAssetAtPath<MatchTypeDefinition>(newPath) == null &&
                    AssetDatabase.LoadAssetAtPath<MatchTypeDefinition>(oldPath) != null)
                {
                    var error = AssetDatabase.MoveAsset(oldPath, newPath);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                }
                AssetDatabase.DeleteAsset(Root + "/MatchTypes/matchtype_003_TripleThreat.asset");
                AssetDatabase.DeleteAsset(Root + "/MatchTypes/matchtype_003_NWay.asset");
                AssetDatabase.DeleteAsset(Root + "/MatchTypes/matchtype_004_MultiPerson.asset");
            }
            var value = Definition<MatchTypeDefinition>(Root + "/MatchTypes/", id, name);
            value.MinimumParticipants = minimum;
            value.MaximumParticipants = maximum;
            value.MinimumTeamCount = minimumTeamCount;
            value.MaximumTeamCount = maximumTeamCount;
            value.MinimumMembersPerTeam = minimumMembersPerTeam;
            value.MaximumMembersPerTeam = maximumMembersPerTeam;
            value.ConditionCostMultiplier = conditionCostMultiplier;
            EditorUtility.SetDirty(value);
            return value;
        }

        private static List<MatchGimmickDefinition> CreateMatchGimmicks() => new()
        {
            MatchGimmick("gimmick_000", "Standard", 1.00f, 8,
                MatchRuleOverride.Default, MatchRuleOverride.Default, MatchRuleOverride.Default, MatchRuleOverride.Default),
            MatchGimmick("gimmick_001", "Steel Cage", 1.20f, 6,
                MatchRuleOverride.Allowed, MatchRuleOverride.Allowed, MatchRuleOverride.Disabled, MatchRuleOverride.Disabled,
                MatchFinishType.Escape),
            MatchGimmick("gimmick_002", "Ladder", 1.30f, 6,
                MatchRuleOverride.Disabled, MatchRuleOverride.Disabled, MatchRuleOverride.Disabled, MatchRuleOverride.Disabled,
                MatchFinishType.ObjectRetrieval),
            MatchGimmick("gimmick_003", "Tables", 1.15f, 6,
                MatchRuleOverride.Disabled, MatchRuleOverride.Disabled, MatchRuleOverride.Disabled, MatchRuleOverride.Disabled,
                MatchFinishType.TableBreak),
            MatchGimmick("gimmick_004", "Hardcore", 1.25f, 6,
                MatchRuleOverride.Allowed, MatchRuleOverride.Allowed, MatchRuleOverride.Disabled, MatchRuleOverride.Disabled)
        };

        private static MatchGimmickDefinition MatchGimmick(string id, string name, float conditionCostMultiplier,
            int maximumParticipants, MatchRuleOverride pinfallRule, MatchRuleOverride submissionRule,
            MatchRuleOverride disqualificationRule, MatchRuleOverride countOutRule,
            params MatchFinishType[] specialFinishTypes)
        {
            var value = Definition<MatchGimmickDefinition>(Root + "/MatchGimmicks/", id, name);
            value.MinimumParticipants = 2;
            value.MaximumParticipants = maximumParticipants;
            value.CompatibleMatchTypeIds = new List<string> { "matchtype_001", "matchtype_002" };
            value.ConditionCostMultiplier = conditionCostMultiplier;
            value.PinfallRule = pinfallRule;
            value.SubmissionRule = submissionRule;
            value.DisqualificationRule = disqualificationRule;
            value.CountOutRule = countOutRule;
            value.AllowedSpecialFinishTypes = new List<MatchFinishType>(specialFinishTypes);
            EditorUtility.SetDirty(value);
            return value;
        }

        private static List<VenueDefinition> CreateVenues() => new()
        {
            Venue("venue_001", "Studio", VenueScale.Studio, 100, 0, 0, 250, 20),
            Venue("venue_002", "Community Gym", VenueScale.CommunityGym, 500, 0, 0, 2000, 30),
            Venue("venue_003", "Small Arena", VenueScale.SmallArena, 3000, 1000, 300000, 20000, 50),
            Venue("venue_004", "Medium Arena", VenueScale.MediumArena, 8000, 5500, 1500000, 80000, 75),
            Venue("venue_005", "Large Arena", VenueScale.LargeArena, 20000, 20000, 6000000, 250000, 110),
            Venue("venue_006", "Stadium", VenueScale.Stadium, 70000, 55000, 30000000, 1000000, 150)
        };

        private static List<MoveDefinition> CreateMoves() => new()
        {
            Move("move_001", "Running Knee Strike", .70f, 0, .30f, 0, 11, MoveSellingDifficulty.Easy),
            Move("move_002", "Discus Lariat", .80f, .20f, 0, 0, 10, MoveSellingDifficulty.Easy),
            Move("move_003", "Spinning Backfist", .85f, 0, .15f, 0, 10, MoveSellingDifficulty.Easy),
            Move("move_004", "Spear", .45f, .55f, 0, 0, 9, MoveSellingDifficulty.Normal),
            Move("move_005", "Sit-Out Powerbomb", 0, .75f, .25f, 0, 13, MoveSellingDifficulty.Normal, MoveRequiredStat.Power, 10),
            Move("move_006", "Chokeslam", .15f, .85f, 0, 0, 11, MoveSellingDifficulty.Easy, MoveRequiredStat.Power, 9),
            Move("move_007", "Spinebuster", 0, .65f, .35f, 0, 10, MoveSellingDifficulty.Easy),
            Move("move_008", "Piledriver", 0, .45f, .55f, 0, 12, MoveSellingDifficulty.VeryHard, MoveRequiredStat.Technical, 11),
            Move("move_009", "German Suplex", 0, .45f, .55f, 0, 12, MoveSellingDifficulty.Normal),
            Move("move_010", "Dragon Suplex", 0, .30f, .70f, 0, 14, MoveSellingDifficulty.Hard, MoveRequiredStat.Technical, 10),
            Move("move_011", "Crossface", .15f, 0, .85f, 0, 12, MoveSellingDifficulty.Easy, MoveRequiredStat.Technical, 9),
            Move("move_012", "Ankle Lock", .10f, 0, .90f, 0, 11, MoveSellingDifficulty.Easy),
            Move("move_013", "Frog Splash", 0, 0, .20f, .80f, 13, MoveSellingDifficulty.Easy, MoveRequiredStat.HighFlying, 9),
            Move("move_014", "Moonsault", 0, 0, .15f, .85f, 15, MoveSellingDifficulty.Easy, MoveRequiredStat.HighFlying, 11),
            Move("move_015", "Shooting Star Press", 0, 0, .10f, .90f, 18, MoveSellingDifficulty.Easy, MoveRequiredStat.HighFlying, 14),
            Move("move_016", "450 Splash", 0, 0, .10f, .90f, 17, MoveSellingDifficulty.Easy, MoveRequiredStat.HighFlying, 13),
            Move("move_017", "Springboard Cutter", 0, 0, .40f, .60f, 15, MoveSellingDifficulty.Normal, MoveRequiredStat.HighFlying, 11),
            Move("move_018", "Diving Elbow Drop", .45f, 0, 0, .55f, 12, MoveSellingDifficulty.Easy),
            Move("move_019", "Tornado DDT", 0, 0, .55f, .45f, 13, MoveSellingDifficulty.Normal),
            Move("move_020", "Superkick", .60f, 0, .40f, 0, 11, MoveSellingDifficulty.Easy),
            Move("move_021", "Bicycle Kick", .65f, .35f, 0, 0, 12, MoveSellingDifficulty.Easy),
            Move("move_022", "Death Valley Driver", 0, .50f, .50f, 0, 10, MoveSellingDifficulty.Hard, MoveRequiredStat.Power, 10),
            Move("move_023", "Bridging Suplex", 0, .25f, .75f, 0, 13, MoveSellingDifficulty.Normal),
            Move("move_024", "Corkscrew Senton", 0, 0, .25f, .75f, 16, MoveSellingDifficulty.Easy, MoveRequiredStat.HighFlying, 12)
        };

        private static WrestlingStyleDefinition Style(string id, string name, float b, float p, float h, float t,
            float gb, float gp, float gh, float gt, int male = 0, int female = 0)
        {
            var value = Definition<WrestlingStyleDefinition>(Root + "/Wrestlers/Styles/", id, name);
            value.BrawlingWeight = b; value.PowerWeight = p; value.HighFlyingWeight = h; value.TechnicalWeight = t;
            value.BrawlingGrowthBonus = gb; value.PowerGrowthBonus = gp; value.HighFlyingGrowthBonus = gh; value.TechnicalGrowthBonus = gt;
            value.MinimumMaleHeightCm = male; value.MinimumFemaleHeightCm = female; EditorUtility.SetDirty(value); return value;
        }

        private static TraitDefinition Trait(string id, string name, TraitPolarity polarity, string description)
        {
            var value = Definition<TraitDefinition>(Root + "/Wrestlers/Traits/", id, name);
            value.Polarity = polarity; value.Description = description; value.ConflictingTraitIds.Clear(); EditorUtility.SetDirty(value); return value;
        }

        private static StaffDepartmentDefinition Staff(string id, string name, StaffDepartmentType type, int start)
        {
            var value = Definition<StaffDepartmentDefinition>(Root + "/Staff/", id, name);
            value.DepartmentType = type; value.StartingLevel = start; EditorUtility.SetDirty(value); return value;
        }

        private static MedicalTeamLevelDefinition MedicalLevel(int level, long prestige, long cost, string effect, float recovery, float aggravation)
        { var value = TeamLevel<MedicalTeamLevelDefinition>("MedicalTeam", "medicallevel_", "의료팀", "Medical Team", level, prestige, cost, effect); value.RecoveryDurationMultiplier = recovery; value.InjuryAggravationMultiplier = aggravation; EditorUtility.SetDirty(value); return value; }
        private static ScoutTeamLevelDefinition ScoutLevel(int level, long prestige, long cost, string effect, int candidates, int knowledge, float exact, float error, float highTier)
        { var value = TeamLevel<ScoutTeamLevelDefinition>("ScoutTeam", "scoutlevel_", "스카우트팀", "Scout Team", level, prestige, cost, effect); value.CandidateCount = candidates; value.KnowledgeLevel = knowledge; value.ExactValueChance = exact; value.ValueErrorRange = error; value.HighTierCandidateRate = highTier; EditorUtility.SetDirty(value); return value; }
        private static PromotionTeamLevelDefinition PromotionLevel(int level, long prestige, long cost, string effect, float ticket, float fans)
        { var value = TeamLevel<PromotionTeamLevelDefinition>("PromotionTeam", "promotionlevel_", "홍보팀", "Promotion Team", level, prestige, cost, effect); value.TicketDemandMultiplier = ticket; value.PositiveFanGainMultiplier = fans; EditorUtility.SetDirty(value); return value; }
        private static CommentaryTeamLevelDefinition CommentaryLevel(int level, long prestige, long cost, string effect, float live, float broadcast)
        { var value = TeamLevel<CommentaryTeamLevelDefinition>("CommentaryTeam", "commentarylevel_", "해설진", "Commentary Team", level, prestige, cost, effect); value.LiveAudienceReactionMultiplier = live; value.BroadcastAudienceReactionMultiplier = broadcast; EditorUtility.SetDirty(value); return value; }
        private static T TeamLevel<T>(string folder, string prefix, string korean, string english, int level, long prestige, long cost, string effect) where T : StaffTeamLevelDefinition
        { var id = $"{prefix}{level:000}"; var value = LoadOrCreate<T>($"{Root}/Staff/{folder}/{id}_Lv{level}.asset"); value.SetEditorIdentity(id, $"{korean} Lv.{level}", $"{english} Lv.{level}"); value.Level = level; value.RequiredPrestige = prestige; value.UpgradeCost = cost; value.EffectDescription = effect; return value; }

        private static MoveDefinition Move(string id, string name, float brawling, float power,
            float technical, float highFlying, float execution, MoveSellingDifficulty selling,
            MoveRequiredStat requiredStat = MoveRequiredStat.None, float requiredValue = 0)
        {
            var value = Definition<MoveDefinition>(Root + "/Wrestlers/Moves/", id, name);
            value.BrawlingWeight = brawling;
            value.PowerWeight = power;
            value.TechnicalWeight = technical;
            value.HighFlyingWeight = highFlying;
            value.ExecutionDifficulty = execution;
            value.SellingDifficulty = selling;
            value.RequiredStat = requiredStat;
            value.RequiredStatValue = requiredValue;
            EditorUtility.SetDirty(value);
            return value;
        }

        private static VenueDefinition Venue(string id, string name, VenueScale scale, int capacity,
            long requiredPrestige, long unlockCost, long productionCost, long baseTicketPrice)
        {
            var value = Definition<VenueDefinition>(Root + "/Venues/", id, name);
            value.Scale = scale;
            value.Capacity = capacity;
            value.RequiredPrestige = requiredPrestige;
            value.UnlockCost = unlockCost;
            value.ProductionCost = productionCost;
            value.BaseTicketPrice = baseTicketPrice;
            value.RegionId = "region_001";
            EditorUtility.SetDirty(value);
            return value;
        }

        private static T Definition<T>(string folder, string id, string name) where T : StaticDefinition
        {
            var assetFileName = EnglishAssetNames.TryGetValue(id, out var configuredName)
                ? configuredName
                : System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9]", string.Empty);
            var desiredPath = folder + id + "_" + assetFileName + ".asset";
            var legacyPath = folder + id + ".asset";
            var value = AssetDatabase.LoadAssetAtPath<T>(desiredPath);
            if (value == null)
            {
                value = AssetDatabase.LoadAssetAtPath<T>(legacyPath);
                if (value != null)
                {
                    var error = AssetDatabase.MoveAsset(legacyPath, desiredPath);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                }
                else value = LoadOrCreate<T>(desiredPath);
            }
            var koreanName = KoreanNames.TryGetValue(id, out var configuredKoreanName) ? configuredKoreanName : name;
            var englishName = EnglishDisplayNames.TryGetValue(id, out var configuredEnglishName) ? configuredEnglishName :
                EnglishAssetNames.TryGetValue(id, out var assetEnglishName) ? assetEnglishName : name;
            value.SetEditorIdentity(id, koreanName, englishName);
            return value;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value != null) return value;
            value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); return value;
        }

        private static void Conflict(IReadOnlyList<TraitDefinition> values, int first, int second)
        {
            values[first - 1].ConflictingTraitIds.Add(values[second - 1].Id);
            values[second - 1].ConflictingTraitIds.Add(values[first - 1].Id);
        }
    }
}
