using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class TrapezoidPortraitCard : VisualElement
    {
        private bool isFirst;
        private bool isLast;
        private bool isHovered;
        private IReadOnlyList<Texture> portraits = Array.Empty<Texture>();

        public Texture Portrait
        {
            get => portraits.Count > 0 ? portraits[0] : null;
            set => Portraits = new[] { value };
        }

        public IReadOnlyList<Texture> Portraits
        {
            get => portraits;
            set { portraits = value ?? Array.Empty<Texture>(); MarkDirtyRepaint(); }
        }

        public bool IsFirst
        {
            get => isFirst;
            set
            {
                if (isFirst == value) return;
                isFirst = value;
                EnableInClassList("first", value);
                MarkDirtyRepaint();
            }
        }

        public bool IsLast
        {
            get => isLast;
            set
            {
                if (isLast == value) return;
                isLast = value;
                EnableInClassList("last", value);
                MarkDirtyRepaint();
            }
        }

        public TrapezoidPortraitCard()
        {
            AddToClassList("show-portrait-trapezoid");
            generateVisualContent += DrawCard;
            RegisterCallback<PointerEnterEvent>(_ => { isHovered = true; MarkDirtyRepaint(); });
            RegisterCallback<PointerLeaveEvent>(_ => { isHovered = false; MarkDirtyRepaint(); });
        }

        public override bool ContainsPoint(Vector2 localPoint)
        {
            var width = contentRect.width;
            var height = contentRect.height;
            if (width < 1f || height < 1f || localPoint.y < 0f || localPoint.y > height) return false;
            var inset = SlantInset(width, height);
            var progress = localPoint.y / height;
            var left = isFirst ? 0f : inset * (1f - progress);
            var right = isLast ? width : width - inset * progress;
            return localPoint.x >= left && localPoint.x <= right;
        }

        private void DrawCard(MeshGenerationContext context)
        {
            var width = contentRect.width;
            var height = contentRect.height;
            if (width < 1f || height < 1f) return;

            var inset = SlantInset(width, height);
            var leftTop = isFirst ? 0f : inset;
            var rightBottom = isLast ? width : width - inset;
            var painter = context.painter2D;
            painter.fillColor = isHovered ? new Color(0.082f, 0.180f, 0.275f, 1f) : new Color(0.067f, 0.118f, 0.173f, 1f);
            painter.strokeColor = new Color(0.192f, 0.357f, 0.510f, 1f);
            painter.lineWidth = 2f;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            painter.MoveTo(new Vector2(leftTop, 0f));
            painter.LineTo(new Vector2(width, 0f));
            painter.LineTo(new Vector2(rightBottom, height));
            painter.LineTo(new Vector2(0f, height));
            painter.ClosePath();
            painter.Fill();

            for (var index = 0; index < portraits.Count; index++)
            {
                var portrait = portraits[index];
                if (portrait == null) continue;
                var uv = PortraitUvs(width, height, portrait.width, portrait.height);
                var points = new[] { new Vector2(leftTop, 0f), new Vector2(width, 0f), new Vector2(rightBottom, height), new Vector2(0f, height) };
                var photoLeft = 0f;
                var photoWidth = width;
                if (portraits.Count > 1)
                {
                    var aspect = (float)portrait.width / portrait.height;
                    photoWidth = Mathf.Max(height * aspect, width / portraits.Count * 1.2f);
                    photoLeft = width * (index + .5f) / portraits.Count - photoWidth * .5f;
                    points[0].x = Mathf.Clamp(photoLeft, leftTop, width);
                    points[1].x = Mathf.Clamp(photoLeft + photoWidth, leftTop, width);
                    points[2].x = Mathf.Clamp(photoLeft + photoWidth, 0f, rightBottom);
                    points[3].x = Mathf.Clamp(photoLeft, 0f, rightBottom);
                    var v = height * aspect / photoWidth;
                    uv = new Rect(0f, 1f - v, 1f, v);
                }
                var mesh = context.Allocate(4, 6, portrait);
                foreach (var point in points)
                    mesh.SetNextVertex(new Vertex
                    {
                        position = new Vector3(point.x, point.y, Vertex.nearZ),
                        tint = Color.white,
                        uv = new Vector2(uv.x + (point.x - photoLeft) / photoWidth * uv.width, uv.yMax - point.y / height * uv.height)
                    });
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
            }

            painter.Stroke();
        }

        private static Rect PortraitUvs(float width, float height, float textureWidth, float textureHeight)
        {
            var scale = Mathf.Max(width / textureWidth, height / textureHeight);
            var u = width / (textureWidth * scale);
            var v = height / (textureHeight * scale);
            // Keep the head at the top when the card crops the lower body.
            return new Rect((1f - u) * .5f, 1f - v, u, v);
        }

        private static float SlantInset(float width, float height) => Mathf.Min(32f, width * 0.45f);
    }
}
