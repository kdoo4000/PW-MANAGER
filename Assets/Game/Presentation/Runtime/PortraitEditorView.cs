using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class PortraitEditorView : VisualElement
    {
        private readonly WrestlerGender gender;
        private readonly bool wardrobeOnly;
        private readonly Image preview;
        private readonly ScrollView controls;
        private readonly VisualElement tabs;
        private string activeTab;
        public PortraitAppearanceState Appearance { get; }

        public PortraitEditorView(PortraitAppearanceState appearance, WrestlerGender gender, bool wardrobeOnly = false)
        {
            this.gender = gender; this.wardrobeOnly = wardrobeOnly;
            Appearance = appearance.Clone();
            PortraitAppearanceRules.Normalize(Appearance, gender);
            AddToClassList("portrait-editor");
            styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/PortraitEditor"));
            preview = new Image { name = "portrait-preview", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            preview.AddToClassList("portrait-preview"); Add(preview);
            preview.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.width <= 0) return;
                var height = evt.newRect.width * PortraitRenderer.Height / PortraitRenderer.Width;
                if (Mathf.Abs(preview.style.height.value.value - height) > .5f) preview.style.height = height;
            });
            var panel = new VisualElement(); panel.AddToClassList("portrait-controls"); Add(panel);
            var toolbar = new VisualElement(); toolbar.AddToClassList("portrait-toolbar"); panel.Add(toolbar);
            tabs = new VisualElement(); tabs.AddToClassList("portrait-tabs"); toolbar.Add(tabs);
            activeTab = wardrobeOnly ? "복장" : "신체";
            foreach (var tab in wardrobeOnly ? new[] { "복장", "얼굴", "신체" } : new[] { "신체", "얼굴", "머리", "복장" })
            {
                var current = tab;
                var button = new Button(() => { activeTab = current; RefreshControls(); }) { text = current, name = "portrait-tab-" + current };
                tabs.Add(button);
            }
            var random = new Button(Randomize) { text = "랜덤", name = "portrait-random" }; toolbar.Add(random);
            controls = new ScrollView(); controls.AddToClassList("portrait-fields"); panel.Add(controls);
            RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target != this) return;
                PortraitRenderer.DestroyOwned(preview.image); preview.image = null;
            });
            RefreshControls(); RefreshPreview();
        }

        private static string Tab(string category) => category switch
        {
            "bodytype" or "tattoo" or "bodypaint" => "신체",
            "hair" or "hairback" or "beard" => "머리",
            "clothes" or "mask" or "eyewear" or "headwear" => "복장",
            _ => "얼굴"
        };

        private void RefreshControls()
        {
            foreach (var button in tabs.Children().OfType<Button>()) button.EnableInClassList("selected", button.text == activeTab);
            var offset = controls.scrollOffset;
            controls.Clear();
            foreach (var group in PortraitCatalog.Groups(gender).Where(x => Tab(x.Category) == activeTab && (!wardrobeOnly || x.Wardrobe)))
            {
                var ids = Enumerable.Range(1, group.Count).Where(index => Available(group.Category, index)).ToList();
                if (ids.Count == 0) continue;
                var chosen = group.Category == "bodytype" ? Appearance.BodyType : Appearance.Part(group.Category);
                var field = new VisualElement { name = "portrait-" + group.Category };
                field.AddToClassList("portrait-field");
                field.AddToClassList("portrait-part-selector");
                var label = new Label(group.Label); label.AddToClassList("portrait-part-label"); field.Add(label);
                var selection = new VisualElement(); selection.AddToClassList("portrait-part-selection"); field.Add(selection);
                var value = new Label(group.PartLabel(chosen)) { name = "portrait-" + group.Category + "-value" };
                value.AddToClassList("portrait-part-value");
                void Move(int direction)
                {
                    var current = group.Category == "bodytype" ? Appearance.BodyType : Appearance.Part(group.Category);
                    var position = Math.Max(0, ids.IndexOf(current));
                    var id = ids[(position + direction + ids.Count) % ids.Count];
                    if (group.Category == "bodytype") Appearance.BodyType = id; else Appearance.SetPart(group.Category, id);
                    PortraitAppearanceRules.Normalize(Appearance, gender); RefreshControls(); RefreshPreview();
                    controls.Q<Button>("portrait-" + group.Category + (direction < 0 ? "-previous" : "-next"))?.Focus();
                }
                var previous = new Button(() => Move(-1)) { text = "‹", name = "portrait-" + group.Category + "-previous", tooltip = group.Label + " 이전 파츠" };
                var next = new Button(() => Move(1)) { text = "›", name = "portrait-" + group.Category + "-next", tooltip = group.Label + " 다음 파츠" };
                previous.AddToClassList("portrait-part-arrow"); next.AddToClassList("portrait-part-arrow");
                previous.SetEnabled(ids.Count > 1); next.SetEnabled(ids.Count > 1);
                selection.Add(previous); selection.Add(value); selection.Add(next);
                selection.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (ids.Count <= 1 || evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow) return;
                    Move(evt.keyCode == KeyCode.LeftArrow ? -1 : 1); evt.StopPropagation();
                });
                controls.Add(field);
            }
            if (activeTab == "신체" && !wardrobeOnly)
            {
                var skin = new SliderInt("피부 밝기", 40, 150) { name = "portrait-skin", value = Appearance.SkinBrightness, showInputField = true };
                skin.AddToClassList("portrait-field");
                skin.RegisterValueChangedCallback(evt => { Appearance.SkinBrightness = evt.newValue; RefreshPreview(); }); controls.Add(skin);
            }
            foreach (var category in PortraitAppearanceRules.ColorCategories.Where(x => ColorTab(x) == activeTab &&
                (!wardrobeOnly || PortraitAppearanceRules.WardrobeColorCategories.Contains(x)) && (gender == WrestlerGender.Male || x != "beard")))
                AddColor(category);
            controls.scrollOffset = offset;
        }

        private bool Available(string category, int index)
        {
            if (!PortraitCatalog.Selectable(gender, category, index)) return false;
            if (category == "clothes" && gender == WrestlerGender.Female && index == 1) return false;
            if (category == "eyewear" && Appearance.Part("mask") > 1) return index == 1;
            if (category == "headwear" && PortraitCatalog.FullMask(Appearance.Part("mask"))) return index == 1;
            if (category == "hairback")
            {
                var allowed = PortraitCatalog.RearHair(gender, Appearance.Part("hair"));
                return allowed == null || allowed.Contains(index);
            }
            return true;
        }

        private static string ColorTab(string category) => category switch
        {
            "hair" or "beard" => "머리",
            "eyes" or "facepaint" or "facepaint-secondary" => "얼굴",
            "tattoo" or "bodypaint" => "신체",
            _ => "복장"
        };

        private void AddColor(string category)
        {
            var labels = new Dictionary<string, string> { ["hair"] = "머리·눈썹 색", ["beard"] = "수염 색", ["eyes"] = "눈 색", ["clothes"] = "의상 주색", ["clothes-secondary"] = "의상 보조색", ["mask"] = "마스크 주색", ["mask-secondary"] = "마스크 보조색", ["facepaint"] = "페인트 주색", ["facepaint-secondary"] = "페인트 보조색", ["tattoo"] = "타투 색", ["bodypaint"] = "바디 페인트 색", ["headwear"] = "모자·두건 주색", ["headwear-secondary"] = "모자·두건 무늬색" };
            var row = new VisualElement(); row.AddToClassList("portrait-color-row"); controls.Add(row);
            var input = new TextField(labels[category]) { name = "portrait-color-" + category, maxLength = 7, value = Appearance.Color(category) ?? "" };
            input.AddToClassList("portrait-color-input"); row.Add(input);
            input.RegisterValueChangedCallback(evt =>
            {
                var valid = PortraitAppearanceRules.ValidColor(evt.newValue);
                input.EnableInClassList("invalid", !valid);
                foreach (var button in row.Query<Button>(className: "portrait-swatch").ToList())
                    button.EnableInClassList("selected", string.Equals(button.userData as string, evt.newValue, StringComparison.OrdinalIgnoreCase));
                if (valid) { Appearance.SetColor(category, evt.newValue); RefreshPreview(); }
            });
            var swatches = new VisualElement(); swatches.AddToClassList("portrait-swatches"); row.Add(swatches);
            foreach (var hex in category == "eyes" ? PortraitAppearanceRules.EyeColors : PortraitAppearanceRules.FabricColors)
            {
                var color = hex;
                var button = new Button(() => input.value = color) { tooltip = labels[category] + " " + color, userData = color };
                ColorUtility.TryParseHtmlString(color, out var rgb); button.style.backgroundColor = rgb;
                button.EnableInClassList("selected", string.Equals(Appearance.Color(category), color, StringComparison.OrdinalIgnoreCase));
                button.AddToClassList("portrait-swatch"); swatches.Add(button);
            }
            swatches.Add(new Button(() => input.value = "") { text = "원래 색" });
            var palette = new Foldout { text = "색상표", value = false, name = "portrait-palette-" + category };
            palette.AddToClassList("portrait-palette"); row.Add(palette);
            var grid = new VisualElement(); grid.AddToClassList("portrait-palette-grid"); palette.Add(grid);
            var colors = new List<Color>();
            for (var i = 0; i < 8; i++) colors.Add(new Color(i / 7f, i / 7f, i / 7f));
            foreach (var saturationValue in new[] { new Vector2(.4f, 1), new Vector2(.8f, .9f), new Vector2(.85f, .45f) })
                for (var hue = 0; hue < 12; hue++) colors.Add(Color.HSVToRGB(hue / 12f, saturationValue.x, saturationValue.y));
            foreach (var rgb in colors)
            {
                var hex = "#" + ColorUtility.ToHtmlStringRGB(rgb);
                var button = new Button(() => input.value = hex) { name = "portrait-palette-" + category + "-" + hex.Substring(1), tooltip = labels[category] + " " + hex, userData = hex };
                button.style.backgroundColor = rgb;
                button.AddToClassList("portrait-swatch"); button.AddToClassList("portrait-palette-color");
                button.EnableInClassList("selected", string.Equals(Appearance.Color(category), hex, StringComparison.OrdinalIgnoreCase));
                grid.Add(button);
            }
        }

        private void Randomize()
        {
            var identity = new WrestlerIdentityState { Gender = gender, BodyType = Appearance.BodyType == 2 ? WrestlerBodyType.Lightweight : Appearance.BodyType == 3 ? WrestlerBodyType.Heavyweight : WrestlerBodyType.Balanced };
            var random = new System.Random();
            var generated = PortraitAppearanceRules.Generate(identity, null, random);
            foreach (var group in PortraitCatalog.Groups(gender).Where(x => x.Category is "pupils" or "facepaint" or "bodypaint"))
                generated.SetPart(group.Category, random.Next(1, group.Count + 1));
            generated.SetColor("eyes", PortraitAppearanceRules.EyeColors[random.Next(PortraitAppearanceRules.EyeColors.Length)]);
            if (wardrobeOnly) PortraitAppearanceRules.ApplyWardrobe(Appearance, generated, gender);
            else
            {
                Appearance.Parts = generated.Parts; Appearance.Colors = generated.Colors; Appearance.SkinBrightness = generated.SkinBrightness;
            }
            RefreshControls(); RefreshPreview();
        }

        private void RefreshPreview() => preview.image = PortraitRenderer.Render(Appearance, gender, preview.image as Texture2D);
    }
}
