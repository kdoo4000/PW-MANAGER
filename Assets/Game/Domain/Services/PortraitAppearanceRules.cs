using System;
using System.Linq;
using PWManager.Domain.Models;

namespace PWManager.Domain.Services
{
    public sealed class PortraitPartGroup
    {
        public readonly string Category;
        public readonly string Label;
        public readonly int Count;
        public readonly bool HasNone;
        public readonly bool Wardrobe;

        public PortraitPartGroup(string category, string label, int count, bool hasNone, bool wardrobe)
        { Category = category; Label = label; Count = count; HasNone = hasNone; Wardrobe = wardrobe; }

        public string PartLabel(int index) => Category == "bodytype" ? new[] { "기본 체형", "마른 체형", "거구 체형" }[index - 1] :
            HasNone && index == 1 ? Label + " 없음" : Label + " " + (HasNone ? index - 1 : index);
    }

    public static partial class PortraitCatalog
    {
        public static PortraitPartGroup[] Groups(WrestlerGender gender) => gender == WrestlerGender.Female ? FemaleGroups : MaleGroups;
        public static int[] RearHair(WrestlerGender gender, int front) => gender == WrestlerGender.Female ? FemaleRearHair(front) : MaleRearHair(front);
        public static bool FullMask(int mask) => mask > 1 && mask != 6 && mask != 7;
    }

    public static class PortraitAppearanceRules
    {
        private static readonly string[] HairColors = { "#242424", "#513525", "#ac794b", "#dcb86d", "#74342d", "#b6b4ab" };
        public static readonly string[] FabricColors = { "#153260", "#c92c32", "#194e47", "#682c78", "#242424", "#e1b548", "#eee8db", "#cb4d83" };
        public static readonly string[] EyeColors = { "#825128", "#274d69", "#3d5c35", "#807667", "#382818", "#a87c38" };
        public static readonly string[] ColorCategories = { "hair", "beard", "eyes", "clothes", "clothes-secondary", "facepaint", "facepaint-secondary", "mask", "mask-secondary", "tattoo", "bodypaint" };
        public static readonly string[] WardrobeColorCategories = { "eyes", "clothes", "clothes-secondary", "facepaint", "facepaint-secondary", "mask", "mask-secondary", "bodypaint" };

        public static int BodyType(WrestlerIdentityState identity) => identity.BodyType switch
        {
            WrestlerBodyType.Lightweight => 2,
            WrestlerBodyType.Heavyweight or WrestlerBodyType.Giant => 3,
            _ => 1
        };

        public static int BodyType(int heightCm, int weightKg)
        {
            var bmi = weightKg / Math.Pow(Math.Max(1, heightCm) / 100d, 2);
            return bmi < 23 ? 2 : bmi >= 32 ? 3 : 1;
        }

        public static PortraitAppearanceState Generate(WrestlerIdentityState identity, string styleId, Random random, bool preferEastAsianSkin = false)
        {
            var appearance = new PortraitAppearanceState
            {
                BodyType = BodyType(identity),
                SkinBrightness = preferEastAsianSkin && random.NextDouble() < .9 ? random.Next(90, 121) : random.Next(55, 136)
            };
            foreach (var group in PortraitCatalog.Groups(identity.Gender).Where(x => x.Category != "bodytype"))
            {
                if (group.Category is "pupils" or "facepaint" or "bodypaint")
                    appearance.SetPart(group.Category, 1);
                else if (group.Category == "clothes" && identity.Gender == WrestlerGender.Female)
                    appearance.SetPart(group.Category, random.Next(2, group.Count + 1));
                else if (!group.HasNone)
                    appearance.SetPart(group.Category, random.Next(1, group.Count + 1));
                else
                {
                    var chance = group.Category switch
                    {
                        "mask" => styleId == "style_005" ? .7 : .05,
                        "tattoo" => .25, "scars" => .08, "clothes" => .35,
                        "beard" => .45, "freckles" => .15, "eyewear" or "headwear" => .08,
                        "hairback" => identity.Gender == WrestlerGender.Female ? .8 : .35,
                        _ => .25
                    };
                    var part = random.NextDouble() >= chance ? 1 : group.Category == "mask" && styleId == "style_005"
                        ? PortraitCatalog.LuchaMasks[random.Next(PortraitCatalog.LuchaMasks.Length)] : random.Next(2, group.Count + 1);
                    appearance.SetPart(group.Category, part);
                }
            }
            var hair = HairColors[random.Next(HairColors.Length)];
            appearance.SetColor("hair", hair); appearance.SetColor("beard", hair);
            appearance.SetColor("eyes", EyeColors[0]);
            foreach (var category in new[] { "clothes", "clothes-secondary", "facepaint", "facepaint-secondary", "mask", "mask-secondary" })
                appearance.SetColor(category, FabricColors[random.Next(FabricColors.Length)]);
            appearance.SetColor("tattoo", "#111111");
            appearance.SetColor("bodypaint", FabricColors[random.Next(FabricColors.Length)]);
            Normalize(appearance, identity.Gender);
            return appearance;
        }

