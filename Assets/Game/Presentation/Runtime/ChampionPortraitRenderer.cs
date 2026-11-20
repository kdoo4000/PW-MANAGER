using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public static class ChampionPortraitRenderer
    {
        private sealed class BeltTexture
        {
            public string Signature;
            public GameObject Owner;
            public PanelSettings Settings;
            public RenderTexture Texture;
            public BeltPreview Preview;
            public BeltTemplate Template;
            public BeltDesignState Design;
            public bool Ready;
        }
        private sealed class ChampionTexture
        {
            public string Signature, WrestlerId;
            public Texture Raw, FaceMask;
            public WrestlerGender Gender;
            public int Body;
            public BeltTexture[] Belts;
            public RenderTexture Texture;
            public bool Dirty;
        }
        private static readonly Dictionary<string, BeltTexture> belts = new();
        private static readonly Dictionary<string, ChampionTexture> portraits = new();
        private static GameObject driver;
        private static Material composite;

        public static Texture Get(WrestlerState wrestler, Texture raw, GameSave save, string focusedTitleId = null)
        {
            var titles = ChampionshipService.HeldBy(save, wrestler.Id);
            if (focusedTitleId != null) titles = titles.Where(t => t.Id == focusedTitleId).ToList();
            if (titles.Count == 0) return raw;
            var selected = titles.Take(2).ToList();
            var entries = selected.Select(t => Belt(t.Id, t.BeltDesign ?? BeltDesignRules.Default(t.Type, t.IsWorldChampionship), t.Name)).ToArray();
            var signature = raw.GetEntityId() + JsonUtility.ToJson(wrestler.Presentation.Appearance) + string.Join("|", entries.Select(b => b.Signature));
            return Portrait(wrestler.Id + ":" + focusedTitleId, wrestler, raw, entries, signature);
        }

        public static Texture Preview(WrestlerState wrestler, BeltDesignState design, string title, bool doubleChampion = false)
        {
            if (wrestler == null) return null;
            var raw = PortraitRenderer.GetWithoutChampionships(wrestler);
            var belt = Belt("draft", design, title);
            var entries = doubleChampion ? new[] { belt, belt } : new[] { belt };
            return Portrait("draft:" + wrestler.Id, wrestler, raw, entries,
                raw.GetEntityId() + JsonUtility.ToJson(wrestler.Presentation.Appearance) + belt.Signature + doubleChampion);
        }

        private static BeltTexture Belt(string key, BeltDesignState design, string title)
        {
            var effective = BeltCustomizerView.Effective(design, title); var signature = key + JsonUtility.ToJson(effective);
            if (!belts.TryGetValue(key, out var entry))
            {
                EnsureDriver();
                entry = new BeltTexture(); belts.Add(key, entry);
                entry.Owner = new GameObject("Belt texture " + key) { hideFlags = HideFlags.HideAndDontSave }; entry.Owner.transform.SetParent(driver.transform);
                entry.Settings = UnityEngine.Object.Instantiate(Resources.Load<PanelSettings>("PWManagerRuntime/PWManagerPanelSettings"));
                entry.Settings.scaleMode = PanelScaleMode.ConstantPixelSize; entry.Settings.scale = 1;
                entry.Settings.clearColor = true; entry.Settings.colorClearValue = Color.clear;
                var document = entry.Owner.AddComponent<UIDocument>(); document.panelSettings = entry.Settings; document.sortingOrder = -5000;
                var root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
                root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/BeltCustomizer"));
                entry.Preview = new BeltPreview(effective, true); entry.Preview.style.flexGrow = 1; root.Add(entry.Preview);
            }
            if (entry.Signature == signature) return entry;
            entry.Template = BeltPartsCatalog.Load().bases[effective.Template - 1]; entry.Design = effective;
            var height = Mathf.RoundToInt(1024 * entry.Template.height / entry.Template.width);
            if (entry.Texture == null || entry.Texture.height != height)
            {
                DestroyTexture(entry.Texture); entry.Texture = CreateTexture(1024, height, "Belt " + key, true); entry.Settings.targetTexture = entry.Texture;
                var root = entry.Owner.GetComponent<UIDocument>().rootVisualElement; root.style.width = 1024; root.style.height = height;
            }
            entry.Signature = signature; entry.Ready = false; entry.Preview.SetDesign(effective); return entry;
        }

        private static Texture Portrait(string key, WrestlerState wrestler, Texture raw, BeltTexture[] entries, string signature)
        {
            if (!portraits.TryGetValue(key, out var entry))
            {
                entry = new ChampionTexture { Texture = CreateTexture(PortraitRenderer.Width, PortraitRenderer.Height, "Champion " + wrestler.Id), WrestlerId = wrestler.Id };
                portraits.Add(key, entry);
            }
            if (entry.Signature == signature) return entry.Texture;
            entry.Signature = signature; entry.Raw = raw; entry.Belts = entries; entry.Gender = wrestler.Identity.Gender;
            entry.Body = Mathf.Clamp(wrestler.Presentation.Appearance?.BodyType ?? 1, 1, 3);
            var appearance = wrestler.Presentation.Appearance;
            var head = appearance?.Part("head") ?? 1;
            var gender = entry.Gender == WrestlerGender.Male ? "Male" : "Female";
            entry.FaceMask = Resources.Load<Texture2D>($"PWManagerUI/Portraits/{gender}/head-{head}");
            entry.Dirty = true; Graphics.Blit(raw, entry.Texture);
            if (entries.All(b => b.Ready)) Render(entry);
            return entry.Texture;
        }

        private static RenderTexture CreateTexture(int width, int height, string name, bool ui = false)
        {
            var texture = new RenderTexture(width, height, ui ? 24 : 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.Create(); var previous = RenderTexture.active;
            try { RenderTexture.active = texture; GL.Clear(false, true, Color.clear); } finally { RenderTexture.active = previous; }
            return texture;
        }

        private static void EnsureDriver()
        {
            if (driver != null) return;
            driver = new GameObject("Champion portrait rendering") { hideFlags = HideFlags.HideAndDontSave }; driver.AddComponent<BeltPortraitDriver>();
        }

        public static void FlushPrepared()
        {
            foreach (var entry in belts.Values) if (entry.Preview.Prepared) entry.Ready = true;
            foreach (var entry in portraits.Values) if (entry.Dirty && entry.Belts.All(b => b.Ready)) Render(entry);
        }

        private static void Render(ChampionTexture entry)
        {
            if (composite == null) composite = new Material(Resources.Load<Shader>("PWManagerUI/BeltParts/ChampionPortrait"));
            var current = RenderTexture.GetTemporary(PortraitRenderer.Width, PortraitRenderer.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var next = RenderTexture.GetTemporary(PortraitRenderer.Width, PortraitRenderer.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = current; GL.Clear(false, true, Color.clear);
                void Layer(int mode, Texture texture)
                {
                    composite.SetFloat("_Mode", mode); composite.SetTexture("_Layer", texture);
                    Graphics.Blit(current, next, composite); (current, next) = (next, current);
                }
                void Draw(int index, bool front)
                {
                    var belt = entry.Belts[index]; var template = belt.Template; var main = template.boxes[1];
                    var pose = template.poses.First(p => p.gender == (entry.Gender == WrestlerGender.Male ? "men" : "women") && p.body == entry.Body && p.left == (index == 1));
                    composite.SetVector("_Pose", new Vector4(pose.x, pose.y, pose.scale, pose.angle));
                    composite.SetVector("_Fold", new Vector4(template.fold.x, template.fold.slope, template.seam.top, template.seam.bottom));
                    composite.SetVector("_BeltSize", new Vector4(template.width, template.height, main.x + main.width / 2, main.y + main.height / 2));
                    composite.SetFloat("_Front", front ? 1 : 0);
                    ColorUtility.TryParseHtmlString(belt.Design.StrapColor == BeltStrapColor.White ? "#c9c6c0" : belt.Design.StrapColor == BeltStrapColor.Brown ? "#b58355" : "#6f6b61", out var edge);
                    composite.SetVector("_Edge", edge); Layer(0, belt.Texture);
                }
                if (entry.Belts.Length > 1) Draw(1, false); Draw(0, false); Layer(1, entry.Raw);
                if (entry.Belts.Length > 1) Draw(1, true); Draw(0, true);
                if (entry.FaceMask != null) { composite.SetTexture("_FaceMask", entry.FaceMask); Layer(2, entry.Raw); }
                Graphics.Blit(current, entry.Texture); entry.Dirty = false;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(current); RenderTexture.ReleaseTemporary(next); }
        }

        public static void Prune(GameSave save)
        {
            var ids = new HashSet<string>((save?.Wrestlers ?? new()).Select(w => w.Id));
            foreach (var key in portraits.Where(p => !ids.Contains(p.Value.WrestlerId)).Select(p => p.Key).ToList()) { DestroyTexture(portraits[key].Texture); portraits.Remove(key); }
        }
        public static void Clear()
        {
            foreach (var entry in portraits.Values) DestroyTexture(entry.Texture); portraits.Clear();
            foreach (var entry in belts.Values) { PortraitRenderer.DestroyOwned(entry.Owner); PortraitRenderer.DestroyOwned(entry.Settings); DestroyTexture(entry.Texture); } belts.Clear();
            PortraitRenderer.DestroyOwned(driver); driver = null; PortraitRenderer.DestroyOwned(composite); composite = null;
        }
        private static void DestroyTexture(RenderTexture texture) { if (texture != null) { if (RenderTexture.active == texture) RenderTexture.active = null; texture.Release(); PortraitRenderer.DestroyOwned(texture); } }
    }
}
