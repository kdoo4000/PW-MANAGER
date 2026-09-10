using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Identifiers;
using PWManager.Domain.Models;
using PWManager.Data.Definitions;
using PWManager.Domain.Services;

namespace PWManager.Data.Generation
{
    public sealed class ScoutingService
    {
        private readonly WrestlerGenerator generator;
        private readonly IReadOnlyDictionary<int, ScoutTeamLevelDefinition> levels;
        private readonly Func<string> createId;
        private readonly Random random;

        public ScoutingService(WrestlerGenerator generator, IEnumerable<ScoutTeamLevelDefinition> levels, Func<string> createId = null, Random random = null)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.levels = (levels ?? throw new ArgumentNullException(nameof(levels)))
                .Where(x => x != null).ToDictionary(x => x.Level);
            this.createId = createId ?? EntityId.CreateRuntimeId;
            this.random = random ?? new Random();
        }

        public ScoutAssignmentState Start(GameSave save, ScoutSearchConditions conditions)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (conditions == null) throw new ArgumentNullException(nameof(conditions));
            save.ScoutAssignments ??= new List<ScoutAssignmentState>();
            if (save.ScoutAssignments.Any(x => x?.Status == ScoutAssignmentStatus.InProgress))
                throw new InvalidOperationException("Only one scouting assignment can be active.");
            if (ScoutLevel(save) == 0) throw new InvalidOperationException("An active scout team is required.");

