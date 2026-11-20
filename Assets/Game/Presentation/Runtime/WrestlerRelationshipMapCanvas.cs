using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal sealed class WrestlerRelationshipMapCanvas : VisualElement
    {
        private const float CardWidth = 120, CardHeight = 166, Padding = 32;
        private readonly VisualElement world = new() { name = "wr-map-world" };
        private readonly Image background = new() { name = "wr-map-gradient", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
        private Texture2D gradient;
        private readonly VisualElement lines = new() { pickingMode = PickingMode.Ignore };
        private readonly Dictionary<string, VisualElement> nodes = new();
        private readonly List<(string Id, float Affinity, Vector2 From, Vector2 To)> edges = new();
        private readonly Action<string> selected;
        private readonly Vector2 size;
        private readonly Vector2 origin;
        private Vector2 pan, downPosition, downPan;
        private int pointer = -1;
        private bool dragging, needsFit = true;
        private string selectedId;
        public float Zoom { get; private set; } = 1;

        public WrestlerRelationshipMapCanvas(WrestlerState center,
            IReadOnlyList<WrestlerRelationshipsView.Relationship> relationships, Action<string> selected)
        {
            name = "wr-map"; AddToClassList("wr-map"); focusable = true;
            this.selected = selected;
            background.style.position = Position.Absolute;
            background.style.left = 0; background.style.right = 0; background.style.top = 0; background.style.bottom = 0;
            Add(background);
            RegisterCallback<AttachToPanelEvent>(_ => { if (gradient == null) CreateBackground(); });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                background.image = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(gradient);
                else UnityEngine.Object.DestroyImmediate(gradient);
                gradient = null;
            });
            var positions = new Dictionary<string, Vector2> { [center.Id] = Vector2.zero };
            var members = relationships.Where(x => x.Groups.Count > 0).OrderByDescending(x => x.IsTagPartner)
                .ThenBy(x => x.Wrestler.Id, StringComparer.Ordinal).ToList();
            const float spacing = CardWidth + 40;
            for (var i = 0; i < members.Count; i++)
                positions[members[i].Wrestler.Id] = new Vector2((i % 2 == 0 ? -1 : 1) * (i / 2 + 1) * spacing, 0);
            void Place(List<WrestlerRelationshipsView.Relationship> band)
            {
                for (var i = 0; i < band.Count; i++)
                {
                    var relation = band[i];
                    const float distance = CardHeight + 72;
                    positions[relation.Wrestler.Id] = new Vector2((i - (band.Count - 1) / 2f) * spacing,
                        relation.Affinity > 0 ? -distance : distance);
                }
            }
            Place(relationships.Where(x => x.Groups.Count == 0 && x.Affinity > 0).OrderBy(x => x.Wrestler.Id, StringComparer.Ordinal).ToList());
            Place(relationships.Where(x => x.Groups.Count == 0 && x.Affinity < 0).OrderBy(x => x.Wrestler.Id, StringComparer.Ordinal).ToList());
            origin = new Vector2(positions.Values.Max(p => Mathf.Abs(p.x)) + CardWidth / 2 + Padding,
                positions.Values.Max(p => Mathf.Abs(p.y)) + CardHeight / 2 + Padding);
            size = origin * 2;
            world.style.position = Position.Absolute;
            world.style.width = size.x; world.style.height = size.y;
            world.style.transformOrigin = new TransformOrigin(0, 0, 0); Add(world);
            void GroupFrame(string name, IEnumerable<string> ids, float padding)
            {
                var points = ids.Select(id => positions[id] + origin).ToList();
                if (points.Count < 2) return;
                var frame = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                frame.AddToClassList("wr-group-frame");
                frame.EnableInClassList("wr-tag-frame", name == "wr-tag-frame");
                frame.style.left = points.Min(p => p.x) - CardWidth / 2 - padding;
                frame.style.top = origin.y - CardHeight / 2 - padding;
                frame.style.width = points.Max(p => p.x) - points.Min(p => p.x) + CardWidth + padding * 2;
                frame.style.height = CardHeight + padding * 2; world.Add(frame);
            }
            GroupFrame("wr-membership-band", members.Select(x => x.Wrestler.Id).Prepend(center.Id), 28);
            GroupFrame("wr-tag-frame", members.Where(x => x.IsTagPartner).Select(x => x.Wrestler.Id).Prepend(center.Id), 12);
            lines.style.position = Position.Absolute; lines.style.width = size.x; lines.style.height = size.y;
            lines.generateVisualContent += DrawLines; world.Add(lines);
            foreach (var relation in relationships)
            {
                var position = positions[relation.Wrestler.Id] + origin;
                if (relation.Stories.Count > 0)
                {
                    var direction = (position - origin).normalized;
                    var edge = (Id: relation.Wrestler.Id, relation.Affinity,
                        From: Boundary(origin, direction), To: Boundary(position, -direction));
                    edges.Add(edge);
                }
                AddNode(relation.Wrestler, position, false, relation.Affinity);
            }
            AddNode(center, origin, true, 0);
            // Cards stay above the connecting lines.
            foreach (var node in nodes.Values) node.BringToFront();
            RegisterCallback<GeometryChangedEvent>(evt => { if (needsFit || evt.oldRect.size != evt.newRect.size) Fit(); });
            RegisterCallback<WheelEvent>(evt =>
            {
                ZoomAt(this.WorldToLocal(evt.mousePosition), evt.delta.y < 0 ? 1.15f : 1 / 1.15f);
                evt.StopPropagation();
            });
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCancelEvent>(_ => EndDrag());
            RegisterCallback<PointerCaptureOutEvent>(_ => { pointer = -1; dragging = false; });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.target != this) return;
                if (evt.keyCode == KeyCode.Home) Fit();
            });
        }

        private void CreateBackground()
        {
            gradient = new Texture2D(1, 128, TextureFormat.RGBA32, false)
            {
                name = "Wrestler relationship gradient", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var colors = new Color[128];
            Color top = new Color32(22, 66, 108, 255), middle = new Color32(16, 17, 20, 255), bottom = new Color32(100, 27, 43, 255);
            for (var row = 0; row < colors.Length; row++)
            {
                var t = row / (float)(colors.Length - 1);
                colors[row] = t < .5f ? Color.Lerp(bottom, middle, t * 2) : Color.Lerp(middle, top, (t - .5f) * 2);
            }
            gradient.SetPixels(colors); gradient.Apply(false, true); background.image = gradient;
        }

        private static Vector2 Boundary(Vector2 center, Vector2 direction)
        {
            var distance = Mathf.Min(CardWidth / 2 / Mathf.Max(.0001f, Mathf.Abs(direction.x)),
                CardHeight / 2 / Mathf.Max(.0001f, Mathf.Abs(direction.y)));
            return center + direction * distance;
        }

        private void AddNode(WrestlerState wrestler, Vector2 position, bool center, float affinity)
        {
            var node = CreateCard(wrestler, center, affinity, () => { if (center) Fit(); else selected(wrestler.Id); });
            node.style.left = position.x - CardWidth / 2; node.style.top = position.y - CardHeight / 2;
            nodes.Add(wrestler.Id, node); world.Add(node);
        }

        internal static VisualElement CreateCard(WrestlerState wrestler, bool center, float affinity, Action select)
        {
            var node = new VisualElement { name = center ? "wr-center-" + wrestler.Id : "wr-node-" + wrestler.Id };
            node.AddToClassList("sm-wrestler"); node.AddToClassList(center ? "wr-center" : "wr-node");
            node.style.width = CardWidth; node.style.height = CardHeight;
            var portrait = new Button(select)
                { name = "wr-select-" + wrestler.Id, tooltip = center ? "전체 보기" : "관계 보기" };
            portrait.AddToClassList("sm-portrait");
            var frame = WrestlerProfileController.CreatePortraitDisplayWithNameLink(wrestler)
                .Q<VisualElement>(className: "wrestler-portrait-frame");
            frame.RemoveFromHierarchy(); portrait.Add(frame); node.Add(portrait);
            node.Add(WrestlerProfileController.CreateLink(wrestler, "sm-name"));
            if (!center)
            {
                var value = new Label($"우호도 {affinity:+0;-0;0}") { name = "wr-affinity-" + wrestler.Id };
                value.AddToClassList("wr-node-affinity"); node.Add(value);
            }
            return node;
        }

        public void SetSelection(string id)
        {
            selectedId = id;
            foreach (var node in nodes) node.Value.EnableInClassList("wr-selected", node.Key == id);
            lines.MarkDirtyRepaint();
        }

        private void DrawLines(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            foreach (var edge in edges)
            {
                painter.strokeColor = edge.Id == selectedId ? new Color32(217, 180, 91, 255)
                    : edge.Affinity > 0 ? new Color32(101, 183, 146, 255)
                    : edge.Affinity < 0 ? new Color32(220, 112, 123, 255) : new Color32(145, 140, 132, 255);
                painter.lineWidth = edge.Id == selectedId ? 4 : 2;
                painter.BeginPath(); painter.MoveTo(edge.From); painter.LineTo(edge.To); painter.Stroke();
            }
        }

        public void Fit()
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return;
            Zoom = Mathf.Clamp(Mathf.Min((contentRect.width - 32) / size.x, (contentRect.height - 32) / size.y), .04f, 1.5f);
            pan = contentRect.center - origin * Zoom; needsFit = false; ApplyTransform();
        }

        public void ZoomAt(Vector2 position, float factor)
        {
            var before = Zoom; Zoom = Mathf.Clamp(Zoom * factor, .04f, 2.5f);
            pan = position - (position - pan) * (Zoom / before); ApplyTransform();
        }

        private void ApplyTransform()
        {
            world.style.scale = new Scale(new Vector3(Zoom, Zoom, 1));
            world.style.translate = new Translate(pan.x, pan.y, 0);
            var rect = contentRect;
            if (rect.height <= 0) return;
            // Sample world height so the neutral band follows the wrestler during pan and zoom.
            var top = (rect.yMin - pan.y) / Zoom;
            var height = rect.height / Zoom;
            background.uv = new Rect(0, 1 - (top + height) / size.y, 1, height / size.y);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            for (var element = evt.target as VisualElement; element != null && element != this; element = element.parent)
                if (element is Button) return;
            pointer = evt.pointerId; downPosition = this.WorldToLocal(evt.position); downPan = pan; dragging = false;
            Focus(); this.CapturePointer(pointer); evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointer) return;
            var delta = this.WorldToLocal(evt.position) - downPosition;
            dragging |= delta.sqrMagnitude > 36;
            if (dragging) { pan = downPan + delta; ApplyTransform(); }
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pointer || evt.button != 0) return;
            if (!dragging)
            {
                var point = (this.WorldToLocal(evt.position) - pan) / Zoom;
                var hit = edges.Select(e => (e.Id, Distance: StorylineMapCanvas.DistanceToEdge(point, new[] { e.From, e.To })))
                    .Where(e => e.Distance * Zoom <= 12).OrderBy(e => e.Distance).FirstOrDefault();
                if (hit.Id != null) selected(hit.Id);
            }
            EndDrag(); evt.StopPropagation();
        }

        private void EndDrag()
        {
            var previous = pointer; pointer = -1; dragging = false;
            if (previous >= 0 && this.HasPointerCapture(previous)) this.ReleasePointer(previous);
        }
    }
}
