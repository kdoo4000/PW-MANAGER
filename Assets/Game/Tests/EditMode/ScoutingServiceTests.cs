using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace PWManager.Tests
{
    public sealed class ScoutingServiceTests
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        [Test]
        public void Assignment_CompletesAfterSevenDaysAndReportExpiryPreservesPeople()
        {
            var save = GameSave.CreateNew(7, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc));
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Test" };
            save.StaffDepartments.Add(new StaffDepartmentState { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = 1, UnlockedLevel = 1 });
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 7), catalog.ScoutTeamLevels, random: new Random(7));

            Populate(save, 6);

            var assignment = service.Start(save, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female });
            Assert.That(assignment.CompleteDate, Is.EqualTo(save.CurrentDate.AddDays(7)));
            Assert.That(service.CompleteDue(save), Is.Empty);

            save.CurrentDate = assignment.CompleteDate;
            var candidates = service.CompleteDue(save);
            Assert.That(candidates, Has.Count.EqualTo(6));
            Assert.That(candidates, Has.All.Matches<ScoutCandidateState>(x => x.ValueEstimates.Count == 21));
            Assert.That(candidates.SelectMany(x => x.ValueEstimates), Has.All.Matches<ScoutValueEstimate>(x => x.Minimum >= 1f && x.Maximum <= 20f && x.Minimum <= x.Maximum));
            Assert.That(candidates.SelectMany(x => x.ValueEstimates), Has.All.Matches<ScoutValueEstimate>(x => x.HasEvaluation && !x.IsFullyRevealed));
            Assert.That(save.Wrestlers.All(x => x.Identity.Gender == WrestlerGender.Female), Is.True);
            Assert.That(save.Wrestlers.All(x => Math.Max(WrestlerOverallCalculator.Match(x), WrestlerOverallCalculator.Promo(x)) >= 8f), Is.True);
            Assert.That(save.Wrestlers.All(x => Math.Max(WrestlerOverallCalculator.Match(x), WrestlerOverallCalculator.Promo(x)) is >= 9f and <= 12f), Is.True);
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);

            Assert.That(save.Wrestlers.All(x => !x.Identity.ExpiryDate.HasValue), Is.True);
            save.CurrentDate = candidates[0].ExpiryDate.Value;
            Assert.DoesNotThrow(() => ContractNegotiationService.Demand(save, candidates[0].WrestlerId, ContractType.Wrestler));
            save.CurrentDate = save.CurrentDate.AddDays(1); service.ProcessDay(save);
            Assert.That(save.Wrestlers, Has.Count.EqualTo(6));
            Assert.That(save.ScoutCandidates, Has.Count.EqualTo(6));
            Assert.That(save.ScoutCandidates.All(x => x.IsExpired(save.CurrentDate)), Is.True);
            Assert.Throws<InvalidOperationException>(() => ContractNegotiationService.Demand(save, candidates[0].WrestlerId, ContractType.Wrestler));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.That(restored.Wrestlers.Select(x => x.Id), Is.EqualTo(save.Wrestlers.Select(x => x.Id)));
            Assert.That(restored.ScoutCandidates.All(x => x.IsExpired(restored.CurrentDate)), Is.True);
        }

        [Test]
        public void Rediscovery_RefreshesReportsWithoutRecreatingPeopleOrReducingKnowledge()
        {
            var save = GameSave.CreateNew(7, DateTime.UtcNow);
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Test" };
            save.StaffDepartments.Add(new StaffDepartmentState { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = 1, UnlockedLevel = 1 });
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 7), catalog.ScoutTeamLevels, random: new Random(7));
            Populate(save, 6);
            var first = service.Start(save, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female });
            save.CurrentDate = first.CompleteDate; service.CompleteDue(save);
            foreach (var wrestler in save.Wrestlers)
            {
                foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(wrestler.Attributes, 10f);
                wrestler.Growth.MatchPotentialCap = wrestler.Growth.PromoPotentialCap = 20;
                wrestler.Condition.InjuryStatus = InjuryStatus.Minor;
            }
            save.ScoutCandidates[0].KnowledgeLevel = 4;
            var informedId = save.ScoutCandidates[0].WrestlerId;
            var previous = save.Wrestlers.ToDictionary(x => x.Id, x => JsonUtility.ToJson(x));
            save.CurrentDate = save.ScoutCandidates[0].ExpiryDate.Value.AddDays(1);
            var second = service.Start(save, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female });
            save.CurrentDate = second.CompleteDate; var reports = service.CompleteDue(save);
            Assert.That(save.Wrestlers, Has.Count.EqualTo(6));
            Assert.That(save.ScoutCandidates, Has.Count.EqualTo(6));
            Assert.That(reports.Select(x => x.WrestlerId).Distinct().Count(), Is.EqualTo(6));
            Assert.That(reports.All(x => x.AssignmentId == second.Id && !x.IsExpired(save.CurrentDate)), Is.True);
            Assert.That(reports.Single(x => x.WrestlerId == informedId).KnowledgeLevel, Is.EqualTo(4));
            foreach (var wrestler in save.Wrestlers) Assert.That(JsonUtility.ToJson(wrestler), Is.EqualTo(previous[wrestler.Id]));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void Rediscovery_WithSameKnowledgeButLowerScoutTeamPreservesRevealedStats()
        {
            var save = GameSave.CreateNew(7, DateTime.UtcNow);
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Test" };
            var scout = new StaffDepartmentState { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = 5, UnlockedLevel = 5 };
            save.StaffDepartments.Add(scout);
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var service = new ScoutingService(new WrestlerGenerator(new StaticContentRegistry(catalog), 7), catalog.ScoutTeamLevels, random: new Random(7));
            Populate(save, 15);
            var first = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = first.CompleteDate; service.CompleteDue(save);
            var originalIds = save.Wrestlers.Select(x => x.Id).ToHashSet();
            Assert.That(save.ScoutCandidates.All(x => x.KnowledgeLevel == 4 && x.ValueEstimates.All(v => v.IsFullyRevealed)), Is.True);
            foreach (var wrestler in save.Wrestlers)
            {
                foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(wrestler.Attributes, 10f);
                wrestler.Growth.MatchPotentialCap = wrestler.Growth.PromoPotentialCap = 20;
            }
            save.CurrentDate = save.ScoutCandidates[0].ExpiryDate.Value.AddDays(1); scout.CurrentLevel = 4;
            var second = service.Start(save, new ScoutSearchConditions()); save.CurrentDate = second.CompleteDate;
            var reports = service.CompleteDue(save);
            Assert.That(reports, Has.Count.EqualTo(12));
            Assert.That(reports.All(x => originalIds.Contains(x.WrestlerId) && x.KnowledgeLevel == 4 &&
                x.ValueEstimates.All(v => v.IsFullyRevealed)), Is.True);
            Assert.That(save.Wrestlers.Count, Is.EqualTo(originalIds.Count));
        }

        [Test]
        public void Migration22To23_MovesOnlyReportExpiryAndIsIdempotent()
        {
            var save = GameSave.CreateNew(7, DateTime.UtcNow); save.SaveVersion = 22;
            var expiry = new OptionalGameDate(new GameDate(2027, 6, 1));
            var wrestler = new WrestlerState { Identity = new WrestlerIdentityState { Id = Guid.NewGuid().ToString(), RingName = "기존 선수", ExpiryDate = expiry } };
            var initialCandidate = new WrestlerState { Identity = new WrestlerIdentityState { Id = Guid.NewGuid().ToString(), ExpiryDate = expiry } };
            save.Wrestlers.Add(wrestler); save.Wrestlers.Add(initialCandidate);
            save.ScoutCandidates.Add(new ScoutCandidateState { WrestlerId = wrestler.Id, KnowledgeLevel = 2 });
            var migration = new SaveVersion22To23Migration(); migration.Migrate(save);
            Assert.That(save.SaveVersion, Is.EqualTo(23));
            Assert.That(save.ScoutCandidates.Single().ExpiryDate.Value, Is.EqualTo(expiry.Value));
            Assert.That(wrestler.Identity.ExpiryDate.HasValue, Is.False);
            Assert.That(initialCandidate.Identity.ExpiryDate.Value, Is.EqualTo(expiry.Value));
            var serialized = JsonUtility.ToJson(save); migration.Migrate(save);
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(serialized));
        }

        private static void Populate(GameSave save, int count)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            var generator = new WrestlerGenerator(new StaticContentRegistry(catalog), 71);
            var population = Enumerable.Range(0, count).Select(_ => generator.GenerateCandidate(WrestlerGender.Female, save.CurrentDate))
                .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            foreach (var wrestler in population)
            {
                wrestler.Identity.ExpiryDate = default;
                foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(wrestler.Attributes, 10f);
                wrestler.Growth.MatchPotentialCap = wrestler.Growth.PromoPotentialCap = 20;
            }
            save.Wrestlers.AddRange(population);
            save.UndiscoveredWrestlerIds.AddRange(population.Select(x => x.Id));
            save.WorldPopulationInitialized = true;
        }

        [TestCase(0, 9f, 12f)]
        [TestCase(300, 10f, 13f)]
        [TestCase(2500, 11f, 14f)]
        [TestCase(5500, 12f, 15f)]
        [TestCase(20000, 13f, 16f)]
        [TestCase(34000, 14f, 17f)]
        [TestCase(55000, 15f, 18f)]
        public void PromotionPrestige_SelectsHigherCandidateAbilityRanges(long prestige, float minimum, float maximum)
        {
            Assert.That(ScoutingService.AbilityRange(prestige), Is.EqualTo((minimum, maximum)));
        }

    }
}
