using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
    public sealed class WrestlerHiddenTests
    {
        [Test]
        public void GeneratedWrestler_HasFifteenIndependentHiddenStatsAndNoLegacyTraits()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
            var generator = new WrestlerGenerator(new StaticContentRegistry(catalog), 71);
            var wrestler = generator.GenerateCandidate(WrestlerGender.Male, new GameDate(2026, 6, 1));
            Assert.That(wrestler.Hidden, Is.Not.Null);
            var fields = typeof(WrestlerHiddenState).GetFields();
            Assert.That(fields, Has.Length.EqualTo(15));
            Assert.That(fields.All(x => x.FieldType == typeof(int) && (int)x.GetValue(wrestler.Hidden) is >= 1 and <= 20), Is.True);
            Assert.That(fields.Select(x => (int)x.GetValue(wrestler.Hidden)).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(wrestler.Presentation.TraitIds, Is.Empty);
            Assert.That(wrestler.Condition.Satisfaction, Is.EqualTo(50));
        }
        [Test]
        public void Creation_IsStablePerPersonAndDoesNotReadLegacyTraitsOrCurrentEmotion()
        {
            var first = Create(); var second = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(first));
            first.Wrestlers[0].Hidden = second.Wrestlers[0].Hidden = null;
            first.Wrestlers[0].Presentation.TraitIds.Add("trait_005"); second.Wrestlers[0].Presentation.TraitIds.Add("trait_017");
            first.Wrestlers[0].Condition.Satisfaction = 1; second.Wrestlers[0].Condition.Satisfaction = 99;
            WrestlerHiddenRules.InitializeSave(first); WrestlerHiddenRules.InitializeSave(second);
            Assert.That(JsonUtility.ToJson(first.Wrestlers[0].Hidden), Is.EqualTo(JsonUtility.ToJson(second.Wrestlers[0].Hidden)));
            Assert.That(first.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(1));
            Assert.That(second.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(99));
            Assert.That(first.Wrestlers[0].Presentation.TraitIds, Is.Empty);
            var before = JsonUtility.ToJson(first); WrestlerHiddenRules.InitializeSave(first);
            Assert.That(JsonUtility.ToJson(first), Is.EqualTo(before));
        }

        [Test]
        public void Legacy26File_CreatesNewHiddenWithoutChangingPublicStateOrRevealingIt()
        {
            var save = Create(); var person = save.Wrestlers[0]; person.Hidden = null;
            person.Presentation.TraitIds.Add("trait_005"); person.Condition.Satisfaction = 37;
            save.ScoutCandidates.Add(new ScoutCandidateState { WrestlerId = person.Id, Source = ScoutReportSource.InitialRecruitment,
                KnowledgeLevel = 4, RevealedTraitIds = new() { "trait_005" } });
            var attributes = JsonUtility.ToJson(person.Attributes); var growth = JsonUtility.ToJson(person.Growth);
            var contracts = JsonUtility.ToJson(save.Contracts[0]); save.SaveVersion = 26;
            var legacy = Regex.Replace(JsonUtility.ToJson(save), ",\"Hidden\":(null|\\{[^{}]*\\})", string.Empty);
            Assert.That(legacy, Does.Not.Contain("\"Hidden\""));
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-hidden-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "legacy.json"), legacy);
                var storage = new SaveService(directory); var restored = storage.Load("legacy"); var actual = restored.Wrestlers[0];
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
                Assert.That(actual.Hidden, Is.Not.Null); Assert.That(WrestlerHiddenRules.IsValid(actual.Hidden), Is.True);
                Assert.That(actual.Presentation.TraitIds, Is.Empty); Assert.That(restored.ScoutCandidates[0].RevealedTraitIds, Is.Empty);
                Assert.That(JsonUtility.ToJson(actual.Attributes), Is.EqualTo(attributes));
                Assert.That(JsonUtility.ToJson(actual.Growth), Is.EqualTo(growth));
                Assert.That(JsonUtility.ToJson(restored.Contracts[0]), Is.EqualTo(contracts));
                Assert.That(actual.Condition.Satisfaction, Is.EqualTo(37));
                var hidden = JsonUtility.ToJson(actual.Hidden); var before = JsonUtility.ToJson(restored);
                new SaveVersion26To27Migration().Migrate(restored);
                Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(before));
                storage.Save("legacy", restored); Assert.That(JsonUtility.ToJson(storage.Load("legacy").Wrestlers[0].Hidden), Is.EqualTo(hidden));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void EveryHiddenStat_RejectsOutOfRangeValuesAndSimulationCopiesAreIndependent()
        {
            var save = Create(); var person = save.Wrestlers[0]; person.Hidden = new();
            foreach (var field in typeof(WrestlerHiddenState).GetFields())
            {
                foreach (var value in new[] { 0, 21 })
                {
                    field.SetValue(person.Hidden, value);
                    Assert.That(GameSaveValidator.Validate(save), Has.Some.Contains("hidden attributes"));
                }
                field.SetValue(person.Hidden, 10);
            }
            var copy = save.CopyForGrowthSimulation(); copy.Wrestlers[0].Hidden.Ambition = 20;
            Assert.That(person.Hidden.Ambition, Is.EqualTo(10));
            var career = save.CopyForCareerSimulation(); career.Wrestlers[0].Hidden.Professionalism = 1;
            Assert.That(person.Hidden.Professionalism, Is.EqualTo(10));
        }

        [Test]
        public void InjuryProneness_ChangesExistingTrainingRiskWithoutChangingRecoveryDuration()
        {
            var low = Create(); var high = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(low));
            low.Wrestlers[0].Hidden = new() { InjuryProneness = 1 }; high.Wrestlers[0].Hidden = new() { InjuryProneness = 20 };
            foreach (var save in new[] { low, high })
            {
                TrainingService.Initialize(save); var service = new TrainingService(random: new FixedRandom(.0004));
                service.SetPlan(save, save.Wrestlers[0].Id, TrainingMode.Normal, WrestlerAttribute.Power);
                save.CurrentDate = save.CurrentDate.AddDays(1); service.ProcessDay(save);
            }
            Assert.That(low.Wrestlers[0].Condition.InjuryStatus, Is.EqualTo(InjuryStatus.None));
            Assert.That(high.Wrestlers[0].Condition.InjuryStatus, Is.EqualTo(InjuryStatus.Minor));
            Assert.That(high.Wrestlers[0].Condition.RecoveryDate.Value, Is.EqualTo(high.CurrentDate.AddDays(7)));
        }

        [Test]
        public void Ambition_DoesNotGiveTrainingExperienceOrChangeEmotion()
        {
            var low = Create(); var high = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(low));
            low.Wrestlers[0].Hidden = new() { Ambition = 1 }; high.Wrestlers[0].Hidden = new() { Ambition = 20 };
            foreach (var save in new[] { low, high })
            {
                TrainingService.Initialize(save); var service = new TrainingService(random: new FixedRandom(1));
                service.SetPlan(save, save.Wrestlers[0].Id, TrainingMode.Normal, WrestlerAttribute.Power);
                save.CurrentDate = save.CurrentDate.AddDays(1); service.ProcessDay(save);
            }
            Assert.That(JsonUtility.ToJson(low.Training), Is.EqualTo(JsonUtility.ToJson(high.Training)));
            Assert.That(low.Wrestlers[0].Condition.Satisfaction, Is.EqualTo(high.Wrestlers[0].Condition.Satisfaction));
        }

        [Test]
        public void Profile_DoesNotExposeHiddenValuesOrInventPersonalityDescriptions()
        {
            var save = Create(); var person = save.Wrestlers[0]; person.Hidden = new();
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var go = new GameObject("Hidden profile disclosure test"); go.SetActive(false);
            var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            try
            {
                DashboardSession.SetActiveSave(save, true);
                typeof(WrestlerProfileController).GetMethod("BindInteractiveRoot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { root });
                var bind = typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic);
                bind.Invoke(controller, new object[] { person }); var labels = root.Query<Label>().ToList().Select(x => x.text).ToArray();
                foreach (var field in typeof(WrestlerHiddenState).GetFields()) field.SetValue(person.Hidden, 20);
                bind.Invoke(controller, new object[] { person });
                Assert.That(root.Query<Label>().ToList().Select(x => x.text), Is.EqualTo(labels));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly double value;
            public FixedRandom(double value) => this.value = value;
            public override double NextDouble() => value;
        }

        private static GameSave Create()
        {
            var save = GameSave.CreateNew(71, DateTime.UtcNow);
            save.Promotion = new() { Id = Guid.NewGuid().ToString(), Name = "Hidden Test", InitialCash = 10000 };
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
            var person = new WrestlerGenerator(new StaticContentRegistry(catalog), 71).GenerateCandidate(WrestlerGender.Male, save.CurrentDate);
            person.PromotionId = save.Promotion.Id; person.Roster.ActivityState = RosterActivityState.Active; person.Identity.ExpiryDate = default;
            save.Wrestlers.Add(person);
            save.Contracts.Add(new() { Id = Guid.NewGuid().ToString(), PersonId = person.Id, PromotionId = save.Promotion.Id,
                Type = ContractType.Wrestler, StartDate = save.CurrentDate, EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active });
            return save;
        }
    }
}
