using System.Collections.Generic;
using PWManager.Domain.Models;
using UnityEngine;

namespace PWManager.Presentation
{
    internal static class BeltPartTextures
    {
        private static readonly Dictionary<string, Texture2D> generated = new();
        private static Material material;
        internal static readonly string[] StrapColors = { "#262626", "#ffffff", "#8b5735" };
        internal static readonly string[] MetalColors = { "#e8b650", "#cd9485", "#bcc5ce" };

        internal static Texture2D Get(string path, BeltDesignState design, string maskPath = null)
        {
            var source = Resources.Load<Texture2D>(path);
            if (source == null) throw new System.InvalidOperationException("벨트 부품을 찾을 수 없습니다: " + path);
            if (design.MetalColor == BeltMetalColor.Gold && (maskPath == null || design.StrapColor == BeltStrapColor.Black)) return source;
            var key = path + ":" + (int)design.MetalColor + ":" + (maskPath == null ? -1 : (int)design.StrapColor);
            if (generated.TryGetValue(key, out var cached) && cached != null) return cached;
            if (material == null) material = new Material(Resources.Load<Shader>("PWManagerUI/BeltParts/BeltMaterial"));
            ColorUtility.TryParseHtmlString(MetalColors[(int)design.MetalColor], out var metal);
            ColorUtility.TryParseHtmlString(StrapColors[(int)design.StrapColor], out var strap);
            material.SetVector("_Metal", metal); material.SetVector("_Strap", strap);
            material.SetFloat("_MetalChanged", design.MetalColor == BeltMetalColor.Gold ? 0 : 1);
            material.SetFloat("_StrapChanged", design.StrapColor == BeltStrapColor.Black ? 0 : 1);
            material.SetFloat("_White", design.StrapColor == BeltStrapColor.White ? 1 : 0);
            material.SetFloat("_HasMask", maskPath == null ? 0 : 1);
            material.SetTexture("_Mask", maskPath == null ? Texture2D.blackTexture : Resources.Load<Texture2D>(maskPath));
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target, material); RenderTexture.active = target;
                var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { name = key };
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); result.Apply(); generated.Add(key, result); return result;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        internal static void Clear()
        {
            foreach (var texture in generated.Values)
                if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
            generated.Clear();
        }
    }
}
