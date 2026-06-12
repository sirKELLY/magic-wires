using System.Collections.Generic;
using System.Linq;
using EnergyFlow.Core;
using EnergyFlow.Core.Nodes;
using NUnit.Framework;

namespace EnergyFlow.Core.Tests
{
    public class GraphValidationTests
    {
        private static SwitchNode Sw(string id) => new SwitchNode(id);
        private static SinkNode Sink(string id, double cost = 1) => new SinkNode(id) { Cost = cost };
        private static SourceNode Src(string id, double rate = 1) => new SourceNode(id) { ProductionRate = rate };

        [Test]
        public void Cycle_IsRejected()
        {
            var ex = Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Sw("a"), Sw("b") },
                new[] { new Edge("a", 0, "b", 0), new Edge("b", 0, "a", 0) }));
            StringAssert.Contains("cycle", ex.Message);
        }

        [Test]
        public void DuplicateNodeId_IsRejected()
        {
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), Sink("a") }, new Edge[0]));
        }

        [Test]
        public void EdgeToUnknownNode_IsRejected()
        {
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("src") }, new[] { new Edge("src", 0, "ghost", 0) }));
        }

        [Test]
        public void SourceWithInput_IsRejected()
        {
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), Src("b"), Sink("k") },
                new[] { new Edge("a", 0, "b", 0), new Edge("b", 0, "k", 0) }));
        }

        [Test]
        public void MultipleInputsOnNonMerger_IsRejected()
        {
            // Spec 5.3: only the Merger may have multiple input edges.
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), Src("b"), Sink("k") },
                new[] { new Edge("a", 0, "k", 0), new Edge("b", 0, "k", 1) }));
        }

        [Test]
        public void MultipleOutputsOnNonSplitter_IsRejected()
        {
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), Sw("s"), Sink("k1"), Sink("k2") },
                new[]
                {
                    new Edge("a", 0, "s", 0),
                    new Edge("s", 0, "k1", 0),
                    new Edge("s", 1, "k2", 0),
                }));
        }

        [Test]
        public void SplitterWeightCountMismatch_IsRejected()
        {
            var splitter = new SplitterNode("sp") { Weights = new List<double> { 5, 1 } };
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), splitter, Sink("k1"), Sink("k2"), Sink("k3") },
                new[]
                {
                    new Edge("a", 0, "sp", 0),
                    new Edge("sp", 0, "k1", 0),
                    new Edge("sp", 1, "k2", 0),
                    new Edge("sp", 2, "k3", 0),
                }));
        }

        [Test]
        public void NonPositiveSplitterWeight_IsRejected()
        {
            var splitter = new SplitterNode("sp") { Weights = new List<double> { 5, 0 } };
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), splitter, Sink("k1"), Sink("k2") },
                new[]
                {
                    new Edge("a", 0, "sp", 0),
                    new Edge("sp", 0, "k1", 0),
                    new Edge("sp", 1, "k2", 0),
                }));
        }

        [Test]
        public void NonPositiveGateThreshold_IsRejected()
        {
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), new GateNode("g") { Threshold = 0 }, Sink("k") },
                new[] { new Edge("a", 0, "g", 0), new Edge("g", 0, "k", 0) }));
        }

        [Test]
        public void NonPositiveSinkCost_IsRejected()
        {
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), Sink("k", 0) },
                new[] { new Edge("a", 0, "k", 0) }));
        }

        [Test]
        public void PortGap_IsRejected()
        {
            // Merger input ports must be exactly 0..n-1.
            Assert.Throws<GraphValidationException>(() => new Graph(
                new Node[] { Src("a"), Src("b"), new MergerNode("m"), Sink("k") },
                new[]
                {
                    new Edge("a", 0, "m", 0),
                    new Edge("b", 0, "m", 2),
                    new Edge("m", 0, "k", 0),
                }));
        }

        [Test]
        public void TopologicalOrder_IsStable_RegardlessOfInsertionOrder()
        {
            List<string> Build(IEnumerable<Node> nodes)
            {
                var graph = new Graph(nodes, new[]
                {
                    new Edge("a", 0, "m", 0),
                    new Edge("b", 0, "m", 1),
                    new Edge("m", 0, "k", 0),
                });
                return graph.TopologicalOrder.Select(n => n.Id).ToList();
            }

            var order1 = Build(new Node[] { Src("a"), Src("b"), new MergerNode("m"), Sink("k") });
            var order2 = Build(new Node[] { Sink("k"), new MergerNode("m"), Src("b"), Src("a") });

            CollectionAssert.AreEqual(order1, order2);
            // Stable tie-break: both parentless sources ordered by id.
            CollectionAssert.AreEqual(new[] { "a", "b", "m", "k" }, order1);
        }

        [Test]
        public void Registry_CreatesAllSevenCoreTypes_AndRejectsUnknown()
        {
            var registry = NodeTypeRegistry.CreateDefault();
            // Conduit is the edge (spec 4.2); six node classes + edge = seven types.
            foreach (var typeId in new[] { "source", "merger", "splitter", "gate", "switch", "sink" })
            {
                Assert.IsTrue(registry.IsRegistered(typeId), $"'{typeId}' should be registered");
                Assert.AreEqual(typeId, registry.Create(typeId, "n1").TypeId);
            }
            Assert.Throws<KeyNotFoundException>(() => registry.Create("transformer", "n1"));
        }
    }
}
