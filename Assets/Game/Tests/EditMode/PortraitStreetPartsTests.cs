using System;
using System.Linq;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class PortraitStreetPartsTests
    {
        [TearDown]
        public void ClearCache() => PortraitRenderer.ClearCache();

        [TestCase(WrestlerGender.Male, 11)]
        [TestCase(WrestlerGender.Male, 12)]
        [TestCase(WrestlerGender.Female, 8)]
        [TestCase(WrestlerGender.Female, 9)]
        public void LegacyBodyPaintStillMigratesWithoutBecomingNewTattoo(WrestlerGender gender, int oldPaint)
        {
            var appearance = new PortraitAppearanceState();
            appearance.SetPart("tattoo", oldPaint); appearance.SetColor("tattoo", "#c92c32");
            PortraitAppearanceRules.Normalize(appearance, gender);
            Assert.That(appearance.Part("tattoo"), Is.EqualTo(1));
            Assert.That(PortraitCatalog.BodyPaintSources(gender)[appearance.Part("bodypaint") - 2], Is.EqualTo(oldPaint));
            Assert.That(appearance.Color("bodypaint"), Is.EqualTo("#c92c32"));
        }

        [TestCase(WrestlerGender.Male)]
        [TestCase(WrestlerGender.Female)]
        public void ExistingAppearanceKeepsItsPartIdsAndColors(WrestlerGender gender)
        {
            var appearance = new PortraitAppearanceState { BodyType = 2 };
            appearance.SetPart("hair", 2); appearance.SetPart("hairback", 6);
            appearance.SetPart("tattoo", 7); appearance.SetPart("clothes", 2);
            appearance.SetColor("hair", "#513525"); appearance.SetColor("clothes", "#194e47");
            PortraitAppearanceRules.Normalize(appearance, gender);
            var saved = JsonUtility.ToJson(appearance);
            var restored = JsonUtility.FromJson<PortraitAppearanceState>(saved);
            PortraitAppearanceRules.Normalize(restored, gender);
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(saved));
        }

        [TestCase(WrestlerGender.Male, 17, 19, 4)]
        [TestCase(WrestlerGender.Female, 11, 15, 3)]
        public void NewHairAndHeadwearRenderWithEveryFaceAndBandanaMask(WrestlerGender gender, int first, int last, int faces)
        {
            Texture2D texture = null;
            try
            {
                for (var hair = first; hair <= last; hair++)
                for (var face = 1; face <= faces; face++)
                for (var hat = 1; hat <= 7; hat++)
                foreach (var mask in new[] { 1, 12 })
                {
                    var appearance = new PortraitAppearanceState();
                    appearance.SetPart("hair", hair); appearance.SetPart("head", face);
                    appearance.SetPart("headwear", hat); appearance.SetPart("mask", mask);
                    appearance.SetPart("hairback", gender == WrestlerGender.Male ? 6 : 7);
                    appearance.SetColor("headwear", "#b82c35"); appearance.SetColor("headwear-secondary", "#d3a52b");
                    texture = PortraitRenderer.Render(appearance, gender, texture);
                    Assert.That(appearance.Part("headwear"), Is.EqualTo(hat));
                    Assert.That(texture.GetPixels32().Count(pixel => pixel.a > 128), Is.GreaterThan(40000));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [TestCase(WrestlerGender.Male, 13, 14, 14)]
        [TestCase(WrestlerGender.Female, 10, 11, 17)]
        public void NewTattoosAndGiKeepTheirIdsOnEveryBodyType(WrestlerGender gender, int arms, int full, int gi)
        {
            Texture2D texture = null;
            try
            {
                for (var body = 1; body <= 3; body++)
                foreach (var tattoo in new[] { arms, full })
                foreach (var clothes in new[] { 2, gi })
                {
                    var appearance = new PortraitAppearanceState { BodyType = body };
                    appearance.SetPart("tattoo", tattoo); appearance.SetPart("clothes", clothes);
                    appearance.SetColor("tattoo", "#111111"); appearance.SetColor("clothes", "#194e47");
                    appearance.SetColor("clothes-secondary", "#e1b548");
                    texture = PortraitRenderer.Render(appearance, gender, texture);
                    Assert.That(appearance.Part("tattoo"), Is.EqualTo(tattoo));
                    Assert.That(appearance.Part("clothes"), Is.EqualTo(clothes));
                    Assert.That(appearance.Part("bodypaint"), Is.EqualTo(1));
                    var restored = JsonUtility.FromJson<PortraitAppearanceState>(JsonUtility.ToJson(appearance));
                    PortraitAppearanceRules.Normalize(restored, gender);
                    Assert.That(restored.Part("tattoo"), Is.EqualTo(tattoo));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [TestCase(WrestlerGender.Male)]
        [TestCase(WrestlerGender.Female)]
        public void BodyPaintSlotsAreExcludedFromTattooSelectionsAndGeneration(WrestlerGender gender)
        {
            var random = new System.Random(238);
            foreach (var oldPaint in PortraitCatalog.BodyPaintSources(gender))
                Assert.That(PortraitCatalog.Selectable(gender, "tattoo", oldPaint), Is.False);
            for (var i = 0; i < 1000; i++)
            {
                var appearance = PortraitAppearanceRules.Generate(new WrestlerIdentityState { Gender = gender }, null, random);
                Assert.That(PortraitCatalog.Selectable(gender, "tattoo", appearance.Part("tattoo")), Is.True);
                Assert.That(appearance.Part("bodypaint"), Is.EqualTo(1));
            }
        }

        [TestCase(WrestlerGender.Male)]
        [TestCase(WrestlerGender.Female)]
        public void BandanaPreservesBrowsAndEyesButHidesNoseAndMouth(WrestlerGender gender)
        {
            var appearance = new PortraitAppearanceState();
            appearance.SetPart("mask", 12); appearance.SetPart("headwear", 7);
            Texture2D texture = null;
            try
            {
                texture = PortraitRenderer.Render(appearance, gender);
                var original = texture.GetPixels32();
                appearance.SetPart("nose", 3); appearance.SetPart("mouth", 6);
                texture = PortraitRenderer.Render(appearance, gender, texture);
                Assert.That(texture.GetPixels32(), Is.EqualTo(original));
                appearance.SetPart("brows", 6); appearance.SetColor("eyes", "#ff0000");
                texture = PortraitRenderer.Render(appearance, gender, texture);
                Assert.That(texture.GetPixels32(), Is.Not.EqualTo(original));
                Assert.That(appearance.Part("headwear"), Is.EqualTo(7));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test]
        public void BaldBeanieAndHeadwearColorsUseFittedAssetsAndSurviveWardrobeEdit()
        {
            var original = new PortraitAppearanceState();
            original.SetPart("hair", 11); original.SetPart("headwear", 3);
            Texture2D texture = null;
            try
            {
                texture = PortraitRenderer.Render(original, WrestlerGender.Male);
                var pixels = texture.GetPixels32();
                var edit = original.Clone(); edit.SetColor("headwear", "#c92c32"); edit.SetColor("headwear-secondary", "#eeeeee");
                PortraitAppearanceRules.ApplyWardrobe(original, edit, WrestlerGender.Male);
                texture = PortraitRenderer.Render(original, WrestlerGender.Male, texture);
                Assert.That(texture.GetPixels32(), Is.Not.EqualTo(pixels));
                Assert.That(original.Color("headwear"), Is.EqualTo("#c92c32"));
                Assert.That(original.Color("headwear-secondary"), Is.EqualTo("#eeeeee"));
                Assert.That(Resources.Load<Texture2D>("PWManagerUI/Portraits/Male/headwear-3-bald"), Is.Not.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
