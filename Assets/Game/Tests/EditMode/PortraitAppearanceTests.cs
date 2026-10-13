using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Data.Catalogs;
using PWManager.Data.Generation;
using PWManager.Data.Loading;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class PortraitAppearanceTests
    {
        [TestCase(WrestlerGender.Male)]
        [TestCase(WrestlerGender.Female)]
        public void SeparateBodyPaintRendersOnEveryBodyTypeWithoutReplacingTattoo(WrestlerGender gender)
        {
            for (var body = 1; body <= 3; body++)
            {
                var appearance = new PortraitAppearanceState { BodyType = body };
                appearance.SetPart("tattoo", 2); appearance.SetPart("bodypaint", 1); appearance.SetColor("tattoo", "#111111");
                Texture2D texture = null;
                try
                {
                    texture = PortraitRenderer.Render(appearance, gender);
                    var original = texture.GetPixels32();
                    foreach (var paint in new[] { 2, 3 })
                    {
                        appearance.SetPart("bodypaint", paint); appearance.SetColor("bodypaint", "#c92c32");
                        texture = PortraitRenderer.Render(appearance, gender, texture);
                        var painted = texture.GetPixels32();
                        Assert.That(painted.Where((pixel, index) => !pixel.Equals(original[index])).Count(), Is.GreaterThan(100));
                        Assert.That(appearance.Part("tattoo"), Is.EqualTo(2));
                        Assert.That(appearance.Color("tattoo"), Is.EqualTo("#111111"));
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            PortraitRenderer.ClearCache();
        }

        [TestCase(WrestlerGender.Male)]
        [TestCase(WrestlerGender.Female)]
        public void GeneratedSpecialPartsUseSeparateWearProbabilitiesAndBlackTattoos(WrestlerGender gender)
        {
            var random = new System.Random(921);
            var categories = new[] { "mask", "tattoo", "facepaint", "bodypaint", "scars", "clothes" };
            var counts = categories.ToDictionary(x => x, _ => 0);
            for (var i = 0; i < 10000; i++)
            {
                var appearance = PortraitAppearanceRules.Generate(new WrestlerIdentityState { Gender = gender }, "style_007", random);
                foreach (var category in categories) if (appearance.Part(category) > 1) counts[category]++;
                Assert.That(appearance.Color("tattoo"), Is.EqualTo("#111111"));
                Assert.That(appearance.Part("pupils"), Is.EqualTo(1));
                Assert.That(appearance.Color("eyes"), Is.EqualTo(PortraitAppearanceRules.EyeColors[0]));
            }
            Assert.That(counts["mask"], Is.InRange(350, 650));
            Assert.That(counts["tattoo"], Is.InRange(2300, 2700));
            Assert.That(counts["facepaint"], Is.Zero);
            Assert.That(counts["bodypaint"], Is.Zero);
            Assert.That(counts["scars"], Is.InRange(600, 1000));
            Assert.That(counts["clothes"], gender == WrestlerGender.Female ? Is.EqualTo(10000) : Is.InRange(3250, 3750));
        }

        [Test]
        public void EastAsianNameSkinBiasFavorsMiddleAndLightTonesWithoutFixingThem()
        {
            var biased = new System.Random(41);
            var unrestricted = new System.Random(41);
            var preferredCount = 0;
            var ordinaryCount = 0;
            var values = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < 10000; i++)
            {
                var skin = PortraitAppearanceRules.Generate(new WrestlerIdentityState(), null, biased, true).SkinBrightness;
                var ordinary = PortraitAppearanceRules.Generate(new WrestlerIdentityState(), null, unrestricted).SkinBrightness;
                if (skin >= 90 && skin <= 120) preferredCount++;
                if (ordinary >= 90 && ordinary <= 120) ordinaryCount++;
                values.Add(skin);
            }
            Assert.That(preferredCount, Is.InRange(9200, 9550));
            Assert.That(ordinaryCount, Is.InRange(3600, 4100));
            Assert.That(values.Min(), Is.LessThan(75));
            Assert.That(values.Max(), Is.GreaterThan(125));
        }

        [TestCase(WrestlerGender.Male, 11)]
        [TestCase(WrestlerGender.Male, 12)]
        [TestCase(WrestlerGender.Female, 8)]
        [TestCase(WrestlerGender.Female, 9)]
        public void LegacyBodyPaintMigratesWithoutChangingItsArtOrColor(WrestlerGender gender, int oldId)
        {
            var appearance = new PortraitAppearanceState(); appearance.SetPart("tattoo", oldId); appearance.SetColor("tattoo", "#c92c32");
            PortraitAppearanceRules.Normalize(appearance, gender);
            Assert.That(appearance.Part("tattoo"), Is.EqualTo(1));
            Assert.That(PortraitCatalog.BodyPaintSources(gender)[appearance.Part("bodypaint") - 2], Is.EqualTo(oldId));
            Assert.That(appearance.Color("bodypaint"), Is.EqualTo("#c92c32"));
            Assert.That(appearance.Color("tattoo"), Is.EqualTo("#111111"));
            var saved = JsonUtility.ToJson(appearance); PortraitAppearanceRules.Normalize(appearance, gender);
            Assert.That(JsonUtility.ToJson(appearance), Is.EqualTo(saved));
        }

        [Test]
        public void PartArrowsWrapAndSkipIncompatibleOptionsWithoutChangingSource()
        {
            var original = new PortraitAppearanceState(); original.SetPart("clothes", 2);
            var editor = new PortraitEditorView(original, WrestlerGender.Female, true);
            var window = ScriptableObject.CreateInstance<PortraitPaletteTestWindow>();
            try
            {
                window.Show(); window.rootVisualElement.Add(editor);
                void Click(string name) => typeof(Clickable).GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(editor.Q<Button>(name).clickable, new object[] { null });
                Click("portrait-clothes-previous");
                Assert.That(editor.Appearance.Part("clothes"), Is.EqualTo(16));
                Click("portrait-clothes-next");
                Assert.That(editor.Appearance.Part("clothes"), Is.EqualTo(2));
                Assert.That(editor.Q<Label>("portrait-clothes-value").text, Is.EqualTo("의상 1"));
                Click("portrait-mask-next");
                Assert.That(editor.Appearance.Part("mask"), Is.EqualTo(2));
                Assert.That(editor.Q<Button>("portrait-eyewear-next").enabledSelf, Is.False);
                Assert.That(editor.Q<Button>("portrait-headwear-next").enabledSelf, Is.False);
                Assert.That(original.Part("mask"), Is.EqualTo(1));
                Assert.That(editor.Query<DropdownField>().ToList(), Is.Empty);
                Click("portrait-tab-얼굴");
                Assert.That(editor.Q("portrait-head"), Is.Null);
                Assert.That(editor.Q("portrait-eyes"), Is.Null);
                Assert.That(editor.Q("portrait-color-eyes"), Is.Not.Null);
                Click("portrait-pupils-next"); Click("portrait-facepaint-next");
                Assert.That(editor.Appearance.Part("pupils"), Is.EqualTo(2));
                Assert.That(editor.Appearance.Part("facepaint"), Is.EqualTo(2));
                Click("portrait-tab-신체");
                Assert.That(editor.Q("portrait-bodytype"), Is.Null);
                Assert.That(editor.Q("portrait-skin"), Is.Null);
                Assert.That(editor.Q("portrait-tattoo"), Is.Null);
                Click("portrait-bodypaint-next");
                Assert.That(editor.Appearance.Part("bodypaint"), Is.EqualTo(2));
                Assert.That(original.Part("pupils"), Is.EqualTo(1));
                Assert.That(original.Part("facepaint"), Is.EqualTo(1));
                Assert.That(original.Part("bodypaint"), Is.EqualTo(1));
                var bodyEditor = new PortraitEditorView(new PortraitAppearanceState(), WrestlerGender.Female);
                window.rootVisualElement.Add(bodyEditor);
                Assert.That(bodyEditor.Q("portrait-tattoo"), Is.Not.Null);
                Assert.That(bodyEditor.Q("portrait-bodypaint"), Is.Not.Null);
            }
            finally { window.Close(); PortraitRenderer.ClearCache(); }
        }

        [TestCase(WrestlerAvailability.Available, false, InjuryStatus.None, true)]
        [TestCase(WrestlerAvailability.Limited, false, InjuryStatus.Minor, true)]
        [TestCase(WrestlerAvailability.MatchUnavailable, false, InjuryStatus.None, false)]
        [TestCase(WrestlerAvailability.Unavailable, false, InjuryStatus.None, false)]
        [TestCase(WrestlerAvailability.Available, true, InjuryStatus.Minor, false)]
        [TestCase(WrestlerAvailability.Available, true, InjuryStatus.None, true)]
        public void ProfileAvailabilityShowsBinaryMatchStatusInStateHeader(WrestlerAvailability availability, bool absent, InjuryStatus storyInjury, bool expected)
        {
            var host = new GameObject("Profile availability test");
            var instance = typeof(WrestlerProfileController).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
            var previousInstance = instance.GetValue(null);
            try
            {
                host.AddComponent<UIDocument>();
                var controller = host.AddComponent<WrestlerProfileController>();
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree().Q<VisualElement>("wrestler-profile");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(WrestlerProfileController).GetField("root", flags).SetValue(controller, root);
                var wrestler = new WrestlerState { Id = "availability-test" };
                wrestler.Condition.Availability = availability;
                wrestler.KayfabeInjury = new KayfabeInjuryState { Absent = absent, Severity = storyInjury };
                typeof(WrestlerProfileController).GetMethod("Bind", flags).Invoke(controller, new object[] { wrestler });
                var label = root.Q<Label>("wp-availability");
                Assert.That(label.text, Is.EqualTo(expected ? "출전 가능" : "출전 불가"));
                Assert.That(label.parent.ClassListContains("wp-state-heading"), Is.True);
                Assert.That(label.ClassListContains(expected ? "state-good" : "state-danger"), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); instance.SetValue(null, previousInstance); PortraitRenderer.ClearCache(); }
        }

        [TestCase(-10f, 0f)]
        [TestCase(140f, 100f)]
        [TestCase(float.NaN, 0f)]
        public void ProfileGaugeClampsValueAndKeepsExactTooltip(float input, float expected)
        {
            var gauge = new ProfileMetricGauge();
            gauge.SetValue("컨디션", input, "좋음", Color.green);
            Assert.That(gauge.Value, Is.EqualTo(expected));
            Assert.That(gauge.tooltip, Does.Contain(expected.ToString("0") + " / 100"));
            Assert.That(gauge.Q<Label>().text, Is.EqualTo("좋음"));
            Assert.That(gauge.focusable, Is.True);
        }

        [Test]
        public void ProfileGaugeShowsNumberOnlyWhileHoveredAndUpdatesHoveredValue()
        {
            var host = new GameObject("Gauge hover test");
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                var gauge = new ProfileMetricGauge();
                document.rootVisualElement.Add(gauge);
                var label = gauge.Q<Label>();
                gauge.SetValue("컨디션", 82f, "좋음", Color.green);
                gauge.Focus();
                using (var click = ClickEvent.GetPooled()) { click.target = gauge; gauge.SendEvent(click); }
                Assert.That(label.text, Is.EqualTo("좋음"));
                using (var enter = PointerEnterEvent.GetPooled(new Event { type = EventType.MouseMove })) { enter.target = gauge; gauge.SendEvent(enter); }
                Assert.That(label.text, Is.EqualTo("82"));
                gauge.Blur();
                Assert.That(label.text, Is.EqualTo("82"));
                gauge.SetValue("컨디션", 71f, "보통", Color.yellow);
                Assert.That(label.text, Is.EqualTo("71"));
                using (var leave = PointerLeaveEvent.GetPooled(new Event { type = EventType.MouseMove })) { leave.target = gauge; gauge.SendEvent(leave); }
                Assert.That(label.text, Is.EqualTo("보통"));
                gauge.Focus();
                Assert.That(label.text, Is.EqualTo("보통"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void ProfileGroupsStandingAndMomentumWithAudienceAndPreservesSeparateFanAxes()
        {
            var host = new GameObject("Profile metrics test");
            var instance = typeof(WrestlerProfileController).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
            var previousInstance = instance.GetValue(null);
            try
            {
                host.AddComponent<UIDocument>();
                var controller = host.AddComponent<WrestlerProfileController>();
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree().Q<VisualElement>("wrestler-profile");
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(WrestlerProfileController).GetField("root", flags).SetValue(controller, root);
                var wrestler = new WrestlerState { Id = "profile-metrics" };
                wrestler.Condition.Condition = 82;
                wrestler.Condition.Satisfaction = 35;
                wrestler.Momentum.Momentum = 71;
                wrestler.FanReaction.UsesTwoAxes = true;
                wrestler.FanReaction.Hardcore = new FanResponseState { Preference = 15, Interest = 90 };
                typeof(WrestlerProfileController).GetMethod("Bind", flags).Invoke(controller, new object[] { wrestler });
                var standing = root.Q("wp-standing-card");
                Assert.That(standing.Q("wp-card-status"), Is.Not.Null);
                Assert.That(standing.Q("wp-momentum").Q<ProfileMetricGauge>().Value, Is.EqualTo(71));
                var profile = root.Q(className: "wp-player-panel");
                Assert.That(profile.parent.ClassListContains("wp-layout"), Is.True);
                Assert.That(profile.Q("wp-card-status"), Is.Null);
                Assert.That(profile.Q("wp-current-state").Q("wp-condition").Q<ProfileMetricGauge>().Value, Is.EqualTo(82));
                Assert.That(profile.Q("wp-satisfaction").Q<ProfileMetricGauge>().Value, Is.EqualTo(35));
                Assert.That(profile.Q("wp-momentum"), Is.Null);
                Assert.That(standing.Q("wp-mania-preference").Q<ProgressBar>().value, Is.EqualTo(15));
                Assert.That(standing.Q("wp-mania-interest").Q<ProgressBar>().value, Is.EqualTo(90));
                Assert.That(standing.Query<ProgressBar>().ToList(), Has.Count.EqualTo(6));
                Assert.That(profile.Q<Label>("wp-real-injury"), Is.Not.Null);
                Assert.That(profile.Q<Label>("wp-story-injury"), Is.Not.Null);
                Assert.That(profile.Q<Label>("wp-stable"), Is.Not.Null);
                var abilities = root.Q(className: "wp-abilities-panel");
                var settings = root.Q(className: "wp-layout-bottom");
                Assert.That(abilities.parent.ClassListContains("wp-layout-top"), Is.True);
                Assert.That(settings.parent, Is.SameAs(abilities.parent.parent));
                Assert.That(settings.childCount, Is.EqualTo(2));
                Assert.That(settings.Q(className: "wp-settings-pair").childCount, Is.EqualTo(2));
                Assert.That(root.Q(className: "wp-last-card").parent, Is.SameAs(settings));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); instance.SetValue(null, previousInstance); PortraitRenderer.ClearCache(); }
        }

        [Test]
        public void PortraitRecreatesObjectsDestroyedWhenLeavingPlayMode()
        {
            var appearance = new PortraitAppearanceState();
            var first = PortraitRenderer.Get(appearance, WrestlerGender.Male, "destroyed-portrait-test");
            var material = typeof(PortraitRenderer).GetField("composite", BindingFlags.Static | BindingFlags.NonPublic);
            UnityEngine.Object.DestroyImmediate((Material)material.GetValue(null));
            UnityEngine.Object.DestroyImmediate(first);
            var next = PortraitRenderer.Get(appearance, WrestlerGender.Male, "destroyed-portrait-test");
            Assert.That(next != null, Is.True);
            Assert.That((Material)material.GetValue(null) != null, Is.True);
            Assert.That(next.width, Is.EqualTo(PortraitRenderer.Width));
            Assert.That(next.height, Is.EqualTo(PortraitRenderer.Height));
            Assert.That(next.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }

        [TestCase(300f, 200f)]
        [TestCase(100f, 300f)]
        [TestCase(512f, 461f)]
        public void ShowPortraitCropFillsCardWithoutStretchingAndKeepsHeadVisible(float width, float height)
        {
            var method = typeof(TrapezoidPortraitCard).GetMethod("PortraitUvs", BindingFlags.Static | BindingFlags.NonPublic);
            var uv = (Rect)method.Invoke(null, new object[] { width, height, 512f, 461f });
            Assert.That(uv.width, Is.InRange(0f, 1f));
            Assert.That(uv.height, Is.InRange(0f, 1f));
            Assert.That(Mathf.Max(uv.width, uv.height), Is.EqualTo(1f).Within(.0001f));
            Assert.That(uv.width * 512f / (uv.height * 461f), Is.EqualTo(width / height).Within(.0001f));
            Assert.That(uv.center.x, Is.EqualTo(.5f).Within(.0001f));
            Assert.That(uv.yMax, Is.EqualTo(1f).Within(.0001f));
        }

        [Test]
        public void ShowParticipantUsesFullCardPortraitAndKeepsProfileNameLink()
        {
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard").CloneTree();
            DashboardViewHost.MountMissingViews(root);
            var type = typeof(DashboardController).Assembly.GetType("PWManager.Presentation.ShowPlanningController");
            var controller = Activator.CreateInstance(type, root);
            var wrestler = new WrestlerState { Id = "full-card-portrait" };
            var save = new GameSave(); save.Wrestlers.Add(wrestler);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("save", flags).SetValue(controller, save);
            var host = new VisualElement();
            var card = (TrapezoidPortraitCard)type.GetMethod("AddParticipantPortrait", flags).Invoke(controller, new object[] { host, wrestler.Id, -1, true });
            Assert.That(card.Portrait, Is.SameAs(PortraitRenderer.Get(wrestler)));
            Assert.That(card.Q(className: "wrestler-portrait-frame"), Is.Null);
            var name = card.Q<Button>(className: "show-participant-portrait-name");
            Assert.That(name, Is.Not.Null);
            Assert.That(name.ClassListContains("wrestler-link"), Is.True);
            Assert.That(name.text, Is.EqualTo(WrestlerNameText.DisplayName(wrestler)));
        }

        [TestCase(WrestlerBodyType.Lightweight, 2)]
        [TestCase(WrestlerBodyType.Balanced, 1)]
        [TestCase(WrestlerBodyType.Muscular, 1)]
        [TestCase(WrestlerBodyType.Heavyweight, 3)]
        [TestCase(WrestlerBodyType.Giant, 3)]
        public void GeneratedBodyMatchesPhysicalType(WrestlerBodyType body, int expected)
        {
            foreach (var gender in new[] { WrestlerGender.Male, WrestlerGender.Female })
            {
                var appearance = PortraitAppearanceRules.Generate(new WrestlerIdentityState { BodyType = body, Gender = gender }, "style_005", new System.Random(44));
                Assert.That(appearance.BodyType, Is.EqualTo(expected));
                Assert.That(appearance.Parts.Any(x => x.Category == "wrinkles"), Is.False);
                if (gender == WrestlerGender.Female) Assert.That(appearance.Part("clothes"), Is.GreaterThan(1));
            }
        }

        [Test]
        public void CompatibilityAppliesToSelectionsAndSavedWardrobe()
        {
            var appearance = new PortraitAppearanceState();
            appearance.SetPart("clothes", 1); appearance.SetPart("mask", 10); appearance.SetPart("eyewear", 3); appearance.SetPart("headwear", 4);
            PortraitAppearanceRules.Normalize(appearance, WrestlerGender.Female);
            Assert.That(appearance.Part("clothes"), Is.EqualTo(2));
            Assert.That(appearance.Part("eyewear"), Is.EqualTo(1));
            Assert.That(appearance.Part("headwear"), Is.EqualTo(1));
            foreach (var front in new[] { 11, 12, 13, 16 })
            {
                appearance.SetPart("hair", front); appearance.SetPart("hairback", 6);
                PortraitAppearanceRules.Normalize(appearance, WrestlerGender.Male);
                Assert.That(appearance.Part("hairback"), Is.EqualTo(1));
            }
        }

        [Test]
        public void WardrobeEditCannotChangePhysicalAppearance()
        {
            var identity = new WrestlerIdentityState { Gender = WrestlerGender.Female, BodyType = WrestlerBodyType.Heavyweight };
            var original = PortraitAppearanceRules.Generate(identity, null, new System.Random(12));
            var edit = PortraitAppearanceRules.Generate(identity, null, new System.Random(19));
            edit.BodyType = 2; edit.SkinBrightness = 40;
            edit.SetPart("pupils", 3); edit.SetPart("facepaint", 2); edit.SetPart("bodypaint", 3);
            edit.SetColor("eyes", "#274d69"); edit.SetColor("facepaint", "#c92c32");
            edit.SetColor("facepaint-secondary", "#eeeeee"); edit.SetColor("bodypaint", "#194e47");
            var physical = original.Parts.Where(x => !PortraitCatalog.Groups(identity.Gender).Single(g => g.Category == x.Category).Wardrobe).Select(x => (x.Category, x.Index)).ToList();
            var skin = original.SkinBrightness; var hair = original.Color("hair");
            PortraitAppearanceRules.ApplyWardrobe(original, edit, identity.Gender);
            Assert.That(original.BodyType, Is.EqualTo(3)); Assert.That(original.SkinBrightness, Is.EqualTo(skin));
            Assert.That(original.Color("hair"), Is.EqualTo(hair));
            foreach (var (category, index) in physical) Assert.That(original.Part(category), Is.EqualTo(index));
            Assert.That(original.Part("clothes"), Is.EqualTo(edit.Part("clothes")));
            foreach (var category in new[] { "pupils", "facepaint", "bodypaint" }) Assert.That(original.Part(category), Is.EqualTo(edit.Part(category)));
            foreach (var category in PortraitAppearanceRules.WardrobeColorCategories) Assert.That(original.Color(category), Is.EqualTo(edit.Color(category)));
        }

        [Test]
        public void LuchadorMasksAreOptionalAndOnlyLuchaMasks()
        {
            var random = new System.Random(884); var masked = 0;
            for (var i = 0; i < 1000; i++)
            {
                var appearance = PortraitAppearanceRules.Generate(new WrestlerIdentityState(), "style_005", random);
                var mask = appearance.Part("mask");
                if (mask == 1) continue;
                Assert.That(PortraitCatalog.LuchaMasks, Does.Contain(mask)); masked++;
            }
            Assert.That(masked, Is.InRange(640, 760));
        }

        [Test]
        public void GeneratedCandidatesAndPlayerAppearanceSurviveGameStartAndSerialization()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StaticContentCatalog>("Assets/Game/Data/Static/GameStaticContentCatalog.asset");
            var candidates = new WrestlerGenerator(new StaticContentRegistry(catalog), 42).GenerateInitialCandidates(new GameDate(2026, 6, 1));
            foreach (var wrestler in candidates) Assert.That(wrestler.Presentation.Appearance.BodyType, Is.EqualTo(PortraitAppearanceRules.BodyType(wrestler.Identity)));
            var player = new PortraitAppearanceState { BodyType = 3 }; player.SetPart("clothes", 11); player.SetColor("clothes", "#194e47");
            PortraitAppearanceRules.Normalize(player, WrestlerGender.Female);
            var request = new GameStartRequest
            {
                HasPlayerCharacter = true, PlayerName = "플레이어", PlayerGender = WrestlerGender.Female,
                PlayerHeightCm = 170, PlayerWeightKg = 95, PlayerAppearance = player,
                PromotionName = "Portrait Test", InitialCash = 100000, WorldSeed = 42, UtcNow = DateTime.UtcNow,
                RegularVenueId = "venue_001", WrestlerContracts = candidates.Take(8).Select(x => new InitialWrestlerContractInput { Wrestler = x }).ToList()
            };
            var save = new GameStartService().CreateInitialSave(request);
            var restored = JsonUtility.FromJson<GameSave>(JsonUtility.ToJson(save));
            var wrestlerPlayer = restored.Wrestlers.Single(x => x.Id == restored.Player.WrestlerId);
            Assert.That(JsonUtility.ToJson(wrestlerPlayer.Presentation.Appearance), Is.EqualTo(JsonUtility.ToJson(player)));
            Assert.That(restored.Player.Appearance, Is.Not.Null);
            foreach (var candidate in candidates.Take(8))
                Assert.That(JsonUtility.ToJson(restored.Wrestlers.Single(x => x.Id == candidate.Id).Presentation.Appearance), Is.EqualTo(JsonUtility.ToJson(candidate.Presentation.Appearance)));
            save.Player.Appearance.SetPart("clothes", 2); Assert.That(player.Part("clothes"), Is.EqualTo(11));
        }

        [Test]
        public void MissingOldAppearanceIsStableAndAlreadyAssignedAppearanceIsPreserved()
        {
            var first = new WrestlerState { Id = "old-one" }; var second = new WrestlerState { Id = "old-one" };
            PortraitAppearanceRules.Ensure(first); PortraitAppearanceRules.Ensure(second);
            Assert.That(JsonUtility.ToJson(first.Presentation.Appearance), Is.EqualTo(JsonUtility.ToJson(second.Presentation.Appearance)));
            var before = JsonUtility.ToJson(first.Presentation.Appearance); PortraitAppearanceRules.Ensure(first);
            Assert.That(JsonUtility.ToJson(first.Presentation.Appearance), Is.EqualTo(before));
        }

        [Test]
        public void NormalizationRejectsMalformedAndRemovedSaveParts()
        {
            var appearance = JsonUtility.FromJson<PortraitAppearanceState>("{\"BodyType\":99,\"SkinBrightness\":0,\"Parts\":[null,{\"Category\":\"clothes\",\"Index\":-1},{\"Category\":\"wrinkles\",\"Index\":3}],\"Colors\":[null,{\"Category\":\"hair\",\"Hex\":\"bad\"}]}");
            Assert.DoesNotThrow(() => PortraitAppearanceRules.Normalize(appearance, WrestlerGender.Female));
            Assert.That(appearance.BodyType, Is.EqualTo(3)); Assert.That(appearance.SkinBrightness, Is.EqualTo(40));
            Assert.That(appearance.Part("clothes"), Is.EqualTo(2));
            Assert.That(appearance.Parts.Any(x => x.Category == "wrinkles"), Is.False); Assert.That(appearance.Colors, Is.Empty);
        }

        [Test]
        public void EditorUsesNumberedLabelsAndHasNoForbiddenFields()
        {
            var appearance = new PortraitAppearanceState();
            var editor = new PortraitEditorView(appearance, WrestlerGender.Female, true);
            Assert.That(editor.Q<Label>("portrait-clothes-value").text, Is.EqualTo("의상 1"));
            Assert.That(editor.Appearance.Part("clothes"), Is.EqualTo(2));
            Assert.That(editor.Query<DropdownField>().ToList(), Is.Empty);
            Assert.That(editor.Q("portrait-hair"), Is.Null);
            Assert.That(editor.Q("portrait-bodytype"), Is.Null);
            Assert.That(editor.Q<SliderInt>("portrait-skin"), Is.Null);
            Assert.That(editor.Q<SliderInt>("portrait-width"), Is.Null);
            UnityEngine.Object.DestroyImmediate(editor.Q<Image>("portrait-preview").image);
            PortraitRenderer.ClearCache();
        }

        [Test]
        public void ColorPaletteSelectsColorAndUpdatesPreviewWithoutChangingSourceAppearance()
        {
            var original = new PortraitAppearanceState();
            var editor = new PortraitEditorView(original, WrestlerGender.Female, true);
            var window = ScriptableObject.CreateInstance<PortraitPaletteTestWindow>();
            try
            {
                window.Show(); window.rootVisualElement.Add(editor);
                var palette = editor.Q<Foldout>("portrait-palette-clothes");
                palette.value = true;
                Assert.That(palette.text, Is.EqualTo("색상표"));
                Assert.That(palette.Query<Button>(className: "portrait-palette-color").ToList(), Has.Count.EqualTo(44));
                var black = palette.Q<Button>("portrait-palette-clothes-000000");
                // This Unity version exposes click simulation only as an internal method.
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(black.clickable, new object[] { null });
                Assert.That(editor.Appearance.Color("clothes"), Is.EqualTo("#000000"));
                Assert.That(editor.Q<TextField>("portrait-color-clothes").value, Is.EqualTo("#000000"));
                Assert.That(black.ClassListContains("selected"), Is.True);
                Assert.That(original.Color("clothes"), Is.Null);
            }
            finally { window.Close(); PortraitRenderer.ClearCache(); }
        }

        private sealed class PortraitPaletteTestWindow : EditorWindow { }

        [TestCase(true)]
        [TestCase(false)]
        public void PlayerCanEditOwnWardrobeWithoutContract(bool player)
        {
            var host = new GameObject("Portrait permissions test");
            var previousSave = DashboardSession.ActiveSave;
            var activeSave = typeof(DashboardSession).GetProperty(nameof(DashboardSession.ActiveSave));
            try
            {
                var wrestler = new WrestlerState { Id = "wardrobe-player" };
                var save = new GameSave { Player = new PlayerCharacterState { WrestlerId = player ? wrestler.Id : "other" } };
                save.Wrestlers.Add(wrestler); activeSave.SetValue(null, save);
                host.AddComponent<UIDocument>();
                var controller = host.AddComponent<WrestlerProfileController>();
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree().Q<VisualElement>("wrestler-profile");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(WrestlerProfileController);
                type.GetField("root", flags).SetValue(controller, root);
                type.GetField("editAppearanceButton", flags).SetValue(controller, root.Q<Button>("wp-edit-appearance"));
                type.GetMethod("Bind", flags).Invoke(controller, new object[] { wrestler });
                Assert.That((bool)type.GetField("canSaveStyles", flags).GetValue(controller), Is.False);
                Assert.That(root.Q<Button>("wp-edit-appearance").enabledSelf, Is.EqualTo(player));
            }
            finally { activeSave.SetValue(null, previousSave); UnityEngine.Object.DestroyImmediate(host); PortraitRenderer.ClearCache(); }
        }

        [Test]
        public void ProfileHeaderContainsWardrobeButtonOutsidePortraitHero()
        {
            var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree();
            var button = root.Q<Button>("wp-edit-appearance");
            Assert.That(button.parent.ClassListContains("wp-profile-heading"), Is.True);
            Assert.That(root.Q<VisualElement>(className: "wp-profile-hero").Q<Button>("wp-edit-appearance"), Is.Null);
        }

        [TestCase(WrestlerGender.Male)]
        [TestCase(WrestlerGender.Female)]
        public void EveryBodyAndOutfitRendersWithCurrentAssets(WrestlerGender gender)
        {
            var count = PortraitCatalog.Groups(gender).Single(x => x.Category == "clothes").Count;
            for (var body = 1; body <= 3; body++)
            for (var clothes = gender == WrestlerGender.Female ? 2 : 1; clothes <= count; clothes++)
            {
                var appearance = new PortraitAppearanceState { BodyType = body };
                appearance.SetPart("clothes", clothes); appearance.SetColor("clothes", "#194e47"); appearance.SetColor("clothes-secondary", "#e1b548");
                var texture = PortraitRenderer.Render(appearance, gender);
                Assert.That(texture.GetPixels32().Count(x => x.a > 128), Is.GreaterThan(40000));
                UnityEngine.Object.DestroyImmediate(texture);
            }
            PortraitRenderer.ClearCache();
        }

        [Test]
        public void AllBeardsHaveFaceSpecificAssets()
        {
            for (var beard = 2; beard <= 12; beard++)
            for (var face = 1; face <= 4; face++)
                Assert.That(Resources.Load<Texture2D>($"PWManagerUI/Portraits/Male/beard-{beard}-face-{face}"), Is.Not.Null);
        }

        [Test]
        public void RebindingProfileDiscardsPreviousWardrobeEditorWithoutChangingEitherWrestler()
        {
            var host = new GameObject("Portrait profile test");
            Texture preview = null;
            try
            {
                host.AddComponent<UIDocument>();
                var controller = host.AddComponent<WrestlerProfileController>();
                var root = Resources.Load<VisualTreeAsset>("PWManagerUI/WrestlerProfile").CloneTree().Q<VisualElement>("wrestler-profile");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(WrestlerProfileController);
                var first = new WrestlerState { Id = "wardrobe-a" }; var second = new WrestlerState { Id = "wardrobe-b" };
                PortraitAppearanceRules.Ensure(first); PortraitAppearanceRules.Ensure(second);
                var beforeFirst = JsonUtility.ToJson(first.Presentation.Appearance); var beforeSecond = JsonUtility.ToJson(second.Presentation.Appearance);
                type.GetField("root", flags).SetValue(controller, root);
                type.GetField("boundWrestler", flags).SetValue(controller, first);
                type.GetField("canSaveStyles", flags).SetValue(controller, true);
                type.GetField("canEditAppearance", flags).SetValue(controller, true);
                type.GetMethod("OpenAppearanceEditor", flags).Invoke(controller, null);
                var overlay = (VisualElement)type.GetField("appearanceOverlay", flags).GetValue(controller);
                Assert.That(overlay, Is.Not.Null);
                preview = overlay.Q<Image>("portrait-preview").image;
                var editor = overlay.Q<PortraitEditorView>();
                typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(editor.Q<Button>("portrait-clothes-next").clickable, new object[] { null });
                type.GetMethod("Bind", flags).Invoke(controller, new object[] { second });
                Assert.That(overlay.parent, Is.Null);
                Assert.That(type.GetField("appearanceOverlay", flags).GetValue(controller), Is.Null);
                Assert.That(JsonUtility.ToJson(first.Presentation.Appearance), Is.EqualTo(beforeFirst));
                Assert.That(JsonUtility.ToJson(second.Presentation.Appearance), Is.EqualTo(beforeSecond));
            }
            finally
            {
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                UnityEngine.Object.DestroyImmediate(host); PortraitRenderer.ClearCache();
            }
        }

        [TestCase(WrestlerGender.Male, 10)]
        [TestCase(WrestlerGender.Male, 11)]
        [TestCase(WrestlerGender.Female, 10)]
        [TestCase(WrestlerGender.Female, 11)]
        public void CoveredMasksDoNotExposeEyes(WrestlerGender gender, int mask)
        {
            var appearance = new PortraitAppearanceState(); appearance.SetPart("mask", mask);
            var first = PortraitRenderer.Render(appearance, gender);
            appearance.SetPart("eyes", 6); appearance.SetPart("pupils", 3); appearance.SetColor("eyes", "#ff0000");
            var second = PortraitRenderer.Render(appearance, gender);
            Assert.That(second.GetPixels32(), Is.EqualTo(first.GetPixels32()));
            UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); PortraitRenderer.ClearCache();
        }
    }
}
