using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using PWManager.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PWManager.Tests
{
    public sealed class ChampionPortraitTests
    {
        private GameSave previous, save;
        private bool transient;
        private WrestlerState wrestler;
        [SetUp]
        public void SetUp()
        {
            previous = DashboardSession.ActiveSave; transient = DashboardSession.IsTransient;
            save = new GameSave { Promotion = new PromotionState { Id = "promotion", Name = "PW" }, CurrentDate = new GameDate(2026, 6, 1) };
            wrestler = new WrestlerState { Id = "a", PromotionId = save.Promotion.Id }; wrestler.Identity.RingName = "A"; save.Wrestlers.Add(wrestler);
            var second = new WrestlerState { Id = "b", PromotionId = save.Promotion.Id }; second.Identity.RingName = "B"; save.Wrestlers.Add(second);
            DashboardSession.SetActiveSave(save, true);
        }
        [TearDown] public void TearDown() => DashboardSession.SetActiveSave(previous, transient);

        [Test]
        public void DefaultPortraitAddsBeltsOnlyToCurrentChampionsIncludingTagMembers()
        {
            Assert.That(PortraitRenderer.Get(wrestler), Is.SameAs(PortraitRenderer.GetWithoutChampionships(wrestler)));
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, "a");
            Assert.That(PortraitRenderer.Get(wrestler), Is.TypeOf<RenderTexture>());
            ChampionshipService.SetHolder(save, title, "b");
            Assert.That(PortraitRenderer.Get(wrestler), Is.SameAs(PortraitRenderer.GetWithoutChampionships(wrestler)));
            var team = WrestlerGroupService.CreateTagTeam(save, "Team", new[] { "a", "b" });
            ChampionshipService.Create(save, "Tag", ChampionshipType.TagTeam, holderId: team.Id);
            Assert.That(PortraitRenderer.Get(wrestler), Is.TypeOf<RenderTexture>());
            Assert.That(ChampionshipService.HeldBy(save, "b"), Has.Count.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ShoulderCompositionChangesTheBodyAndPreservesTheFaceForAllBodyTypes()
        {
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, "a");
            foreach (var gender in new[] { WrestlerGender.Male, WrestlerGender.Female })
            foreach (var body in new[] { 1, 2, 3 })
            {
                wrestler.Identity.Gender = gender; PortraitAppearanceRules.Ensure(wrestler); wrestler.Presentation.Appearance.BodyType = body;
                var raw = PortraitRenderer.GetWithoutChampionships(wrestler); var painted = PortraitRenderer.Get(wrestler);
                yield return DrawFrames();
                var source = Read(raw); var result = Read(painted); var face = Read(Resources.Load<Texture2D>($"PWManagerUI/Portraits/{(gender == WrestlerGender.Male ? "Male" : "Female")}/head-{wrestler.Presentation.Appearance.Part("head")}"));
                var changed = 0; var protectedPixels = 0;
                for (var i = 0; i < result.Length; i++)
                {
                    var delta = Difference(source[i], result[i]); if (delta > 12) changed++;
                    if (face[i].a < 254 || source[i].a < 254) continue;
                    protectedPixels++; Assert.That(delta, Is.LessThanOrEqualTo(3), $"{gender}/{body}: protected face pixel {i}");
                }
                Assert.That(protectedPixels, Is.GreaterThan(1000)); Assert.That(changed, Is.GreaterThan(500), $"{gender}/{body}: belt missing");
            }
        }

        [UnityTest]
        public IEnumerator CustomDesignAndSecondBeltUpdateTheDefaultPortrait()
        {
            var title = ChampionshipService.Create(save, "World", ChampionshipType.Individual, true, "a");
            var portrait = PortraitRenderer.Get(wrestler); yield return DrawFrames(); var before = Read(portrait);
            var draft = title.BeltDesign.Copy(); draft.MetalColor = BeltMetalColor.Silver; draft.MainEmblem = 4; ChampionshipService.SetBeltDesign(save, title, draft);
            var updated = PortraitRenderer.Get(wrestler); yield return DrawFrames(); var after = Read(updated);
            Assert.That(before.Zip(after, Difference).Count(d => d > 12), Is.GreaterThan(500));
            ChampionshipService.Create(save, "National", ChampionshipType.Individual, holderId: "a");
            var doubleChampion = PortraitRenderer.Get(wrestler); yield return DrawFrames(); var both = Read(doubleChampion);
            var left = 0;
            for (var y = 0; y < PortraitRenderer.Height; y++) for (var x = 0; x < PortraitRenderer.Width / 2; x++)
                if (Difference(after[y * PortraitRenderer.Width + x], both[y * PortraitRenderer.Width + x]) > 12) left++;
            Assert.That(left, Is.GreaterThan(500));
            var focused = PortraitRenderer.GetForChampionship(wrestler, title.Id); yield return DrawFrames();
            Assert.That(Read(focused).Zip(both, Difference).Count(d => d > 12), Is.GreaterThan(500));
        }

        internal static IEnumerator DrawFrames()
        {
            var utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            for (var frame = 0; frame < 4; frame++)
            {
                utility.GetMethod("UpdatePanels", flags).Invoke(null, null);
                utility.GetMethod("RepaintPanels", flags).Invoke(null, new object[] { true });
                utility.GetMethod("RenderOffscreenPanels", flags).Invoke(null, null);
                ChampionPortraitRenderer.FlushPrepared(); yield return null;
            }
        }
        private static int Difference(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));
        private static Color32[] Read(Texture source)
        {
            var target = RenderTexture.GetTemporary(PortraitRenderer.Width, PortraitRenderer.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; var image = new Texture2D(PortraitRenderer.Width, PortraitRenderer.Height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image); }
        }
    }
}