            var assignment = new ScoutAssignmentState
            {
                Id = createId(), SearchConditions = conditions,
                StartDate = save.CurrentDate, CompleteDate = save.CurrentDate.AddDays(28),
                Status = ScoutAssignmentStatus.InProgress
            };
            save.ScoutAssignments.Add(assignment);
            return assignment;
        }

        public IReadOnlyList<ScoutCandidateState> CompleteDue(GameSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.ScoutAssignments ??= new List<ScoutAssignmentState>();
            save.ScoutCandidates ??= new List<ScoutCandidateState>();
            save.Wrestlers ??= new List<WrestlerState>();
            var created = new List<ScoutCandidateState>();
            foreach (var assignment in save.ScoutAssignments.Where(x => x?.Status == ScoutAssignmentStatus.InProgress && x.CompleteDate.CompareTo(save.CurrentDate) <= 0))
            {
                var definition = Level(ScoutLevel(save));
                for (var i = 0; i < definition.CandidateCount; i++)
                {
                    var wrestler = Generate(assignment.SearchConditions, save.CurrentDate, save.Promotion?.PromotionPrestige ?? 0);
                    save.Wrestlers.Add(wrestler);
                    var candidate = new ScoutCandidateState
                    {
                        WrestlerId = wrestler.Id, AssignmentId = assignment.Id,
                        KnowledgeLevel = definition.KnowledgeLevel,
                        ValueEstimates = CreateEstimates(wrestler, definition)
                    };
                    save.ScoutCandidates.Add(candidate);
                    created.Add(candidate);
                }
                assignment.Status = ScoutAssignmentStatus.Completed;
            }
            return created;
        }

        public int RemoveExpiredCandidates(GameSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.ScoutCandidates ??= new List<ScoutCandidateState>();
            save.Wrestlers ??= new List<WrestlerState>();
            var knownCandidateIds = save.ScoutCandidates.Where(x => x != null).Select(x => x.WrestlerId).ToHashSet(StringComparer.Ordinal);
            var expiredIds = save.Wrestlers.Where(x => knownCandidateIds.Contains(x?.Id) && x.Roster?.ActivityState == RosterActivityState.Inactive &&
                    x.Identity.ExpiryDate.HasValue && x.Identity.ExpiryDate.Value.CompareTo(save.CurrentDate) < 0)
                .Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            save.ScoutCandidates.RemoveAll(x => x == null || expiredIds.Contains(x.WrestlerId));
            return save.Wrestlers.RemoveAll(x => x != null && expiredIds.Contains(x.Id));
        }

        private WrestlerState Generate(ScoutSearchConditions conditions, GameDate date, long prestige)
        {
            var range = AbilityRange(prestige);
            var target = Math.Max(8f, Math.Min(20f, Normal((range.Minimum + range.Maximum) * .5f, 1.5f)));
            WrestlerState closest = null;
            var closestDistance = float.MaxValue;
            var sampled = 0;
            for (var attempt = 0; attempt < 200 && sampled < 40; attempt++)
            {
                var gender = conditions.HasGender ? conditions.Gender : random.Next(2) == 0 ? WrestlerGender.Male : WrestlerGender.Female;
                var candidate = generator.GenerateCandidate(gender, date);
                if (conditions.HasBackground && candidate.Identity.Background != conditions.Background) continue;
                var ability = Math.Max(WrestlerOverallCalculator.Match(candidate), WrestlerOverallCalculator.Promo(candidate));
                if (ability < 8f) continue;
                sampled++;
                var distance = Math.Abs(ability - target);
                if (distance < closestDistance) { closest = candidate; closestDistance = distance; }
                if (distance < .05f) break;
            }
            if (closest != null) return closest;
            throw new InvalidOperationException("Could not generate a candidate matching the search conditions.");
        }

        public static (float Minimum, float Maximum) AbilityRange(long prestige) => prestige switch
        {
            < 300 => (9f, 12f),
            < 2_500 => (10f, 13f),
            < 5_500 => (11f, 14f),
            < 20_000 => (12f, 15f),
            < 34_000 => (13f, 16f),
            < 55_000 => (14f, 17f),
            _ => (15f, 18f)
        };

        private static int ScoutLevel(GameSave save) => Math.Max(0, Math.Min(5, save.StaffDepartments
            .FirstOrDefault(x => x?.DepartmentType == StaffDepartmentType.Scout)?.CurrentLevel ?? 0));

        private ScoutTeamLevelDefinition Level(int level) => levels.TryGetValue(level, out var definition)
            ? definition : throw new InvalidOperationException($"Scout level definition is missing: {level}");

        private List<ScoutValueEstimate> CreateEstimates(WrestlerState wrestler, ScoutTeamLevelDefinition definition)
        {
            var a = wrestler.Attributes;
            return new List<ScoutValueEstimate>
            {
                Estimate(ScoutValueType.RingPsychology, a.RingPsychology, definition), Estimate(ScoutValueType.RingImprovisation, a.RingImprovisation, definition),
                Estimate(ScoutValueType.Technical, a.Technical, definition, StyleErrorReduction(wrestler.Presentation.WrestlingStyleId, "테크니컬")),
                Estimate(ScoutValueType.Brawling, a.Brawling, definition, StyleErrorReduction(wrestler.Presentation.WrestlingStyleId, "브롤링")),
                Estimate(ScoutValueType.Power, a.Power, definition, StyleErrorReduction(wrestler.Presentation.WrestlingStyleId, "파워")),
                Estimate(ScoutValueType.HighFlying, a.HighFlying, definition, StyleErrorReduction(wrestler.Presentation.WrestlingStyleId, "하이플라잉")),
                Estimate(ScoutValueType.SpotWork, a.SpotWork, definition), Estimate(ScoutValueType.SpecialtyMatches, a.SpecialtyMatches, definition),
                Estimate(ScoutValueType.Selling, a.Selling, definition), Estimate(ScoutValueType.Stamina, a.Stamina, definition), Estimate(ScoutValueType.Charisma, a.Charisma, definition),
                Estimate(ScoutValueType.MicWork, a.MicWork, definition), Estimate(ScoutValueType.Improvisation, a.Improvisation, definition), Estimate(ScoutValueType.Acting, a.Acting, definition),
                Estimate(ScoutValueType.FaceWork, a.FaceWork, definition), Estimate(ScoutValueType.HeelWork, a.HeelWork, definition), Estimate(ScoutValueType.Comedy, a.Comedy, definition),
                Estimate(ScoutValueType.MatchOverall, WrestlerOverallCalculator.Match(wrestler), definition), Estimate(ScoutValueType.PromoOverall, WrestlerOverallCalculator.Promo(wrestler), definition),
                Estimate(ScoutValueType.MatchPotential, wrestler.Growth.MatchPotentialCap, definition), Estimate(ScoutValueType.PromoPotential, wrestler.Growth.PromoPotentialCap, definition)
            };
        }

        private ScoutValueEstimate Estimate(ScoutValueType type, float actual, ScoutTeamLevelDefinition definition, float errorReduction = 0f)
        {
            actual = RoundTenth(actual);
            var configuredError = Math.Max(0f, definition.ValueErrorRange - errorReduction);
            var error = random.NextDouble() < definition.ExactValueChance ? 0f : configuredError;
            var center = actual + (float)(random.NextDouble() * 2d - 1d) * error;
            return new ScoutValueEstimate
            {
                Type = type,
                Minimum = RoundTenth(Math.Max(1f, center - error)),
                Maximum = RoundTenth(Math.Min(20f, center + error))
            };
        }

        private static float RoundTenth(float value) => (float)Math.Round(value, 1, MidpointRounding.AwayFromZero);

        private float Normal(float mean, float standardDeviation)
        {
            var u1 = 1d - random.NextDouble();
            var u2 = 1d - random.NextDouble();
            return mean + standardDeviation * (float)(Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2));
        }

        private static float StyleErrorReduction(string styleId, string ability)
        {
            var priorities = WrestlerOverallCalculator.MatchStylePriorities(styleId);
            return priorities.Primary.Contains(ability) ? 1f : priorities.Secondary.Contains(ability) ? .5f : 0f;
        }
    }
}
