using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class ProfileMetricGauge : VisualElement
    {
        private readonly Label state = new();
        private string description;
        private Color color;
        private bool isHovered;
        public float Value { get; private set; }

        public ProfileMetricGauge()
        {
            AddToClassList("wp-metric-gauge");
            state.AddToClassList("wp-gauge-state");
            Add(state);
            focusable = true;
            tabIndex = 0;
            generateVisualContent += Draw;
            RegisterCallback<PointerEnterEvent>(_ => { isHovered = true; RefreshState(); });
            RegisterCallback<PointerLeaveEvent>(_ => { isHovered = false; RefreshState(); });
        }

        public void SetValue(string title, float value, string text, Color tint)
        {
            Value = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, 100f);
            description = text;
            color = tint;
            RefreshState();
            tooltip = $"{title}: {Value:0} / 100 · {text}";
            MarkDirtyRepaint();
        }

        private void RefreshState() => state.text = isHovered ? Value.ToString("0") : description;

        private void Draw(MeshGenerationContext context)
        {
            var radius = Mathf.Min(contentRect.width, contentRect.height) * .5f - 4f;
            if (radius <= 0f) return;
            var center = contentRect.center;
            var painter = context.painter2D;
            painter.lineWidth = 4f;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = new Color(.29f, .34f, .42f);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90), Angle.Degrees(270));
            painter.Stroke();
            if (Value <= 0f) return;
            painter.strokeColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90), Angle.Degrees(-90f + Value * 3.6f));
            painter.Stroke();
        }
    }
}
