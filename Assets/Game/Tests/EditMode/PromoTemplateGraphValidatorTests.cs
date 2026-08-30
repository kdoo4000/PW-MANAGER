using NUnit.Framework;
using PWManager.Data.Definitions;
using PWManager.Data.Validation;
using UnityEngine;

namespace PWManager.Tests
{
    public sealed class PromoTemplateGraphValidatorTests
    {
        [Test]
        public void Validate_ConnectedStartAndOutcome_IsValid()
        {
            var graph = CreateGraph();

            var errors = PromoTemplateGraphValidator.Validate(graph);

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void Validate_UnreachableNode_IsRejected()
        {
            var graph = CreateGraph();
            graph.Nodes.Add(new PromoGraphNodeData { Id = "orphan", Type = PromoGraphNodeType.Beat, Title = "Orphan" });

            var errors = PromoTemplateGraphValidator.Validate(graph);

            Assert.That(errors, Does.Contain("Node Orphan is unreachable from the start node."));
        }

        [Test]
        public void Validate_OutcomeWithOutgoingEdge_IsRejected()
        {
            var graph = CreateGraph();
            graph.Edges.Add(new PromoGraphEdgeData { Id = "invalid", FromNodeId = "outcome", ToNodeId = "start" });

            var errors = PromoTemplateGraphValidator.Validate(graph);

            Assert.That(errors, Does.Contain("Outcome node Outcome cannot have an outgoing edge."));
            Assert.That(errors, Does.Contain("Start node cannot have an incoming edge."));
        }

        [Test]
        public void Validate_ChoiceWithOneBranch_IsRejected()
        {
            var graph = CreateGraph();
            graph.Nodes.Insert(1, new PromoGraphNodeData { Id = "choice", Type = PromoGraphNodeType.Choice, Title = "Decision" });
            graph.Edges[0].ToNodeId = "choice";
            graph.Edges.Add(new PromoGraphEdgeData { Id = "choice-out", FromNodeId = "choice", ToNodeId = "outcome" });

            var errors = PromoTemplateGraphValidator.Validate(graph);

            Assert.That(errors, Does.Contain("Choice node Decision requires at least two branches."));
        }

        [Test]
        public void Validate_ReachableCycle_IsRejected()
        {
            var graph = CreateGraph();
            graph.Nodes.Insert(1, new PromoGraphNodeData { Id = "beat", Type = PromoGraphNodeType.Beat, Title = "Beat" });
            graph.Edges[0].ToNodeId = "beat";
            graph.Edges.Add(new PromoGraphEdgeData { Id = "beat-loop", FromNodeId = "beat", ToNodeId = "beat" });
            graph.Edges.Add(new PromoGraphEdgeData { Id = "beat-out", FromNodeId = "beat", ToNodeId = "outcome" });

            var errors = PromoTemplateGraphValidator.Validate(graph);

            Assert.That(errors, Does.Contain("Graph cannot contain a cycle."));
        }

        private static PromoTemplateGraphAsset CreateGraph()
        {
            var graph = ScriptableObject.CreateInstance<PromoTemplateGraphAsset>();
            graph.TemplateId = "promo_template_test";
            graph.DisplayName = "Test Promo";
            graph.Nodes.Add(new PromoGraphNodeData { Id = "start", Type = PromoGraphNodeType.Start, Title = "Start" });
            graph.Nodes.Add(new PromoGraphNodeData { Id = "outcome", Type = PromoGraphNodeType.Outcome, Title = "Outcome" });
            graph.Edges.Add(new PromoGraphEdgeData { Id = "edge", FromNodeId = "start", ToNodeId = "outcome" });
            return graph;
        }
    }
}
