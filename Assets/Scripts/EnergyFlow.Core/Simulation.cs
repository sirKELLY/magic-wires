using System;
using System.Collections.Generic;

namespace EnergyFlow.Core
{
    /// <summary>
    /// Tuning constants (spec 8). The core itself is tick-based and never reads
    /// real time; the host layer uses this to schedule fixed-timestep stepping.
    /// </summary>
    public static class SimConstants
    {
        /// <summary>Global tick rate. Suggested default 20; final value TBD during balancing.</summary>
        public const int DefaultTicksPerSecond = 20;
    }

    /// <summary>
    /// The deterministic fixed-timestep simulation (spec 3, 6). Owns the whole
    /// sim state: instantiate, step, inspect, serialize, discard (spec 7.4).
    /// No global mutable state.
    /// </summary>
    public sealed class Simulation
    {
        private readonly Dictionary<string, double> _incoming = new Dictionary<string, double>();
        private readonly Dictionary<string, bool> _accepting = new Dictionary<string, bool>();
        private readonly List<(string nodeId, bool open)> _pendingToggles = new List<(string, bool)>();
        private readonly NodeContext _context;

        public Simulation(Graph graph, EventBus events = null, long startTick = 0)
        {
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            Events = events ?? new EventBus();
            CurrentTick = startTick;
            _context = new NodeContext(this);
            foreach (var node in graph.TopologicalOrder)
            {
                _incoming[node.Id] = 0.0;
                _accepting[node.Id] = false;
            }
        }

        public Graph Graph { get; }
        public EventBus Events { get; }
        public long CurrentTick { get; private set; }

        /// <summary>Total flow created by sources since t=0 (conservation, spec 6.5).</summary>
        public double TotalProduced { get; internal set; }

        /// <summary>Total flow consumed by sink fire events since t=0 (conservation, spec 6.5).</summary>
        public double TotalConsumed { get; internal set; }

        /// <summary>Sum of all flow currently held/banked in all nodes (conservation, spec 6.5).</summary>
        public double TotalHeld
        {
            get
            {
                double sum = 0.0;
                foreach (var node in Graph.TopologicalOrder) sum += node.Held;
                return sum;
            }
        }

        /// <summary>
        /// Queue a switch toggle. Takes effect on the next tick boundary, never
        /// mid-tick (spec 6.3). The node must implement <see cref="IToggleable"/>.
        /// </summary>
        public void SetSwitchOpen(string nodeId, bool open)
        {
            if (!(Graph.GetNode(nodeId) is IToggleable))
                throw new ArgumentException($"Node '{nodeId}' is not toggleable.", nameof(nodeId));
            _pendingToggles.Add((nodeId, open));
        }

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }

        /// <summary>Advance the simulation by exactly one tick (spec 3.3).</summary>
        public void Step()
        {
            // Toggles apply on the tick boundary, before Pass 1.
            foreach (var (nodeId, open) in _pendingToggles)
            {
                ((IToggleable)Graph.GetNode(nodeId)).Open = open;
                Emit(new SwitchToggled(nodeId, open, CurrentTick));
            }
            _pendingToggles.Clear();

            // Pass 1 — backward accept pass (spec 3.3). One general rule, applied
            // in reverse topological order so every output's target is computed
            // before the node that feeds it:
            //   accepting := localAccept && (no outputs || any output target accepting)
            var order = Graph.TopologicalOrder;
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var node = order[i];
                bool accepting = node.LocalAccept();
                if (accepting)
                {
                    var outputs = Graph.OutputsOf(node.Id);
                    if (outputs.Count > 0)
                    {
                        accepting = false;
                        for (int o = 0; o < outputs.Count; o++)
                        {
                            if (_accepting[outputs[o].ToNodeId]) { accepting = true; break; }
                        }
                    }
                }
                _accepting[node.Id] = accepting;
            }

            // Pass 2 — forward flow pass, topological order, sources first.
            foreach (var node in order) _incoming[node.Id] = 0.0;
            foreach (var node in order)
            {
                _context.Current = node;
                node.Tick(_incoming[node.Id], _context);
            }
            _context.Current = null;

            CurrentTick++;
        }

        private void Emit(ISimEvent evt)
        {
            // Conservation bookkeeping rides on the same events the host sees,
            // so produced/consumed totals always match the event log.
            switch (evt)
            {
                case SourceProduced p: TotalProduced += p.Amount; break;
                case SinkFired f: TotalConsumed += f.Amount; break;
            }
            Events.Publish(evt);
        }

        private sealed class NodeContext : INodeContext
        {
            private readonly Simulation _sim;
            internal Node Current;

            internal NodeContext(Simulation sim) { _sim = sim; }

            public long Tick => _sim.CurrentTick;

            public int OutputCount => _sim.Graph.OutputsOf(Current.Id).Count;

            public bool IsOutputAccepting(int outputPort) =>
                _sim._accepting[GetEdge(outputPort).ToNodeId];

            public void Push(int outputPort, double amount)
            {
                if (amount <= 0.0) return;
                var edge = GetEdge(outputPort);
                if (!_sim._accepting[edge.ToNodeId])
                    throw new InvalidOperationException(
                        $"Node '{Current.Id}' pushed to non-accepting node '{edge.ToNodeId}'. " +
                        "Nodes must only push to accepting outputs (spec 3.3).");
                _sim._incoming[edge.ToNodeId] += amount;
                _sim.Emit(new FlowDelivered(edge.ToNodeId, amount, _sim.CurrentTick));
            }

            public void Emit(ISimEvent evt) => _sim.Emit(evt);

            private Edge GetEdge(int outputPort)
            {
                var outputs = _sim.Graph.OutputsOf(Current.Id);
                if (outputPort < 0 || outputPort >= outputs.Count)
                    throw new ArgumentOutOfRangeException(nameof(outputPort),
                        $"Node '{Current.Id}' has no output port {outputPort}.");
                return outputs[outputPort];
            }
        }
    }
}
