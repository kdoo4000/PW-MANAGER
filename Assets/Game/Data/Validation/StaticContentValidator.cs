using System;
using System.Collections.Generic;
using PWManager.Data.Catalogs;
using PWManager.Data.Definitions;
using PWManager.Domain.Identifiers;

namespace PWManager.Data.Validation
{
    public static class StaticContentValidator
    {
        public static List<string> Validate(StaticContentCatalog catalog)
        {
            var errors = new List<string>();
            if (catalog == null) { errors.Add("Static content catalog is required."); return errors; }
            ValidateNamePool(catalog.NamePool, errors);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateDefinitions(catalog.WrestlingStyles, "style_", ids, errors);
            ValidateDefinitions(catalog.Traits, "trait_", ids, errors);
            ValidateDefinitions(catalog.Moves, "move_", ids, errors);
            ValidateDefinitions(catalog.Venues, "venue_", ids, errors);
            ValidateDefinitions(catalog.StaffDepartments, "staff_", ids, errors);
            ValidateDefinitions(catalog.MatchTypes, "matchtype_", ids, errors);
            ValidateDefinitions(catalog.MatchGimmicks, "gimmick_", ids, errors);

            foreach (var style in catalog.WrestlingStyles ?? new())
            {
                if (style == null) continue;
                var sum = style.BrawlingWeight + style.PowerWeight + style.HighFlyingWeight + style.TechnicalWeight;
                if (float.IsNaN(sum) || float.IsInfinity(sum) || Math.Abs(sum - 1f) > 0.001f) errors.Add($"Style weights must be finite and total 1.0: {style.Id}");
            }
            foreach (var move in catalog.Moves ?? new())
            {
                if (move == null) continue;
                var sum = move.BrawlingWeight + move.PowerWeight + move.HighFlyingWeight + move.TechnicalWeight;
                if (float.IsNaN(sum) || float.IsInfinity(sum) || Math.Abs(sum - 1f) > 0.001f) errors.Add($"Move weights must be finite and total 1.0: {move.Id}");
                if (move.ExecutionDifficulty < 1 || move.ExecutionDifficulty > 20) errors.Add($"Move execution difficulty must be 1-20: {move.Id}");
                if (!Enum.IsDefined(typeof(MoveSellingDifficulty), move.SellingDifficulty))
                    errors.Add($"Move selling difficulty must be 7, 10, 13, or 16: {move.Id}");
                if (move.RequiredStat == MoveRequiredStat.None && move.RequiredStatValue != 0)
                    errors.Add($"Move without a required stat must use required value 0: {move.Id}");
                if (move.RequiredStat != MoveRequiredStat.None && (move.RequiredStatValue < 1 || move.RequiredStatValue > 20))
                    errors.Add($"Move required stat value must be 1-20: {move.Id}");
            }
            foreach (var venue in catalog.Venues ?? new())
            {
                if (venue == null) continue;
                if (venue.Capacity < 1) errors.Add($"Venue capacity must be positive: {venue.Id}");
                if (venue.RequiredPrestige < 0 || venue.UnlockCost < 0 || venue.ProductionCost < 0)
                    errors.Add($"Venue costs and prestige must be non-negative: {venue.Id}");
                if (venue.BaseTicketPrice < 1) errors.Add($"Venue base ticket price must be positive: {venue.Id}");
                if (!EntityId.IsValidStaticId(venue.RegionId) ||
                    !venue.RegionId.StartsWith("region_", StringComparison.Ordinal))
                    errors.Add($"Venue requires a valid region ID: {venue.Id}");
            }
            foreach (var matchType in catalog.MatchTypes ?? new())
            {
                if (matchType == null) continue;
                if (matchType.MinimumParticipants < 2 || matchType.MaximumParticipants < matchType.MinimumParticipants)
                    errors.Add($"Match type participant range is invalid: {matchType.Id}");
                var hasTeamRules = matchType.MinimumTeamCount > 0 || matchType.MaximumTeamCount > 0 ||
                    matchType.MinimumMembersPerTeam > 0 || matchType.MaximumMembersPerTeam > 0;
                if (hasTeamRules && (matchType.MinimumTeamCount < 2 || matchType.MaximumTeamCount < matchType.MinimumTeamCount ||
                    matchType.MinimumMembersPerTeam < 2 || matchType.MaximumMembersPerTeam < matchType.MinimumMembersPerTeam ||
                    matchType.MinimumTeamCount * matchType.MinimumMembersPerTeam < matchType.MinimumParticipants))
                    errors.Add($"Match type team rules are invalid: {matchType.Id}");
                if (float.IsNaN(matchType.ConditionCostMultiplier) || float.IsInfinity(matchType.ConditionCostMultiplier) || matchType.ConditionCostMultiplier <= 0f)
                    errors.Add($"Match type condition cost multiplier must be positive: {matchType.Id}");
            }
            var matchTypeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var matchType in catalog.MatchTypes ?? new()) if (matchType != null) matchTypeIds.Add(matchType.Id);
            foreach (var gimmick in catalog.MatchGimmicks ?? new())
            {
                if (gimmick == null) continue;
                if (gimmick.MinimumParticipants < 2 || gimmick.MaximumParticipants < gimmick.MinimumParticipants)
                    errors.Add($"Match gimmick participant range is invalid: {gimmick.Id}");
                if (float.IsNaN(gimmick.ConditionCostMultiplier) || float.IsInfinity(gimmick.ConditionCostMultiplier) || gimmick.ConditionCostMultiplier <= 0f)
                    errors.Add($"Match gimmick condition cost multiplier must be positive: {gimmick.Id}");
                if (!Enum.IsDefined(typeof(PWManager.Domain.Services.MatchRuleOverride), gimmick.PinfallRule) ||
                    !Enum.IsDefined(typeof(PWManager.Domain.Services.MatchRuleOverride), gimmick.SubmissionRule) ||
                    !Enum.IsDefined(typeof(PWManager.Domain.Services.MatchRuleOverride), gimmick.DisqualificationRule) ||
                    !Enum.IsDefined(typeof(PWManager.Domain.Services.MatchRuleOverride), gimmick.CountOutRule))
                    errors.Add($"Match gimmick finish rule override is invalid: {gimmick.Id}");
                var specialFinishes = new HashSet<PWManager.Domain.Models.MatchFinishType>();
                foreach (var finish in gimmick.AllowedSpecialFinishTypes ?? new())
                    if (finish is not (PWManager.Domain.Models.MatchFinishType.Escape or PWManager.Domain.Models.MatchFinishType.ObjectRetrieval or PWManager.Domain.Models.MatchFinishType.TableBreak) ||
                        !specialFinishes.Add(finish))
                        errors.Add($"Match gimmick special finish is invalid or duplicated: {gimmick.Id}");
                if (gimmick.CompatibleMatchTypeIds == null || gimmick.CompatibleMatchTypeIds.Count == 0)
                    errors.Add($"Match gimmick requires compatible match types: {gimmick.Id}");
                else foreach (var matchTypeId in gimmick.CompatibleMatchTypeIds)
                    if (!matchTypeIds.Contains(matchTypeId)) errors.Add($"Match gimmick references missing match type {matchTypeId}: {gimmick.Id}");
            }