        public static void Ensure(WrestlerState wrestler)
        {
            wrestler.Presentation ??= new WrestlerPresentationState();
            if (wrestler.Presentation.Appearance == null)
                wrestler.Presentation.Appearance = Generate(wrestler.Identity, wrestler.Presentation.WrestlingStyleId, new Random(StableSeed(wrestler.Id)));
            Normalize(wrestler.Presentation.Appearance, wrestler.Identity.Gender);
        }

        public static int StableSeed(string id)
        {
            unchecked { var seed = 17; foreach (var c in id ?? "portrait") seed = seed * 31 + c; return seed; }
        }

        public static void Normalize(PortraitAppearanceState appearance, WrestlerGender gender)
        {
            // Older saves kept body-paint assets in the tattoo category.
            var legacyPaint = appearance.Parts?.Any(x => x?.Category == "bodypaint") == true ? -1 :
                Array.IndexOf(PortraitCatalog.BodyPaintSources(gender), appearance.Part("tattoo"));
            if (legacyPaint >= 0)
            {
                appearance.SetPart("bodypaint", legacyPaint + 2);
                appearance.SetPart("tattoo", 1);
                if (string.IsNullOrEmpty(appearance.Color("bodypaint"))) appearance.SetColor("bodypaint", appearance.Color("tattoo"));
                appearance.SetColor("tattoo", "#111111");
            }
            appearance.BodyType = Math.Clamp(appearance.BodyType, 1, 3);
            appearance.SkinBrightness = Math.Clamp(appearance.SkinBrightness, 40, 150);
            foreach (var group in PortraitCatalog.Groups(gender).Where(x => x.Category != "bodytype"))
                appearance.SetPart(group.Category, Math.Clamp(appearance.Part(group.Category), group.Category == "clothes" && gender == WrestlerGender.Female ? 2 : 1, group.Count));
            appearance.Parts.RemoveAll(x => x == null || !PortraitCatalog.Groups(gender).Any(g => g.Category == x.Category));
            var rear = PortraitCatalog.RearHair(gender, appearance.Part("hair"));
            if (rear != null && !rear.Contains(appearance.Part("hairback"))) appearance.SetPart("hairback", rear[0]);
            if (appearance.Part("mask") > 1) appearance.SetPart("eyewear", 1);
            if (PortraitCatalog.FullMask(appearance.Part("mask"))) appearance.SetPart("headwear", 1);
            appearance.Colors ??= new();
            appearance.Colors.RemoveAll(x => x == null || !ColorCategories.Contains(x.Category) || !ValidColor(x.Hex));
        }

        public static bool ValidColor(string hex) => string.IsNullOrEmpty(hex) || hex.Length == 7 && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);

        public static void ApplyWardrobe(PortraitAppearanceState target, PortraitAppearanceState edit, WrestlerGender gender)
        {
            foreach (var group in PortraitCatalog.Groups(gender).Where(x => x.Wardrobe)) target.SetPart(group.Category, edit.Part(group.Category));
            foreach (var category in WardrobeColorCategories) target.SetColor(category, edit.Color(category));
            Normalize(target, gender);
        }
    }
}
