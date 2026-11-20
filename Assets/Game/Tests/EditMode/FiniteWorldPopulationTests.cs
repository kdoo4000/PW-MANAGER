using System;
using System.IO;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class FiniteWorldPopulationTests
    {
        private const string CatalogPath = "Assets/Game/Data/Static/GameStaticContentCatalog.asset";

        [Test]
        public void WorldInitialization_GeneratesOnceAndPersistsUnknownPeople()
        {
            var save = Create(); var generator = Generator();
            generator.InitializeWorld(save);
            Assert.That(save.Wrestlers, Has.Count.EqualTo(400));
            Assert.That(save.Wrestlers.Count(x => x.Identity.Gender == WrestlerGender.Male), Is.EqualTo(200));
            Assert.That(save.Wrestlers.Count(x => x.Identity.Gender == WrestlerGender.Female), Is.EqualTo(200));
            Assert.That(save.UndiscoveredWrestlerIds, Is.EquivalentTo(save.Wrestlers.Select(x => x.Id)));
            Assert.That(save.Wrestlers.All(x => !x.Identity.ExpiryDate.HasValue && string.IsNullOrEmpty(x.PromotionId)), Is.True);
            Assert.That(save.ScoutCandidates, Is.Empty);
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-finite-world-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storage = new SaveService(directory); storage.Save("world", save); var restored = storage.Load("world");
                Assert.That(restored.WorldPopulationInitialized, Is.True);
                Assert.That(restored.Wrestlers.Select(x => x.Id), Is.EqualTo(save.Wrestlers.Select(x => x.Id)));
                Assert.That(restored.UndiscoveredWrestlerIds, Is.EqualTo(save.UndiscoveredWrestlerIds));
                var before = JsonUtility.ToJson(restored); Generator(999).InitializeWorld(restored);
                Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(before));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void StartingSearch_PreservesKnownPeopleAndPopulationDoesNotDependOnSearchOrServiceSeed()
        {
            var first = Create(); var second = Create();
            var known = Generator().GenerateCandidate(WrestlerGender.Male, first.CurrentDate);
            first.Wrestlers.Add(known); second.Wrestlers.Add(JsonUtility.FromJson<WrestlerState>(JsonUtility.ToJson(known)));
            var original = JsonUtility.ToJson(known);
            Service(1).Start(first, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female });
            Service(999).Start(second, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Male, HasBackground = true, Background = WrestlerBackground.Veteran });
            Assert.That(first.Wrestlers, Has.Count.EqualTo(401));
            Assert.That(first.UndiscoveredWrestlerIds, Has.Count.EqualTo(400));
            Assert.That(first.IsWrestlerDiscovered(known.Id), Is.True);
            Assert.That(JsonUtility.ToJson(known), Is.EqualTo(original));
            Assert.That(first.Wrestlers.Skip(1).All(x => JsonUtility.ToJson(x.Attributes) != JsonUtility.ToJson(known.Attributes)), Is.True);
            Assert.That(first.Wrestlers.Skip(1).Select(x => x.Identity.LegalName + JsonUtility.ToJson(x.Attributes)),
                Is.EqualTo(second.Wrestlers.Skip(1).Select(x => x.Identity.LegalName + JsonUtility.ToJson(x.Attributes))));
        }

        [Test]
        public void RepeatedSearches_ExhaustEligiblePeopleWithoutCreatingReplacements()
        {
            var save = Create(); Generator().InitializeWorld(save);
            var originals = save.Wrestlers.ToDictionary(x => x.Id, x => JsonUtility.ToJson(x));
            var expected = save.Wrestlers.Where(x => x.Identity.Gender == WrestlerGender.Female &&
                x.Identity.Background == WrestlerBackground.Veteran && Ability(x) is >= 9f and <= 12f).Select(x => x.Id).ToHashSet();
            Assert.That(expected.Count, Is.GreaterThan(0));
            var found = new System.Collections.Generic.HashSet<string>();
            var exhausted = false;
            for (var i = 0; i < 35; i++)
            {
                var service = Service(i);
                var assignment = service.Start(save, new ScoutSearchConditions { HasGender = true, Gender = WrestlerGender.Female,
                    HasBackground = true, Background = WrestlerBackground.Veteran });
                save.CurrentDate = assignment.CompleteDate; service.ProcessDay(save);
                var reports = save.ScoutCandidates.Where(x => x.AssignmentId == assignment.Id).ToList();
                Assert.That(assignment.Status, Is.EqualTo(ScoutAssignmentStatus.Completed));
                Assert.That(save.Wrestlers, Has.Count.EqualTo(400));
                foreach (var report in reports)
                {
                    Assert.That(expected.Contains(report.WrestlerId) && found.Add(report.WrestlerId), Is.True);
                    Assert.That(save.IsWrestlerDiscovered(report.WrestlerId), Is.True);
                }
                if (reports.Count == 0)
                {
                    Assert.That(save.InboxMessages.Last().Body, Does.Contain("찾지 못했습니다"));
                    exhausted = true; break;
                }
            }
            Assert.That(exhausted, Is.True);
            Assert.That(found, Is.EquivalentTo(expected));
            Assert.That(save.UndiscoveredWrestlerIds.Count, Is.EqualTo(400 - found.Count));
            foreach (var person in save.Wrestlers) Assert.That(JsonUtility.ToJson(person), Is.EqualTo(originals[person.Id]));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [Test]
        public void UnknownPerson_CannotOpenProfileOrNegotiateUntilDiscovered()
        {
            var save = Create(); Generator().InitializeWorld(save);
            var person = save.Wrestlers.OrderBy(x => x.Id, StringComparer.Ordinal).First(x => Ability(x) is >= 9f and <= 12f);
            Assert.That(ContractNegotiationService.HasCurrentRecruitmentInformation(save, person), Is.False);
            Assert.Throws<InvalidOperationException>(() => ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler));
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new ContractNegotiationService().Submit(save, person.Id, ContractType.Wrestler, 100000, 100000));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Unknown world person profile"); go.SetActive(false);
            try
            {
                DashboardSession.SetActiveSave(save, true);
                var controller = go.AddComponent<WrestlerProfileController>();
                typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { person });
                Assert.That(typeof(WrestlerProfileController).GetField("boundWrestler", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
            var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; var reports = service.CompleteDue(save);
            Assert.That(reports.Any(x => x.WrestlerId == person.Id), Is.True);
            Assert.That(ContractNegotiationService.HasCurrentRecruitmentInformation(save, person), Is.True);
            Assert.DoesNotThrow(() => ContractNegotiationService.Demand(save, person.Id, ContractType.Wrestler));
        }

        [Test]
        public void SparsePool_ReportsOnlyTheOneExistingEligiblePerson()
        {
            var save = Create(); save.WorldPopulationInitialized = true;
            var person = Generator().GenerateCandidate(WrestlerGender.Female, save.CurrentDate);
            foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(person.Attributes, 10f);
            person.Growth.MatchPotentialCap = person.Growth.PromoPotentialCap = 20; person.Identity.ExpiryDate = default;
            save.Wrestlers.Add(person); save.UndiscoveredWrestlerIds.Add(person.Id);
            var service = Service(); var assignment = service.Start(save, new ScoutSearchConditions());
            save.CurrentDate = assignment.CompleteDate; var reports = service.CompleteDue(save);
            Assert.That(reports.Select(x => x.WrestlerId), Is.EqualTo(new[] { person.Id }));
            Assert.That(save.Wrestlers, Has.Count.EqualTo(1)); Assert.That(save.UndiscoveredWrestlerIds, Is.Empty);
            Assert.That(save.Contracts, Is.Empty); Assert.That(save.SalaryPayments, Is.Empty);
            Assert.That(TrainingService.Eligible(save, person), Is.False);
        }

        [Test]
        public void LockedScoutStart_DoesNotGeneratePopulation()
        {
            var save = Create(); save.StaffDepartments[0].CurrentLevel = 0;
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => Service().Start(save, new ScoutSearchConditions()));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void LegacyPendingCompletionFailure_DoesNotCommitPopulationOrReports()
        {
            var save = Create();
            save.ScoutAssignments.Add(new ScoutAssignmentState { Id = Guid.NewGuid().ToString(), StartDate = save.CurrentDate.AddDays(-7),
                CompleteDate = save.CurrentDate, Status = ScoutAssignmentStatus.InProgress });
            var service = new ScoutingService(Generator(), Array.Empty<PWManager.Data.Definitions.ScoutTeamLevelDefinition>());
            var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => service.CompleteDue(save));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void Version24PendingFile_InitializesOnceWithoutHidingExistingPeople()
        {
            var save = Create(); var known = Generator().GenerateInitialCandidates(save.CurrentDate).Take(2).ToList();
            save.Wrestlers.AddRange(known);
            var assignment = new ScoutAssignmentState { Id = Guid.NewGuid().ToString(), StartDate = save.CurrentDate,
                CompleteDate = save.CurrentDate.AddDays(7), Status = ScoutAssignmentStatus.InProgress };
            save.ScoutAssignments.Add(assignment); save.SaveVersion = 24;
            var legacy = JsonUtility.ToJson(save).Replace(",\"WorldPopulationInitialized\":false", string.Empty)
                .Replace(",\"UndiscoveredWrestlerIds\":[]", string.Empty);
            Assert.That(legacy, Does.Not.Contain("\"WorldPopulationInitialized\""));
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-legacy-world-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "legacy.json"), legacy);
                var storage = new SaveService(directory); var restored = storage.Load("legacy");
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
                Assert.That(restored.WorldPopulationInitialized, Is.False);
                Assert.That(restored.UndiscoveredWrestlerIds, Is.Empty);
                Assert.That(known.All(x => restored.IsWrestlerDiscovered(x.Id)), Is.True);
                restored.CurrentDate = assignment.CompleteDate; var reports = Service().CompleteDue(restored);
                Assert.That(reports.Count, Is.GreaterThan(0));
                Assert.That(restored.Wrestlers, Has.Count.EqualTo(402));
                Assert.That(known.All(x => restored.IsWrestlerDiscovered(x.Id)), Is.True);
                Assert.That(reports.All(x => restored.IsWrestlerDiscovered(x.WrestlerId)), Is.True);
                storage.Save("legacy", restored); restored = storage.Load("legacy");
                var before = JsonUtility.ToJson(restored); Service(200).CompleteDue(restored); Generator(100).InitializeWorld(restored);
                Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(before));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void UndiscoveredReferences_RejectInvalidIdentityOrState(int invalidCase)
        {
            var save = Create(); Generator().InitializeWorld(save);
            if (invalidCase == 0) save.UndiscoveredWrestlerIds.Add(save.UndiscoveredWrestlerIds[0]);
            if (invalidCase == 1) save.UndiscoveredWrestlerIds.Add(Guid.NewGuid().ToString());
            if (invalidCase == 2) save.WorldPopulationInitialized = false;
            if (invalidCase == 3) save.Wrestlers[0].Roster.ActivityState = RosterActivityState.Active;
            Assert.That(GameSaveValidator.Validate(save), Is.Not.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void UnknownPeople_AreExcludedFromOtherPickersAndCannotPerformMatchSpots(int entryPoint)
        {
            var save = Create(); Generator().InitializeWorld(save);
            var known = Generator().GenerateInitialCandidates(save.CurrentDate).Take(3).ToList(); save.Wrestlers.AddRange(known);
            var host = new VisualElement();
            if (entryPoint == 0)
            {
                typeof(StoryManagementView).GetMethod("RelationshipHistory", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { host, save, null });
                Assert.That(host.Q<DropdownField>("history-first").choices, Has.Count.EqualTo(3));
                Assert.That(host.Q<DropdownField>("history-second").choices, Has.Count.EqualTo(3));
                return;
            }
            var match = new MatchPlanState { MatchTypeId = "matchtype_001", MatchGimmickId = "gimmick_000",
                Sides = { new MatchSideState { Id = "left", MemberIds = { known[0].Id } }, new MatchSideState { Id = "right", MemberIds = { known[1].Id } } },
                Spots = { new PlannedSpotState { SpotId = "spot_004", ActorId = save.Wrestlers[0].Id,
                    TargetId = known[1].Id, Phase = SpecialMatchSpotCatalog.Find("spot_004").Phases[0] } } };
            if (entryPoint == 1)
            {
                _ = new MatchSpotEditor(host, save, match, save.CurrentDate);
                Assert.That(host.Q<DropdownField>("spot-actor-0").choices, Has.Count.EqualTo(2));
                return;
            }
            Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Not.Empty);
            Assert.Throws<InvalidOperationException>(() => MatchSpotEvaluator.Evaluate(save, match, save.CurrentDate, Guid.NewGuid().ToString(), 7));
            match.Spots[0].ActorId = known[2].Id;
            Assert.That(MatchSpotEvaluator.Validate(save, match), Is.Empty);
        }

        private static float Ability(WrestlerState wrestler) => Math.Max(WrestlerOverallCalculator.Match(wrestler), WrestlerOverallCalculator.Promo(wrestler));

        private static ScoutingService Service(int seed = 7)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath);
            return new ScoutingService(Generator(seed), catalog.ScoutTeamLevels, random: new System.Random(seed));
        }

        private static GameSave Create()
        {
            var save = GameSave.CreateNew(7, DateTime.UtcNow);
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "Finite World", InitialCash = 10000000 };
            save.StaffDepartments.Add(new StaffDepartmentState
                { Id = Guid.NewGuid().ToString(), DepartmentType = StaffDepartmentType.Scout, CurrentLevel = 1, UnlockedLevel = 1 });
            return save;
        }

        private static WrestlerGenerator Generator(int seed = 7, Func<string> createId = null) =>
            new(new StaticContentRegistry(AssetDatabase.LoadAssetAtPath<StaticContentCatalog>(CatalogPath)), seed, createId);
    }
}