            var traitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trait in catalog.Traits ?? new()) if (trait != null) traitIds.Add(trait.Id);
            foreach (var trait in catalog.Traits ?? new())
            foreach (var conflictId in trait?.ConflictingTraitIds ?? new())
                if (!traitIds.Contains(conflictId)) errors.Add($"Trait {trait.Id} references missing conflict {conflictId}.");
            return errors;
        }

        private static void ValidateNamePool(NamePoolDefinition names, List<string> errors)
        {
            if (names == null) { errors.Add("Name pool is required."); return; }
            ValidateNames(names.MaleGivenNames, "male given names", errors);
            ValidateNames(names.FemaleGivenNames, "female given names", errors);
            ValidateNames(names.FamilyNames, "family names", errors);
            ValidateNames(names.Nicknames, "nicknames", errors);
            ValidateNames(names.SingleWordRingNames, "single-word ring names", errors);
        }

        private static void ValidateNames(IEnumerable<string> values, string name, List<string> errors)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var count = 0;
            foreach (var value in values ?? Array.Empty<string>())
            {
                count++;
                if (string.IsNullOrWhiteSpace(value)) errors.Add($"Name pool contains an empty {name} entry.");
                else if (!seen.Add(value.Trim())) errors.Add($"Name pool contains duplicate {name}: {value}");
            }
            if (count == 0) errors.Add($"Name pool requires at least one {name} entry.");
        }

        private static void ValidateDefinitions<T>(IEnumerable<T> definitions, string prefix, HashSet<string> ids, List<string> errors)
            where T : StaticDefinition
        {
            foreach (var definition in definitions ?? Array.Empty<T>())
            {
                if (definition == null) { errors.Add($"{typeof(T).Name} cannot be null."); continue; }
                if (!EntityId.IsValidStaticId(definition.Id) || !definition.Id.StartsWith(prefix, StringComparison.Ordinal))
                    errors.Add($"Invalid static ID: {definition.Id}");
                else if (!ids.Add(definition.Id)) errors.Add($"Duplicate static ID: {definition.Id}");
                if (string.IsNullOrWhiteSpace(definition.KoreanName)) errors.Add($"Korean name is required: {definition.Id}");
                if (string.IsNullOrWhiteSpace(definition.EnglishName)) errors.Add($"English name is required: {definition.Id}");
            }
        }
    }
}
