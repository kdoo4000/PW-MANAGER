using System.Collections.Generic;
using PWManager.Data.Definitions;
using UnityEngine;

namespace PWManager.Data.Catalogs
{
    [CreateAssetMenu(menuName = "PW Manager/Static/Content Catalog")]
    public sealed class StaticContentCatalog : ScriptableObject
    {
        public NamePoolDefinition NamePool;
        public WrestlerGenerationConfig WrestlerGeneration;
        public List<WrestlingStyleDefinition> WrestlingStyles = new();
        public List<TraitDefinition> Traits = new();
        public List<MoveDefinition> Moves = new();
        public List<VenueDefinition> Venues = new();
        public List<StaffDepartmentDefinition> StaffDepartments = new();
        public List<MedicalTeamLevelDefinition> MedicalTeamLevels = new();
        public List<ScoutTeamLevelDefinition> ScoutTeamLevels = new();
        public List<PromotionTeamLevelDefinition> PromotionTeamLevels = new();
        public List<CommentaryTeamLevelDefinition> CommentaryTeamLevels = new();
        public List<MatchTypeDefinition> MatchTypes = new();
        public List<MatchGimmickDefinition> MatchGimmicks = new();
    }
}
