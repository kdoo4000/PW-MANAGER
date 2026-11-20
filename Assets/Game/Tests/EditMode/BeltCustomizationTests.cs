using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Infrastructure.Save;
using PWManager.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PWManager.Tests
{
    public sealed class BeltCustomizationTests
    {
        private static GameSave Save()
        {
            var save = GameSave.CreateNew(17, new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
            save.Promotion = new PromotionState { Id = Guid.NewGuid().ToString(), Name = "PW", Abbreviation = "PW", InitialCash = 10000 };
            return save;
        }

        private static BeltDesignState Customized() => new()
        {
            Template = 9, MainEmblem = 5, SideEmblem = 6, StrapColor = BeltStrapColor.Brown, MetalColor = BeltMetalColor.RoseGold,
            Nameplates = true, NameplateText = "월드 챔피언십", Abbreviation = "PW", OverlayAbbreviation = true,
            MainGems = true, SideGems = true, Lettering = BeltLettering.Embossed
        };

        [Test]
        public void DesignAssignmentCopiesDraftAndRejectsInvalidChangesWithoutTouchingTitle()
        {
            var save = Save(); var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true);
            var draft = Customized(); ChampionshipService.SetBeltDesign(save, title, draft);
            var stored = JsonUtility.ToJson(title.BeltDesign); draft.Template = 1;
            Assert.That(JsonUtility.ToJson(title.BeltDesign), Is.EqualTo(stored));
            draft.Template = 11;
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.SetBeltDesign(save, title, draft));
            Assert.That(JsonUtility.ToJson(title.BeltDesign), Is.EqualTo(stored));
            var foreign = new ChampionshipState { Id = Guid.NewGuid().ToString(), PromotionId = "another" };
            Assert.Throws<InvalidOperationException>(() => ChampionshipService.SetBeltDesign(save, foreign, Customized()));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(11)]
        public void InvalidTemplateIsRejected(int template)
        {
            var design = Customized(); design.Template = template;
            Assert.Throws<InvalidOperationException>(() => BeltDesignRules.Validate(design));
        }

        [Test]
        public void InvalidMaterialsTextAndEmblemsAreRejected()
        {
            var design = Customized(); design.MainEmblem = 8;
            Assert.Throws<InvalidOperationException>(() => BeltDesignRules.Validate(design));
            design = Customized(); design.StrapColor = (BeltStrapColor)3;
            Assert.Throws<InvalidOperationException>(() => BeltDesignRules.Validate(design));
            design = Customized(); design.MetalColor = (BeltMetalColor)(-1);
            Assert.Throws<InvalidOperationException>(() => BeltDesignRules.Validate(design));
            design = Customized(); design.NameplateText = new string('가', 33);
            Assert.Throws<InvalidOperationException>(() => BeltDesignRules.Validate(design));
            design = Customized(); design.Abbreviation = "123456789";
            Assert.Throws<InvalidOperationException>(() => BeltDesignRules.Validate(design));
            design = Customized(); design.NameplateText = "두 줄\n문구";
            Assert.Throws<InvalidOperationException>(() => BeltDesignRules.Validate(design));
            design = Customized(); design.NameplateText = new string('가', 32); design.Abbreviation = "12345678";
            Assert.DoesNotThrow(() => BeltDesignRules.Validate(design));
        }

        [Test]
        public void SaveLoadPreservesEveryDesignSetting()
        {
            var save = Save(); var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true);
            ChampionshipService.SetBeltDesign(save, title, Customized());
            var directory = Path.Combine(Path.GetTempPath(), "PWManagerBeltTests", Guid.NewGuid().ToString("N"));
            try
            {
                var service = new SaveService(directory); service.Save("belt", save);
                var restored = service.Load("belt");
                Assert.That(restored.SaveVersion, Is.EqualTo(17));
                Assert.That(JsonUtility.ToJson(restored.Championships.Single().BeltDesign), Is.EqualTo(JsonUtility.ToJson(title.BeltDesign)));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void Version16UpgradePreservesChampionReignsAndExistingDesign()
        {
            var save = Save(); var wrestler = new WrestlerState { Id = Guid.NewGuid().ToString(), PromotionId = save.Promotion.Id }; save.Wrestlers.Add(wrestler);
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, wrestler.Id);
            title.Reigns[0].Defenses = 4; title.Prestige = 0; title.BeltDesign = null; save.SaveVersion = 16;
            var beforeReigns = JsonUtility.ToJson(title.Reigns[0]);
            new SaveVersion16To17Migration().Migrate(save);
            Assert.That(save.SaveVersion, Is.EqualTo(17)); Assert.That(title.HolderId, Is.EqualTo(wrestler.Id));
            Assert.That(JsonUtility.ToJson(title.Reigns[0]), Is.EqualTo(beforeReigns)); Assert.That(title.Prestige, Is.Zero);
            Assert.That(title.BeltDesign.Template, Is.EqualTo(1));
            title.BeltDesign = Customized(); new SaveVersion16To17Migration().Migrate(save);
            Assert.That(title.BeltDesign.Template, Is.EqualTo(9));
        }

        [Test]
        public void ResultPreparationKeepsAnIndependentCopyOfDesign()
        {
            var save = Save(); var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true);
            title.BeltDesign = Customized(); var result = ChampionshipService.PrepareResults(save, Array.Empty<MatchResultState>(), save.CurrentDate);
            Assert.That(JsonUtility.ToJson(result[0].BeltDesign), Is.EqualTo(JsonUtility.ToJson(title.BeltDesign)));
            result[0].BeltDesign.Template = 1;
            Assert.That(title.BeltDesign.Template, Is.EqualTo(9));
        }

        [Test]
        public void VisibleEditorPreviewsDraftAndSavesOnlyOnConfirmation()
        {
            var previous = DashboardSession.ActiveSave; var transient = DashboardSession.IsTransient;
            var owner = new GameObject("Belt Customization Test");
            try
            {
                var save = Save(); DashboardSession.SetActiveSave(save, true);
                var document = owner.AddComponent<UIDocument>(); document.panelSettings = Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings");
                document.visualTreeAsset = Resources.Load<VisualTreeAsset>("PWManagerUI/Dashboard"); var root = document.rootVisualElement;
                ChampionshipManagementView.Open(root); root.Q<TextField>("championship-name").value = "World";
                Click(root, "championship-editor-tab-belt");
                Assert.That(root.Q("championship-belt-editor").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Click(root, "belt-template-9"); Click(root, "belt-tab-materials"); Click(root, "belt-main-5"); Click(root, "belt-side-6"); Click(root, "belt-strap-2"); Click(root, "belt-metal-1");
                Click(root, "belt-tab-lettering"); root.Q<Toggle>("belt-nameplates").value = true; root.Q<Toggle>("belt-main-gems").value = true; root.Q<Toggle>("belt-side-gems").value = true;
                root.Q<TextField>("belt-nameplate-text").value = "월드 챔피언십"; root.Q<TextField>("belt-abbreviation").value = "PW";
                root.Q<Toggle>("belt-overlay-abbreviation").value = true; root.Q<DropdownField>("belt-lettering").index = 2;
                Assert.That(save.Championships, Is.Empty);
                Assert.That(root.Q<BeltPreview>("belt-live-preview").Design.Template, Is.EqualTo(9));
                Click(root, "championship-create"); var title = save.Championships.Single();
                Assert.That(JsonUtility.ToJson(title.BeltDesign), Is.EqualTo(JsonUtility.ToJson(Customized())));
                Assert.That(root.Q("championship-detail").Query<BeltPreview>().ToList().Single().Design.Template, Is.EqualTo(9));
                var before = JsonUtility.ToJson(title.BeltDesign); Click(root, "championship-edit"); Click(root, "championship-editor-tab-belt"); Click(root, "belt-template-2"); Click(root, "championship-cancel");
                Assert.That(JsonUtility.ToJson(title.BeltDesign), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(owner); DashboardSession.SetActiveSave(previous, transient); }
        }

        [UnityTest]
        public IEnumerator AllTemplatesHaveImagesAndFitThePreview()
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                window.ShowUtility(); window.position = new Rect(24, 60, 720, 480);
                window.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/BeltCustomizer"));
                var preview = new BeltPreview(Customized()); preview.style.height = 360; preview.style.flexGrow = 1; window.rootVisualElement.Add(preview);
                for (var template = 1; template <= 10; template++)
                {
                    var design = Customized(); design.Template = template; preview.SetDesign(design);
                    for (var frame = 0; frame < 3; frame++) { window.Repaint(); yield return null; }
                    Assert.That(preview.Query<Image>().ToList(), Has.Count.GreaterThanOrEqualTo(5));
                    Assert.That(preview.Query<Image>().ToList().All(image => image.image != null), Is.True);
                    var background = preview.Query<Image>().ToList().First();
                    Assert.That(background.worldBound.xMin, Is.GreaterThanOrEqualTo(preview.worldBound.xMin - 1));
                    Assert.That(background.worldBound.xMax, Is.LessThanOrEqualTo(preview.worldBound.xMax + 1));
                    Assert.That(background.worldBound.yMax, Is.LessThanOrEqualTo(preview.worldBound.yMax + 1));
                }
            }
            finally { window.Close(); }
        }

        private static void Click(VisualElement root, string name)
        {
            var button = root.Q<Button>(name); Assert.That(button, Is.Not.Null, name); Assert.That(button.enabledInHierarchy, Is.True, name);
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button.clickable, new object[] { null });
        }
    }
}
