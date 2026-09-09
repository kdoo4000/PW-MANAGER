using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using UnityEditor;

namespace PWManager.Tests
{
    public sealed class ScoutingServiceTests
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        [Test]
        public void Assignment_CompletesAfterFourWeeksAndExpiresCandidates()
        {
            var save = GameSave.CreateNew(7, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc));
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Test" };
            save.StaffDepartments.Add(new StaffDepartmentState { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = 1, UnlockedLevel = 1 });
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 7), catalog.ScoutTeamLevels, random: new Random(7));

            var assignment = service.Start(save, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female });
            Assert.That(assignment.CompleteDate, Is.EqualTo(save.CurrentDate.AddDays(28)));
            Assert.That(service.CompleteDue(save), Is.Empty);

            save.CurrentDate = assignment.CompleteDate;
            var candidates = service.CompleteDue(save);
            Assert.That(candidates, Has.Count.EqualTo(6));
            Assert.That(candidates, Has.All.Matches<ScoutCandidateState>(x => x.ValueEstimates.Count == 21));
            Assert.That(candidates.SelectMany(x => x.ValueEstimates), Has.All.Matches<ScoutValueEstimate>(x => x.Minimum >= 1f && x.Maximum <= 20f && x.Minimum <= x.Maximum));
            var firstWrestler = save.Wrestlers.First(x => x.Id == candidates[0].WrestlerId);
            var stamina = candidates[0].ValueEstimates.Single(x => x.Type == ScoutValueType.Stamina);
            var potential = candidates[0].ValueEstimates.Single(x => x.Type == ScoutValueType.MatchPotential);
            Assert.That(firstWrestler.Attributes.Stamina, Is.InRange(stamina.Minimum, stamina.Maximum));
            Assert.That(firstWrestler.Growth.MatchPotentialCap, Is.InRange(potential.Minimum, potential.Maximum));
            Assert.That(save.Wrestlers.All(x => x.Identity.Gender == WrestlerGender.Female), Is.True);
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);

            save.CurrentDate = save.Wrestlers[0].Identity.ExpiryDate.Value.AddDays(1);
            Assert.That(service.RemoveExpiredCandidates(save), Is.EqualTo(6));
            Assert.That(save.ScoutCandidates, Is.Empty);
        }

        [Test]
        public void StylePriorities_ReduceAbsoluteErrorByLevel()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            foreach (var level in new[] { 3, 4 })
            {
                var save = GameSave.CreateNew(level, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc));
                save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Test" };
                save.StaffDepartments.Add(new StaffDepartmentState { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = level, UnlockedLevel = level });
                var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), level), catalog.ScoutTeamLevels, random: new Random(level));
                var assignment = service.Start(save, new ScoutSearchConditions());
                save.CurrentDate = assignment.CompleteDate;
                foreach (var candidate in service.CompleteDue(save))
                {
                    var wrestler = save.Wrestlers.Single(x => x.Id == candidate.WrestlerId);
                    var priorities = WrestlerOverallCalculator.MatchStylePriorities(wrestler.Presentation.WrestlingStyleId);
                    foreach (var ability in priorities.Primary) AssertExact(candidate, AbilityType(ability));
                    if (level == 4) foreach (var ability in priorities.Secondary) AssertExact(candidate, AbilityType(ability));
                }
            }
        }

        private static void AssertExact(ScoutCandidateState candidate, ScoutValueType type)
        {
            var estimate = candidate.ValueEstimates.Single(x => x.Type == type);
            Assert.That(estimate.Minimum, Is.EqualTo(estimate.Maximum));
        }

        private static ScoutValueType AbilityType(string name) => name switch
        {
            "브롤링" => ScoutValueType.Brawling,
            "파워" => ScoutValueType.Power,
            "테크니컬" => ScoutValueType.Technical,
            _ => ScoutValueType.HighFlying
        };
    }
}
