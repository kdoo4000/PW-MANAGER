using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

namespace PWManager.Presentation
{
    public static class PortraitRenderer
    {
        private sealed class CachedPortrait { public string Signature; public Texture2D Texture; }
        private static readonly Dictionary<string, CachedPortrait> Portraits = new();
        private static readonly Dictionary<string, Texture2D> Parts = new();
        private static Material composite;
        public const int Width = 512;
        public const int Height = 461;

        public static Texture Get(WrestlerState wrestler)
        {
            if (wrestler == null) return null;
            if (wrestler.Presentation?.Appearance == null && !string.IsNullOrEmpty(wrestler.Presentation?.PortraitResourcePath))
                return Resources.Load<Texture2D>(wrestler.Presentation.PortraitResourcePath);
            PortraitAppearanceRules.Ensure(wrestler);
            return Get(wrestler.Presentation.Appearance, wrestler.Identity.Gender, wrestler.Id);
        }

        public static Texture2D Get(PortraitAppearanceState appearance, WrestlerGender gender, string cacheKey)
        {
            var signature = gender + JsonUtility.ToJson(appearance);
            cacheKey ??= signature;
            if (!Portraits.TryGetValue(cacheKey, out var cached)) Portraits[cacheKey] = cached = new CachedPortrait();
            if (cached.Signature == signature && cached.Texture != null) return cached.Texture;
            cached.Texture = Render(appearance, gender, cached.Texture);
            cached.Signature = signature;
            return cached.Texture;
        }

        public static Texture2D Render(PortraitAppearanceState appearance, WrestlerGender gender, Texture2D reuse = null)
        {
            PortraitAppearanceRules.Normalize(appearance, gender);
            if (composite == null) composite = new Material(Resources.Load<Shader>("PWManagerUI/Portraits/PortraitComposite"));
            var current = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var next = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = current; GL.Clear(false, true, Color.clear);
                int Part(string category) => appearance.Part(category);
                string Typed(string key) => appearance.BodyType > 1 && Load(gender, key + "-body-type-" + appearance.BodyType) != null ? key + "-body-type-" + appearance.BodyType : key;
                void Layer(string name, string color = null, int mode = 1, bool skin = false)
                {
                    var texture = Load(gender, name);
                    if (texture == null) throw new InvalidOperationException($"Missing portrait part: {gender}/{name}");
                    var hex = color == null ? null : appearance.Color(color);
                    var tinted = !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out _);
                    composite.SetTexture("_Layer", texture);
                    composite.SetFloat("_Mode", skin ? 4 : tinted ? mode : 0);
                    composite.SetFloat("_Skin", appearance.SkinBrightness);
                    if (tinted) { ColorUtility.TryParseHtmlString(hex, out var tint); composite.SetColor("_Tint", tint); }
                    Graphics.Blit(current, next, composite);
                    (current, next) = (next, current);
                }
                void Materials(string name, string category)
                {
                    Layer(Typed(name));
                    foreach (var region in new[] { "primary", "secondary" })
                    {
                        var color = category + (region == "secondary" ? "-secondary" : "");
                        if (!string.IsNullOrEmpty(appearance.Color(color))) Layer(Typed(name + "-" + region), color);
                    }
                }
                var mask = Part("mask"); var fullMask = PortraitCatalog.FullMask(mask);
                var hat = Part("headwear") > 1 && !fullMask;
                var rear = "hairback-" + Part("hairback") + (hat ? "-headwear-" + Part("headwear") : "");
                if (!fullMask && Part("hairback") > 1) Layer(rear, "hair");
                var suit = Load(gender, "body-clothes-" + Part("clothes")) != null;
                Layer(suit ? Typed("body-clothes-" + Part("clothes")) : appearance.BodyType == 1 ? "body" : "body-type-" + appearance.BodyType, skin: true);
                if (!suit && Part("tattoo") > 1) Layer(Typed("tattoo-" + Part("tattoo")), "tattoo", 3);
                if (!suit && Part("bodypaint") > 1) Layer(Typed("tattoo-" + PortraitCatalog.BodyPaintSources(gender)[Part("bodypaint") - 2]), "bodypaint", 3);
                if (Part("clothes") > 1) Materials("clothes-" + Part("clothes"), "clothes");
                Layer("head-" + Part("head") + (fullMask ? "-masked" : ""), skin: true);
                foreach (var category in new[] { "freckles", "scars", "facepaint" })
                {
                    if (Part(category) == 1) continue;
                    var name = category + "-" + Part(category) + "-face-" + Part("head");
                    if (category == "facepaint") Materials(name, category); else Layer(name, skin: true);
                }
                if (mask == 1 && gender == WrestlerGender.Male && Part("beard") > 1) Layer("beard-" + Part("beard") + "-face-" + Part("head"), "beard");
                if (mask == 1) { Layer("nose-" + Part("nose")); Layer("brows-" + Part("brows"), "hair"); }
                if (!PortraitCatalog.CoveredEyes.Contains(mask))
                {
                    var eyes = "eyes-" + Part("eyes");
                    Layer(eyes + "-shape" + (Part("pupils") == 3 ? "-dark" : ""));
                    Layer(eyes + "-pupil-" + Part("pupils"));
                    if (!string.IsNullOrEmpty(appearance.Color("eyes"))) Layer(eyes + "-pupil-" + Part("pupils") + "-iris", "eyes", 2);
                }
                if (!PortraitCatalog.ClosedMasks.Contains(mask)) Layer("mouth-" + Part("mouth"));
                if (mask > 1) Materials("mask-" + mask + "-face-" + Part("head"), "mask");
                if (!fullMask)
                {
                    if (!hat) Layer("hair-" + Part("hair"), "hair");
                    else if (!(gender == WrestlerGender.Male && Part("hair") == 11)) Layer("headwear-" + Part("headwear") + "-hair", "hair");
                }
                if (mask == 1 && Part("eyewear") > 1) Layer("eyewear-" + Part("eyewear"));
                if (hat) Layer("headwear-" + Part("headwear"));
                RenderTexture.active = current;
                if (reuse == null) reuse = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Wrestler portrait", filterMode = FilterMode.Bilinear };
                reuse.wrapMode = TextureWrapMode.Clamp;
                reuse.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); reuse.Apply(false);
                return reuse;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(current); RenderTexture.ReleaseTemporary(next); }
        }

        private static Texture2D Load(WrestlerGender gender, string name)
        {
            var key = gender + "/" + name;
            if (!Parts.TryGetValue(key, out var texture)) Parts[key] = texture = Resources.Load<Texture2D>("PWManagerUI/Portraits/" + key);
            return texture;
        }

        public static void Prune(GameSave save)
        {
            var ids = new HashSet<string>((save?.Wrestlers ?? new List<WrestlerState>()).Where(x => x != null).Select(x => x.Id));
            foreach (var key in Portraits.Keys.Where(x => !ids.Contains(x)).ToList())
            {
                DestroyOwned(Portraits[key].Texture);
                Portraits.Remove(key);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearCache()
        {
            foreach (var cached in Portraits.Values) DestroyOwned(cached.Texture);
            Portraits.Clear(); Parts.Clear();
            DestroyOwned(composite);
            composite = null;
        }

        internal static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
