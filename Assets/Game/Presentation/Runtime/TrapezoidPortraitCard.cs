using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class TrapezoidPortraitCard : VisualElement
    {
        private bool isFirst;
        private bool isLast;
        private bool isHovered;

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
            painter.Stroke();
        }

        private static float SlantInset(float width, float height) => Mathf.Min(32f, width * 0.45f);
    }
}
