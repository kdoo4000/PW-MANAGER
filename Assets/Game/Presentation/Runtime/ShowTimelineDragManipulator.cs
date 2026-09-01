using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class ShowTimelineDragManipulator : PointerManipulator
    {
        private readonly VisualElement dragVisual;
        private readonly Func<int> currentIndex;
        private readonly Func<Vector2, int> dropIndex;
        private readonly Action<int, int> dropped;
        private Vector2 startPosition;
        private int pointerId;
        private int previewIndex = -1;
        private bool pressed;
        private bool dragging;

        public ShowTimelineDragManipulator(VisualElement dragVisual, Func<int> currentIndex, Func<Vector2, int> dropIndex, Action<int, int> dropped)
        {
            this.dragVisual = dragVisual ?? throw new ArgumentNullException(nameof(dragVisual));
            this.currentIndex = currentIndex;
            this.dropIndex = dropIndex;
            this.dropped = dropped;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt)) return;
            pointerId = evt.pointerId;
            startPosition = evt.position;
            pressed = true;
            dragging = false;
            target.CapturePointer(pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!pressed || !target.HasPointerCapture(pointerId)) return;
            var delta = (Vector2)evt.position - startPosition;
            if (!dragging && delta.sqrMagnitude < 36f) return;
            dragging = true;
            dragVisual.AddToClassList("dragging");
            dragVisual.style.translate = new Translate(0, delta.y, 0);
            var from = currentIndex();
            var to = dropIndex(evt.position);
            if (from >= 0 && to >= 0 && to != previewIndex) PreviewMove(from, to);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!pressed || !CanStopManipulation(evt)) return;
            var wasDragging = dragging;
            var from = currentIndex();
            var to = dropIndex(evt.position);
            Reset();
            if (wasDragging && from >= 0 && to >= 0 && from != to) dropped(from, to);
            evt.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt) => Reset();

        private void PreviewMove(int from, int to)
        {
            ClearPreview();
            previewIndex = to;
            var container = dragVisual.parent;
            if (container == null || from == to) return;
            var distance = dragVisual.resolvedStyle.height + dragVisual.resolvedStyle.marginTop + dragVisual.resolvedStyle.marginBottom;
            if (from < to)
            {
                for (var index = from + 1; index <= to && index < container.childCount; index++)
                    container[index].style.translate = new Translate(0, -distance, 0);
            }
            else
            {
                for (var index = to; index < from && index < container.childCount; index++)
                    container[index].style.translate = new Translate(0, distance, 0);
            }
        }

        private void ClearPreview()
        {
            var container = dragVisual.parent;
            if (container == null) return;
            foreach (var child in container.Children())
                if (child != dragVisual) child.style.translate = new Translate(0, 0, 0);
        }

        private void Reset()
        {
            if (target.HasPointerCapture(pointerId)) target.ReleasePointer(pointerId);
            ClearPreview();
            dragVisual.style.translate = new Translate(0, 0, 0);
            dragVisual.RemoveFromClassList("dragging");
            previewIndex = -1;
            pressed = false;
            dragging = false;
        }
    }
}
