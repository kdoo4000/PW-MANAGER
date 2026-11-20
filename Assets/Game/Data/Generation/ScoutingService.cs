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
            if (save.Promotion == null || conditions.HasGender && !Enum.IsDefined(typeof(WrestlerGender), conditions.Gender) ||
                conditions.HasBackground && !Enum.IsDefined(typeof(WrestlerBackground), conditions.Background))
                throw new InvalidOperationException("단체와 탐색 조건을 확인하세요.");
            save.ScoutAssignments ??= new List<ScoutAssignmentState>();
            if (save.ScoutAssignments.Any(x => x?.Status == ScoutAssignmentStatus.InProgress))
                throw new InvalidOperationException("진행 중인 탐색이 있습니다.");
            if (ScoutLevel(save) == 0) throw new InvalidOperationException("스카우트팀을 먼저 해금하세요.");

            var assignment = new ScoutAssignmentState
            {
                Id = createId(), SearchConditions = new ScoutSearchConditions { HasGender = conditions.HasGender, Gender = conditions.Gender,
                    HasBackground = conditions.HasBackground, Background = conditions.Background },
                StartDate = save.CurrentDate, CompleteDate = save.CurrentDate.AddDays(ScoutAssignmentState.DurationDays),
                Status = ScoutAssignmentStatus.InProgress
            };
            if (!EntityId.IsValidRuntimeId(assignment.Id) || save.ScoutAssignments.Any(x => x.Id == assignment.Id))
                throw new InvalidOperationException("스카우트 배정 ID가 중복되거나 올바르지 않습니다.");
            generator.InitializeWorld(save);
            save.ScoutAssignments.Add(assignment);
            return assignment;
        }

        public IReadOnlyList<ScoutCandidateState> CompleteDue(GameSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.ScoutAssignments ??= new List<ScoutAssignmentState>();
            save.ScoutCandidates ??= new List<ScoutCandidateState>();
            save.Wrestlers ??= new List<WrestlerState>();
            var due = save.ScoutAssignments.Where(x => x?.Status == ScoutAssignmentStatus.InProgress &&
                x.CompleteDate.CompareTo(save.CurrentDate) <= 0).ToList();
            if (due.Count == 0) return Array.Empty<ScoutCandidateState>();
            var newPeople = save.WorldPopulationInitialized ? Array.Empty<WrestlerState>() : generator.GenerateWorldPopulation(save);
            var population = save.Wrestlers.Concat(newPeople).ToList();
            var created = new List<ScoutCandidateState>();
            var completed = new List<ScoutAssignmentState>();
            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assignment in due)
            {
                var definition = Level(ScoutLevel(save));
                var prestige = save.Promotion == null ? 0 : PromotionProgressionRules.ActivePrestige(save);
                var range = AbilityRange(prestige);
                var reportedIds = save.ScoutCandidates.Where(x => x != null && !x.IsExpired(save.CurrentDate))
                    .Select(x => x.WrestlerId).ToHashSet(StringComparer.Ordinal);
                var available = population.Where(x => x != null && string.IsNullOrEmpty(x.PromotionId) &&
                        x.Roster.ActivityState == RosterActivityState.Inactive && !reportedIds.Contains(x.Id))
                    .Where(x => !assignment.SearchConditions.HasGender || x.Identity.Gender == assignment.SearchConditions.Gender)
                    .Where(x => !assignment.SearchConditions.HasBackground || x.Identity.Background == assignment.SearchConditions.Background)
                    .Where(x => Math.Max(WrestlerOverallCalculator.Match(x), WrestlerOverallCalculator.Promo(x)) >= range.Minimum &&
                        Math.Max(WrestlerOverallCalculator.Match(x), WrestlerOverallCalculator.Promo(x)) <= range.Maximum)
                    .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
                for (var i = 0; i < definition.CandidateCount; i++)
                {
                    var wrestler = available.FirstOrDefault(x => !selectedIds.Contains(x.Id));
                    if (wrestler == null) break;
                    selectedIds.Add(wrestler.Id);
                    var previous = save.ScoutCandidates.SingleOrDefault(x => x?.WrestlerId == wrestler.Id);
                    var evaluation = previous != null && previous.KnowledgeLevel > definition.KnowledgeLevel
                        ? levels.Values.Where(x => x.KnowledgeLevel >= previous.KnowledgeLevel)
                            .OrderBy(x => x.KnowledgeLevel).ThenBy(x => x.Level).First()
                        : definition;
                    var candidate = new ScoutCandidateState
                    {
                        WrestlerId = wrestler.Id, AssignmentId = assignment.Id,
                        KnowledgeLevel = evaluation.KnowledgeLevel,
                        ExpiryDate = new OptionalGameDate(save.CurrentDate.AddDays(generator.CandidateRetentionDays)),
                        ValueEstimates = CreateEstimates(wrestler, evaluation, previous?.ValueEstimates)
                    };
                    created.Add(candidate);
                }
                completed.Add(assignment);
            }
            save.Wrestlers.AddRange(newPeople);
            save.UndiscoveredWrestlerIds ??= new();
            save.UndiscoveredWrestlerIds.AddRange(newPeople.Select(x => x.Id));
            save.WorldPopulationInitialized = true;
            foreach (var candidate in created)
            {
                save.Wrestlers.Single(x => x.Id == candidate.WrestlerId).Identity.ExpiryDate = default;
                save.UndiscoveredWrestlerIds.Remove(candidate.WrestlerId);
                save.ScoutCandidates.RemoveAll(x => x?.WrestlerId == candidate.WrestlerId);
                save.ScoutCandidates.Add(candidate);
            }
            foreach (var assignment in completed) assignment.Status = ScoutAssignmentStatus.Completed;
            return created;
        }

        public void ProcessDay(GameSave save)
        {
            var dueIds = save.ScoutAssignments.Where(x => x.Status == ScoutAssignmentStatus.InProgress && x.CompleteDate.CompareTo(save.CurrentDate) <= 0).Select(x => x.Id).ToHashSet();
            CompleteDue(save);
            foreach (var assignment in save.ScoutAssignments.Where(x => dueIds.Contains(x.Id)))
            {
                var count = save.ScoutCandidates.Count(x => x.AssignmentId == assignment.Id);
                new InboxService(createId).Publish(save, $"scouting-report:{assignment.Id}", InboxMessageType.Navigation, InboxPriority.Important,
                    "스카우트팀", "스카우트 보고서가 도착했습니다", count == 0 ? "조건에 맞는 새로운 영입 후보를 찾지 못했습니다." : $"새 영입 후보 {count}명을 확인할 수 있습니다.", InboxTargetType.Promotion, save.Promotion.Id);
            }
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

        private List<ScoutValueEstimate> CreateEstimates(WrestlerState wrestler, ScoutTeamLevelDefinition definition,
            IEnumerable<ScoutValueEstimate> previous = null)
        {
            var known = (previous ?? Array.Empty<ScoutValueEstimate>()).Where(x => x != null &&
                    (x.IsFullyRevealed || !x.HasEvaluation && x.Minimum == x.Maximum))
                .Select(x => x.Type).ToHashSet();
            var detailed = Enumerable.Range(0, ScoutAssessmentRules.DetailedStatCount).Select(x => (ScoutValueType)x).ToArray();
            for (var i = detailed.Length - 1; i > 0; i--)
            {
                var selected = random.Next(i + 1);
                (detailed[i], detailed[selected]) = (detailed[selected], detailed[i]);
            }
            var revealed = detailed.Take(ScoutAssessmentRules.RevealedStatCount(definition.Level)).ToHashSet();
            ScoutValueEstimate Estimate(ScoutValueType type, float actual) => ScoutAssessmentRules.Evaluate(type, actual, definition.Level,
                known.Contains(type) || ((int)type < ScoutAssessmentRules.DetailedStatCount ? revealed.Contains(type) : random.NextDouble() < definition.ExactValueChance), random);
            return ScoutAssessmentRules.ActualValues(wrestler).Select(x => Estimate(x.Type, x.Value)).ToList();
        }

    }
}
