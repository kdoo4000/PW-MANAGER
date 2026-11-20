using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PWManager.Presentation
{
    public static class StorylineMapRouting
    {
        private const float Clearance = 4, LaneGap = 24, BendCost = 80;
        private readonly struct Port
        {
            public readonly Vector2 Anchor, Normal;
            public readonly float Bias;
            public Port(Vector2 anchor, Vector2 normal, float bias = 0) { Anchor = anchor; Normal = normal; Bias = bias; }
        }

        public static Vector2[] Route(Rect from, Rect to, IReadOnlyList<Rect> obstacles, IReadOnlyList<Vector2[]> placed,
            Rect? fromContainer = null, Rect? toContainer = null, IReadOnlyList<Rect> frames = null)
        {
            Rect Expand(Rect r, float gap) => Rect.MinMaxRect(r.xMin - gap, r.yMin - gap, r.xMax + gap, r.yMax + gap);
            var expanded = obstacles.Select(r => Expand(r, Clearance)).ToArray();
            var blocked = expanded.Concat(new[] { from, to }).ToArray();
            bool Clear(Vector2 a, Vector2 b) => !blocked.Any(r => CrossesInterior(a, b, r));
            bool Leaves(Port port, Vector2 direction) => Vector2.Dot(port.Normal, direction) > .001f;
            var starts = Ports(from).ToArray(); var ends = Ports(to).ToArray();
            var best = Array.Empty<Vector2>(); var bestCost = float.PositiveInfinity; var bestOverlap = float.PositiveInfinity;
            void Consider(Vector2[] candidate, Port start, Port end)
            {
                var path = Simplify(candidate);
                if (path.Length < 2 || !Leaves(start, path[1] - path[0]) || !Leaves(end, path[path.Length - 2] - path.Last())) return;
                var length = 0f; var overlap = 0f;
                for (var i = 1; i < path.Length; i++)
                {
                    if (!Clear(path[i - 1], path[i])) return;
                    length += Vector2.Distance(path[i - 1], path[i]); overlap += OverlapCost(path[i - 1], path[i], placed);
                }
                var cost = length + BendCost * (path.Length - 2) + start.Bias + end.Bias + overlap;
                if (cost < bestCost - .01f) { best = path; bestCost = cost; bestOverlap = overlap; }
            }

            foreach (var path in SharedAxisConnections(from, to))
            {
                var normal = (path[1] - path[0]).normalized;
                Consider(path, new Port(path[0], normal), new Port(path[1], -normal));
            }
            if (best.Length > 1 && bestOverlap < .01f) return best;

            // Deep group exits use a clear outer lane; they must not drift down an internal gutter.
            var vertical = Mathf.Abs(to.center.y - from.center.y) >= Mathf.Abs(to.center.x - from.center.x);
            bool LongExit(Rect node, Rect other, Rect frame)
            {
                var span = (vertical ? node.height : node.width) + LaneGap + Clearance;
                return vertical
                    ? other.yMin >= frame.yMax && frame.yMax - node.yMax > span || other.yMax <= frame.yMin && node.yMin - frame.yMin > span
                    : other.xMin >= frame.xMax && frame.xMax - node.xMax > span || other.xMax <= frame.xMin && node.xMin - frame.xMin > span;
            }
            if (fromContainer.HasValue && LongExit(from, to, fromContainer.Value) || toContainer.HasValue && LongExit(to, from, toContainer.Value))
            {
                var previous = (best, bestCost, bestOverlap);
                best = Array.Empty<Vector2>(); bestCost = bestOverlap = float.PositiveInfinity;
                var exitFrames = new[] { fromContainer, toContainer }.Where(r => r.HasValue).Select(r => r.Value).ToArray();
                var low = exitFrames.Min(r => vertical ? r.xMin : r.yMin); var high = exitFrames.Max(r => vertical ? r.xMax : r.yMax);
                var direction = Mathf.Sign(vertical ? to.center.y - from.center.y : to.center.x - from.center.x);
                foreach (var lane in new[] { low - LaneGap, high + LaneGap, low - LaneGap - 12, high + LaneGap + 12 })
                {
                    var startNormal = vertical ? lane < from.center.x ? Vector2.left : Vector2.right : lane < from.center.y ? Vector2.down : Vector2.up;
                    var endNormal = vertical ? lane < to.center.x ? Vector2.left : Vector2.right : lane < to.center.y ? Vector2.down : Vector2.up;
                    foreach (var start in starts.Where(p => p.Normal == startNormal))
                    foreach (var end in ends.Where(p => p.Normal == endNormal))
                    {
                        var a = vertical ? new Vector2(lane, start.Anchor.y + direction * Mathf.Abs(lane - start.Anchor.x))
                            : new Vector2(start.Anchor.x + direction * Mathf.Abs(lane - start.Anchor.y), lane);
                        var b = vertical ? new Vector2(lane, end.Anchor.y - direction * Mathf.Abs(end.Anchor.x - lane))
                            : new Vector2(end.Anchor.x - direction * Mathf.Abs(end.Anchor.y - lane), lane);
                        if ((vertical ? b.y - a.y : b.x - a.x) * direction > .01f) Consider(new[] { start.Anchor, a, b, end.Anchor }, start, end);
                    }
                }
                if (best.Length > 1) return best;
                (best, bestCost, bestOverlap) = previous;
            }

            // Crossings are allowed; a clear diagonal joins the facing corners without a detour.
            if ((from.xMax < to.xMin || to.xMax < from.xMin) && (from.yMax < to.yMin || to.yMax < from.yMin))
            {
                var right = to.center.x > from.center.x; var below = to.center.y > from.center.y;
                var a = new Vector2(right ? from.xMax : from.xMin, below ? from.yMax : from.yMin);
                var b = new Vector2(right ? to.xMin : to.xMax, below ? to.yMin : to.yMax);
                if (Clear(a, b) && OverlapCost(a, b, placed) < .01f) return new[] { a, b };
            }
            foreach (var start in starts)
            foreach (var end in ends) Consider(new[] { start.Anchor, end.Anchor }, start, end);
            if (best.Length > 1 && bestOverlap < .01f) return best;

            // Aligned portraits use an outer lane rather than weaving through the row or column.
            var row = Mathf.Abs(from.yMin - to.yMin) < .01f && Mathf.Abs(from.height - to.height) < .01f;
            var column = Mathf.Abs(from.xMin - to.xMin) < .01f && Mathf.Abs(from.width - to.width) < .01f;
            if (row || column)
            {
                var previous = (best, bestCost, bestOverlap);
                best = Array.Empty<Vector2>(); bestCost = bestOverlap = float.PositiveInfinity;
                var laneBounds = new[] { from, to }.Concat(new[] { fromContainer, toContainer }.Where(r => r.HasValue).Select(r => r.Value)).ToArray();
                var extent = frames != null && frames.Count > 0 ? frames : blocked;
                var middle = row ? (extent.Min(r => r.yMin) + extent.Max(r => r.yMax)) / 2 : (extent.Min(r => r.xMin) + extent.Max(r => r.xMax)) / 2;
                var coordinate = row ? from.center.y : from.center.x;
                var outward = (row ? Vector2.up : Vector2.right) * (coordinate > middle ? 1 : -1);
                foreach (var normal in new[] { outward, -outward })
                {
                    var margin = row
                        ? normal.y > 0 ? laneBounds.Max(r => r.yMax) - from.yMax : from.yMin - laneBounds.Min(r => r.yMin)
                        : normal.x > 0 ? laneBounds.Max(r => r.xMax) - from.xMax : from.xMin - laneBounds.Min(r => r.xMin);
                    foreach (var start in starts.Where(p => p.Normal == normal))
                    foreach (var end in ends.Where(p => p.Normal == normal))
                    foreach (var gap in new[] { LaneGap, LaneGap + 12, LaneGap + 24, LaneGap + 36 })
                    {
                        var offset = gap + margin;
                        var delta = end.Anchor - start.Anchor; var distance = row ? delta.x : delta.y;
                        if (Mathf.Abs(distance) <= offset * 2) continue;
                        var along = (row ? Vector2.right : Vector2.up) * Mathf.Sign(distance) * offset;
                        Consider(new[] { start.Anchor, start.Anchor + along + normal * offset, end.Anchor - along + normal * offset, end.Anchor }, start, end);
                    }
                }
                if (best.Length > 1) return best;
                (best, bestCost, bestOverlap) = previous;
            }
            foreach (var start in starts)
            foreach (var end in ends)
            foreach (var path in Connections(start.Anchor, end.Anchor)) Consider(path, start, end);
            if (best.Length > 1) return best;

            // Each visibility edge is straight, so each intermediate node has a real turn cost.
            bool Inside(Vector2 p) => blocked.Any(r => p.x > r.xMin + .01f && p.x < r.xMax - .01f && p.y > r.yMin + .01f && p.y < r.yMax - .01f);
            IEnumerable<Vector2> Corners(Rect r) => new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            var containers = new[] { fromContainer, toContainer }.Where(r => r.HasValue).Select(r => Expand(r.Value, LaneGap));
            var corners = expanded.Concat(containers).SelectMany(Corners).Where(p => !Inside(p)).Distinct().OrderBy(p => p.x).ThenBy(p => p.y);
            var points = starts.Select(p => p.Anchor).Concat(ends.Select(p => p.Anchor)).Concat(corners).ToArray();
            var costs = Enumerable.Repeat(float.PositiveInfinity, points.Length).ToArray();
            var parent = Enumerable.Repeat(-1, points.Length).ToArray();
            var queue = new SortedSet<(float estimate, float cost, int serial, int node)>();
            var serial = 0;
            float Heuristic(Vector2 p) => Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, to.xMin, to.xMax), Mathf.Clamp(p.y, to.yMin, to.yMax)));
            for (var i = 0; i < starts.Length; i++)
            {
                if (Inside(points[i])) continue;
                costs[i] = starts[i].Bias; queue.Add((costs[i] + Heuristic(points[i]), costs[i], serial++, i));
            }
            while (queue.Count > 0)
            {
                var current = queue.Min; queue.Remove(current);
                if (current.cost > costs[current.node] + .01f) continue;
                if (current.node >= starts.Length && current.node < starts.Length + ends.Length)
                {
                    var result = new List<Vector2>();
                    for (var i = current.node; i >= 0; i = parent[i]) result.Add(points[i]);
                    result.Reverse(); return Simplify(result);
                }
                for (var next = starts.Length; next < points.Length; next++)
                {
                    var a = points[current.node]; var b = points[next];
                    if (next == current.node || Vector2.Distance(a, b) < .01f || !Clear(a, b)) continue;
                    if (current.node < starts.Length && !Leaves(starts[current.node], b - a)) continue;
                    var target = next < starts.Length + ends.Length;
                    if (target && !Leaves(ends[next - starts.Length], a - b)) continue;
                    var cost = current.cost + Vector2.Distance(a, b) + OverlapCost(a, b, placed)
                        + (current.node < starts.Length ? 0 : BendCost) + (target ? ends[next - starts.Length].Bias : 0);
                    if (cost >= costs[next] - .01f) continue;
                    costs[next] = cost; parent[next] = current.node;
                    queue.Add((cost + Heuristic(b), cost, serial++, next));
                }
            }
            return Array.Empty<Vector2>();
        }

        private static IEnumerable<Port> Ports(Rect r)
        {
            foreach (var fraction in new[] { .5f, .25f, .75f })
            {
                var bias = fraction == .5f ? 0 : 8;
                yield return new Port(new Vector2(r.xMax, Mathf.Lerp(r.yMin, r.yMax, fraction)), Vector2.right, bias);
                yield return new Port(new Vector2(r.xMin, Mathf.Lerp(r.yMin, r.yMax, fraction)), Vector2.left, bias);
                yield return new Port(new Vector2(Mathf.Lerp(r.xMin, r.xMax, fraction), r.yMax), Vector2.up, bias);
                yield return new Port(new Vector2(Mathf.Lerp(r.xMin, r.xMax, fraction), r.yMin), Vector2.down, bias);
            }
        }

        private static IEnumerable<Vector2[]> SharedAxisConnections(Rect from, Rect to)
        {
            IEnumerable<float> Coordinates(float min, float max, float preferred)
            {
                if (max - min <= .01f) yield break;
                var inset = Mathf.Min(8, (max - min) / 4); min += inset; max -= inset;
                foreach (var value in new[] { Mathf.Clamp(preferred, min, max), (min + max) / 2, Mathf.Lerp(min, max, .25f), Mathf.Lerp(min, max, .75f) }.Distinct()) yield return value;
            }
            if (from.xMax < to.xMin || to.xMax < from.xMin)
            {
                var preferred = from.height <= to.height ? from.center.y : to.center.y;
                foreach (var y in Coordinates(Mathf.Max(from.yMin, to.yMin), Mathf.Min(from.yMax, to.yMax), preferred))
                    yield return new[] { new Vector2(from.center.x < to.center.x ? from.xMax : from.xMin, y), new Vector2(from.center.x < to.center.x ? to.xMin : to.xMax, y) };
            }
            if (from.yMax < to.yMin || to.yMax < from.yMin)
            {
                var preferred = from.width <= to.width ? from.center.x : to.center.x;
                foreach (var x in Coordinates(Mathf.Max(from.xMin, to.xMin), Mathf.Min(from.xMax, to.xMax), preferred))
                    yield return new[] { new Vector2(x, from.center.y < to.center.y ? from.yMax : from.yMin), new Vector2(x, from.center.y < to.center.y ? to.yMin : to.yMax) };
            }
        }

        private static IEnumerable<Vector2[]> Connections(Vector2 a, Vector2 b)
        {
            var delta = b - a; var dx = Mathf.Abs(delta.x); var dy = Mathf.Abs(delta.y);
            if (dx < .001f || dy < .001f) yield break;
            yield return new[] { a, new Vector2(a.x, b.y), b };
            yield return new[] { a, new Vector2(b.x, a.y), b };
            var diagonal = new Vector2(Mathf.Sign(delta.x), Mathf.Sign(delta.y)) * Mathf.Min(dx, dy);
            yield return new[] { a, a + diagonal, b };
            yield return new[] { a, b - diagonal, b };
            foreach (var fraction in new[] { .5f, .25f, .75f }) yield return new[] { a, a + diagonal * fraction, b - diagonal * (1 - fraction), b };
        }

        public static bool CrossesInterior(Vector2 a, Vector2 b, Rect rect)
        {
            var enter = 0f; var exit = 1f;
            bool Clip(float origin, float delta, float min, float max)
            {
                if (Mathf.Abs(delta) < .0001f) return origin > min && origin < max;
                var first = (min - origin) / delta; var last = (max - origin) / delta;
                enter = Mathf.Max(enter, Mathf.Min(first, last)); exit = Mathf.Min(exit, Mathf.Max(first, last));
                return enter < exit;
            }
            return Clip(a.x, b.x - a.x, rect.xMin + .01f, rect.xMax - .01f) && Clip(a.y, b.y - a.y, rect.yMin + .01f, rect.yMax - .01f);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static float OverlapCost(Vector2 a, Vector2 b, IReadOnlyList<Vector2[]> placed)
        {
            var direction = b - a; var length = direction.magnitude;
            if (length < .01f) return 0;
            var unit = direction / length; var cost = 0f;
            foreach (var path in placed)
            for (var i = 1; i < path.Length; i++)
            {
                var c = path[i - 1]; var d = path[i]; var other = d - c;
                if (other.sqrMagnitude < .0001f || Mathf.Abs(Cross(unit, other.normalized)) >= .01f) continue;
                var separation = Mathf.Abs(Cross(c - a, unit));
                var first = Vector2.Dot(c - a, unit); var last = Vector2.Dot(d - a, unit);
                var overlap = Mathf.Min(length, Mathf.Max(first, last)) - Mathf.Max(0, Mathf.Min(first, last));
                if (overlap > 0 && separation < 10) cost += overlap * (separation < .1f ? 12 : 2);
            }
            return cost;
        }

        private static Vector2[] Simplify(IEnumerable<Vector2> points)
        {
            var result = new List<Vector2>();
            foreach (var point in points)
            {
                if (result.Count > 0 && Vector2.Distance(result.Last(), point) < .01f) continue;
                while (result.Count > 1 && Mathf.Abs(Cross(result.Last() - result[result.Count - 2], point - result.Last())) < .01f) result.RemoveAt(result.Count - 1);
                result.Add(point);
            }
            return result.ToArray();
        }
    }
}
