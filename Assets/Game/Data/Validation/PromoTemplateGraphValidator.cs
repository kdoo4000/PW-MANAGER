using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Definitions;

namespace PWManager.Data.Validation
{
    public static class PromoTemplateGraphValidator
    {
        public static IReadOnlyList<string> Validate(PromoTemplateGraphAsset graph)
        {
            var errors = new List<string>();
            if (graph == null)
            {
                errors.Add("Graph asset is missing.");
                return errors;
            }

            var nodes = graph.Nodes ?? new List<PromoGraphNodeData>();
            var edges = graph.Edges ?? new List<PromoGraphEdgeData>();
            if (string.IsNullOrWhiteSpace(graph.TemplateId)) errors.Add("Template ID is required.");
            if (string.IsNullOrWhiteSpace(graph.DisplayName)) errors.Add("Display name is required.");
            if (nodes.Count == 0) errors.Add("Graph requires at least one node.");

            var validNodes = nodes.Where(x => x != null).ToList();
            if (validNodes.Count != nodes.Count) errors.Add("Graph contains a missing node.");
            AddDuplicateErrors(validNodes.Select(x => x.Id), "node", errors);

            var starts = validNodes.Where(x => x.Type == PromoGraphNodeType.Start).ToList();
            if (starts.Count != 1) errors.Add("Graph requires exactly one start node.");
            if (validNodes.All(x => x.Type != PromoGraphNodeType.Outcome))
                errors.Add("Graph requires at least one outcome node.");

            var nodeIds = new HashSet<string>(validNodes.Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
            var validEdges = edges.Where(x => x != null).ToList();
            if (validEdges.Count != edges.Count) errors.Add("Graph contains a missing edge.");
            AddDuplicateErrors(validEdges.Select(x => x.Id), "edge", errors);

            foreach (var edge in validEdges)
            {
                if (!nodeIds.Contains(edge.FromNodeId)) errors.Add($"Edge {edge.Id} has a missing source node.");
                if (!nodeIds.Contains(edge.ToNodeId)) errors.Add($"Edge {edge.Id} has a missing target node.");
                if (edge.FromNodeId == edge.ToNodeId) errors.Add($"Edge {edge.Id} cannot connect a node to itself.");
            }

            foreach (var start in starts)
                if (validEdges.Any(x => x.ToNodeId == start.Id)) errors.Add("Start node cannot have an incoming edge.");
            foreach (var outcome in validNodes.Where(x => x.Type == PromoGraphNodeType.Outcome))
                if (validEdges.Any(x => x.FromNodeId == outcome.Id)) errors.Add($"Outcome node {outcome.Title} cannot have an outgoing edge.");

            foreach (var node in validNodes.Where(x => x.Type != PromoGraphNodeType.Outcome))
                if (!validEdges.Any(x => x.FromNodeId == node.Id))
                    errors.Add($"Node {node.Title} requires an outgoing edge.");

            foreach (var outcome in validNodes.Where(x => x.Type == PromoGraphNodeType.Outcome))
                if (!validEdges.Any(x => x.ToNodeId == outcome.Id))
                    errors.Add($"Outcome node {outcome.Title} requires an incoming edge.");

            foreach (var choice in validNodes.Where(x => x.Type == PromoGraphNodeType.Choice))
                if (validEdges.Count(x => x.FromNodeId == choice.Id) < 2)
                    errors.Add($"Choice node {choice.Title} requires at least two branches.");

            if (starts.Count == 1)
            {
                var reachable = FindReachable(starts[0].Id, validEdges);
                foreach (var node in validNodes.Where(x => !reachable.Contains(x.Id)))
                    errors.Add($"Node {node.Title} is unreachable from the start node.");

                if (ContainsCycle(starts[0].Id, validEdges))
                    errors.Add("Graph cannot contain a cycle.");
            }

            return errors;
        }

        private static void AddDuplicateErrors(IEnumerable<string> ids, string kind, ICollection<string> errors)
        {
            if (ids.Any(string.IsNullOrWhiteSpace)) errors.Add($"Every {kind} requires an ID.");
            if (ids.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.Ordinal).Any(x => x.Count() > 1))
                errors.Add($"Graph contains duplicate {kind} IDs.");
        }

        private static HashSet<string> FindReachable(string startId, IReadOnlyList<PromoGraphEdgeData> edges)
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal) { startId };
            var pending = new Queue<string>();
            pending.Enqueue(startId);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var target in edges.Where(x => x.FromNodeId == current).Select(x => x.ToNodeId))
                    if (reachable.Add(target)) pending.Enqueue(target);
            }
            return reachable;
        }

        private static bool ContainsCycle(string startId, IReadOnlyList<PromoGraphEdgeData> edges)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            return Visit(startId);

            bool Visit(string nodeId)
            {
                if (active.Contains(nodeId)) return true;
                if (!visited.Add(nodeId)) return false;
                active.Add(nodeId);
                foreach (var target in edges.Where(x => x.FromNodeId == nodeId).Select(x => x.ToNodeId))
                    if (Visit(target)) return true;
                active.Remove(nodeId);
                return false;
            }
        }
    }
}
