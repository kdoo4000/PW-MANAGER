using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Domain.Validation;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class TrainingServiceTests
    {
        private static GameSave Create()
        {
            var save = GameSave.CreateNew(71, DateTime.UtcNow); save.CurrentDate = new GameDate(2026, 9, 7);
            save.Promotion = new() { Id = Guid.NewGuid().ToString("D"), Name = "Training", InitialCash = 2000000, PromotionPrestige = 2500 };
            var catalog = Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog");
            var w = new WrestlerGenerator(new StaticContentRegistry(catalog), 71).GenerateCandidate(WrestlerGender.Male, save.CurrentDate);
            w.PromotionId = save.Promotion.Id; w.Identity.ExpiryDate = default; w.Roster.ActivityState = RosterActivityState.Active;
            w.Presentation.TraitIds.Clear(); w.Growth.MatchPotentialCap = w.Growth.PromoPotentialCap = 20;
            w.Hidden = new WrestlerHiddenState();
            foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(w.Attributes, 10f);
            save.Wrestlers.Add(w); save.Contracts.Add(new() { Id = Guid.NewGuid().ToString("D"), PersonId = w.Id, Type = ContractType.Wrestler,
                StartDate = save.CurrentDate, EndDate = new GameDate(2027, 5, 31), Status = ContractStatus.Active, MonthlySalary = 100 });
            TrainingService.Initialize(save); return save;
        }
        private static void Days(GameSave save, int count, TrainingService service)
        {
            for (var i = 0; i < count; i++) { save.CurrentDate = save.CurrentDate.AddDays(1); service.ProcessDay(save); }
        }
        private static TrainingService Safe() => new(random: new FixedRandom(.999));

        [Test]
        public void Week_AccumulatesRealDailyChoicesAndPaysExperienceOnlyOnce()
        {
            var save = Create(); var service = Safe(); var w = save.Wrestlers[0];
            service.SetPlan(save, w.Id, TrainingMode.Normal, WrestlerAttribute.Power); Days(save, 3, service);
            Assert.That(save.ExperienceRecords, Is.Empty);
            service.SetPlan(save, w.Id, TrainingMode.Focused, WrestlerAttribute.Stamina); Days(save, 4, service);
            Assert.That(save.Training.History, Has.Count.EqualTo(1)); Assert.That(save.ExperienceRecords, Has.Count.EqualTo(1));
            var xp = save.ExperienceRecords.Single();
            Assert.That(xp.Activity, Is.EqualTo(ExperienceActivity.Training)); Assert.That(xp.BaseExperience, Is.EqualTo(4).Within(.0001));
            Assert.That(xp.Progress.Power, Is.EqualTo(3 * 4f / 7 * .12f).Within(.0001));
            Assert.That(xp.Progress.Stamina, Is.GreaterThan(xp.Progress.Power));
            var before = JsonUtility.ToJson(save); service.ProcessDay(save); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            Assert.That(GameSaveValidator.Validate(save), Is.Empty);
        }

        [TestCase(1, .82f)]
        [TestCase(10, 1f)]
        [TestCase(20, 1.2f)]
        public void Professionalism_ChangesBothExperienceAndConditionCost(int professionalism, float multiplier)
        {
            var save = Create(); var w = save.Wrestlers[0]; w.Hidden.Professionalism = professionalism; var service = Safe();
            service.SetPlan(save, w.Id, TrainingMode.Normal, WrestlerAttribute.Power); Days(save, 7, service);
            Assert.That(save.ExperienceRecords.Single().BaseExperience, Is.EqualTo(4 * multiplier).Within(.001));
            Assert.That(save.Training.History.Single().Results.Single().ConditionChange, Is.EqualTo(-6 * multiplier).Within(.001));
        }

        [Test]
        public void LegacyFastLearning_NoLongerAddsGrowthBonus_AndRestDoesNotGiveExperience()
        {
            var save = Create(); var w = save.Wrestlers[0]; w.Presentation.TraitIds.Add("trait_005"); var service = Safe();
            service.SetPlan(save, w.Id, TrainingMode.Focused, WrestlerAttribute.Power); Days(save, 7, service);
            Assert.That(save.ExperienceRecords.Single().Progress.Power, Is.EqualTo(4 * .75f).Within(.001));
            service.SetPlan(save, w.Id, TrainingMode.Rest, WrestlerAttribute.Power); Days(save, 7, service);
            Assert.That(save.ExperienceRecords, Has.Count.EqualTo(1)); Assert.That(save.Training.History.Last().Results.Single().RestDays, Is.EqualTo(7));
        }

        [Test]
        public void SevereInjury_BlocksAssignmentAndExistingTrainingAutomaticallyRests()
        {
            var save = Create(); var w = save.Wrestlers[0]; var service = Safe();
            service.SetPlan(save, w.Id, TrainingMode.Normal, WrestlerAttribute.Power); w.Condition.InjuryStatus = InjuryStatus.Severe;
            Assert.Throws<InvalidOperationException>(() => service.SetPlan(save, w.Id, TrainingMode.Focused, WrestlerAttribute.Power));
            Days(save, 7, service); Assert.That(save.ExperienceRecords, Is.Empty);
            Assert.That(save.Training.History.Single().Results.Single().RestDays, Is.EqualTo(7));
        }

        [Test]
        public void TrainingInjury_UsesActualHealthAndCanRecover()
        {
            var save = Create(); var w = save.Wrestlers[0]; var service = new TrainingService(random: new FixedRandom(0));
            service.SetPlan(save, w.Id, TrainingMode.Focused, WrestlerAttribute.Power); Days(save, 1, service);
            Assert.That(w.Condition.InjuryStatus, Is.EqualTo(InjuryStatus.Minor)); Assert.That(w.Condition.Availability, Is.EqualTo(WrestlerAvailability.Limited));
            MatchHealth.Recover(w, w.Condition.RecoveryDate.Value);
            Assert.That(w.Condition.InjuryStatus, Is.EqualTo(InjuryStatus.None));
        }

        [TestCase("trait_003", 7)]
        [TestCase("trait_004", 7)]
        public void LegacyRecoveryTraits_NoLongerAdjustTrainingInjuryDuration(string trait, int days)
        {
            var save = Create(); var w = save.Wrestlers[0]; w.Presentation.TraitIds.Add(trait);
            var service = new TrainingService(random: new FixedRandom(0));
            service.SetPlan(save, w.Id, TrainingMode.Normal, WrestlerAttribute.Power); Days(save, 1, service);
            Assert.That(w.Condition.RecoveryDate.Value, Is.EqualTo(save.CurrentDate.AddDays(days)));
        }

        [Test]
        public void OptionalTraining_RoundTripDoesNotActivateAndMigrationInitializesCurrentDate()
        {
            var save = Create(); save.Training = null;
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            Assert.DoesNotThrow(() => TrainingService.ValidateState(restored));
            Assert.DoesNotThrow(() => new TimeFlowService().AdvanceTo(restored, restored.CurrentDate.AddDays(1)));
            TrainingService.Initialize(restored);
            Assert.That(restored.Training.IsInitialized, Is.True);
            Assert.That(restored.Training.LastProcessedDate, Is.EqualTo(restored.CurrentDate));
        }

        [Test]
        public void QuarterBoundary_FlushesPartialWeekIntoExistingGrowthAndPreservesCap()
        {
            var save = Create(); save.CurrentDate = new GameDate(2026, 8, 29); save.Contracts[0].StartDate = new GameDate(2026, 6, 1);
            save.Wrestlers[0].Identity.CreatedDate = save.Contracts[0].StartDate; save.Training = null; TrainingService.Initialize(save);
            save.Wrestlers[0].Adaptation.LastProcessedDate = new(save.CurrentDate);
            save.Wrestlers[0].Growth.MatchPotentialCap = save.Wrestlers[0].Growth.PromoPotentialCap = 10;
            Safe().SetPlan(save, save.Wrestlers[0].Id, TrainingMode.Focused, WrestlerAttribute.Power);
            new TimeFlowService().AdvanceTo(save, new GameDate(2026, 9, 1));
            Assert.That(save.Training.History.Single().EndDate, Is.EqualTo(new GameDate(2026, 8, 31)));
            Assert.That(save.ExperienceRecords.Single().SettledQuarter.Value, Is.EqualTo(new GameDate(2026, 8, 31)));
            Assert.That(WrestlerOverallCalculator.MatchTotal(save.Wrestlers[0].Attributes), Is.LessThanOrEqualTo(100.001));
        }

        [Test]
        public void FacilityUpgrade_IsPermanentSequentialAndRequiresSeasonEligibilityAndCash()
        {
            var save = Create(); save.Promotion.PromotionPrestige = 0; PromotionQuarterService.Initialize(save); save.Promotion.PromotionPrestige = 300;
            var service = Safe(); var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => service.Upgrade(save)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            save.Promotion.UnlockedIds.Add(PromotionProgressionRules.TierId(1));
            save.Promotion.InitialCash = 24999; before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => service.Upgrade(save)); Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            save.Promotion.InitialCash = 25000; service.Upgrade(save);
            Assert.That(save.Training.FacilityLevel, Is.EqualTo(2)); Assert.That(save.Transactions.Single().Type, Is.EqualTo(TransactionType.TrainingFacilityUpgrade));
            Assert.That(save.Promotion.CalculateCurrentCash(save.Transactions), Is.Zero);
        }

        [Test]
        public void TrainerControlsExperience_WhileFacilityControlsCapacityAndRecovery()
        {
            var save = Create(); var service = Safe(); var id = save.Wrestlers[0].Id;
            service.SetPlan(save, id, TrainingMode.Normal, WrestlerAttribute.Power); service.Upgrade(save); Days(save, 7, service);
            Assert.That(save.ExperienceRecords.Single().BaseExperience, Is.EqualTo(4).Within(.001));
            service.UpgradeTrainer(save); Days(save, 7, service);
            Assert.That(save.ExperienceRecords.Last().BaseExperience, Is.EqualTo(4.2f).Within(.001));
            Assert.That(save.Training.FacilityLevel, Is.EqualTo(2)); Assert.That(save.Training.TrainerLevel, Is.EqualTo(2));
            Assert.That(save.Transactions.Count(x => x.Type == TransactionType.TrainerUpgrade), Is.EqualTo(1));
        }

        [Test]
        public void TrainerUpgrade_RejectsInsufficientFundsWithoutChangingFacility()
        {
            var save = Create(); save.Promotion.InitialCash = 9999; var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => Safe().UpgradeTrainer(save));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void LegacyFacilityUpgrade_InitializesMatchingTrainerLevelWithoutResettingPendingExperience()
        {
            var save = Create(); var service = Safe(); service.SetPlan(save, save.Wrestlers[0].Id, TrainingMode.Normal, WrestlerAttribute.Power); Days(save, 3, service);
            save.SaveVersion = 14; save.Training.FacilityLevel = 3; save.Training.TrainerLevel = 0;
            var pending = JsonUtility.ToJson(save.Training.Pending[0]);
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-trainer-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "legacy.json"), JsonUtility.ToJson(save));
                var storage = new SaveService(directory); var restored = storage.Load("legacy");
                Assert.That(restored.Training.TrainerLevel, Is.EqualTo(3)); Assert.That(restored.Training.FacilityLevel, Is.EqualTo(3));
                Assert.That(JsonUtility.ToJson(restored.Training.Pending[0]), Is.EqualTo(pending));
                storage.Save("legacy", restored); Assert.That(storage.Load("legacy").Training.TrainerLevel, Is.EqualTo(3));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void CapacityLimitAndUncontractedCandidates_CannotBeBypassed()
        {
            var save = Create(); var service = Safe(); var first = save.Wrestlers[0];
            for (var i = 1; i < 31; i++)
            {
                var w = JsonUtility.FromJson<WrestlerState>(JsonUtility.ToJson(first)); w.Id = Guid.NewGuid().ToString("D"); save.Wrestlers.Add(w);
                var contract = save.Contracts[0].Copy(); contract.Id = Guid.NewGuid().ToString("D"); contract.PersonId = w.Id; save.Contracts.Add(contract);
            }
            foreach (var w in save.Wrestlers.Take(30)) service.SetPlan(save, w.Id, TrainingMode.Normal, WrestlerAttribute.Power);
            Assert.Throws<InvalidOperationException>(() => service.SetPlan(save, save.Wrestlers[30].Id, TrainingMode.Normal, WrestlerAttribute.Power));
            save.Wrestlers[30].PromotionId = null;
            Assert.Throws<InvalidOperationException>(() => service.SetPlan(save, save.Wrestlers[30].Id, TrainingMode.Rest, WrestlerAttribute.Power));
        }

        [Test]
        public void FailedWeeklyRecordId_DoesNotPartiallyPayExperienceOrCondition()
        {
            var save = Create(); var service = Safe(); service.SetPlan(save, save.Wrestlers[0].Id, TrainingMode.Normal, WrestlerAttribute.Power); Days(save, 6, service);
            save.CurrentDate = save.CurrentDate.AddDays(1); var before = JsonUtility.ToJson(save);
            Assert.Throws<InvalidOperationException>(() => new TrainingService(() => save.Wrestlers[0].Id, new FixedRandom(.999)).ProcessDay(save));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
        }

        [Test]
        public void SaveUpgradeAndReload_PreservePendingPlanWithoutRetroactiveTraining()
        {
            var save = Create(); var current = save.CurrentDate; save.SaveVersion = 13; save.Training = null;
            var directory = Path.Combine(Path.GetTempPath(), "pwmanager-training-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "legacy.json"), JsonUtility.ToJson(save));
                var storage = new SaveService(directory); var restored = storage.Load("legacy");
                Assert.That(restored.Training.LastProcessedDate, Is.EqualTo(current)); Assert.That(restored.Training.History, Is.Empty);
                Safe().SetPlan(restored, restored.Wrestlers[0].Id, TrainingMode.Normal, WrestlerAttribute.Power); Days(restored, 3, Safe());
                storage.Save("legacy", restored); var resumed = storage.Load("legacy"); Days(resumed, 4, Safe());
                Assert.That(resumed.ExperienceRecords, Has.Count.EqualTo(1)); Assert.That(GameSaveValidator.Validate(resumed), Is.Empty);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CorruptTrainingHistory_IsRejectedWithoutCrashingValidation(bool missingResults)
        {
            var save = Create(); var service = Safe();
            service.SetPlan(save, save.Wrestlers[0].Id, TrainingMode.Normal, WrestlerAttribute.Power); Days(save, 7, service);
            if (missingResults) save.Training.History[0].Results = null;
            else save.Training.WeekStartDate = save.Training.WeekStartDate.AddDays(-7);
            Assert.That(GameSaveValidator.Validate(save), Is.Not.Empty);
        }

        [Test]
        public void TrainingScreen_AppliesFocusedPlanAndSeparateUpgradesAndLinksWrestler()
        {
            var save = Create(); var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            try
            {
                DashboardSession.SetActiveSave(save, true);
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree(); DashboardViewHost.MountMissingViews(root);
                var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.DashboardTrainingController");
                var controller = Activator.CreateInstance(type, new object[] { root }); type.GetMethod("Render").Invoke(controller, new object[] { save });
                Assert.That(root.Q("training-trainer-card").ClassListContains("facility-card"), Is.True);
                Assert.That(root.Q("training-facility-card").ClassListContains("facility-card"), Is.True);
                Assert.That(root.Q("facilities-content").Q("training-panel"), Is.Null);
                var id = save.Wrestlers[0].Id; root.Q<DropdownField>("training-mode-" + id).index = 2;
                root.Q<DropdownField>("training-focus-" + id).index = (int)WrestlerAttribute.Power; Click(root.Q<Button>("training-apply-" + id));
                Assert.That(save.Training.Plans.Single().Mode, Is.EqualTo(TrainingMode.Focused));
                Assert.That(root.Q("training-roster").Query<Button>(className: "wrestler-link").ToList(), Has.Count.EqualTo(1));
                Assert.That(root.Q<ProfileMetricGauge>("training-condition").Value, Is.EqualTo(save.Wrestlers[0].Condition.Condition));
                Click(root.Q<Button>("training-tab-resources"));
                Assert.That(root.Q("training-resources-panel").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q("training-plans-panel").style.display.value, Is.EqualTo(DisplayStyle.None));
                Click(root.Q<Button>("training-upgrade")); Assert.That(save.Training.FacilityLevel, Is.EqualTo(2));
                Assert.That(save.Training.TrainerLevel, Is.EqualTo(1));
                Click(root.Q<Button>("training-trainer-upgrade")); Assert.That(save.Training.TrainerLevel, Is.EqualTo(2));
                Assert.That(save.Training.FacilityLevel, Is.EqualTo(2));
                Assert.That(root.Q("training-resources-panel").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            }
            finally { DashboardSession.SetActiveSave(previous, transient); }
        }

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
        private sealed class FixedRandom : System.Random { private readonly double value; public FixedRandom(double value) => this.value = value; public override double NextDouble() => value; }
    }
}
