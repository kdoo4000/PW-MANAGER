using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Loading;
using PWManager.Data.Validation;
using UnityEditor;

namespace PWManager.Tests
{
    public sealed class StaticContentCatalogTests
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        [Test]
        public void ConfirmedNotionContent_IsValidAndIndexable()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var errors = StaticContentValidator.Validate(catalog);
            var registry = new StaticContentRegistry(catalog);

            Assert.That(errors, Is.Empty);
            Assert.That(registry.Styles, Has.Count.EqualTo(7));
            Assert.That(registry.Traits, Has.Count.EqualTo(21));
            Assert.That(registry.StaffDepartments, Has.Count.EqualTo(4));
            Assert.That(catalog.NamePool.MaleGivenNames, Has.Count.EqualTo(30));
            Assert.That(catalog.NamePool.FemaleGivenNames, Has.Count.EqualTo(30));
            Assert.That(catalog.NamePool.FamilyNames, Has.Count.EqualTo(50));
            Assert.That(catalog.NamePool.Nicknames, Has.Count.EqualTo(10));
            Assert.That(catalog.NamePool.SingleWordRingNames, Has.Count.EqualTo(10));
            Assert.That(catalog.WrestlerGeneration.UseLegalNameAsRingName, Is.True);
            Assert.That(registry.Moves, Has.Count.EqualTo(24));
            Assert.That(registry.Venues, Has.Count.EqualTo(6));
            Assert.That(registry.MatchTypes, Has.Count.EqualTo(2));
            Assert.That(registry.MatchGimmicks, Has.Count.EqualTo(5));
            Assert.That(registry.MatchTypes["matchtype_001"].DisplayName, Is.EqualTo("Individual"));
            Assert.That(registry.MatchTypes["matchtype_001"].MinimumParticipants, Is.EqualTo(2));
            Assert.That(registry.MatchTypes["matchtype_001"].MaximumParticipants, Is.EqualTo(6));
            Assert.That(registry.MatchTypes["matchtype_002"].MinimumParticipants, Is.EqualTo(4));
            Assert.That(registry.MatchTypes["matchtype_002"].MaximumParticipants, Is.EqualTo(8));
            Assert.That(registry.MatchTypes["matchtype_002"].MinimumTeamCount, Is.EqualTo(2));
            Assert.That(registry.MatchTypes["matchtype_002"].MaximumTeamCount, Is.EqualTo(4));
            Assert.That(registry.MatchTypes["matchtype_002"].MinimumMembersPerTeam, Is.EqualTo(2));
            Assert.That(registry.MatchTypes["matchtype_002"].MaximumMembersPerTeam, Is.EqualTo(4));
            Assert.That(registry.MatchTypes["matchtype_001"].ConditionCostMultiplier, Is.EqualTo(1f));
            Assert.That(registry.MatchTypes["matchtype_002"].ConditionCostMultiplier, Is.EqualTo(.8f));
            Assert.That(registry.MatchGimmicks["gimmick_001"].DisplayName, Is.EqualTo("Steel Cage"));
            Assert.That(registry.MatchGimmicks["gimmick_000"].DisplayName, Is.EqualTo("Standard"));
            Assert.That(registry.MatchGimmicks["gimmick_000"].MaximumParticipants, Is.EqualTo(8));
            Assert.That(registry.MatchGimmicks["gimmick_001"].AllowedSpecialFinishTypes, Does.Contain(PWManager.Domain.Models.MatchFinishType.Escape));
            Assert.That(registry.MatchGimmicks["gimmick_002"].PinfallRule, Is.EqualTo(PWManager.Domain.Services.MatchRuleOverride.Disabled));
            Assert.That(registry.MatchGimmicks["gimmick_002"].AllowedSpecialFinishTypes, Does.Contain(PWManager.Domain.Models.MatchFinishType.ObjectRetrieval));
            Assert.That(registry.MatchGimmicks["gimmick_003"].AllowedSpecialFinishTypes, Does.Contain(PWManager.Domain.Models.MatchFinishType.TableBreak));
            Assert.That(registry.MatchGimmicks["gimmick_004"].DisqualificationRule, Is.EqualTo(PWManager.Domain.Services.MatchRuleOverride.Disabled));
            Assert.That(registry.MatchGimmicks["gimmick_002"].ConditionCostMultiplier, Is.EqualTo(1.30f));
            Assert.That(registry.MatchGimmicks["gimmick_004"].CompatibleMatchTypeIds, Does.Contain("matchtype_002"));
        }

        [Test]
        public void PrototypeVenues_UseConfirmedEconomyValues()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var venues = new StaticContentRegistry(catalog).Venues;

            Assert.That(venues["venue_001"].ProductionCost, Is.EqualTo(500));
            Assert.That(venues["venue_003"].UnlockCost, Is.EqualTo(300000));
            Assert.That(venues["venue_005"].ProductionCost, Is.EqualTo(1000000));
            Assert.That(venues["venue_006"].Capacity, Is.EqualTo(70000));
            Assert.That(venues["venue_006"].ProductionCost, Is.EqualTo(5000000));
            Assert.That(venues["venue_006"].UnlockCost, Is.EqualTo(30000000));
        }

        [Test]
        public void PrototypeMoves_KeepExecutionAndSellingDifficultyIndependent()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var registry = new StaticContentRegistry(catalog);

            Assert.That(registry.Moves["move_015"].HighFlyingWeight, Is.EqualTo(.90f));
            Assert.That(registry.Moves["move_015"].ExecutionDifficulty, Is.EqualTo(18));
            Assert.That(registry.Moves["move_015"].SellingDifficulty, Is.EqualTo(PWManager.Data.Definitions.MoveSellingDifficulty.Easy));
            Assert.That(registry.Moves["move_008"].ExecutionDifficulty, Is.EqualTo(12));
            Assert.That(registry.Moves["move_008"].SellingDifficulty, Is.EqualTo(PWManager.Data.Definitions.MoveSellingDifficulty.VeryHard));
        }

        [Test]
        public void GiantStyle_UsesConfirmedHeightAndWeightRules()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var giant = new StaticContentRegistry(catalog).Styles["style_006"];

            Assert.That(giant.MinimumMaleHeightCm, Is.EqualTo(195));
            Assert.That(giant.MinimumFemaleHeightCm, Is.EqualTo(180));
            Assert.That(giant.PowerWeight, Is.EqualTo(.55f));
        }
    }
}
