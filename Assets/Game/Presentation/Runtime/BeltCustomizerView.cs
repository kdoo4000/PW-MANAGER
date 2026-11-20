using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class BeltCustomizerView : VisualElement
    {
        private readonly BeltDesignState design;
        private readonly Func<string> title;
        private readonly Action changed;
        private readonly Func<WrestlerState> wearer;
        private readonly Image shoulderPreview;
        private readonly VisualElement wearerName;
        private bool doubleChampion;
        private readonly BeltPreview preview;
        private readonly List<(Button Button, Func<bool> Selected)> choices = new();
        private readonly List<(BeltPreview Preview, int Template)> templates = new();
        private readonly List<(Image Image, int Emblem)> emblems = new();

        internal BeltCustomizerView(BeltDesignState draft, Func<string> titleText, Action onChanged, Func<WrestlerState> wearer = null)
        {
            design = draft; title = titleText; changed = onChanged; this.wearer = wearer; name = "belt-customizer"; AddToClassList("belt-customizer");
            var viewTabs = new VisualElement(); viewTabs.AddToClassList("ops-tabs"); Add(viewTabs);
            preview = new BeltPreview(Effective(design, title())); preview.name = "belt-live-preview"; preview.AddToClassList("belt-live-preview"); Add(preview);
            var shoulder = new VisualElement { name = "belt-shoulder-view" }; shoulder.style.display = DisplayStyle.None; Add(shoulder);
            shoulderPreview = new Image { name = "belt-shoulder-preview", scaleMode = ScaleMode.ScaleToFit }; shoulderPreview.AddToClassList("belt-live-preview"); shoulder.Add(shoulderPreview);
            wearerName = new VisualElement(); wearerName.AddToClassList("belt-wearer"); shoulder.Add(wearerName);
            Toggle(shoulder, "양쪽 어깨 미리보기", "belt-double-preview", false, value => doubleChampion = value);
            foreach (var (key, label) in new[] { ("belt", "벨트"), ("shoulder", "어깨 착용") })
            {
                var tab = new Button(() =>
                {
                    preview.style.display = key == "belt" ? DisplayStyle.Flex : DisplayStyle.None; shoulder.style.display = key == "shoulder" ? DisplayStyle.Flex : DisplayStyle.None;
                    foreach (var id in new[] { "belt", "shoulder" }) viewTabs.Q<Button>("belt-view-" + id).EnableInClassList("ops-active", id == key);
                    RefreshPreview(false);
                }) { text = label, name = "belt-view-" + key };
                tab.AddToClassList("ops-tab"); tab.EnableInClassList("ops-active", key == "belt"); viewTabs.Add(tab);
            }
            var tabs = new VisualElement(); tabs.AddToClassList("ops-tabs"); Add(tabs);
            var panels = new Dictionary<string, VisualElement>();
            foreach (var (key, label) in new[] { ("shape", "기본형"), ("materials", "문양·소재"), ("lettering", "문구·장식") })
            {
                var panel = new VisualElement(); panel.AddToClassList("ops-form"); panel.name = "belt-panel-" + key; panels.Add(key, panel);
                var button = new Button(() =>
                {
                    foreach (var pair in panels) { pair.Value.style.display = pair.Key == key ? DisplayStyle.Flex : DisplayStyle.None; this.Q<Button>("belt-tab-" + pair.Key).EnableInClassList("ops-active", pair.Key == key); }
                }) { text = label, name = "belt-tab-" + key };
                button.AddToClassList("ops-tab"); button.EnableInClassList("ops-active", key == "shape"); tabs.Add(button);
                panel.style.display = key == "shape" ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (var panel in panels.Values) Add(panel);
            var shapes = Grid(panels["shape"]);
            for (var index = 1; index <= BeltDesignRules.TemplateCount; index++)
            {
                var number = index; var button = Choice(shapes, $"기본형 {number:00}", "belt-template-" + number, () => design.Template == number, () => design.Template = number);
                var sample = Effective(design, title()); sample.Template = number;
                var picture = new BeltPreview(sample); picture.AddToClassList("belt-template-image"); button.Insert(0, picture); templates.Add((picture, number));
            }
            var materials = panels["materials"];
            Palette(materials, "스트랩", "strap", new[] { "블랙", "화이트", "가죽색" }, BeltPartTextures.StrapColors,
                () => (int)design.StrapColor, value => design.StrapColor = (BeltStrapColor)value);
            Palette(materials, "플레이트", "metal", new[] { "골드", "로즈골드", "실버" }, BeltPartTextures.MetalColors,
                () => (int)design.MetalColor, value => design.MetalColor = (BeltMetalColor)value);
            EmblemChoices(materials, "메인 문양", "main", () => design.MainEmblem, value => design.MainEmblem = value);
            EmblemChoices(materials, "사이드 문양", "side", () => design.SideEmblem, value => design.SideEmblem = value);
            var lettering = panels["lettering"];
            Toggle(lettering, "이름판 넣기", "belt-nameplates", design.Nameplates, value => design.Nameplates = value);
            var text = new TextField("명판 문구") { name = "belt-nameplate-text", value = design.NameplateText, maxLength = BeltDesignRules.MaximumTitleLength, tooltip = "비워두면 챔피언십 이름을 사용합니다. 최대 32자." }; lettering.Add(text);
            text.RegisterValueChangedCallback(e => { design.NameplateText = e.newValue; RefreshPreview(); });
            var abbreviation = new TextField("단체 약자") { name = "belt-abbreviation", value = design.Abbreviation, maxLength = BeltDesignRules.MaximumAbbreviationLength }; lettering.Add(abbreviation);
            abbreviation.RegisterValueChangedCallback(e => { design.Abbreviation = e.newValue; RefreshPreview(); });
            Toggle(lettering, "메인 문양 위에 약자 표시", "belt-overlay-abbreviation", design.OverlayAbbreviation, value => design.OverlayAbbreviation = value);
            var font = new DropdownField("글자 장식", new List<string> { "장식 글씨", "각인", "양각" }, (int)design.Lettering) { name = "belt-lettering" }; lettering.Add(font);
            font.RegisterValueChangedCallback(_ => { design.Lettering = (BeltLettering)font.index; RefreshPreview(); });
            Toggle(lettering, "메인 테두리 보석", "belt-main-gems", design.MainGems, value => design.MainGems = value);
            Toggle(lettering, "사이드 테두리 보석", "belt-side-gems", design.SideGems, value => design.SideGems = value);
            RefreshPreview(false);
        }

        internal static BeltDesignState Effective(BeltDesignState state, string fallbackTitle)
        {
            var copy = state.Copy();
            if (string.IsNullOrWhiteSpace(copy.NameplateText)) copy.NameplateText = new string((fallbackTitle ?? "").Take(BeltDesignRules.MaximumTitleLength).ToArray());
            return copy;
        }

        internal void RefreshPreview(bool notify = true)
        {
            preview.SetDesign(Effective(design, title()));
            if (this.Q("belt-shoulder-view").style.display.value != DisplayStyle.None)
            {
                var wrestler = wearer?.Invoke(); wearerName.Clear();
                shoulderPreview.image = wrestler == null ? null : ChampionPortraitRenderer.Preview(wrestler, design, title(), doubleChampion);
                if (wrestler == null) wearerName.Add(new Label("착용할 소속 선수가 없습니다."));
                else wearerName.Add(WrestlerProfileController.CreateLink(wrestler));
            }
            foreach (var (picture, number) in templates) { var sample = Effective(design, title()); sample.Template = number; picture.SetDesign(sample); }
            foreach (var (picture, emblem) in emblems) picture.image = BeltPartTextures.Get(BeltPartsCatalog.Load().ornaments[emblem - 1].image, design);
            foreach (var choice in choices) choice.Button.EnableInClassList("belt-choice-selected", choice.Selected());
            if (notify) changed?.Invoke();
        }

        private VisualElement Grid(VisualElement parent)
        {
            var grid = new VisualElement(); grid.AddToClassList("belt-choice-grid"); parent.Add(grid); return grid;
        }

        private Button Choice(VisualElement parent, string label, string id, Func<bool> selected, Action select)
        {
            var button = new Button(() => { select(); RefreshPreview(); }) { name = id }; button.AddToClassList("belt-choice"); parent.Add(button);
            var text = new Label(label); text.pickingMode = PickingMode.Ignore; button.Add(text); choices.Add((button, selected)); return button;
        }

        private void Palette(VisualElement parent, string label, string key, string[] labels, string[] colors, Func<int> current, Action<int> select)
        {
            OperationsView.Heading(parent, label); var grid = Grid(parent);
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i; var button = Choice(grid, labels[i], "belt-" + key + "-" + i, () => current() == index, () => select(index)); button.AddToClassList("belt-color-choice");
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore }; swatch.AddToClassList("belt-swatch"); ColorUtility.TryParseHtmlString(colors[i], out var color); swatch.style.backgroundColor = color; button.Insert(0, swatch);
            }
        }

        private void EmblemChoices(VisualElement parent, string label, string key, Func<int> current, Action<int> select)
        {
            OperationsView.Heading(parent, label); var grid = Grid(parent);
            var names = new[] { "없음", "지구본", "월계수", "독수리", "왕관", "사자", "교차 횃불", "약자" };
            for (var i = 0; i < names.Length; i++)
            {
                var index = i; var button = Choice(grid, names[i], "belt-" + key + "-" + i, () => current() == index, () => select(index)); button.AddToClassList("belt-emblem-choice");
                if (i < 1 || i > 6) continue;
                var image = new Image { image = BeltPartTextures.Get(BeltPartsCatalog.Load().ornaments[i - 1].image, design), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore }; image.AddToClassList("belt-emblem-image"); button.Insert(0, image); emblems.Add((image, i));
            }
        }

        private void Toggle(VisualElement parent, string label, string id, bool value, Action<bool> set)
        {
            var toggle = new UnityEngine.UIElements.Toggle(label) { name = id, value = value }; parent.Add(toggle);
            toggle.RegisterValueChangedCallback(e => { set(e.newValue); RefreshPreview(); });
        }
    }
}
