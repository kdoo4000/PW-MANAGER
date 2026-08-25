using System;
using System.Collections.Generic;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Data.Validation;

namespace PWManager.Data.Loading
{
    public sealed class StaticContentRegistry : PWManager.Domain.Services.IMatchTypeRules, PWManager.Domain.Services.IWrestlingStyleRules
    {
        public StaticContentCatalog Catalog { get; }
        public IReadOnlyDictionary<string, WrestlingStyleDefinition> Styles { get; }
        public IReadOnlyDictionary<string, TraitDefinition> Traits { get; }
        public IReadOnlyDictionary<string, MoveDefinition> Moves { get; }
        public IReadOnlyDictionary<string, VenueDefinition> Venues { get; }
        public IReadOnlyDictionary<string, StaffDepartmentDefinition> StaffDepartments { get; }
        public IReadOnlyDictionary<string, MatchTypeDefinition> MatchTypes { get; }
        public IReadOnlyDictionary<string, MatchGimmickDefinition> MatchGimmicks { get; }

        public StaticContentRegistry(StaticContentCatalog catalog)
        {
            var errors = StaticContentValidator.Validate(catalog);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            Catalog = catalog;
            Styles = Index(catalog.WrestlingStyles);
            Traits = Index(catalog.Traits);
            Moves = Index(catalog.Moves);
            Venues = Index(catalog.Venues);
            StaffDepartments = Index(catalog.StaffDepartments);
            MatchTypes = Index(catalog.MatchTypes);
            MatchGimmicks = Index(catalog.MatchGimmicks);
        }

        public bool TryGetParticipantRange(string matchTypeId, out int minimum, out int maximum)
        {
            if (MatchTypes.TryGetValue(matchTypeId, out var definition))
            {
                minimum = definition.MinimumParticipants;
                maximum = definition.MaximumParticipants;
                return true;
            }
            minimum = 0;
            maximum = 0;
            return false;
        }

        public bool TryGetTeamRules(string matchTypeId, out int minimumTeamCount, out int maximumTeamCount,
            out int minimumMembersPerTeam, out int maximumMembersPerTeam)
        {
            if (MatchTypes.TryGetValue(matchTypeId, out var definition))
            {
                minimumTeamCount = definition.MinimumTeamCount;
                maximumTeamCount = definition.MaximumTeamCount;
                minimumMembersPerTeam = definition.MinimumMembersPerTeam;
                maximumMembersPerTeam = definition.MaximumMembersPerTeam;
                return true;
            }
            minimumTeamCount = maximumTeamCount = minimumMembersPerTeam = maximumMembersPerTeam = 0;
            return false;
        }

        public bool TryGetConditionCostMultiplier(string matchTypeId, out float multiplier)
        {
            if (MatchTypes.TryGetValue(matchTypeId, out var definition))
            {
                multiplier = definition.ConditionCostMultiplier;
                return true;
            }
            multiplier = 0f;
            return false;
        }

        public bool TryGetGimmickRules(string gimmickId, out PWManager.Domain.Services.MatchGimmickRules rules)
        {
            if (MatchGimmicks.TryGetValue(gimmickId, out var definition))
            {
                rules = new PWManager.Domain.Services.MatchGimmickRules(
                    definition.MinimumParticipants, definition.MaximumParticipants,
                    definition.CompatibleMatchTypeIds, definition.ConditionCostMultiplier,
                    definition.DisqualificationRule, definition.CountOutRule,
                    definition.PinfallRule, definition.SubmissionRule,
                    definition.AllowedSpecialFinishTypes);
                return true;
            }
            rules = default;
            return false;
        }

        public bool TryGetStyleWeights(string styleId, out PWManager.Domain.Services.WrestlingStyleWeights weights)
        {
            if (Styles.TryGetValue(styleId, out var definition))
            {
                weights = new PWManager.Domain.Services.WrestlingStyleWeights(
                    definition.BrawlingWeight, definition.PowerWeight,
                    definition.HighFlyingWeight, definition.TechnicalWeight);
                return true;
            }
            weights = default;
            return false;
        }

        private static IReadOnlyDictionary<string, T> Index<T>(IEnumerable<T> values) where T : StaticDefinition
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<T>()) result.Add(value.Id, value);
            return result;
        }
    }
}
