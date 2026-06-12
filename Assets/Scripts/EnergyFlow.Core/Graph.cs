using System;
using System.Collections.Generic;
using System.Linq;

namespace EnergyFlow.Core
{
    public sealed class GraphValidationException : Exception
    {
        public GraphValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// A conduit (spec 4.2): connects exactly one output port to one input port.
    /// Instant and uncapped in the core; carries no flow itself (flow lives in
    /// nodes). <see cref="ThroughputCap"/> is a reserved field — stored and
    /// serialized, no behavior until Phase C.
    /// </summary>
    public sealed class Edge
    {
        public Edge(string fromNodeId, int fromPort, string toNodeId, int toPort,
            double throughputCap = double.PositiveInfinity)
        {
            FromNodeId = fromNodeId;
            FromPort = fromPort;
            ToNodeId = toNodeId;
            ToPort = toPort;
            ThroughputCap = throughputCap;
        }

        public string FromNodeId { get; }
        public int FromPort { get; }
        public string ToNodeId { get; }
        public int ToPort { get; }
        public double ThroughputCap { get; }
    }

    /// <summary>
    /// An immutable, validated DAG of nodes and edges (spec 5). Construction
    /// rejects cycles, arity violations and bad config. Topological order is
    /// computed once, with stable tie-breaking by node ID (ordinal), and reused
    /// every tick (spec 5.4).
    /// </summary>
    public sealed class Graph
    {
        private readonly Dictionary<string, Node> _nodesById;
        private readonly Dictionary<string, List<Edge>> _outputsByNode;
        private readonly List<Node> _topologicalOrder;

        public Graph(IEnumerable<Node> nodes, IEnumerable<Edge> edges)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (edges == null) throw new ArgumentNullException(nameof(edges));

            _nodesById = new Dictionary<string, Node>();
            foreach (var node in nodes)
            {
                if (_nodesById.ContainsKey(node.Id))
                    throw new GraphValidationException($"Duplicate node id '{node.Id}'.");
                _nodesById[node.Id] = node;
            }

            Edges = edges.ToList();
            _outputsByNode = new Dictionary<string, List<Edge>>();
            var inputsByNode = new Dictionary<string, List<Edge>>();
            foreach (var id in _nodesById.Keys)
            {
                _outputsByNode[id] = new List<Edge>();
                inputsByNode[id] = new List<Edge>();
            }

            foreach (var edge in Edges)
            {
                if (!_nodesById.ContainsKey(edge.FromNodeId))
                    throw new GraphValidationException($"Edge references unknown node '{edge.FromNodeId}'.");
                if (!_nodesById.ContainsKey(edge.ToNodeId))
                    throw new GraphValidationException($"Edge references unknown node '{edge.ToNodeId}'.");
                if (edge.FromPort < 0 || edge.ToPort < 0)
                    throw new GraphValidationException(
                        $"Edge {edge.FromNodeId}:{edge.FromPort} -> {edge.ToNodeId}:{edge.ToPort} has a negative port index.");
                _outputsByNode[edge.FromNodeId].Add(edge);
                inputsByNode[edge.ToNodeId].Add(edge);
            }

            foreach (var node in _nodesById.Values)
            {
                var outs = _outputsByNode[node.Id];
                var ins = inputsByNode[node.Id];
                ValidatePortsContiguous(node.Id, "output", outs.Select(e => e.FromPort));
                ValidatePortsContiguous(node.Id, "input", ins.Select(e => e.ToPort));
                node.ValidateTopology(ins.Count, outs.Count);
                node.ValidateConfig();
                // Pass 2 looks up output edges by port index.
                outs.Sort((a, b) => a.FromPort.CompareTo(b.FromPort));
            }

            _topologicalOrder = ComputeTopologicalOrder(inputsByNode);
        }

        public IReadOnlyList<Edge> Edges { get; }

        /// <summary>Nodes in deterministic topological order, sources first (spec 5.4).</summary>
        public IReadOnlyList<Node> TopologicalOrder => _topologicalOrder;

        public IEnumerable<Node> Nodes => _topologicalOrder;

        public Node GetNode(string id) =>
            _nodesById.TryGetValue(id, out var node)
                ? node
                : throw new KeyNotFoundException($"No node with id '{id}'.");

        public bool TryGetNode(string id, out Node node) => _nodesById.TryGetValue(id, out node);

        /// <summary>Output edges of a node, sorted by output port index.</summary>
        public IReadOnlyList<Edge> OutputsOf(string nodeId) => _outputsByNode[nodeId];

        private static void ValidatePortsContiguous(string nodeId, string side, IEnumerable<int> ports)
        {
            var sorted = ports.OrderBy(p => p).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != i)
                    throw new GraphValidationException(
                        $"Node '{nodeId}' {side} ports must be exactly 0..{sorted.Count - 1} with one edge each " +
                        $"(found duplicate or gap at port {sorted[i]}).");
            }
        }

        private List<Node> ComputeTopologicalOrder(Dictionary<string, List<Edge>> inputsByNode)
        {
            // Kahn's algorithm with a sorted ready set for stable, deterministic
            // tie-breaking by node ID (spec 5.4, 6.2).
            var inDegree = _nodesById.Keys.ToDictionary(id => id, id => inputsByNode[id].Count);
            var ready = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var kv in inDegree)
                if (kv.Value == 0) ready.Add(kv.Key);

            var order = new List<Node>(_nodesById.Count);
            while (ready.Count > 0)
            {
                string id = ready.Min;
                ready.Remove(id);
                order.Add(_nodesById[id]);
                foreach (var edge in _outputsByNode[id])
                {
                    if (--inDegree[edge.ToNodeId] == 0) ready.Add(edge.ToNodeId);
                }
            }

            if (order.Count != _nodesById.Count)
            {
                var cycleNodes = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(id => id, StringComparer.Ordinal);
                throw new GraphValidationException(
                    $"Graph contains a cycle involving: {string.Join(", ", cycleNodes)}. The network must be a DAG (spec 2.2).");
            }
            return order;
        }
    }
}
