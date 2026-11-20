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
    public sealed class FixedPotentialTests
    {
        [Test]
        public void TotalBudget_AllowsStrongSpecialityButCountsLowWeightAbilities()
        {
            var save = CreateSave(); var wrestler = save.Wrestlers[0];
            wrestler.Attributes.Brawling = 20; wrestler.Growth.MatchPotentialCap = 11;
            wrestler.Presentation.WrestlingStyleId = "style_001";
            Assert.That(GameSaveValidator.Validate(save), Does.Not.Contain("Match attributes exceed the match potential cap."));
            wrestler.Attributes.Brawling = 10; wrestler.Attributes.HighFlying = 20;
            wrestler.Growth.MatchPotentialCap = 10.3f; wrestler.Presentation.WrestlingStyleId = "style_002";
            Assert.That(GameSaveValidator.Validate(save), Does.Contain("Match attributes exceed the match potential cap."));
        }

        [Test]
        public void PromoTotalBudget_IsIndependentOfRoleWeights()
        {
            var save = CreateSave(); var wrestler = save.Wrestlers[0];
            wrestler.Attributes.Comedy = 20; wrestler.Growth.PromoPotentialCap = 11.1f;
            Assert.That(GameSaveValidator.Validate(save), Does.Contain("Promo attributes exceed the promo potential cap."));
            wrestler.Attributes.Comedy = 10; wrestler.Attributes.FaceWork = 20; wrestler.Growth.PromoPotentialCap = 11.5f;
            Assert.That(GameSaveValidator.Validate(save), Does.Not.Contain("Promo attributes exceed the promo potential cap."));
        }

        [Test]
        public void GrowthRoomAndCaps_DoNotChangeWithStyleOrAlignment()
        {
            var save = CreateSave(); var wrestler = save.Wrestlers[0];
            wrestler.Attributes.Brawling = 20; wrestler.Growth.MatchPotentialCap = 12;
            wrestler.Growth.PromoPotentialCap = 12;
            var matchCap = wrestler.Growth.MatchPotentialCap; var promoCap = wrestler.Growth.PromoPotentialCap;
            foreach (var style in new[] { "style_001", "style_002", "style_004", "style_007" })
            {
                wrestler.Presentation.WrestlingStyleId = style; wrestler.Roster.Alignment = KayfabeAlignment.Heel;
                wrestler.Presentation.PromoDisposition = PromoDisposition.Comic;
                Assert.That(WrestlerOverallCalculator.MatchGrowthRoom(wrestler), Is.EqualTo(1).Within(.001));
                Assert.That(WrestlerOverallCalculator.PromoGrowthRoom(wrestler), Is.EqualTo(2).Within(.001));
                Assert.That(wrestler.Growth.MatchPotentialCap, Is.EqualTo(matchCap)); Assert.That(wrestler.Growth.PromoPotentialCap, Is.EqualTo(promoCap));
            }
        }

        [Test]
        public void VersionFiveMigration_PreservesAttributesAndOnlyRaisesInsufficientCapOnce()
        {
            var save = CreateSave(); var wrestler = save.Wrestlers[0];
            wrestler.Attributes.HighFlying = 20; wrestler.Presentation.WrestlingStyleId = "style_002";
            wrestler.Growth.MatchPotentialCap = 10.3f; wrestler.Growth.PromoPotentialCap = 15;
            var original = JsonUtility.ToJson(wrestler.Attributes); save.SaveVersion = 5;
            var directory = Path.Combine(Path.GetTempPath(), "PWManagerTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "v5.json"), JsonUtility.ToJson(save));
                var service = new SaveService(directory); var restored = service.Load("v5"); var person = restored.Wrestlers[0];
                Assert.That(person.Growth.MatchPotentialCap, Is.EqualTo(11).Within(.001));
                Assert.That(person.Growth.PromoPotentialCap, Is.EqualTo(15)); Assert.That(JsonUtility.ToJson(person.Attributes), Is.EqualTo(original));
                person.Presentation.WrestlingStyleId = "style_001"; service.Save("v5", restored); restored = service.Load("v5");
                Assert.That(restored.Wrestlers[0].Growth.MatchPotentialCap, Is.EqualTo(11).Within(.001));
                Assert.That(restored.SaveVersion, Is.EqualTo(GameSave.CurrentSaveVersion));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void InvalidLegacyCap_IsRejectedRatherThanSilentlyRepaired()
        {
            var save = CreateSave(); save.SaveVersion = 5; save.Wrestlers[0].Growth.MatchPotentialCap = 0;
            var directory = Path.Combine(Path.GetTempPath(), "PWManagerTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "invalid.json"), JsonUtility.ToJson(save));
                Assert.Throws<InvalidDataException>(() => new SaveService(directory).Load("invalid"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void GeneratedCandidates_RespectBothTotalBudgets()
        {
            var content = new StaticContentRegistry(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"));
            var generator = new WrestlerGenerator(content, 112233);
            for (var i = 0; i < 512; i++)
            {
                var wrestler = generator.GenerateCandidate(i % 2 == 0 ? WrestlerGender.Male : WrestlerGender.Female, new GameDate(2026, 6, 1));
                Assert.That(WrestlerOverallCalculator.MatchTotal(wrestler.Attributes), Is.LessThanOrEqualTo(wrestler.Growth.MatchPotentialCap * 10 + .01f));
                Assert.That(WrestlerOverallCalculator.PromoTotal(wrestler.Attributes), Is.LessThanOrEqualTo(wrestler.Growth.PromoPotentialCap * 7 + .01f));
            }
        }

        [Test]
        public void Profile_ShowsFixedPotentialGradesAcrossStyles()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient; var save = CreateSave(); var wrestler = save.Wrestlers[0];
            wrestler.Growth.MatchPotentialCap = 10; wrestler.Growth.PromoPotentialCap = 14;
            var go = new GameObject("Growth profile test"); go.SetActive(false); var controller = go.AddComponent<WrestlerProfileController>();
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            typeof(WrestlerProfileController).GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, root);
            var bind = typeof(WrestlerProfileController).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                DashboardSession.SetActiveSave(save, transient: true); bind.Invoke(controller, new object[] { wrestler });
                Assert.That(root.Q<Label>("wp-match-potential").text, Is.EqualTo("D+"));
                Assert.That(root.Q<Label>("wp-promo-potential").text, Is.EqualTo("B+"));
                Assert.That(root.Query<Label>().ToList().Any(x => x.text == "경기 잠재력"), Is.True);
                Assert.That(root.Q<Label>("wp-match-potential").ClassListContains("grade-dplus"), Is.True);
                wrestler.Presentation.WrestlingStyleId = "style_004"; bind.Invoke(controller, new object[] { wrestler });
                Assert.That(root.Q<Label>("wp-match-potential").text, Is.EqualTo("D+"));
                Assert.That(wrestler.Growth.MatchPotentialCap, Is.EqualTo(10));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); DashboardSession.SetActiveSave(previous, transient); }
        }

        private static GameSave CreateSave()
        {
            var save = GameSave.CreateNew(123, DateTime.UtcNow); save.Promotion = new PromotionState
                { Id = Guid.NewGuid().ToString("D"), Name = "Potential test", InitialCash = 100000 };
            var content = new StaticContentRegistry(Resources.Load<StaticContentCatalog>("PWManagerRuntime/GameStaticContentCatalog"));
            var wrestler = new WrestlerGenerator(content, 234).GenerateCandidate(WrestlerGender.Male, save.CurrentDate);
            foreach (var field in typeof(WrestlerAttributesState).GetFields()) field.SetValue(wrestler.Attributes, 10f);
            wrestler.PromotionId = save.Promotion.Id; wrestler.Roster.ActivityState = RosterActivityState.Active;
            wrestler.Identity.ExpiryDate = default; wrestler.Growth.MatchPotentialCap = wrestler.Growth.PromoPotentialCap = 10;
            save.Wrestlers.Add(wrestler); return save;
        }
    }
}
