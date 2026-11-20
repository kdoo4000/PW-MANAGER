using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class StorylineMapCanvas : VisualElement
    {
        private sealed class ClusterView
        {
            public StorylineMapCluster Data;
            public Rect Bounds;
            public readonly List<EdgeView> Edges = new();
        }

        private sealed class CardView
        {
            public WrestlerState Wrestler;
            public VisualElement Root;
            public Rect Bounds;
            public BlockView Block;
        }

        private sealed class BlockView
        {
            public StorylineMapGroup Group;
            public VisualElement Root;
            public Rect Bounds;
            public readonly List<CardView> Cards = new();
            public readonly Dictionary<string, Rect> PartyBounds = new(StringComparer.Ordinal);
        }

        private sealed class EdgeView
        {
            public StorylineMapRelation Relation;
            public Button Label;
            public Vector2[] Points;
            public string FromId, ToId;
            public readonly List<ClusterView> Stories = new();
            public readonly List<Button> Contacts = new();
        }

        private sealed class LayoutUnit
        {
            public BlockView Main;
            public Vector2 Size;
            public readonly List<(BlockView block, Vector2 offset)> Parts = new();
        }

        private const float CardWidth = 120, CardHeight = 166, CardGap = 16;
        private readonly VisualElement world = new() { name = "story-map-world" };
        private readonly VisualElement lines = new() { pickingMode = PickingMode.Ignore };
        private readonly List<ClusterView> clusters = new();
        private readonly List<BlockView> blocks = new();
        private readonly Dictionary<string, CardView> cards = new(StringComparer.Ordinal);
        private readonly List<EdgeView> edges = new();
        private int incidentFilter;
        private readonly Action<StorylineMapCluster, WrestlerState, StorylineMapRelation> selected;
        private readonly Action cleared;
        private string focusedStory, selectedWrestler;
        private StorylineMapRelation selectedRelation;
        private StorylineMapCluster selectedCluster;
        private Vector2 pan, downPosition, downPan;
        private int dragPointer = -1;
        private bool dragging;
        private bool needsFit = true;
        private Rect overviewBounds;
        public float Zoom { get; private set; } = 1;

        public StorylineMapCanvas(StorylineMapData data,
            Action<StorylineMapCluster, WrestlerState, StorylineMapRelation> selected, Action cleared)
        {
            name = "story-map-canvas";
            AddToClassList("sm-canvas");
            focusable = true;
            this.selected = selected;
            this.cleared = cleared;
            world.style.position = Position.Absolute;
            world.style.transformOrigin = new TransformOrigin(0, 0, 0);
            Add(world);
            BuildBoard(data);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCancelEvent>(_ => EndDrag());
            RegisterCallback<PointerCaptureOutEvent>(_ => { dragPointer = -1; dragging = false; });
            RegisterCallback<WheelEvent>(evt =>
            {
                ZoomAt(new Vector2(evt.localMousePosition.x, evt.localMousePosition.y), Mathf.Exp(-evt.delta.y * .08f));
                evt.StopPropagation();
            });
            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (needsFit || evt.oldRect.size != evt.newRect.size) Fit();
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                var movement = evt.keyCode switch
                {
                    KeyCode.LeftArrow => new Vector2(48, 0), KeyCode.RightArrow => new Vector2(-48, 0),
                    KeyCode.UpArrow => new Vector2(0, 48), KeyCode.DownArrow => new Vector2(0, -48), _ => Vector2.zero
                };
                if (evt.target != this || movement == Vector2.zero) return;
                pan += movement; ApplyTransform(); evt.StopPropagation(); focusController?.IgnoreEvent(evt);
            });
            UpdateEmphasis();
        }

        private void BuildBoard(StorylineMapData map)
        {
            foreach (var story in map.Clusters) clusters.Add(new ClusterView { Data = story });
            var people = map.Wrestlers.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            void Block(StorylineMapGroup group, IEnumerable<WrestlerState> members)
            {
                var block = new BlockView { Group = group, Root = new VisualElement() };
                block.Root.AddToClassList(group == null ? "sm-independent" : "sm-faction");
                foreach (var wrestler in members)
                {
                    var card = new CardView { Wrestler = wrestler, Root = new VisualElement(), Block = block };
                    cards.Add(wrestler.Id, card); block.Cards.Add(card); assigned.Add(wrestler.Id);
                    BuildCard(card, map); block.Root.Add(card.Root);
                }
                var nested = group?.Type == StoryPartyType.Stable ? map.Groups.Where(x => x.Type == StoryPartyType.TagTeam
                    && x.MemberIds.All(id => block.Cards.Any(c => c.Wrestler.Id == id))).OrderBy(x => x.Id, StringComparer.Ordinal).ToList() : new List<StorylineMapGroup>();
                var columns = nested.Count > 0 ? 2 : Math.Min(3, block.Cards.Count);
                var rows = Mathf.CeilToInt(block.Cards.Count / (float)columns);
                var inset = group == null ? 0 : 12;
                var header = group == null ? 0 : 38;
                block.Bounds = new Rect(0, 0, columns * CardWidth + (columns - 1) * CardGap + inset * 2,
                    rows * CardHeight + (rows - 1) * CardGap + header + inset);
                if (group != null)
                {
                    var name = WrestlerNameText.Create(group.Name, block.Cards.Select(x => x.Wrestler));
                    name.AddToClassList("sm-faction-name"); block.Root.Add(name);
                }
                var nextY = (float)header;
                var nestedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var team in nested)
                {
                    var boxBounds = new Rect(6, nextY, block.Bounds.width - 12, CardHeight + 36);
                    block.PartyBounds[team.Id] = boxBounds;
                    var box = new VisualElement { name = "story-map-group-" + team.Id, pickingMode = PickingMode.Ignore };
                    box.AddToClassList("sm-subteam");
                    box.style.left = boxBounds.x; box.style.top = boxBounds.y; box.style.width = boxBounds.width; box.style.height = boxBounds.height;
                    var label = WrestlerNameText.Create(team.Name, block.Cards.Select(x => x.Wrestler)); label.AddToClassList("sm-subteam-name"); box.Add(label);
                    block.Root.Add(box);
                    for (var i = 0; i < team.MemberIds.Count; i++)
                    {
                        var card = block.Cards.Single(x => x.Wrestler.Id == team.MemberIds[i]);
                        card.Bounds = new Rect(inset + i * (CardWidth + CardGap), nextY + 28, CardWidth, CardHeight);
                        nestedIds.Add(card.Wrestler.Id);
                    }
                    nextY += boxBounds.height + 12;
                }
                var remaining = block.Cards.Where(x => !nestedIds.Contains(x.Wrestler.Id)).ToList();
                for (var i = 0; i < remaining.Count; i++)
                    remaining[i].Bounds = new Rect(inset + i % columns * (CardWidth + CardGap), nextY + i / columns * (CardHeight + CardGap), CardWidth, CardHeight);
                if (nested.Count > 0) block.Bounds.height = nextY + Mathf.CeilToInt(remaining.Count / (float)columns) * (CardHeight + CardGap) + inset;
                foreach (var card in block.Cards) { card.Root.style.left = card.Bounds.x; card.Root.style.top = card.Bounds.y; }
                block.Root.style.width = block.Bounds.width; block.Root.style.height = block.Bounds.height;
                if (group != null) { block.Root.name = "story-map-group-" + group.Id; block.PartyBounds[group.Id] = new Rect(Vector2.zero, block.Bounds.size); }
                blocks.Add(block); world.Add(block.Root);
            }
            // Stable boxes contain their tag teams, so shared wrestlers never need duplicate cards.
            foreach (var group in map.Groups.OrderByDescending(x => x.Type == StoryPartyType.Stable)
                         .ThenByDescending(x => x.MemberIds.Count).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var members = group.MemberIds.Where(x => people.ContainsKey(x) && !assigned.Contains(x)).Select(x => people[x]).ToList();
                if (members.Count > 1) Block(group, members);
            }
            foreach (var wrestler in people.Values.Where(x => !assigned.Contains(x.Id)).OrderBy(x => x.Id, StringComparer.Ordinal))
                Block(null, new[] { wrestler });

            foreach (var story in clusters)
            foreach (var relation in story.Data.Relations.Where(x => x.IsStoryOpposition))
            {
                if (!cards.ContainsKey(relation.FirstId) || !cards.ContainsKey(relation.SecondId)) continue;
                var edge = new EdgeView { Relation = relation, FromId = relation.FirstId, ToId = relation.SecondId };
                edges.Add(edge);
                edge.Stories.Add(story); story.Edges.Add(edge);
            }
            var remainingBlocks = blocks.ToList(); blocks.Clear();
            int Connections(BlockView a, BlockView b) => edges.Count(e =>
                cards[e.FromId].Block == a && cards[e.ToId].Block == b || cards[e.FromId].Block == b && cards[e.ToId].Block == a);
            while (remainingBlocks.Count > 0)
            {
                var next = remainingBlocks.OrderByDescending(b => blocks.Sum(a => Connections(a, b)))
                    .ThenByDescending(b => remainingBlocks.Sum(a => Connections(a, b))).First();
                blocks.Add(next); remainingBlocks.Remove(next);
            }
            var groupBlocks = blocks.Where(b => b.Group != null).ToList();
            var attachments = groupBlocks.ToDictionary(b => b, _ => new List<BlockView>());
            var independent = new List<BlockView>();
            foreach (var block in blocks.Where(b => b.Group == null))
            {
                var owner = groupBlocks.Where(g => Connections(g, block) > 0).OrderByDescending(g => Connections(g, block)).FirstOrDefault();
                if (owner == null) independent.Add(block); else attachments[owner].Add(block);
            }
            var units = new List<LayoutUnit>();
            var offsets = new Dictionary<BlockView, Vector2>();
            void ArrangeAttachments(LayoutUnit unit)
            {
                var main = unit.Main;
                unit.Parts.Clear(); unit.Size = main.Bounds.size;
                unit.Parts.Add((main, Vector2.zero)); offsets[main] = Vector2.zero;
                if (!attachments.TryGetValue(main, out var attached) || attached.Count == 0) return;
                float RelatedX(BlockView b) => edges.Where(e => cards[e.FromId].Block == b && cards[e.ToId].Block == main || cards[e.ToId].Block == b && cards[e.FromId].Block == main)
                    .Average(e => (cards[e.FromId].Block == main ? cards[e.FromId] : cards[e.ToId]).Bounds.center.x) + 1;
                var columns = Math.Max(1, Mathf.FloorToInt((main.Bounds.width + CardGap) / (CardWidth + CardGap)));
                var ordered = attached.OrderBy(RelatedX).ThenBy(b => b.Cards[0].Wrestler.Id, StringComparer.Ordinal).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var block = ordered[i];
                    var x = ordered.Count == 1 ? Mathf.Clamp(RelatedX(block) - CardWidth / 2, 0, main.Bounds.width - CardWidth)
                        : i % columns * (CardWidth + CardGap) + (main.Bounds.width - columns * CardWidth - (columns - 1) * CardGap) / 2;
                    var offset = new Vector2(x, main.Bounds.height + 64 + i / columns * (CardHeight + 48));
                    unit.Parts.Add((block, offset)); offsets[block] = offset;
                    unit.Size.y = Mathf.Max(unit.Size.y, offset.y + block.Bounds.height);
                }
            }
            foreach (var main in groupBlocks.Concat(independent))
            {
                var unit = new LayoutUnit { Main = main }; units.Add(unit); ArrangeAttachments(unit);
            }
            var targetWidth = Mathf.Clamp(Mathf.Sqrt(units.Sum(u => (u.Size.x + 64) * (u.Size.y + 80)) * 1.6f), 800, 1600);
            List<List<LayoutUnit>> Pack()
            {
                var rows = new List<List<LayoutUnit>> { new() };
                var rowWidth = 48f;
                foreach (var unit in units)
                {
                    if (rowWidth + unit.Size.x + 48 > targetWidth && rows[rows.Count - 1].Count > 0)
                    { rows.Add(new()); rowWidth = 48; }
                    unit.Main.Bounds.position = new Vector2(rowWidth, 0);
                    rows[rows.Count - 1].Add(unit); rowWidth += unit.Size.x + 64;
                }
                var rowY = 48f;
                foreach (var row in rows)
                {
                    var height = row.Select(u => u.Size.y).DefaultIfEmpty(0).Max();
                    foreach (var unit in row) unit.Main.Bounds.position = new Vector2(unit.Main.Bounds.x,
                        rowY + (unit.Main.Group == null ? (height - unit.Size.y) / 2 : 0));
                    rowY += height + 64;
                }
                return rows;
            }
            var owners = units.SelectMany(unit => unit.Parts.Select(part => (part.block, unit))).ToDictionary(x => x.block, x => x.unit);
            var layoutLinks = edges.Select(edge => (From: cards[edge.FromId], To: cards[edge.ToId],
                FirstGroup: edge.Stories[0].Data.Story.PartyType == StoryPartyType.Wrestler ? (Rect?)null : Endpoint(edge, true).bounds,
                SecondGroup: edge.Stories[0].Data.Story.PartyType == StoryPartyType.Wrestler ? (Rect?)null : Endpoint(edge, false).bounds)).ToArray();
            float OpponentDistance()
            {
                var distance = 0f;
                foreach (var link in layoutLinks)
                {
                    var from = link.From; var to = link.To;
                    var first = owners[from.Block]; var second = owners[to.Block];
                    var delta = first.Main.Bounds.position + offsets[from.Block] + (link.FirstGroup ?? from.Bounds).center -
                        second.Main.Bounds.position - offsets[to.Block] - (link.SecondGroup ?? to.Bounds).center;
                    distance += Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
                }
                return distance;
            }
            Pack();
            var bestDistance = OpponentDistance();
            void ImproveUnits(int passes)
            {
                for (var pass = 0; pass < Math.Min(passes, units.Count); pass++)
                {
                    var first = -1; var second = -1;
                    for (var a = 0; a < units.Count; a++)
                    for (var b = a + 1; b < units.Count; b++)
                    {
                        (units[a], units[b]) = (units[b], units[a]); Pack();
                        var distance = OpponentDistance();
                        if (distance < bestDistance - .01f) { bestDistance = distance; first = a; second = b; }
                        (units[a], units[b]) = (units[b], units[a]);
                    }
                    if (first < 0) break;
                    (units[first], units[second]) = (units[second], units[first]);
                }
                Pack();
            }
            ImproveUnits(2);
            int BlockedOpponentLines() => layoutLinks.Count(link =>
            {
                var from = link.From; var to = link.To;
                if (link.FirstGroup.HasValue || from.Block == to.Block) return false;
                var first = owners[from.Block].Main.Bounds.position + offsets[from.Block];
                var second = owners[to.Block].Main.Bounds.position + offsets[to.Block];
                var a = first + from.Bounds.center; var b = second + to.Bounds.center;
                return from.Block.Cards.Concat(to.Block.Cards).Any(card => card != from && card != to &&
                    StorylineMapRouting.CrossesInterior(a, b, new Rect(card.Bounds.position + (card.Block == from.Block ? first : second), card.Bounds.size)));
            });
            var bestBlocked = BlockedOpponentLines();
            var bestMemberScore = bestDistance + bestBlocked * (CardWidth + CardGap);
            // Swap slots only within the same team or the ungrouped members of a stable.
            var memberSlots = groupBlocks.SelectMany(block => block.Cards.GroupBy(card => block.PartyBounds
                    .Where(p => p.Key != block.Group.Id && p.Value.Contains(card.Bounds.center)).OrderBy(p => p.Value.width * p.Value.height)
                    .Select(p => p.Key).FirstOrDefault() ?? "").Select(g => g.ToArray())).Where(g => g.Length > 1).ToList();
            for (var pass = 0; pass < 3; pass++)
            {
                var changed = false;
                foreach (var members in memberSlots)
                {
                    var unit = owners[members[0].Block]; var first = -1; var second = -1;
                    for (var a = 0; a < members.Length; a++)
                    for (var b = a + 1; b < members.Length; b++)
                    {
                        (members[a].Bounds, members[b].Bounds) = (members[b].Bounds, members[a].Bounds);
                        ArrangeAttachments(unit); Pack();
                        var distance = OpponentDistance();
                        var blocked = BlockedOpponentLines();
                        var score = distance + blocked * (CardWidth + CardGap);
                        if (score < bestMemberScore - .01f || Mathf.Abs(score - bestMemberScore) <= .01f && blocked < bestBlocked)
                        { bestDistance = distance; bestBlocked = blocked; bestMemberScore = score; first = a; second = b; }
                        (members[a].Bounds, members[b].Bounds) = (members[b].Bounds, members[a].Bounds);
                    }
                    if (first >= 0)
                    {
                        (members[first].Bounds, members[second].Bounds) = (members[second].Bounds, members[first].Bounds); changed = true;
                    }
                    ArrangeAttachments(unit); Pack();
                }
                if (!changed) break;
            }
            ImproveUnits(2);
            foreach (var card in cards.Values) { card.Root.style.left = card.Bounds.x; card.Root.style.top = card.Bounds.y; }
            var rowUnits = Pack();
            var y = 48f; var width = 0f;
            foreach (var row in rowUnits)
            {
                var height = row.Select(u => u.Size.y).DefaultIfEmpty(0).Max();
                foreach (var unit in row)
                {
                    var origin = unit.Main.Bounds.position;
                    foreach (var (block, offset) in unit.Parts)
                    {
                        block.Bounds.position = origin + offset;
                        block.Root.style.left = block.Bounds.x; block.Root.style.top = block.Bounds.y;
                        var childOffset = block.Group == null ? Vector2.zero : Vector2.one;
                        foreach (var card in block.Cards) card.Bounds.position += block.Bounds.position + childOffset;
                        foreach (var id in block.PartyBounds.Keys.ToList())
                        {
                            var bounds = block.PartyBounds[id];
                            bounds.position += block.Bounds.position + (id == block.Group?.Id ? Vector2.zero : childOffset);
                            block.PartyBounds[id] = bounds;
                        }
                        width = Mathf.Max(width, block.Bounds.xMax + 48);
                    }
                }
                y += height + 64;
            }
            var anchored = new HashSet<BlockView>(blocks.Except(independent));
            foreach (var block in independent)
            {
                var neighbors = edges.Where(e => cards[e.FromId].Block == block || cards[e.ToId].Block == block)
                    .Select(e => cards[cards[e.FromId].Block == block ? e.ToId : e.FromId])
                    .Where(c => c.Block != block && anchored.Contains(c.Block))
                    .GroupBy(c => c.Wrestler.Id).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.First());
                var aligned = false;
                foreach (var neighbor in neighbors)
                {
                    var difference = neighbor.Bounds.center - block.Bounds.center;
                    var vertical = Mathf.Abs(difference.y) >= Mathf.Abs(difference.x);
                    foreach (var alignVertical in new[] { vertical, !vertical })
                    {
                        var candidate = block.Bounds;
                        if (alignVertical) candidate.x = neighbor.Bounds.center.x - candidate.width / 2;
                        else candidate.y = neighbor.Bounds.center.y - candidate.height / 2;
                        if (candidate.x < 48 || candidate.y < 48 || blocks.Any(other => other != block && candidate.Overlaps(
                                Rect.MinMaxRect(other.Bounds.xMin - CardGap, other.Bounds.yMin - CardGap, other.Bounds.xMax + CardGap, other.Bounds.yMax + CardGap)))) continue;
                        var delta = neighbor.Bounds.center - candidate.center;
                        var start = alignVertical ? new Vector2(candidate.center.x, delta.y > 0 ? candidate.yMax : candidate.yMin)
                            : new Vector2(delta.x > 0 ? candidate.xMax : candidate.xMin, candidate.center.y);
                        var end = alignVertical ? new Vector2(neighbor.Bounds.center.x, delta.y > 0 ? neighbor.Bounds.yMin : neighbor.Bounds.yMax)
                            : new Vector2(delta.x > 0 ? neighbor.Bounds.xMin : neighbor.Bounds.xMax, neighbor.Bounds.center.y);
                        if (blocks.Any(other => other != block && other != neighbor.Block && StorylineMapRouting.CrossesInterior(start, end, other.Bounds))) continue;
                        var movement = candidate.position - block.Bounds.position;
                        block.Bounds = candidate;
                        block.Root.style.left = candidate.x; block.Root.style.top = candidate.y;
                        foreach (var card in block.Cards) card.Bounds.position += movement;
                        aligned = true; break;
                    }
                    if (aligned) break;
                }
                anchored.Add(block);
            }
            width = Mathf.Max(width, blocks.Select(b => b.Bounds.xMax + 48).DefaultIfEmpty(0).Max());
            y = Mathf.Max(y, blocks.Select(b => b.Bounds.yMax + 48).DefaultIfEmpty(0).Max());
            world.style.width = width; world.style.height = y;
            lines.style.position = Position.Absolute; lines.StretchToParentSize();
            lines.generateVisualContent += context => DrawEdges(context.painter2D);
            world.Add(lines);
            var placed = new List<Vector2[]>();
            foreach (var edge in edges)
            {
                var first = cards[edge.FromId]; var second = cards[edge.ToId];
                var story = edge.Stories[0].Data;
                var fromEndpoint = Endpoint(edge, true); var toEndpoint = Endpoint(edge, false);
                var from = fromEndpoint.bounds; var to = toEndpoint.bounds;
                var obstacles = new List<Rect>();
                foreach (var block in blocks)
                {
                    if (block != first.Block && block != second.Block)
                    {
                        var gap = block.Group == null ? 0 : 16;
                        obstacles.Add(Rect.MinMaxRect(block.Bounds.xMin - gap, block.Bounds.yMin - gap, block.Bounds.xMax + gap, block.Bounds.yMax + gap)); continue;
                    }
                    obstacles.AddRange(block.Cards.Select(c => c.Bounds));
                    if (block.Group != null) obstacles.Add(new Rect(block.Bounds.x + 4, block.Bounds.y + 2, block.Bounds.width - 8, 32));
                    obstacles.AddRange(block.PartyBounds.Where(p => p.Key != block.Group?.Id)
                        .Select(p => new Rect(p.Value.x + 2, p.Value.y + 1, p.Value.width - 4, 25)));
                }
                bool Contains(Rect outer, Rect inner) => outer.xMin <= inner.xMin && outer.xMax >= inner.xMax && outer.yMin <= inner.yMin && outer.yMax >= inner.yMax;
                obstacles.RemoveAll(r => Contains(from, r) || Contains(to, r));
                Rect? fromContainer = first.Block != second.Block && first.Block.Group != null && from != first.Block.Bounds ? first.Block.Bounds : null;
                Rect? toContainer = first.Block != second.Block && second.Block.Group != null && to != second.Block.Bounds ? second.Block.Bounds : null;
                var frames = blocks.SelectMany(b => b.PartyBounds.Values.Concat(new[] { b.Bounds })).ToArray();
                edge.Points = StorylineMapRouting.Route(from, to, obstacles, placed, fromContainer, toContainer, frames);
                if (edge.Points.Length < 2) continue;
                placed.Add(edge.Points);
                width = Mathf.Max(width, edge.Points.Max(p => p.x) + 24);
                y = Mathf.Max(y, edge.Points.Max(p => p.y) + 24);
                var segment = Enumerable.Range(1, edge.Points.Length - 1).OrderByDescending(i => Vector2.Distance(edge.Points[i - 1], edge.Points[i])).First();
                var focusPoint = (edge.Points[segment - 1] + edge.Points[segment]) / 2;
                var label = new Button(() => SelectRelation(EdgeStory(edge), edge.Relation)) { name = "story-map-line-" + edge.Stories[0].Data.Story.Id };
                edge.Label = label;
                label.AddToClassList("sm-line-target");
                label.style.left = focusPoint.x - 14; label.style.top = focusPoint.y - 14; label.style.width = 28; label.style.height = 28;
                world.Add(label);
                foreach (var (point, hint, suffix) in new[] { (edge.Points[0], fromEndpoint.hint, "first"), (edge.Points[edge.Points.Length - 1], toEndpoint.hint, "second") })
                {
                    var contact = new Button(() => SelectRelation(EdgeStory(edge), edge.Relation))
                    { name = "story-map-contact-" + story.Story.Id + "-" + suffix, tooltip = hint };
                    contact.AddToClassList("sm-contact-target");
                    contact.style.left = point.x - 8; contact.style.top = point.y - 8;
                    edge.Contacts.Add(contact); world.Add(contact);
                }
            }
            world.style.width = width; world.style.height = y;
            overviewBounds = new Rect(0, 0, Mathf.Max(width, 360), Mathf.Max(y, 240));
            foreach (var cluster in clusters)
            {
                var bounds = cluster.Data.Wrestlers.Where(x => cards.ContainsKey(x.Id)).Select(x => cards[x.Id].Block.Bounds).ToList();
                bounds.AddRange(cluster.Edges.SelectMany(x => x.Points).Select(p => new Rect(p, Vector2.one)));
                cluster.Bounds = bounds.Count == 0 ? new Rect(0, 0, Mathf.Max(width, 360), Mathf.Max(y, 240))
                    : Rect.MinMaxRect(bounds.Min(x => x.xMin) - 16, bounds.Min(x => x.yMin) - 16, bounds.Max(x => x.xMax) + 16, bounds.Max(x => x.yMax) + 24);
            }
            if (map.Clusters.Any(x => x.MissingParticipants) || edges.Any(x => x.Points.Length < 2))
            {
                var warning = new Label(map.Clusters.Any(x => x.MissingParticipants)
                    ? "일부 참가자 또는 그룹 정보를 찾을 수 없습니다." : "일부 스토리라인의 연결선을 배치하지 못했습니다.");
                warning.AddToClassList("sm-map-warning"); Add(warning);
            }
        }

        private (Rect bounds, string hint) Endpoint(EdgeView edge, bool source)
        {
            var card = cards[source ? edge.FromId : edge.ToId]; var story = edge.Stories[0].Data;
            var firstParty = story.FirstPartyIds.Contains(card.Wrestler.Id);
            var groupId = firstParty ? story.Story.FirstGroupId : story.Story.SecondGroupId;
            if (story.Story.PartyType != StoryPartyType.Wrestler && groupId != null && card.Block.PartyBounds.TryGetValue(groupId, out var party))
                return (party, (story.Story.PartyType == StoryPartyType.Stable ? "스테이블 · " : "태그팀 · ") + (firstParty ? story.FirstParty : story.SecondParty));
            return (card.Bounds, "개인 · " + card.Wrestler.Identity.RingName);
        }

        private ClusterView EdgeStory(EdgeView edge) => edge.Stories.FirstOrDefault(x => x.Data.Story.Id == focusedStory) ?? edge.Stories[0];

        private void BuildCard(CardView card, StorylineMapData map)
        {
            var wrestler = card.Wrestler;
            card.Root.name = "story-map-card-" + wrestler.Id;
            card.Root.AddToClassList("sm-wrestler");
            var alignment = wrestler.Roster?.Alignment ?? KayfabeAlignment.Tweener;
            card.Root.AddToClassList(alignment == KayfabeAlignment.Face ? "sm-face" : alignment == KayfabeAlignment.Heel ? "sm-heel" : "sm-tweener");
            card.Root.style.width = CardWidth; card.Root.style.height = CardHeight;
            var portrait = new Button(() => SelectWrestler(clusters.FirstOrDefault(x => x.Data.Story.Id == focusedStory && x.Data.Wrestlers.Any(w => w.Id == wrestler.Id))
                ?? clusters.FirstOrDefault(x => x.Data.Wrestlers.Any(w => w.Id == wrestler.Id)), wrestler));
            portrait.name = "story-map-select-" + wrestler.Id; portrait.AddToClassList("sm-portrait");
            portrait.tooltip = "연결된 스토리라인 선택";
            var frame = WrestlerProfileController.CreatePortraitDisplayWithNameLink(wrestler).Q<VisualElement>(className: "wrestler-portrait-frame");
            frame.RemoveFromHierarchy(); portrait.Add(frame); card.Root.Add(portrait);
            var link = WrestlerProfileController.CreateLink(wrestler, "sm-name"); link.tooltip = "선수 페이지 열기"; card.Root.Add(link);
            var held = map.ChampionTitles.TryGetValue(wrestler.Id, out var titles) ? titles : null;
            var champion = new Label(held == null ? "" : string.Join(" · ", held.Select(t => t.Name))) { pickingMode = PickingMode.Ignore };
            champion.tooltip = champion.text; champion.AddToClassList("sm-champion"); card.Root.Add(champion);
        }

        public void SetStory(string id)
        {
            focusedStory = id;
            selectedCluster = clusters.FirstOrDefault(x => x.Data.Story.Id == id)?.Data;
            selectedWrestler = null; selectedRelation = null;
            UpdateEmphasis(); Fit();
        }

        private void SelectWrestler(ClusterView view, WrestlerState wrestler)
        {
            focusedStory = view?.Data.Story.Id;
            selectedCluster = view?.Data; selectedWrestler = wrestler.Id; selectedRelation = null;
            UpdateEmphasis(); selected?.Invoke(view?.Data, wrestler, null);
        }

        private void SelectRelation(ClusterView view, StorylineMapRelation relation)
        {
            focusedStory = view.Data.Story.Id;
            selectedCluster = view.Data; selectedWrestler = null; selectedRelation = relation;
            UpdateEmphasis(); selected?.Invoke(view.Data, null, relation);
        }

        public void ClearSelection()
        {
            selectedCluster = null; selectedWrestler = null; selectedRelation = null;
            UpdateEmphasis(); cleared?.Invoke();
        }

        public void SetIncidentFilter(int filter)
        {
            incidentFilter = filter;
            if (selectedRelation != null && !edges.Any(x => x.Relation == selectedRelation && Visible(x))) ClearSelection();
            UpdateEmphasis();
        }

        private bool Visible(EdgeView edge)
        {
            if (incidentFilter == 0) return true;
            return edge.Stories.Any(s => s.Data.History.Any(e => e != null && (incidentFilter switch
            {
                1 => e.Incident == StoryIncident.Challenge,
                2 => e.Incident is StoryIncident.Attack or StoryIncident.Betrayal,
                3 => e.Incident is StoryIncident.Rescue or StoryIncident.Respect or StoryIncident.Reconciliation,
                _ => true
            })));
        }

        private bool Highlighted(EdgeView edge)
        {
            if (selectedRelation != null) return selectedRelation == edge.Relation;
            if (selectedWrestler != null) return edge.Stories.Any(x => x.Data.Wrestlers.Any(w => w.Id == selectedWrestler));
            var id = selectedCluster?.Story.Id ?? focusedStory;
            return id == null || edge.Stories.Any(x => x.Data.Story.Id == id);
        }

        private void UpdateEmphasis()
        {
            var storyFocus = selectedCluster?.Story.Id ?? focusedStory;
            var participants = clusters.Where(x => selectedWrestler != null
                    ? x.Data.Wrestlers.Any(w => w.Id == selectedWrestler)
                    : storyFocus == null || x.Data.Story.Id == storyFocus)
                .SelectMany(x => x.Data.Wrestlers).Select(x => x.Id).ToHashSet();
            if (storyFocus == null && selectedWrestler == null) participants.UnionWith(cards.Keys);
            if (selectedWrestler != null) participants.Add(selectedWrestler);
            foreach (var card in cards.Values)
            {
                card.Root.style.opacity = participants.Contains(card.Wrestler.Id) ? 1 : .3f;
                card.Root.EnableInClassList("sm-selected", card.Wrestler.Id == selectedWrestler || selectedRelation != null && participants.Contains(card.Wrestler.Id));
            }
            foreach (var block in blocks) block.Root.EnableInClassList("sm-faction-selected", block.Cards.Any(x => participants.Contains(x.Wrestler.Id)) && storyFocus != null);
            foreach (var edge in edges)
            {
                var display = Visible(edge) ? DisplayStyle.Flex : DisplayStyle.None;
                if (edge.Label != null) edge.Label.style.display = display;
                foreach (var contact in edge.Contacts) contact.style.display = display;
            }
            lines.MarkDirtyRepaint();
        }

        private void DrawEdges(Painter2D painter)
        {
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            foreach (var edge in edges)
            {
                var relation = edge.Relation; var points = edge.Points;
                if (!Visible(edge) || points.Length < 2) continue;
                ColorUtility.TryParseHtmlString(relation == selectedRelation ? "#d9b45b" : "#ef7b83", out var color);
                color.a = Highlighted(edge) ? 1 : .22f;
                painter.strokeColor = color;
                painter.lineWidth = relation == selectedRelation ? 4 : 2.5f;
                painter.BeginPath(); painter.MoveTo(points[0]);
                for (var i = 1; i < points.Length; i++) painter.LineTo(points[i]);
                painter.Stroke();
                painter.fillColor = color;
                foreach (var point in new[] { points[0], points[points.Length - 1] })
                {
                    painter.BeginPath(); painter.Arc(point, 4.5f, Angle.Degrees(0), Angle.Degrees(360));
                    painter.ClosePath(); painter.Fill();
                }
            }
        }

        public void Fit()
        {
            if (contentRect.width < 1 || contentRect.height < 1 || cards.Count == 0) return;
            var targets = clusters.Where(x => focusedStory == null || x.Data.Story.Id == focusedStory).ToList();
            if (targets.Count == 0) targets = clusters;
            var bounds = focusedStory == null || targets.Count == 0 ? overviewBounds
                : Rect.MinMaxRect(targets.Min(x => x.Bounds.xMin), targets.Min(x => x.Bounds.yMin), targets.Max(x => x.Bounds.xMax), targets.Max(x => x.Bounds.yMax));
            Zoom = Mathf.Clamp(Mathf.Min((contentRect.width - 40) / bounds.width, (contentRect.height - 40) / bounds.height), .04f, 1.5f);
            pan = contentRect.size / 2 - bounds.center * Zoom;
            needsFit = false; ApplyTransform();
        }

        public void ZoomAt(Vector2 position, float factor)
        {
            var before = Zoom;
            Zoom = Mathf.Clamp(Zoom * factor, .04f, 2.5f);
            pan = position - (position - pan) * (Zoom / before);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            world.style.scale = new Scale(new Vector3(Zoom, Zoom, 1));
            world.style.translate = new Translate(pan.x, pan.y, 0);
        }

        private static bool IsControl(VisualElement target, VisualElement stop)
        {
            for (var current = target; current != null && current != stop; current = current.parent)
                if (current is Button) return true;
            return false;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || IsControl(evt.target as VisualElement, this)) return;
            downPosition = this.WorldToLocal(evt.position); downPan = pan;
            dragPointer = evt.pointerId; dragging = false;
            Focus(); this.CapturePointer(dragPointer); evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != dragPointer) return;
            var delta = this.WorldToLocal(evt.position) - downPosition;
            if (delta.sqrMagnitude > 36) dragging = true;
            if (dragging) { pan = downPan + delta; ApplyTransform(); }
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != dragPointer || evt.button != 0) return;
            var position = this.WorldToLocal(evt.position);
            if (!dragging)
            {
                var point = (position - pan) / Zoom;
                var hit = edges.Where(Visible).Select(edge => (edge, distance: DistanceToEdge(point, edge.Points)))
                    .Where(x => x.distance * Zoom <= 12).OrderBy(x => x.distance).FirstOrDefault();
                if (hit.edge != null) SelectRelation(EdgeStory(hit.edge), hit.edge.Relation);
                else ClearSelection();
            }
            EndDrag(); evt.StopPropagation();
        }

        private void EndDrag()
        {
            var pointer = dragPointer; dragPointer = -1; dragging = false;
            if (pointer >= 0 && this.HasPointerCapture(pointer)) this.ReleasePointer(pointer);
        }

        public static float DistanceToEdge(Vector2 point, IReadOnlyList<Vector2> points)
        {
            var nearest = float.MaxValue;
            for (var i = 1; i < points.Count; i++)
            {
                var segment = points[i] - points[i - 1];
                var t = segment.sqrMagnitude < .001f ? 0 : Mathf.Clamp01(Vector2.Dot(point - points[i - 1], segment) / segment.sqrMagnitude);
                nearest = Mathf.Min(nearest, Vector2.Distance(point, points[i - 1] + segment * t));
            }
            return nearest;
        }
    }
}
