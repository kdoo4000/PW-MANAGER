using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class BeltPreview : VisualElement
    {
        private BeltDesignState design;
        private BeltTemplate template;
        private BeltBox viewBox;
        private readonly bool fullWidth;
        internal bool Prepared { get; private set; }
        private float scale;
        private Vector2 origin;
        private readonly VisualElement layers;
        private readonly VisualElement gems;
        public BeltDesignState Design => design.Copy();

        public BeltPreview(BeltDesignState value, bool fullWidth = false)
        {
            design = value.Copy(); this.fullWidth = fullWidth; pickingMode = PickingMode.Ignore;
            AddToClassList("belt-preview");
            layers = new VisualElement { pickingMode = PickingMode.Ignore }; layers.style.position = Position.Absolute; Add(layers);
            gems = new VisualElement { pickingMode = PickingMode.Ignore }; gems.style.position = Position.Absolute; gems.generateVisualContent += DrawGems;
            RegisterCallback<GeometryChangedEvent>(_ => Rebuild());
            generateVisualContent += _ => Prepared = true;
        }

        public void SetDesign(BeltDesignState value)
        {
            BeltDesignRules.Validate(value); design = value.Copy(); Rebuild();
        }

        private void Rebuild()
        {
            if (float.IsNaN(contentRect.width) || float.IsNaN(contentRect.height) || float.IsInfinity(contentRect.width) ||
                float.IsInfinity(contentRect.height) || contentRect.width < 1 || contentRect.height < 1) return;
            var catalog = BeltPartsCatalog.Load(); template = catalog.bases[design.Template - 1];
            Prepared = false;
            viewBox = fullWidth ? new BeltBox { width = template.width, height = template.height } : template.crop;
            scale = Mathf.Min(contentRect.width / viewBox.width, contentRect.height / viewBox.height);
            origin = new Vector2((contentRect.width - viewBox.width * scale) / 2, (contentRect.height - viewBox.height * scale) / 2);
            layers.Clear(); layers.style.left = 0; layers.style.top = 0; layers.style.width = contentRect.width; layers.style.height = contentRect.height;
            Picture(BeltPartTextures.Get(fullWidth ? template.fullImage : template.image, design, fullWidth ? template.fullMask : template.mask), viewBox);
            Emblem(design.MainEmblem, 1, design.Nameplates); Emblem(design.SideEmblem, 0, false); Emblem(design.SideEmblem, 2, false);
            if (design.OverlayAbbreviation && design.MainEmblem != 7) Abbreviation(design.Nameplates ? template.namedMain : template.boxes[1]);
            gems.style.left = 0; gems.style.top = 0; gems.style.width = contentRect.width; gems.style.height = contentRect.height; layers.Add(gems); gems.MarkDirtyRepaint();
            if (design.Nameplates)
            {
                var frames = catalog.nameplates[design.Template - 1].frames;
                for (var i = template.lowerTitle ? 1 : 0; i < 2; i++)
                {
                    var frame = frames[i]; var box = template.nameplates[i];
                    Picture(BeltPartTextures.Get(frame.image, design), frame.box ?? box);
                    if (template.lowerTitle)
                    {
                        CurvedText(design.NameplateText, box, frame, true, false);
                        CurvedText("CHAMPION", box, frame, true, true);
                    }
                    else CurvedText(i == 0 ? design.NameplateText : "CHAMPION", box, frame, false, false);
                }
            }
            MarkDirtyRepaint();
        }

        private Rect Project(BeltBox box) => new(origin.x + (box.x - viewBox.x) * scale, origin.y + (box.y - viewBox.y) * scale, box.width * scale, box.height * scale);

        private void Picture(Texture texture, BeltBox box, BeltBox clipBox = null)
        {
            var rect = Project(box);
            VisualElement parent = layers;
            if (clipBox != null)
            {
                var clip = Project(clipBox); var container = new VisualElement { pickingMode = PickingMode.Ignore };
                container.style.position = Position.Absolute; container.style.left = clip.x; container.style.top = clip.y;
                container.style.width = clip.width; container.style.height = clip.height; container.style.overflow = Overflow.Hidden; layers.Add(container);
                rect.x -= clip.x; rect.y -= clip.y; parent = container;
            }
            var image = new Image { image = texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.style.position = Position.Absolute; image.style.left = rect.x; image.style.top = rect.y; image.style.width = rect.width; image.style.height = rect.height; parent.Add(image);
        }

        private void Emblem(int emblem, int plate, bool named)
        {
            if (emblem == 0) return;
            if (emblem == 7) { Abbreviation(named ? template.namedMain : template.boxes[plate]); return; }
            var placement = template.placements[emblem - 1];
            Picture(BeltPartTextures.Get(BeltPartsCatalog.Load().ornaments[emblem - 1].image, design), named ? placement.named : placement.plates[plate], template.plateRegions[plate]);
        }

        private void Abbreviation(BeltBox box)
        {
            if (string.IsNullOrWhiteSpace(design.Abbreviation)) return;
            var rect = Project(box); var units = design.Abbreviation.Sum(WidthUnit);
            var size = Mathf.Min(rect.height * .65f, rect.width * .8f / Mathf.Max(1, units));
            var label = Letter(design.Abbreviation, size, false); label.style.left = rect.x; label.style.top = rect.y;
            label.style.width = rect.width; label.style.height = rect.height; layers.Add(label);
        }

        private Label Letter(string text, float size, bool lightFace)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("belt-letter"); label.AddToClassList("belt-letter-" + design.Lettering.ToString().ToLowerInvariant());
            label.style.position = Position.Absolute; label.style.fontSize = Mathf.Max(.5f, size); label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.color = lightFace ? new Color(.20f, .15f, .08f) : new Color(.98f, .87f, .60f);
            return label;
        }

        private static float WidthUnit(char value) => value > 255 ? 1 : char.IsWhiteSpace(value) ? .35f : char.IsUpper(value) ? .66f : .56f;

        private void CurvedText(string text, BeltBox box, BeltNameplateFrame frame, bool shield, bool right)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var raw = frame.path.Where(p => !shield || (right ? box.x + p.x * box.width > template.shieldV.x + box.width * .035f : box.x + p.x * box.width < template.shieldV.x - box.width * .035f)).ToArray();
            if (raw.Length < 2) return;
            var points = raw.Select(p => new Vector2(box.x + p.x * box.width, shield ? ShieldY(box.x + p.x * box.width) : box.y + p.y * box.height)).ToArray();
            var distance = new float[points.Length];
            for (var i = 1; i < points.Length; i++) distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            var available = distance[distance.Length - 1]; var units = text.Sum(WidthUnit);
            var height = raw.Min(p => p.height * box.height);
            var size = Mathf.Min(height * .84f, available * .92f / Mathf.Max(1, units));
            var cursor = (available - units * size) / 2;
            foreach (var character in text)
            {
                var width = WidthUnit(character) * size; var middle = cursor + width / 2;
                var i = 1; while (i < distance.Length - 1 && distance[i] < middle) i++;
                var delta = points[i] - points[i - 1]; var point = Vector2.Lerp(points[i - 1], points[i], Mathf.InverseLerp(distance[i - 1], distance[i], middle));
                var label = Letter(character.ToString(), size * scale, frame.face == "light");
                label.style.width = size * scale * 1.8f; label.style.height = size * scale * 1.8f;
                label.style.left = origin.x + (point.x - viewBox.x) * scale - size * scale * .9f;
                label.style.top = origin.y + (point.y - viewBox.y) * scale - size * scale * .9f;
                label.style.rotate = new Rotate(new Angle(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg)); layers.Add(label);
                cursor += width;
            }
        }

        private float ShieldY(float x)
        {
            var rim = template.shieldV;
            return rim.y + (x - rim.x) * (x < rim.x ? rim.left : rim.right) - rim.inset;
        }

        private void DrawGems(MeshGenerationContext context)
        {
            if (template == null || scale <= 0) return;
            var painter = context.painter2D;
            if (!fullWidth && design.StrapColor == BeltStrapColor.White && template.stitchPath != null)
            {
                painter.strokeColor = new Color(.47f, .47f, .47f); painter.lineWidth = Mathf.Max(.5f, 2.5f * scale);
                var phase = 0f;
                for (var i = 1; i <= template.stitchPath.Length; i++)
                {
                    var a = template.stitchPath[i - 1]; var b = template.stitchPath[i % template.stitchPath.Length];
                    var from = new Vector2(a.x, a.y); var to = new Vector2(b.x, b.y); var length = Vector2.Distance(from, to);
                    for (var cursor = 0f; cursor < length;)
                    {
                        var step = Mathf.Min(length - cursor, (phase < 8 ? 8 : 13) - phase);
                        if (phase < 8)
                        {
                            painter.BeginPath(); painter.MoveTo(origin + (Vector2.Lerp(from, to, cursor / length) - new Vector2(viewBox.x, viewBox.y)) * scale);
                            painter.LineTo(origin + (Vector2.Lerp(from, to, (cursor + step) / length) - new Vector2(viewBox.x, viewBox.y)) * scale); painter.Stroke();
                        }
                        cursor += step; phase = (phase + step) % 13;
                    }
                }
            }
            var colors = new[] { "#d2d9dc", "#a3adb5", "#e1e8eb", "#fafcfc", "#bcc7cd", "#edf2f3", "#ffffff", "#dbe4e7" };
            for (var plate = 0; plate < 3; plate++)
            {
                if (!(plate == 1 ? design.MainGems : design.SideGems)) continue;
                foreach (var gem in template.gems[plate].points)
                {
                    var center = origin + new Vector2(gem.x - viewBox.x, gem.y - viewBox.y) * scale;
                    for (var face = 0; face < 8; face++)
                    {
                        var a = gem.angle + face * Mathf.PI / 4; var b = a + Mathf.PI / 4;
                        ColorUtility.TryParseHtmlString(colors[face], out var color); painter.fillColor = color;
                        painter.BeginPath(); painter.MoveTo(center);
                        painter.LineTo(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * gem.radius * scale);
                        painter.LineTo(center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * gem.radius * scale); painter.ClosePath(); painter.Fill();
                    }
                }
            }
        }
    }
}
